# LivreMente — Backend

API REST do LivreMente, plataforma de leitura que integra livros em domínio público (Project Gutenberg, via Gutendex) e artigos científicos de acesso aberto (arXiv). Este repositório contém o backend, em ASP.NET Core.

Repositório do frontend: [livremente](https://github.com/amandapellin/livremente)

## Tecnologias

- ASP.NET Core Minimal API (C#, **.NET 10 LTS**)
- Entity Framework Core + Npgsql (driver PostgreSQL), abordagem **Database First**
- PostgreSQL (hospedado no Azure Database for PostgreSQL Flexible Server)
- Autenticação via JWT
- Envio de e-mail abstraído por `IEmailSender`: Azure Communication Services (produção), SMTP ou log (desenvolvimento)
- OpenAPI/Swagger para documentação da API
- Hospedagem em produção no Azure App Service, com CI/CD por GitHub Actions (ver seção [Deploy](#deploy-produção))

## Pré-requisitos

- [.NET SDK 10 (LTS)](https://dotnet.microsoft.com/download) ou superior
- Acesso ao servidor PostgreSQL do projeto no Azure (peça a credencial de aplicação a quem já configurou o banco — não é a mesma senha do administrador do Azure)
- [pgAdmin](https://www.pgadmin.org/) (opcional, útil para inspecionar o banco diretamente)

## Configuração do banco de dados

O banco já está criado, com as tabelas do dicionário de dados prontas, porém ainda **sem dados**. A importação inicial do catálogo (Gutendex e arXiv) precisa ser executada manualmente.
### 1. Obtenha as credenciais de conexão

Você vai precisar de:
- **Host**: endereço do servidor Azure (`livremente.postgres.database.azure.com`)
- **Porta**: `5432`
- **Nome do banco**: `livre_mente_dev`
- **Usuário**: o role de aplicação (`livremente_app`), **não** o usuário administrador do Azure (`livremente_admin`)
- **Senha**: a senha do role de aplicação

Se você não tem essas credenciais, peça a quem configurou o servidor — elas não estão neste repositório por segurança.

### 2. Configure a connection string via User Secrets

**Nunca coloque a connection string no `appsettings.json`.** Use User Secrets para desenvolvimento local:

```bash
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=<host>;Port=5432;Database=livre_mente_dev;Username=livremente_app;Password=<senha>;SSL Mode=Require;Trust Server Certificate=true"
```

O `SSL Mode=Require` é obrigatório — o Azure Flexible Server recusa conexões sem SSL.

### 3. Confirme se seu IP está liberado no firewall do Azure

Se a aplicação não conseguir conectar (timeout, não erro de autenticação), o mais provável é seu IP não estar liberado. Confira no portal do Azure, em **Settings > Networking** do recurso do PostgreSQL, e adicione seu IP atual se necessário.

### 4. (Só se o schema do banco mudar) Aplique a migração e regenere os modelos

Mudanças de esquema são versionadas como **scripts SQL em `docs/sql/`** e aplicadas pelo **administrador/owner** do banco (`livremente_admin`) — o role de aplicação (`livremente_app`) tem apenas permissões de dados (DML) e **não pode** alterar o esquema (`CREATE`/`ALTER`/`DROP`, inclusive tipos enum). Exemplo:

```bash
psql "host=<host> port=5432 dbname=livre_mente_dev user=livremente_admin sslmode=require" -f docs/sql/<script>.sql
```

Como o projeto é Database First, os modelos C# são gerados a partir do banco, não o contrário. Depois de aplicar a mudança, regenere com:

```bash
dotnet ef dbcontext scaffold "Name=ConnectionStrings:DefaultConnection" Npgsql.EntityFrameworkCore.PostgreSQL -o Models --context LivreMenteDbContext --force
```

O `--force` sobrescreve os modelos existentes — cuidado se você fez alterações manuais neles.

## Rodando o projeto localmente

```bash
dotnet restore
dotnet run
```

A API sobe, por padrão, em `https://localhost:<porta>` (a porta exata aparece no console ao rodar). A documentação interativa (Swagger UI) fica disponível em `/swagger` em ambiente de desenvolvimento.

## Principais endpoints

Visão geral por recurso. O Swagger (`/swagger`) é a referência completa e sempre atual; o **status de implementação por issue** (o que já está mergeado vs. em revisão) fica no `AGENTS.md`, seção "Status por issue".

| Grupo | Endpoints | Observações |
|---|---|---|
| **Autenticação** (`/api/auth`) | `POST /register`, `GET /confirm`, `POST /login`, `POST /refresh`, `POST /logout` | Cadastro com preferências em transação; confirmação por e-mail; JWT + *refresh token* rotacionado |
| **Usuário** (`/api/users/me`, autenticado) | `GET`/`PUT /me`, `PATCH /me/password`, `PUT /me/avatar`, `GET`/`PUT /me/preferences`, `GET`/`PUT /me/genres` + `DELETE /me/genres/{id}`, `GET`/`PUT /me/consent`, `DELETE /me` | Identidade lida do JWT (RN04 por construção); preferências cobrem EAV (idioma/tipo/área), gênero (M:N) e leitor (tema/*toggles*); consentimento e exclusão de conta (LGPD) |
| **Avatar público** (`/api/users/{id}/avatar`) | `GET` | Serve a imagem (anônimo) |
| **Gêneros** (`/api/genres`) | `GET /`, `GET /{id}`, `GET /{id}/publications` | Catálogo de gêneros e publicações por gênero |
| **Publicações/Catálogo** (`/api/publications`) | `GET /` (listagem com busca/paginação/*counts*), `GET /search` (alias), `GET /{id}`, `GET /{id}/cover` | Busca por título/autor com `unaccent`; filtros e ordenação; detalhes da obra; proxy de capa |
| **Leitura** (`/api/reading`, autenticado) | `GET /{id}/epub` e `/pdf` (proxy), progresso (`last-page`, `percentage`, `bookmark`), sessões (`session`), grifos/anotações (`highlights`, `annotations`), `calendar`, `stats` | Proxy *same-origin* dos arquivos (EPUB/PDF) com cache; salvamento de posição, grifos, anotações, tempo de leitura e frequência |
| **Estante** (`/api/shelf`, autenticado) | `GET /` (filtro por estado), `PUT /{id}` (estado), `DELETE /{id}` | Organização das obras por estado (RF05/RF06) |
| **Dicionário** (`/api/dictionary`, autenticado) | `POST /lookup`, `GET /history` | Consulta a fonte externa; histórico opcional de palavras |
| **Recomendações** (`/api/recommendations`, autenticado) | `GET /` | Recomendação *content-based* (TF-IDF) com *cold start* por popularidade |
| **Suporte** (`/api/support`) | `POST /contact` | Formulário de contato (público, com *rate limit* e honeypot) |

## Variáveis/segredos necessários

| Nome | Onde configurar | Descrição |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | User Secrets (dev) / variável de ambiente (produção) | String de conexão do PostgreSQL |
| `Jwt:Key` | User Secrets (dev) / variável de ambiente (produção) | Chave usada para assinar os tokens JWT |
| `Jwt:Issuer` / `Jwt:Audience` | `appsettings.json` (não sensível) | Metadados do token JWT |
| `Email:Provider` | `appsettings.json` (dev: `Logging`) / variável de ambiente (prod: `Acs`) | Seleciona o provedor de e-mail sem mudar código: `Acs`, `Smtp` ou `Logging` |
| `Email:Acs:ConnectionString` | Key Vault (prod) | Connection string do Azure Communication Services quando `Email:Provider = Acs` |
| `Email:Acs:FromAddress` | variável de ambiente (prod) | Remetente MailFrom do domínio ACS (ex.: `DoNotReply@<domínio>.azurecomm.net`) |
| `Email:Smtp:Host` / `Port` / `Username` / `Password` / `FromAddress` / `FromName` | User Secrets (dev/staging) / variável de ambiente | Credenciais SMTP quando `Email:Provider = Smtp` |
| `Support:Email` | `appsettings.json` / variável de ambiente | Caixa de destino do formulário de contato (`POST /api/support/contact`) |
| `Cors:AllowedOrigins:0` | variável de ambiente (produção) | Origem do front liberada no CORS (URL do Static Web App) |
| `App:FrontendBaseUrl` / `App:PublicApiBaseUrl` | `appsettings.json` por ambiente | URLs base do link de confirmação e do redirect pós-confirmação |

## Configuração de e-mail (confirmação de cadastro)

O envio de e-mail (RN01) é abstraído por `IEmailSender`; a implementação é escolhida pela chave `Email:Provider` (`Acs`, `Smtp` ou `Logging`), **sem mudança de código** — a mesma build roda em todos os ambientes, mudando só a configuração. Em **produção** o provedor é o **Azure Communication Services (`Acs`)**; em desenvolvimento, `Logging` (padrão) ou `Smtp`. Segredos (connection string do ACS, usuário/senha SMTP) **nunca** vão no `appsettings.json`: use User Secrets em dev e variáveis de ambiente / Key Vault em produção.

> Copie sempre Host/Port/Username/Password **da tela de SMTP do Mailtrap** (é a fonte da verdade; os valores abaixo são o formato esperado).

### 1. Dev (padrão) — sem configurar nada

`Email:Provider = "Logging"` já vem no `appsettings.json`. Nenhum e-mail é enviado: o link de confirmação é **impresso no console/log** (`[E-mail DEV]`). Basta rodar a API e copiar o link do log.

### 2. Dev / Staging — Mailtrap **Email Testing** (sandbox: captura, não entrega)

Ideal para inspecionar o e-mail numa UI sem entregar a ninguém.

1. Crie uma conta no Mailtrap → produto **Email Testing** → um **Inbox**.
2. No inbox, abra **Integrations → SMTP Settings** e copie Host / Port / Username / Password.
3. Configure via User Secrets (dentro de `LivreMente.Api`):

```bash
dotnet user-secrets set "Email:Provider" "Smtp"
dotnet user-secrets set "Email:Smtp:Host" "sandbox.smtp.mailtrap.io"
dotnet user-secrets set "Email:Smtp:Port" "587"
dotnet user-secrets set "Email:Smtp:Username" "<username do inbox>"
dotnet user-secrets set "Email:Smtp:Password" "<password do inbox>"
dotnet user-secrets set "Email:Smtp:FromAddress" "no-reply@livremente.test"
dotnet user-secrets set "Email:Smtp:FromName" "LivreMente"
```

4. Cadastre um usuário: o e-mail aparece no inbox do Mailtrap (no sandbox o `FromAddress` pode ser qualquer um — é apenas capturado).

### 3. Produção — Azure Communication Services (ACS)

Em produção o e-mail é **entregue de verdade** pelo **Azure Communication Services (Email)**, via SDK `Azure.Communication.Email` (`AcsEmailSender`). Ele entrega **sem domínio próprio** (usa um subdomínio `azurecomm` grátis, com SPF/DKIM prontos) e **sem SMTP** — a autenticação é por uma única connection string.

1. No portal do Azure, crie um recurso **Communication Services** e um **Email Communication Service** com o domínio grátis (`azurecomm`); conecte o domínio ao recurso de Communication Services.
2. Guarde a **connection string** do recurso no **Key Vault** (segredo `Email--Acs--ConnectionString`) e referencie-a nas App Settings do App Service.
3. Configure as variáveis de ambiente (no App Service; `__` aninha as chaves):

```bash
Email__Provider=Acs
Email__Acs__ConnectionString=<referência ao segredo do Key Vault>
Email__Acs__FromAddress=DoNotReply@<guid>.azurecomm.net   # MailFrom do domínio ACS
```

4. Ajuste também `App:PublicApiBaseUrl` e `App:FrontendBaseUrl` para as URLs públicas do deploy — senão o link de confirmação e o redirect pós-confirmação apontam para `localhost`.

> O `Email:Acs:FromAddress` é **não sensível** (vai nas App Settings); apenas a connection string é segredo (Key Vault). O passo a passo completo está em [`docs/deploy-azure.md`](docs/deploy-azure.md) (Passo 5). Alternativamente, para entrega real via SMTP, o Mailtrap **Email Sending** também atende com `Email:Provider = Smtp`.

## Deploy (produção)

A aplicação está publicada no Azure, com **CI/CD por GitHub Actions a cada push na `main`** (integração segue na `develop`):

- **API:** Azure App Service (Linux, plano B1, runtime `.NET 10`), *startup* `dotnet LivreMente.Api.dll`.
- **Banco de produção:** `livre_mente_prod` no mesmo PostgreSQL Flexible Server.
- **Segredos:** Azure Key Vault (connection string do banco, `Jwt:Key`, connection string do ACS), referenciados nas App Settings.
- **E-mail:** Azure Communication Services (`Email:Provider=Acs`).
- **Front:** Azure Static Web Apps; o CORS do backend libera a origem do SWA (`Cors__AllowedOrigins__0`).
- **Observabilidade:** Application Insights.

O passo a passo completo (serviços, variáveis de produção e pontos de atenção) está em [`docs/deploy-azure.md`](docs/deploy-azure.md).

## Fluxo de contribuição

- Branches: `feat/<n>-descricao` (n = número da issue) a partir de `develop`
- Pull requests referenciam a issue correspondente (`Closes #N`) e passam por revisão da outra desenvolvedora antes do merge
- A cada pull request, os workflows de CI (GitHub Actions: build e análise de segurança CodeQL) são executados automaticamente
- Integração na `develop`; a `main` é produção e dispara o deploy automático
- Board de acompanhamento: [Project "Livremente"](https://github.com/users/amandapellin/projects/3)

## Populando o catálogo

O catálogo (livros do Gutendex e artigos do arXiv) é importado por um projeto console separado, `LivreMente.Importer`, incluído neste repositório. Ele não precisa ser executado toda vez que a API sobe — só quando o catálogo estiver vazio ou quando vocês quiserem atualizar os dados.

### Pré-requisitos

- User Secrets configurado dentro de `LivreMente.Importer` com a mesma connection string usada na API (veja a seção "Configuração do banco de dados" acima — o Importer tem seu próprio User Secrets, independente do da API).
- Conexão com a internet, já que ele consulta as APIs externas do Gutendex e do arXiv diretamente.

### Executando a importação

```bash
cd LivreMente.Importer
dotnet run
```

O importador roda em duas etapas:

- **Gutendex**: importa o catálogo completo (~79 mil livros), ordenado por popularidade (mais baixados primeiro). O campo `copyright` do Gutendex é tri-estado e o filtro de domínio público (RN #16) **pula apenas os itens com `copyright: true`** (sob copyright); os `false` (domínio público confirmado) e `null` (indeterminado) são importados.
- **arXiv**: importa até 150 artigos de cada uma das 20 áreas de conhecimento do arXiv (astro-ph, cond-mat, cs, econ, eess, gr-qc, hep-ex, hep-lat, hep-ph, hep-th, math, math-ph, nlin, nucl-ex, nucl-th, physics, q-bio, q-fin, quant-ph, stat), usando busca por categoria com wildcard — um total de até ~3.000 artigos, representando todas as áreas do arXiv.

Para ajustar quantidade ou áreas, edite os parâmetros em `LivreMente.Importer/Program.cs`.

### Tempo esperado

A importação **é demorada**, principalmente por causa do Gutendex: com o catálogo completo (sem limite de páginas) e ~2.500 requisições sequenciais, a execução pode levar de uma a poucas horas. O arXiv é mais rápido individualmente, mas soma o intervalo mínimo de 3 segundos entre páginas em cada uma das 20 categorias.

Recomenda-se rodar num momento em que o computador não seja necessário para outra coisa, e não perto de um prazo apertado.

### Rodando de novo sem duplicar

O importador é seguro para rodar mais de uma vez: cada publicação é verificada pelo par `source` + `external_id` antes de ser inserida (é a mesma `UNIQUE (source, external_id)` já definida no schema do banco). Itens já importados são pulados automaticamente, então rodar novamente só traz o que ainda não existe no catálogo — útil, por exemplo, se a importação for interrompida no meio e precisar ser retomada.

### Verificando o resultado

```sql
SELECT source, type, COUNT(*) FROM publication GROUP BY source, type;
```

Esse comando no pgAdmin mostra quantos livros e artigos já foram importados de cada fonte. Para ver a distribuição de artigos por área do conhecimento:

```sql
SELECT knowledge_area, COUNT(*) FROM publication WHERE source = 'arxiv' GROUP BY knowledge_area ORDER BY COUNT(*) DESC;
```
## Padrão de commits

Este projeto segue o padrão [Conventional Commits](https://www.conventionalcommits.org/), adaptado aos épicos já documentados no board do projeto.

### Formato

tipo(escopo): descrição curta no imperativo
Corpo opcional explicando o porquê, não o quê.

`Refs: RFxx, RNxx` e `Closes #N` são opcionais, incluídos apenas quando ajudam a rastrear a mudança até o requisito ou fechar a issue automaticamente.

### Tipos utilizados

| Tipo | Quando usar |
|---|---|
| `feat` | Implementação de uma nova funcionalidade |
| `fix` | Correção de um bug |
| `docs` | Mudança em documentação (README, comentários) |
| `refactor` | Reorganização de código sem mudar comportamento |
| `test` | Criação ou ajuste de testes |
| `chore` | Configuração, dependências, tarefas de manutenção |

### Escopo

O escopo reflete o épico ao qual a mudança pertence: `auth`, `catalog`, `reading`, `shelf`, `recommendation` ou `setup`.

### Regras práticas

- **Um commit, uma mudança lógica.** Evite misturar funcionalidades diferentes num único commit.
- **Imperativo, não passado.** Use `adiciona endpoint`, não `adicionado` ou `adicionei`.
- **Se usar `Closes #N`**, inclua apenas no commit que efetivamente fecha a issue — em branches com vários commits, evite repetir em todos.

### Exemplos

feat(auth): implementa endpoint de cadastro de usuário

fix(catalog): corrige duplicação de gênero durante importação do Gutendex

O .Local não estava sendo consultado antes do banco, causando
violação de UNIQUE em concorrência dentro da mesma página.

chore(setup): configura Npgsql e User Secrets no LivreMente.Api
