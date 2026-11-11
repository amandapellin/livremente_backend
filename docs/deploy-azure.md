# Deploy na Azure — LivreMente

> Documento **vivo**: planejamento de deploy do sistema (back-end .NET + front SPA + banco
> já hospedado na Azure). Atualize a cada mudança relevante de infraestrutura. **Não contém
> segredos** (connection string, chaves e credenciais ficam em Key Vault / variáveis de
> ambiente). Alinhado ao estado do código: back-end em **.NET 10 (LTS)**, Database First
> (sem EF Migrations), serviços de segundo plano e proxy de arquivos (ver `AGENTS.md`).

Escopo pensado como **protótipo acadêmico de baixo custo** (TCC), com os pontos de upgrade
para produção sinalizados em cada passo. O banco já está no **Azure Database for PostgreSQL
Flexible Server**, então o plano encaixa o restante no mesmo tenant/região.

---

## 1. Mapa dos serviços Azure

| Peça do sistema | Serviço Azure | Por quê |
|---|---|---|
| Banco (já existe) | **Azure Database for PostgreSQL Flexible Server** | Já provisionado (`livremente.postgres.database.azure.com`). |
| API .NET 10 | **Azure App Service (Linux, plano B1)** | PaaS gerenciado; roda `BackgroundService`, disco persistente em `/home` (índice de recomendação), saída de internet (proxy de arquivos), TLS grátis, Key Vault + identidade gerenciada, deploy direto de código (sem Docker). |
| Front SPA (Vite) | **Azure Static Web Apps** | Feito para SPA: CDN global, TLS/domínio grátis, fallback de rota, CI do GitHub embutido, e pode fazer *proxy* de `/api` → App Service (mata o CORS). |
| Segredos | **Azure Key Vault** | Connection string do banco, `Jwt:Key`, connection string do ACS fora do repositório e das App Settings em claro. |
| E-mail | **Azure Communication Services — Email** | Entrega real sem domínio próprio; o back envia via SDK `Azure.Communication.Email` (`Email:Provider=Acs`, `AcsEmailSender`). |
| Observabilidade | **Application Insights** (Azure Monitor) | Logs, requisições, exceções do back; integra direto no App Service. |
| Importer (console) | **execução manual / Azure Container Instance** | Não é serviço web; popular catálogo é tarefa ocasional. |
| CI/CD | **GitHub Actions** | Os dois repos já estão no GitHub; App Service e SWA têm deploy de primeira classe. |

> **Decisão-chave de arquitetura:** rodar o back em **uma única instância** (sem scale-out).
> O cache de arquivos (`IMemoryCache` ~256 MB) e o índice TF-IDF vivem **em memória do
> processo**; escalar horizontalmente duplicaria isso sem coerência. Para o TCC, 1 instância
> com RAM suficiente (B1 = 1,75 GB) é o certo. Escalar só **verticalmente**, se precisar.

---

## 2. Passo a passo

### Passo 0 — Pré-requisitos (uma vez)
- Subscription Azure com o Postgres já criado.
- Azure CLI (`az login`) ou acesso ao portal.
- Acesso de **admin do banco** (`livremente_admin`) para DDL.
- Os dois repositórios no GitHub com permissão para configurar Actions.
- **Região:** use a **mesma do Postgres** para App Service, Key Vault, ACS e Application
  Insights (menor latência e saída de dados).

### Passo 1 — Grupo de recursos
Crie (ou reutilize) um **Resource Group** único para tudo do LivreMente, na região do banco.
Facilita custo, limpeza e permissões.

### Passo 2 — Preparar o banco (que já existe)

> **Atenção (correção importante):** apesar do que o `AGENTS.md` sugere em entradas antigas,
> **não existe um conjunto de scripts `docs/sql/issue_*.sql` versionado para "rodar em ordem"**
> — varrendo todos os commits/branches, o único `.sql` que já existiu é
> `issue_18_enable_unaccent.sql`, e ele nem está na `main`. O schema foi aplicado direto no
> banco de dev (como admin), com SQL avulso. **A fonte da verdade do schema é o próprio banco
> de dev** (`livre_mente_dev`), que já tem tudo. Trate a preparação do banco de produção como
> **replicar esse schema**, não como "rodar migrations".

Três frentes: **(A) rede**, **(B) TLS/connection string**, **(C) schema** — mais a decisão dev vs. prod.

**A) Rede / firewall**
- **Protótipo (acesso público + firewall):** Portal → Postgres → *Networking* → marque
  **"Allow public access from Azure services and resources within Azure"** (deixa o App
  Service conectar sem depender do IP de saída) + regra com o **seu IP** para administrar.
  - *Tradeoff:* "serviços Azure" inclui recursos de outras subscriptions; a barreira passa a
    ser usuário+senha+SSL. Aceitável para TCC, não para produção.
  - CLI: `az postgres flexible-server firewall-rule create --resource-group <rg> --name
    livremente --rule-name AllowAzure --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0`.
- **Produção:** troque por **Private Endpoint** no Postgres + **VNet integration** no App
  Service e **desligue o acesso público**.

**B) SSL / connection string**
O Flexible Server **exige TLS**. A string do Npgsql de produção:
```
Host=livremente.postgres.database.azure.com;Port=5432;Database=<banco>;Username=livremente_app;Password=<senha>;SSL Mode=Require;Trust Server Certificate=true
```
- `SSL Mode=Require` obriga o canal cifrado; `Trust Server Certificate=true` aceita o
  certificado gerenciado da Azure sem distribuir a CA. (Mais rigoroso: `VerifyFull` + a
  cadeia da CA — opcional.) **A string inteira é segredo** → Key Vault (Passo 3).

**C) Schema (Database First, sem EF Migrations)**
- **Reusar o banco de dev** (`livre_mente_dev`) em produção → **nada a fazer no schema**.
- **Criar `livre_mente_prod` (recomendado)** → espelhe a estrutura do dev com dump **só de
  schema**, como **admin**:
  ```bash
  pg_dump --schema-only --no-owner \
    "host=livremente.postgres.database.azure.com dbname=livre_mente_dev user=livremente_admin sslmode=require" \
    > schema.sql
  psql "...dbname=livre_mente_prod user=livremente_admin sslmode=require" -f schema.sql
  ```
  Recria tabelas, **enums nativos** (`gender`, `publication_type`, `reading_status_enum`…) e
  `CHECK`/índices idênticos — sem esquecer nenhum `ALTER` avulso.
- **Extensão `unaccent` (gotcha da Azure):** a busca usa `unaccent`. No Flexible Server,
  `CREATE EXTENSION` só funciona se a extensão estiver na **allowlist**:
  1. Portal → Postgres → *Server parameters* → `azure.extensions` → adicionar **`UNACCENT`** → salvar.
  2. Como admin: `CREATE EXTENSION IF NOT EXISTS unaccent;`
  (No banco de dev já está feito.)

**D) Papéis (quem roda o quê)**
- **`livremente_admin`** (owner): toda DDL — `CREATE DATABASE/EXTENSION`, dump/restore de schema.
- **`livremente_app`** (DML-only): vai na **connection string da aplicação**. Num banco novo,
  conceda os grants (o dump `--no-owner` não os traz):
  ```sql
  GRANT CONNECT ON DATABASE livre_mente_prod TO livremente_app;
  GRANT USAGE ON SCHEMA public TO livremente_app;
  GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO livremente_app;
  ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO livremente_app;
  GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO livremente_app;
  ```

**Recomendação:** criar um **`livre_mente_prod` separado** (mesmo servidor, outro database) e
popular com o Importer, para não misturar dados de teste com a demonstração.

### Passo 3 — Key Vault + segredos
1. Crie um **Key Vault** no mesmo RG/região.
2. Guarde como *secrets*: `ConnectionStrings--DefaultConnection`, `Jwt--Key`,
   `Email--Acs--ConnectionString` (a connection string do recurso ACS — ver Passo 5).
   **RBAC:** criar o cofre com autorização RBAC **não** dá permissão de dados; atribua a si
   mesma a função **Key Vault Secrets Officer** no cofre antes de gravar segredos (`az role
   assignment create ... --role "Key Vault Secrets Officer"`), e aguarde a propagação.
3. No App Service, ligue a **Managed Identity** e dê a ela acesso de leitura ao Key Vault
   (RBAC `Key Vault Secrets User`).
4. Referencie nas App Settings com `@Microsoft.KeyVault(SecretUri=...)` — nunca em claro.

> Caminho mínimo (TCC): pular o Key Vault e pôr os valores direto nas **App Settings**. Menos
> seguro, aceitável para protótipo. Em ambos os casos: **nada de segredo no `appsettings.json`
> nem commitado.**

### Passo 4 — App Service (back-end .NET 10)
1. Crie um **App Service Plan Linux B1** e um **Web App**.
2. **Pilha de runtime: `.NET 10 (LTS)` / Linux.**
   - O código está em **.NET 10** (migrado do 9, que é STS e saiu de suporte). O portal **só
     oferece .NET 10/11** — por isso a migração; ver a entrada no `AGENTS.md`.
   - *Pegadinha:* um app .NET 9 *framework-dependent* **não inicia** num stack .NET 10 (procura
     o runtime 9). Com o projeto já em net10, isso está resolvido.
3. **Configuração obrigatória (Configuration → General settings):**
   - **Always On = On** — sem isso o App Service descarrega o processo ocioso e os
     `BackgroundService` (construtor do índice, cache warmer) parariam.
   - Stack = **.NET 10**, HTTPS Only = On.
4. **App Settings (variáveis de ambiente — aninhamento com `__`):**
   - `ConnectionStrings__DefaultConnection` → referência ao Key Vault
   - `Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience`
   - `Cors__AllowedOrigins__0` → URL do front (SWA) — ver Passo 8
   - `App__PublicApiBaseUrl` → **URL pública desta API** (App Service). ⚠️ O link do e-mail de
     confirmação é `{App:PublicApiBaseUrl}/api/auth/confirm?token=...` (`AuthService`); o default
     é `http://localhost:5091`, então **sem isto o link aponta para localhost e a conta não confirma**.
   - `App__FrontendBaseUrl` → URL do front (SWA); após confirmar, a API redireciona para
     `{App:FrontendBaseUrl}/login?confirmed=1` (`AuthEndpoints`)
   - `Email__Provider=Acs` + `Email__Acs__ConnectionString` (Key Vault) + `Email__Acs__FromAddress` — ver Passo 5
   - `Support__Email` (destino do formulário de contato)
   - `Recommendation__IndexCachePath` → default (`App_Data/...` em `/home`, persistente) **ou**
     `/home/site/recommendation-index`
   - `ASPNETCORE_ENVIRONMENT=Production`
5. **Deploy do código.** Sem Dockerfile, publique o resultado de `dotnet publish -c Release`
   direto (via GitHub Actions no Passo 9, ou `az webapp deploy` para o primeiro teste). Não
   precisa containerizar.
6. **Índice de recomendação em disco:** o `/home` do App Service é **persistente**, então o
   `RecommendationIndexStore` recarrega o `model.zip`/`index.bin` sem refazer o TF-IDF. Se o
   cache sumir (1ª subida, troca de plano), o builder apenas **reconstrói no startup**
   (best-effort) e repersiste — comportamento já previsto no código.

### Passo 5 — E-mail (confirmação de cadastro, suporte) — Azure Communication Services

> **Entrega real** (usuários reais recebem o e-mail). O Mailtrap **Sandbox** só *captura* e
> não entrega — não serve para produção. Escolhido o **ACS Email via SDK** (`Email:Provider=Acs`,
> `AcsEmailSender`): entrega sem domínio próprio e **sem SMTP/registro de app no Entra** —
> autentica por uma única connection string. Código já implementado (ver `AGENTS.md`).

**Fase 1 — Criar os recursos e o domínio grátis (portal):**
1. **Email Communication Services** → Criar (RG `livremente-rg`, ex. `livremente-email`; *Data
   location* = a permitida mais próxima — só metadados).
2. Dentro dele → **Provision domains → Add domain → "Azure domain"** (grátis, 1 clique;
   auto-verifica, já traz SPF/DKIM). Anote o remetente `DoNotReply@<guid>.azurecomm.net`
   (= `FromAddress`).
3. **Communication Services** → Criar (RG `livremente-rg`, ex. `livremente-comm`).
4. No `livremente-comm` → **Email → Domains → Connect domain** → selecione o domínio azurecomm.

> **Domínio custom (opcional):** em vez do azurecomm, dá para usar um domínio próprio
> (ex.: `livrementeapp.com.br`) — **Add domain → Custom domain**, verificar por **TXT** e
> autenticar com **SPF (TXT)** + **DKIM (2 CNAME)** no DNS do domínio; depois **Connect
> domain** no `livremente-comm` e o `FromAddress` vira `nao-responda@seudominio`.

**Fase 2 — Configurar o app:**
- No `livremente-comm` → **Keys** → copie a **connection string**.
- Em **MailFrom addresses** do domínio, defina o **Display name = `LivreMente`** (é o nome que
  o destinatário vê; **não** é configurável no código — o SDK do ACS só aceita o endereço do
  remetente, então o nome amigável vem daqui).
- Defina as App Settings (connection string como **segredo** no Key Vault):
  - `Email__Provider=Acs`
  - `Email__Acs__ConnectionString` → segredo `Email--Acs--ConnectionString` no Key Vault
  - `Email__Acs__FromAddress` = o MailFrom (`DoNotReply@<guid>.azurecomm.net` ou `nao-responda@seudominio`, não-secreto)
- Em **dev** continua `Email:Provider=Logging` (imprime o link no log). O `Smtp` (MailKit)
  segue disponível como alternativa.

Gravar a connection string no Key Vault:
```bash
az keyvault secret set --vault-name livremente-kv --name Email--Acs--ConnectionString --value "<connection-string-do-livremente-comm>"
```

### Passo 6 — Application Insights
Crie um recurso **Application Insights** e conecte ao Web App (Configuration → Application
Insights → Enable). Instrumenta requests/falhas e captura os `ILogger` (avisos best-effort do
índice e do proxy).

### Passo 7 — Static Web Apps (front)
1. Crie um **Static Web App** apontando para `amandapellin/livremente`, branch `develop` (ou a de release).
2. Build Vite + pnpm: App location `/` · Output `dist` · Build `pnpm install && pnpm build`.
3. **Fallback de SPA** (rota desconhecida → `index.html`) via `staticwebapp.config.json`
   (`navigationFallback`).
4. **`VITE_API_URL`:**
   - **Opção A (recomendada): proxy/linked backend** — SWA roteia `/api/*` para o App Service;
     o front chama `/api` na **mesma origem**, sem CORS, e `VITE_API_URL` fica vazio (espelha o
     proxy do Vite em dev).
   - **Opção B: CORS** — `VITE_API_URL` = URL do App Service, e o back libera a origem do SWA.

### Passo 8 — Amarrar o contrato front ↔ back
Três pontas (todas configuráveis, sem código):
- **CORS:** `Cors__AllowedOrigins__0` = domínio do SWA (só na Opção B).
- **`App__FrontendBaseUrl`:** = domínio do SWA (redirect do `GET /api/auth/confirm`).
- **`App__PublicApiBaseUrl`:** = URL pública da API (App Service) — usada no link do e-mail de confirmação.
- **`VITE_API_URL`:** conforme a opção A/B.

### Passo 9 — CI/CD com GitHub Actions
- **Front:** o SWA **gera o workflow** no repo do front (build pnpm → deploy); revise
  `VITE_API_URL` como variável do Actions.
- **Back:** App Service → *Deployment Center → GitHub Actions* gera um workflow que faz
  `dotnet publish` do `LivreMente.Api` e publica a cada push na branch escolhida.
  - **O workflow do deploy deve usar o SDK .NET 10** (`setup-dotnet` com `dotnet-version:
    10.0.x`). O CI de build do repo (`backend-ci.yml`) **já foi atualizado** de `9.0.x` → `10.0.x`.
- Autentique o deploy com **publish profile** (ou OIDC federado) como secret do GitHub.

### Passo 10 — Popular o catálogo (Importer)
Console, não serviço web. Opções, da mais simples:
1. **Rodar localmente** apontando a connection string de produção (via User Secrets) — roda uma
   vez, é demorado. Suficiente para o TCC. Precisa do **SDK .NET 10** na máquina.
2. **Azure Container Instance** (ou job pontual) para rodar dentro da Azure.
3. O backfill de títulos (`--clean-titles`) segue o mesmo caminho — DML puro, role da app basta.
> O Importer também mapeia os enums no `UseNpgsql`; confirme a connection string de produção ao rodar.

### Passo 11 — Smoke test pós-deploy
1. Cadastro → **e-mail de confirmação** (link para o domínio do SWA) → confirmar → login (JWT).
2. Catálogo lista, busca e **filtro por área** funcionam.
3. Abrir obra: **proxy de EPUB/PDF e capa** baixa da fonte (valida saída de internet) e o cache
   acelera a 2ª abertura.
4. Recomendações respondem (índice carregado ou cold start por popularidade).
5. Formulário de suporte envia (rate limit/honeypot ativos).
6. **Application Insights:** sem erros de connection string / CORS / e-mail (ACS).

---

## 3. Checklist de variáveis (produção)

```
ConnectionStrings__DefaultConnection   (Key Vault)   Host=...;Database=livre_mente_prod;Username=livremente_app;Password=...;SSL Mode=Require;Trust Server Certificate=true
Jwt__Key / Jwt__Issuer / Jwt__Audience (Key Vault + settings)
Cors__AllowedOrigins__0                = https://<seu-front>.azurestaticapps.net
App__FrontendBaseUrl                   = https://<seu-front>.azurestaticapps.net
App__PublicApiBaseUrl                  = https://<sua-api>.azurewebsites.net   (link do e-mail de confirmação)
Email__Provider                        = Acs
Email__Acs__ConnectionString           (Key Vault: Email--Acs--ConnectionString)
Email__Acs__FromAddress                = DoNotReply@<guid>.azurecomm.net
Support__Email                         = <caixa de suporte>
Recommendation__IndexCachePath         = /home/site/recommendation-index  (persistente)
ASPNETCORE_ENVIRONMENT                 = Production
```
Front (build): `VITE_API_URL` (vazio se usar proxy do SWA).

---

## 4. Pontos de atenção específicos deste sistema
- **Uma instância, escala vertical.** Cache de arquivos e índice são em memória — não habilite
  autoscale horizontal; se faltar RAM, suba o plano (B2/B3), não o nº de instâncias.
- **`Always On` é obrigatório** pelos `BackgroundService`.
- **Índice em `/home`** persiste entre reinícios; no pior caso reconstrói sozinho no startup
  (minutos, best-effort). Sem ação manual.
- **Saída de internet** é essencial: proxy de arquivos (Gutenberg/arXiv), dicionário
  (`api.dictionaryapi.dev` + fallback Wiktionary). Liberada por padrão no App Service.
- **Schema é manual** (Database First). Toda coluna nova exige SQL como **admin antes** do
  código subir; não há migration automática.
- **Runtime .NET 10 (LTS).** O projeto foi migrado do 9 (STS, fora de suporte). Não voltar para
  o 9: o App Service não o oferece e exigiria container só para contornar.
- **Segredos nunca no repo.** Só Key Vault / App Settings. A connection string de dev segue em
  User Secrets local.

---

## 5. Custo aproximado (protótipo)
App Service **B1** (~US$13/mês) · Static Web Apps **Free** · Key Vault (centavos) · Application
Insights (free tier) · ACS Email (por e-mail, baixíssimo) · Postgres **já pago**. O plano **F1
(Free)** derruba o custo do App Service, mas **não tem Always On** — os background services não
ficariam de pé; por isso o **B1 é o piso** recomendado.

---

## 6. Documentos relacionados
- `AGENTS.md` — contexto do projeto, stack (.NET 10), status por issue (inclui a migração 9→10).
- `README.md` — setup do banco, execução, e-mail por ambiente.
