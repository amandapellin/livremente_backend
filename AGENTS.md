# Contexto do Projeto — LivreMente (para agentes de IA)

> Arquivo de contexto para qualquer agente de IA continuar a implementação **sem
> precisar de contexto adicional**. É um documento **vivo**: atualize a seção
> "Status por issue" e o que for pertinente **a cada issue concluída**.
> Não contém segredos (senhas/tokens ficam em User Secrets / variáveis de ambiente).

## 1. O que é o projeto

LivreMente é uma **plataforma web de leitura** que reúne, num único acervo,
livros de domínio público (Project Gutenberg, via API **Gutendex**) e artigos
científicos de acesso aberto (**arXiv**). O usuário se cadastra, informa
preferências de leitura e recebe recomendações; lê obras (EPUB/PDF) e registra
grifos, anotações, progresso e consultas ao dicionário.

O sistema tem **dois repositórios** integrados por contrato OpenAPI:

| Parte | Repositório | Caminho local | Branch padrão |
|---|---|---|---|
| Back-end (API REST) | `amandapellin/livremente_backend` | `/home/amanda/Documentos/livremente_backend` | `main` (integração em `develop`) |
| Front-end (SPA) | `amandapellin/livremente` | `/home/amanda/WebstormProjects/livremente` | `develop` |

Acompanhamento por *issues* no GitHub + quadro de projeto. Requisitos são
referenciados por código: **RFxx** (funcional), **RNxx** (regra de negócio).

## 2. Stack

**Back-end:** C# / .NET 9 · ASP.NET Core **Minimal API** · EF Core 9 + **Npgsql**
(PostgreSQL) · **Database First** · Swagger/OpenAPI · BCrypt.Net-Next (hash de
senha) · MailKit (SMTP). Projeto de importação à parte: `LivreMente.Importer`
(console).

**Front-end:** React + TypeScript · Vite · Material UI (MUI) · React Router ·
React Hook Form + Zod · TanStack Query · **orval** (gera cliente HTTP a partir do
`openapi.json`) · pnpm.

**Banco:** PostgreSQL no **Azure Database for PostgreSQL (Flexible Server)**.
Host `livremente.postgres.database.azure.com`, database `livre_mente_dev`,
role de aplicação `livremente_app`. **Credenciais só em User Secrets** (nunca no
`appsettings.json`).

## 3. Arquitetura e convenções (back-end)

- **Camadas (Layered pragmático):** `Dtos/` (contrato de entrada/saída) →
  `Services/` (regra de negócio + acesso a dados) → `Endpoints/` (handlers finos
  que só delegam). Regras: endpoint **nunca** acessa `DbContext` direto; entidade
  EF **nunca** vaza na resposta (sempre via DTO).
- **Padrões com parcimônia:** só se adota um padrão quando ele se paga. Já em uso:
  *Strategy* para `IPasswordHasher` e `IEmailSender`. **Não** usar Repository/UoW
  (o EF já é isso), MediatR/CQRS, FluentValidation — a menos que haja necessidade
  real e justificada.
- **Endpoints:** Minimal API agrupada por recurso (`MapGroup("/api/...")`), um
  arquivo por recurso em `Endpoints/`, registrado no `Program.cs`.
- **Pastas relevantes:** `Models/` (entidades + `LivreMenteDbContext` + `Enums/`),
  `Mappings/PreferenceCatalog.cs`, `Security/`, `Email/`, `Validation/`.

**Front-end:** *contract-first*. O back-end publica o OpenAPI; o front regenera o
cliente com orval (tipos + hooks + schemas Zod). Ao mudar o contrato do back,
**regenerar o cliente do front**.

## 4. Banco de dados — conhecimento operacional crítico

- **Database First:** o esquema é definido **no banco** (SQL manual); as classes
  são geradas/ajustadas a partir dele. **Não há EF Migrations.**
- **Papéis e permissões:** `livremente_app` tem **apenas DML** (SELECT/INSERT/
  UPDATE/DELETE) — **não pode** `CREATE`/`ALTER`/`DROP` nem alterar tipos enum.
  **Toda mudança de esquema exige o admin/owner** (`livremente_admin`). Ao
  precisar de mudança de esquema: **entregue o SQL para a pessoa rodar como
  admin**, não tente aplicar com o role da aplicação.
- **Enums nativos do PostgreSQL:** mapeados em **dois lugares** que precisam estar
  sincronizados: `modelBuilder.HasPostgresEnum<T>("nome_enum")` no `OnModelCreating`
  **e** `o.MapEnum<T>("nome_enum")` dentro de `UseNpgsql(...)` no `Program.cs`
  (e no Importer). No EF Core 9, **o `MapEnum` no nível do `UseNpgsql` é
  obrigatório** — sem ele o enum é enviado como inteiro e falha.
- **Timestamps:** as colunas são `timestamp without time zone`. O Npgsql **recusa**
  `DateTime` com `Kind=Utc` nessas colunas — use `DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)` (ver helper `Now()` em `AuthService`).
- **Nomes de coluna:** o `DbContext` scaffolded já teve *drift* de nomes vs. o banco
  recriado (ex.: `created_at` → `create_date`). Ao inserir numa tabela nova, confira
  se os `HasColumnName` batem com o banco real.
- **Conexão em dev:** string em User Secrets (`ConnectionStrings:DefaultConnection`).
  Para `psql` sem senha inline, use `~/.pgpass`.

## 5. Modelo de domínio (essencial)

Entidades: `User`, `Publication` (antes "Material" — **renomeado**; tabela
`publication`), `Author`, `Genre`, `UserPreference` (EAV), `Shelf`,
`ReadingSession`, `Highlight`, `Annotation`, `WordLookup`.
Relações M:N: `publication_genre`, `publication_author`, **`user_genre`** (não
"user_genre_preference") e `user_genre` para preferências de gênero do usuário.

Enums (nativos): `Gender` (`female,male,non_binary,other,prefer_not_to_say`),
`PublicationType` (`book,scientific_article`), `PublicationSource`
(`gutendex,arxiv`), `PreferenceType` (`language,knowledge_area,content_type`),
`ReadingStatus` (`read,reading,want_to_read,abandoned`).

**Preferências (EAV + M:N):** idioma e área/tipo ficam em `user_preference`
(tipo+valor); gênero literário fica na M:N `user_genre`. O
**`Mappings/PreferenceCatalog.cs`** traduz os *slugs* do front para o vocabulário
das fontes: `BookGenres` (slug → nomes de *bookshelves* do Gutendex) e
`ArticleArchives` (slug → *archives* do arXiv). **As chaves do catálogo devem
corresponder aos `value` das opções do front.**

## 6. Contrato front ↔ back (pontos que já causaram atrito)

- Cadastro: **`POST /api/auth/register`** (não `/api/users`).
- Campo de tipo de obra no payload de preferências: **`publications`** (não
  "materials"), com valores **`book` / `scientific_article`** (singular).
- Respostas de erro: `{ message, code }` (ex.: `code: "EMAIL_ALREADY_EXISTS"`).
- Confirmação de e-mail: o back **redireciona** para `{FrontendBaseUrl}/login?confirmed=1|invalid`.
- **Enums serializam como string** (global): `JsonStringEnumConverter` em `ConfigureHttpJsonOptions` (`Program.cs`). O valor JSON é o **nome do membro** do enum (`book`, `scientific_article`, `want_to_read`…), que casa com os slugs do contrato — vale para entrada e saída, e o Swashbuckle documenta como `type: string`. Ao expor um enum cru num DTO (Estante/Catálogo), regenerar o `openapi.json` do front.

## 7. Configuração / execução

- **Rodar a API (dev, HTTP puro para evitar redirect HTTPS):**
  `cd LivreMente.Api && dotnet run --urls http://localhost:5091`
- **Front:** `pnpm dev` (Vite em `localhost:5173`); há **proxy** de `/api` para
  `http://localhost:5091` no `vite.config.ts` (sem CORS).
- **E-mail:** `Email:Provider` = `Logging` (dev, imprime o link no log) ou `Smtp`
  (real, via MailKit); credenciais SMTP em `Email:Smtp:*` (User Secrets / env).
  Ver `README.md` → "Configuração de e-mail".
- **Importar catálogo:** `cd LivreMente.Importer && dotnet run` (demorado; ver README).

## 8. Convenções de contribuição

- **Commits:** Conventional Commits em português, no imperativo. Escopos:
  `auth`, `catalog`, `reading`, `shelf`, `recommendation`, `setup`.
  Ex.: `feat(auth): implementa endpoint de cadastro de usuário`.
- **Branches:** `feat/<n>-descricao` (n = número da issue).
- **PRs:** referenciam a issue (`Closes #n`); template em `.github/pull_request_template.md`.
- **Segurança:** nunca commitar segredos; connection string e credenciais só em
  User Secrets / variáveis de ambiente.

## 9. Status por issue (ATUALIZAR A CADA ISSUE)

**Concluído:**
- Setup: API, EF Core/Npgsql, PostgreSQL/Azure, Swagger, CORS, pipeline de CI.
- Importação do catálogo (Gutendex + arXiv) via `LivreMente.Importer`.
- Endpoints de gênero (`GET /api/genres`, `/{id}`, `/{id}/publications`) — molde da arquitetura em camadas.
- **#5 (RF01) Cadastro:** `POST /api/auth/register` composto (usuário + preferências em uma transação), BCrypt, tradução via `PreferenceCatalog`, 201/409/400. Usuário nasce **não confirmado**.
- **#6 (RN01) Confirmação de e-mail:** token com *hash* + expiração (colunas em `users`: `email_confirmation_token_hash`, `email_confirmation_expires_at`, `email_confirmed_at`), `IEmailSender` (Logging/SMTP-MailKit), `GET /api/auth/confirm` redireciona ao front. (Refatorado: removida a tabela `email_confirmation`).
- **#7 (RN03) Consentimento LGPD:** o cadastro exige `lgpdConsent = true` (validação → 400) e registra a data/hora do aceite em `users.lgpd_consented_at`. Migração em `docs/sql/issue_7_add_lgpd_consent.sql` (rodar como admin).
- **#8 (RF02) Login com JWT + #9 (refresh):** `POST /api/auth/login` valida senha (`Verify`), rejeita conta não confirmada (**403 `EMAIL_NOT_CONFIRMED`**), emite JWT (HS256, `IJwtTokenService`) + refresh token persistido (tabela `refresh_token`, só o hash; TTL por `rememberMe`). `POST /api/auth/refresh` rotaciona (revoga o usado, emite novo). Credencial inválida → **401 genérico** (anti-enumeração por timing). Middleware `AddJwtBearer` habilitado (`[Authorize]` disponível). `Jwt:Key` em User Secrets; config `Jwt:*` no appsettings. Migração em `docs/sql/issue_8_add_refresh_token.sql`. Helper de token opaco renomeado `ConfirmationTokens` → `OpaqueTokens` (reusado por confirmação e refresh).
- **#10 (RF03) Edição de perfil:** `UserService` + `UserEndpoints` sob `/api/users` com `RequireAuthorization()`. `GET /api/users/me` (inclui `avatarUrl`) e `PUT /api/users/me` (só o nome; e-mail somente-leitura) → 200 `UserProfileDto`. `PATCH /api/users/me/password` (senha atual + nova ≥ 8) → 204; atual incorreta → 422. **Avatar:** `PUT /api/users/me/avatar` (multipart, PNG/JPG por *magic bytes*, ≤ 2 MB, senão 422) grava bytes na tabela `user_avatar` (1:1); `GET /api/users/{id}/avatar` é **público** (`.AllowAnonymous()`) e serve a imagem. Identidade do claim `sub` (`ClaimsPrincipalExtensions.GetUserId`, `MapInboundClaims = false`) — **RN04 por construção** (sem `{id}`, sem IDOR). Migração do avatar em `docs/sql/issue_10_add_user_avatar.sql` (admin). Swagger com botão **Authorize** (JWT, `AddSecurityDefinition`). **Fora**: troca de e-mail (é somente-exibição).

- **#11 (RF04) Preferências EAV:** `GET`/`PUT /api/users/me/preferences` (autenticado, sob `UserEndpoints`) para os tipos EAV `language`, `content_type`, `knowledge_area` (tabela `user_preference`). `PreferenceService` + `PreferenceValidation` + `UpdatePreferencesRequest`/`UserPreferencesDto`. **Contrato EAV** `{ languages, contentTypes, knowledgeAreas }` (campo ausente = tipo não tocado; lista presente = **substitui** aquele tipo, idempotente). Validação: idiomas `{pt,en,es,fr,ru}`, `content_type ∈ {book,scientific_article}`, `knowledge_area` valida slugs de área contra `PreferenceCatalog.ArticleArchives`. **`knowledge_area` é gravado traduzido em *archives*** (consistente com o #5); o `GET` faz a **tradução reversa** para os slugs de área. RN04 por construção (`/me`). **Follow-up do front:** o contrato do front (`/me/preferences`) ainda envia `{ languages, publications, categories, literaryGenres }` — precisa passar a enviar `{ languages, contentTypes, knowledgeAreas }` (só EAV) e mandar os gêneros de livro à parte (#12).

**Próximas / dependências conhecidas:**
- **#12 (RF04) Preferências de gênero (M:N):** endpoint para os gêneros literários/categorias de livro do usuário (`user_genre`), reaproveitando a resolução de slugs → linhas de `genre` do `AuthService.ApplyPreferences`.
- Follow-ups: reenvio de confirmação; provedor SMTP real em produção (só configuração); persistência do consentimento de *marketing* (opt-in opcional, ainda não gravado).

**Implementado / aguardando revisão e merge:**

- **#29 (RF29) Logout:** `POST /api/auth/logout` exige JWT e recebe
  `{ refreshToken }`. Revoga somente o token informado pertencente ao usuário
  autenticado, preenchendo `refresh_token.revoked_at`; o refresh subsequente
  com esse token retorna 401 `INVALID_REFRESH_TOKEN`. Retorna 204 inclusive
  para token inexistente, de outro usuário ou já revogado (sem alterar outras
  sessões); token vazio retorna 400 `VALIDATION_ERROR`. Sem alteração de esquema.
  O JWT de acesso permanece válido até expirar; o front deve apagar ambos os
  tokens e enviar o refresh mais recente, sem renovação simultânea ao logout.
  A condição de corrida preexistente entre refresh e logout não foi resolvida.
  Contrato exposto pelo Swagger com respostas 204/400/401.
  Build Release da API aprovado (avisos NU1900 de acesso ao NuGet e CS8604
  preexistente no SMTP); `git diff --check` aprovado.
  Pendentes: testes HTTP com banco e regeneração do cliente do front
  (repositório do front indisponível neste workspace).

- **#18 (RF11) Busca por palavra-chave:** implementado
  `GET /api/publications/search?query=...&page=1&pageSize=20`.
  Pesquisa título e autores, ignorando maiúsculas/minúsculas e
  acentuação com `ILIKE` e `unaccent`. Reutiliza
  `PagedResult<PublicationSummaryDto>`, com paginação e tratamento
  de caracteres especiais. Script em
  `docs/sql/issue_18_enable_unaccent.sql` (executar como admin;
  requer UNACCENT permitido em azure.extensions no Azure).
  Testes HTTP manuais realizados antes da integração de develop;
  build Release aprovado após a integração.
  Pendentes: atualização do OpenAPI/cliente do front e repetição
  dos testes HTTP após reiniciar a API com o código integrado.

## 10. Documentos relacionados

- `README.md` — setup do banco, execução, e-mail por ambiente, padrão de commits.
- `docs/desenvolvimento.md` — texto de metodologia do artigo (3.1 Tecnologias), também vivo.
