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

- **#11 (RF04) Preferências EAV:** `GET`/`PUT /api/users/me/preferences` (autenticado, sob `UserEndpoints`) para os tipos EAV `language`, `content_type`, `knowledge_area` (tabela `user_preference`). `PreferenceService` + `PreferenceValidation` + `UpdatePreferencesRequest`/`UserPreferencesDto`. **Contrato EAV** `{ languages, contentTypes, knowledgeAreas }` (campo ausente = tipo não tocado; lista presente = **substitui** aquele tipo, idempotente). Validação: idiomas `{pt,en,es,fr,ru}`, `content_type ∈ {book,scientific_article}`, `knowledge_area` valida slugs de área contra `PreferenceCatalog.ArticleArchives`. **`knowledge_area` é gravado traduzido em *archives*** (consistente com o #5); o `GET` faz a **tradução reversa** para os slugs de área. RN04 por construção (`/me`). **Follow-up do front: concluído** — o front passou a enviar `{ languages, contentTypes, knowledgeAreas }` (só EAV) a este endpoint e os gêneros de livro à parte ao `/me/genres` (#12); `openapi.json` + cliente orval regenerados.
  **Extensão RF26 (preferências do leitor):** o mesmo endpoint ganhou os campos escalares `theme` (`light|sepia|dark`, nullable), `resumeAuto` e `saveDictionary` (bool, default `true`), gravados em **colunas de `users`** (`reader_theme`, `resume_auto`, `save_dictionary`) — não EAV. **O `PUT` passou a ser merge parcial de verdade**: cada campo é opcional e ausente/`null` = **não tocado** (o perfil grava os chips EAV e a seção "Leitor e interface" grava tema/toggles sem se apagar). `theme` inválido → 400. Migração (admin): `ALTER TABLE users ADD COLUMN reader_theme varchar(10), resume_auto bool DEFAULT true, save_dictionary bool DEFAULT true`. Front: `useReaderPreferences` (back = fonte da verdade + cache `localStorage`, sem flash).

- **#12 (RF04) Preferências de gênero (M:N):** `GET`/`PUT /api/users/me/genres` + `DELETE /api/users/me/genres/{genreId}` (autenticado, sob `UserEndpoints`). Grava os gêneros literários/categorias de livro do usuário na M:N `user_genre`. **Contrato** `{ genres: [slugs] }` (Categorias de livro ∪ Gênero literário). `GenrePreferenceValidation` rejeita slug fora do `PreferenceCatalog.BookGenres` (400). No `PreferenceService`: `ReplaceGenresAsync` traduz slug → `genre.name` (fan-out via `BookGenres`, consistente com o #5) e substitui o conjunto por **diff mínimo** (idempotente; lista vazia limpa); `GetGenresAsync` faz a **tradução reversa** (um slug entra se todos os seus `genre.name` estão presentes — listas do catálogo ~disjuntas); `RemoveGenreAsync` remove uma associação por id (204/404). RN04 por construção (`/me`). **Sem migração** (`user_genre` já existe, PK `user_id, genre_id`). E2E validado contra o banco de dev. Mergeado (PR #57).

**Próximas / dependências conhecidas:**
- **Toggles do leitor (RF26) a serem honrados por épicos futuros:** `users.save_dictionary` e `users.resume_auto` são hoje **apenas persistidos/expostos** pelo `/me/preferences`; nada os consome ainda. ~~Ao implementar o dicionário (**#30 RF07** consulta / **#31**,**#34** RF08 histórico), **só gravar em `word_lookup` quando `save_dictionary = true`**.~~ **(feito na #30)** Ao implementar a retomada de leitura (**#23 RF18**), **só retomar automaticamente quando `resume_auto = true`**.
- Follow-ups: reenvio de confirmação; provedor SMTP real em produção (só configuração).

**Implementado / aguardando revisão e merge:**

- **#22 (RF17/RN08) Salvamento automático da última página (EPUB):** `PUT /api/reading/{publicationId:int}/last-page` `{ lastPageRead }` (autenticado, grupo novo `/api/reading`; RN04 por construção). `IReadingService.SaveLastPageAsync` faz **upsert na estante**: se a obra não está na `shelf`, cria com `status = reading` + `last_page_read`; se está, só atualiza `last_page_read` (respeita `uq_shelf_user_publication`). **Só EPUB** (publicação com `epub_file_url`); artigo/PDF → **422 `PUBLICATION_NOT_EPUB`**; publicação inexistente → 404; `lastPageRead < 0` → 400. **Mapeada a coluna `shelf.status`** (`reading_status_enum`) na entidade `Shelf`/DbContext — o scaffolding a tinha omitido (drift); só mapeamento, **sem migração** (coluna já existe), e também destrava o épico de estante (#32/#33). E2E contra o banco de dev (cria/atualiza sem duplicar; 422/404/400/401). Primeiro endpoint do épico de Leitura.

- **#23 (RF18) Retomada de leitura:** `GET /api/reading/{id}/last-page` (autenticado) → `{ lastPageRead: int|null }`. `ReadingService.GetLastPageAsync` consulta a `shelf` por `(user, publication)`; `null` tanto sem linha de estante quanto com `last_page_read` nulo (= "nunca leu / não salvo"). Mesmo recurso do `PUT` do #22, método `GET`. Identidade resolvida antes de tudo (padrão CodeQL `cs/user-controlled-bypass`). E2E contra o banco de dev (null → salva 77 → 77; sem token 401). **Front:** só retomar automaticamente quando `resume_auto = true` (decisão do leitor); `last_page_read` é inteiro (índice de *location* do epub.js), não CFI.

- **#24 (RF19) Percentual lido:** `PUT /api/reading/{id}/percentage` `{ readPercentage }` (autenticado). `ReadingService.SavePercentageAsync` valida EPUB e faz **upsert atômico** em `shelf.read_percentage` (`ON CONFLICT`, mesma proteção do #22). Valor **0–100** (senão 400, validado no endpoint); não-EPUB → **422 `PUBLICATION_NOT_EPUB`**; inexistente → 404. Identidade antes da validação (padrão CodeQL). Reaproveita o enum `SaveLastPageResult`. E2E contra o banco de dev (50→100 sem duplicar; 150/-1 → 400; artigo → 422). **Follow-up do front:** o `useEpubReader` já calcula o `%` (`locations.percentageFromCfi`); pode enviar junto do save da última página.

- **#25 (RF20) Tempo de sessão de leitura:** modelado na **`reading_session`** (histórico por sessão), **não** em `shelf`. _(Decisão de projeto: a issue dizia `shelf.current_session_time`, mas `reading_session` (user/pub/start/end) é o lugar certo para o histórico e alimenta o calendário RF09; o `current_session_time` foi **revertido**/dropado.)_ Coluna nova `reading_session.active_seconds` (tempo de tela **ativa**, em segundos — RF20 fala em "tela ativa"; `end-start` seria relógio de parede). **`POST /api/reading/{id}/session`** (autenticado; EPUB e PDF) abre a sessão (`start_time = agora`, `active_seconds = 0`) → `{ sessionId }`; 404 se a publicação não existe. **`PUT /api/reading/sessions/{sessionId}`** `{ activeSeconds }` é *heartbeat*/encerramento: grava `active_seconds` e move `end_time` para agora; `< 0` → 400; só a sessão **do próprio usuário** (RN04/anti-IDOR → 404 se não for dele); identidade antes da validação (CodeQL). Migrações (admin): `ALTER TABLE shelf DROP COLUMN current_session_time` e `ALTER TABLE reading_session ADD COLUMN active_seconds integer NOT NULL DEFAULT 0`. E2E contra o banco de dev (abrir livro/artigo, heartbeat 120→300, IDOR→404, histórico). **Follow-up do front:** o `session-timer` já conta o tempo ativo; abrir a sessão ao entrar no leitor e mandar *heartbeats*.

- **#26 (RF21) Marcação manual de página (bookmark):** `PUT /api/reading/{id}/bookmark` `{ bookmarkedPage }` e `GET /api/reading/{id}/bookmark` → `{ bookmarkedPage: int|null }` (autenticado; RN04 por construção). `SaveBookmarkAsync` faz **upsert atômico** (`ON CONFLICT`) em `shelf.bookmarked_page` — cria com `status = reading` se a obra não está na estante; no conflito só atualiza `bookmarked_page` (não toca em `last_page_read`/`read_percentage`/`status`). **EPUB e PDF — sem restrição de formato** (a issue pede explícito; só 404 se a publicação não existe, igual ao #25/StartSession — **não** há 422 de formato). `GetBookmarkAsync` espelha o `GetLastPageAsync` (null sem linha de estante ou `bookmarked_page` nulo). A unidade de "página" é definida pelo front (location do epub.js p/ EPUB; página do PDF). Validação **`bookmarkedPage >= 0`** (0-based, como o `last_page_read`), senão **400 `VALIDATION_ERROR`**. Identidade antes da validação (CodeQL). Retorno `bool`→204/404 (sem reusar `SaveLastPageResult`, pois não há caso `NotEpub`). **GET adicionado** além do PUT pedido na issue, para a marca ser utilizável (saltar à referência — RF21); DELETE (remover marcação) fica como follow-up se necessário. **Migração (admin) — relaxa os CHECKs de `> 0` para `>= 0`** em `shelf_bookmarked_page_check` **e** `shelf_last_page_read_check`: a unidade real é o índice de *location* do epub.js, que é **0-based**, então `> 0` estava errado e impedia salvar a 1ª página (0 → 500) — **conserta de passagem o bug de 1ª página do #22**. E2E contra o banco de dev (EPUB cria 5→atualiza 42 sem duplicar; **PDF** marca 7; limite **0→204**; `-1`→400; inexistente→404; sem marcação→null; sem token→401). **Follow-up do front:** EPUB marca a *location* atual; **PDF usa input manual de número de página** — o leitor nativo do navegador (`<iframe>`) **não expõe a página atual** ao JS, só permite *saltar* via `#page=N` (PDF Open Parameters). "Marcar a página atual" automático no PDF só com troca para pdf.js (fora de escopo). `PUT` para marcar, `GET` para ir à marcação.

- **#27 (RF22/RN07) Criação de grifo (só EPUB):** `POST /api/reading/{id}/highlights` `{ excerpt, epubPosition, color }` → **201** `HighlightDto { id, excerpt, epubPosition, color, createdAt }` e `GET /api/reading/{id}/highlights` → lista (mais recentes primeiro) (autenticado; RN04 por construção). `CreateHighlightAsync` valida **EPUB** (`epub_file_url`) e insere em `highlight`; **RN07**: não-EPUB → **422 `PUBLICATION_NOT_EPUB`**; inexistente → 404. Mapeamento front→banco: `text`→`excerpt`, `cfiRange`→`epub_position` (CFI range do epub.js; valida `<= 255` → 400), `createdAt`←`create_date` (default do banco, lido de volta). **`chapter` e `note` ficam de fora**: `chapter` é cosmético; `note` pertence à tabela `annotation` (RF23/#28). Validação no endpoint: `excerpt` não-vazio, `epubPosition` 1–255, `color ∈ {amarelo,verde,azul,rosa}` (senão **400 `VALIDATION_ERROR`**); identidade antes da validação (CodeQL). Status reusa `SaveLastPageResult` (Saved/PublicationNotFound/NotEpub) via `CreateHighlightResult`. **Migração (admin):** `ALTER TABLE highlight ADD COLUMN color varchar(10) NOT NULL DEFAULT 'amarelo'` + CHECK `highlight_color_check (color IN (...))` — a coluna `color` **não existia**; a issue só citava `excerpt`+`epub_position`, mas o front já escolhe cor por grifo (decisão da autora: persistir a cor). **GET adicionado** além do POST pedido na issue, para renderizar os grifos ao reabrir o leitor; DELETE/PATCH (remover/recolorir) e a **nota (annotation)** ficam para issues próprias (#28 etc.). **Follow-up do front:** ligar `USE_HIGHLIGHTS_API`, repontar de `/api/users/me/reading-progress/{id}/highlights` (mock) para `/api/reading/{id}/highlights`, e adaptar o `id` (int) + `createdAt`. **Follow-up do front: concluído** — `USE_HIGHLIGHTS_API` ligado (create+list), `fetchHighlightsRemote` (GET) semeia ao abrir só quando o local está vazio, `epub-reader` desenha grifos de forma idempotente por `cfiRange`; recolorir/nota/excluir seguem locais (`USE_HIGHLIGHT_MUTATIONS_API = false`) até terem endpoint. Verificado no navegador.

- **#28 (RF23/RN07) Criação de anotação (só EPUB):** `POST /api/reading/{id}/annotations` `{ linkedExcerpt, content, epubPosition }` → **201** `AnnotationDto { id, linkedExcerpt, content, epubPosition, createdAt }` e `GET /api/reading/{id}/annotations` → lista (mais recentes primeiro) (autenticado; RN04 por construção). **Paralelo quase perfeito da #27**: `CreateAnnotationAsync` valida **EPUB** (`epub_file_url`) e insere em `annotation`; **RN07**: não-EPUB → **422 `PUBLICATION_NOT_EPUB`**; inexistente → 404. A `annotation` é **recurso independente** (não exige grifo): `linked_excerpt` (trecho), `content` (nota), `epub_position` (CFI range, valida `<= 255`), `createdAt`←`create_date` (default do banco). Validação no endpoint: `linkedExcerpt`/`content` não-vazios, `epubPosition` 1–255 (senão **400 `VALIDATION_ERROR`**); identidade antes da validação (CodeQL). Status reusa `SaveLastPageResult` via `CreateAnnotationResult`. **Sem migração** — a tabela `annotation` já tem todas as colunas da issue. **GET adicionado** além do POST pedido na issue (renderizar as notas ao reabrir). E2E contra o banco de dev (EPUB 201 com id/createdAt; GET ordenado; PDF → 422; inexistente → 404; content/linkedExcerpt vazios/epubPosition>255 → 400; sem token → 401). **Follow-up do front:** hoje a nota é um campo `note` do `Highlight` (chaveado por `cfiRange`) e o `updateHighlightNoteRemote` dá PATCH no grifo (mock); migrar para **recurso separado**: `POST /api/reading/{id}/annotations` ao salvar nota e `GET` ao abrir, correlacionando com o grifo por `epub_position = cfiRange`. **Follow-up do front: concluído** — `reader-annotations.ts` (GET lista + POST cria); `useHighlights` semeia as notas por `cfiRange` ao abrir e dá POST da anotação só na 1ª nota (editar/limpar local até update/delete); removido o `updateHighlightNoteRemote`. Verificado no navegador.

- **#30 (RF07) Consulta ao dicionário:** `POST /api/dictionary/lookup` `{ word, publicationId }` → **200** `DictionaryEntryDto { word, phonetic?, meanings: [{ partOfSpeech, definitions: [{ definition, example? }] }] }` (autenticado; RN04 por construção). **Recurso próprio** (`DictionaryService`/`IDictionaryService` + `DictionaryEndpoints`, grupo `/api/dictionary`). Fonte **externa**: Free Dictionary API (`api.dictionaryapi.dev`), HttpClient nomeado `dictionary` (BaseAddress `Dictionary:BaseUrl`, default `https://api.dictionaryapi.dev/`, timeout 10s). **Idioma deriva da publicação** (`publication.language` → código da API; suportados en/fr/de/es/it/pt→pt-BR/ru/ja/ko/ar/tr/hi; vazio/não-suportado → fallback **en**). **`word_lookup` guarda só o evento** (palavra + publicação + `consult_date`), **não** o significado. **RF26: só grava em `word_lookup` quando `users.save_dictionary = true`** (desligado → retorna o significado mas não registra). Publicação inexistente → 404; sem verbete → **404 `WORD_NOT_FOUND`** (não grava); falha na fonte → **502 `DICTIONARY_UNAVAILABLE`**; `word` vazio/>100 → 400; identidade antes da validação (CodeQL). Sem restrição de formato (dicionário vale p/ EPUB e PDF). `MapEntry` normaliza a resposta (junta homógrafos, ≤6 acepções, ≤3 definições cada). **Sem migração** (`word_lookup` já existe). E2E contra o banco de dev (lookup en grava 2; `save_dictionary=false` não grava; inexistente/typo→404; publicação→404; vazio/>100→400; sem token→401). **Follow-up do front:** ainda não há UI de dicionário — construir a consulta ao selecionar palavra no leitor (`POST /api/dictionary/lookup`). **#31 (RF08, histórico) vai no mesmo PR, commit à parte.**

- **#31 (RF08) Histórico de palavras consultadas:** `GET /api/dictionary/history?order=recent|oldest` → `WordLookupHistoryDto[] { word, consultedAt, publicationId, publicationTitle }` (autenticado; RN04 — só as consultas do próprio usuário). **Uma entrada por consulta** (não distinct); inclui a **obra de origem** (join `word_lookup`→`publication`, `publicationTitle`); ordenação por `consult_date` (`recent`=desc, padrão; `oldest`=asc; desempate por id). Lista vazia (não erro) quando não há histórico; sem token → 401. `GetHistoryAsync` adicionado ao `DictionaryService`. Só aparecem consultas efetivamente registradas (i.e., bem-sucedidas **e** com `save_dictionary=true` no momento — herdado da #30). **Sem migração.** E2E contra o banco de dev (vazio→[]; recent/oldest; 2 obras distintas com títulos; RN04→[]; 401). **Vai no mesmo PR da #30, commit à parte.** **Follow-up do front:** exibir o histórico no **perfil** (`GET /api/dictionary/history`).

- **#29 (RF24) Listagem combinada de grifos/anotações:** `GET /api/reading/{id}/highlights-annotations` → `ReadingNotesDto { highlights: HighlightDto[], annotations: AnnotationDto[] }` (autenticado; RN04 por construção). **Snapshot de "reabrir obra"** num único round-trip. `GetHighlightsAndAnnotationsAsync` reusa `GetHighlightsAsync` + `GetAnnotationsAsync` (sequencial — DbContext não é thread-safe). **Listas vazias (não erro)** quando não há nada; publicação inexistente/PDF também → 200 com listas vazias (não restringe formato — quem não é EPUB apenas não tem grifos/anotações); sem token → 401. **Nota:** os GETs por recurso (`/highlights`, `/annotations`) das #27/#28 continuam (o front os consome); este endpoint é a alternativa de 1 chamada. RF24 de usuário **já estava entregue** pelo front (exibe os dois ao reabrir via #27/#28); este endpoint formaliza a listagem única. **Sem alteração de esquema.** E2E contra o banco de dev (vazio→`{[],[]}`; com 2 grifos+1 anotação; PDF/inexistente→`{[],[]}`; sem token→401). **Follow-up do front (opcional):** trocar as duas chamadas (`fetchHighlightsRemote` + `fetchAnnotationsRemote`) por esta única.

- **(RF15/RF16) Proxy dos arquivos de leitura (EPUB/PDF):** `GET /api/reading/{id}/epub` e `GET /api/reading/{id}/pdf` (**públicos**; a rota `/leitura` do front não é autenticada). **Motivo:** o front não pode buscar o arquivo direto do Gutenberg/arXiv no navegador — **CORS** (as fontes não enviam `Access-Control-Allow-Origin`) e, no EPUB, o epub.js cai em "modo diretório" (URL sem `.epub` → tenta `META-INF/container.xml`). `ReadingService.GetFileAsync` baixa no **servidor** (sem CORS) via `IHttpClientFactory` (**com retry** — Gutenberg derruba conexões concorrentes) e transmite com `application/epub+zip`/`application/pdf`; 404 (inexistente/sem arquivo), 502 `UPSTREAM_ERROR` (falha na fonte). `AddHttpClient` no `Program.cs`. **Front:** o `epub-reader` aponta para o proxy e o `useEpubReader` abre com `ePub(url, { openAs: 'epub' })` (força modo arquivo); o `pdf-reader` aponta o `<iframe>` para o proxy. Verificado no navegador (Carmen renderiza capa + texto). **Cache + single-flight** (`IMemoryCache`, TTL 30 min, limite ~256 MB; `SemaphoreSlim` por arquivo): uma só busca no upstream mesmo com requisições concorrentes (mata o fetch duplicado do StrictMode e o 502); reaberturas instantâneas.
  **Otimizações (commit à parte):** (1) **EPUB sem imagens** — o proxy transforma a URL do Gutenberg para `.epub.noimages` (ex.: Pride and Prejudice **24,8 MB → 561 KB**), com *fallback* à versão com imagens se o `.noimages` der 404; sem imagens/capa, mas abre rápido. Configurável `Reading:PreferNoImagesEpub` (default `true`). (2) **`PopularBooksCacheWarmer`** (`BackgroundService`): pré-aquece no startup o cache dos N livros mais populares (sequencial, 2s entre buscas, best-effort); `Reading:CacheWarmCount` (default 12, 0 desliga).

- **#20 (RF13) Detalhes de uma publicação:** `GET /api/publications/{id:int}` (público, `.AllowAnonymous()`; não colide com `/search`). `PublicationService.GetByIdAsync` → `PublicationDetailsDto` com metadados (`title`, `authors[]`, `year`, `synopsis`←`summary`, `language`, `source`) + `genres` (slugs, mesma tradução nome→slug do card, reusando `BookGenreSlugs`/`ArticleAreaSlugs`), `coverUrl` (nula p/ artigo), `publicDomain`←`!copyright_flag`, `epubFileUrl`/`pdfFileUrl`/`downloadUrl` (= epub ?? pdf). Inexistente → **404**. Sem alteração de esquema. E2E contra o banco de dev (livro, artigo, 404). **Follow-up do front:** repontar `usePublicationDetails` de `/api/catalog/{id}` (mock) para `GET /api/publications/{id}` e adaptar o shape. _(Nota: `publicDomain` em artigos arXiv deriva de `copyright_flag=false` — impreciso para open-access; refinar se necessário.)_

- **#16 (RN) Filtro de `copyright` na importação do Gutendex:** no `GutendexImporter`, só entram no `publication` obras **não** sob copyright. O campo `copyright` do Gutendex é **tri-estado** — o DTO `GutendexBook.Copyright` virou **`bool?`** (evita `JsonException` quando vem `null`, que abortaria a página inteira). Filtro `if (book.Copyright == true) continue;` → descarta só o estado explicitamente sob copyright; `false` (confirmado) e `null` (indeterminado) **entram** (decisão da autora: manter `null` no catálogo). Regra documentada no código citando a RN #16. `CopyrightFlag = book.Copyright ?? false`. Build aprovado; sem alteração de esquema (coluna `copyright_flag` já existe). Importação real não reexecutada (consome Gutendex/arXiv, demorada).

- **#21 (RF14) Dados resumidos do card — coberta pela #17 (sem novo código):** o `GET /api/publications` já devolve o `PublicationCardDto` com todos os campos do card — capa (`coverUrl`), título, ano, autor (`authors[]`), gênero (`genres[]` slugs), fonte (`source`) + `id` para "mais detalhes". **Capa nula para artigo** na projeção do `ListAsync` (`type == scientific_article ? null : CoverUrl`), sem erro. Verificado por E2E (card de livro com capa; card de artigo com `coverUrl: null`). Fechar referenciando a #17.

- **#17 (RF10) Listagem do catálogo — implementada, aguardando revisão:**
  Issue aberta no GitHub em 07/10/2026; commits `e1923b0` e `899012d`
  publicados na branch `feature10/endpoint-de-listagem-do-catalogo`, sem merge.
  `GET /api/publications` público com `page=1`, `pageSize=10` (limite 50),
  validação 400 `VALIDATION_ERROR` e resposta `CatalogPage` com cards,
  `totalPages` e `counts { all, book, scientific_article }` antes da paginação.
  Cards com ID string, gêneros/áreas traduzidos para slugs do `PreferenceCatalog`
  e capa nula para artigos. Ordem inicial por popularidade, nulos por último,
  desempate por ID. Página fora do total retorna 200 com lista vazia.
  `q` opcional busca título/autor com `unaccent` + `ILIKE`; vazio lista tudo.
  A busca compartilhada com `/search` preserva espaços/hífens e escapa `%`, `_`
  e `\` como literais. `/search` é alias público da listagem: mesmo `CatalogPage`,
  validação, paginação (10 itens, máximo 50) e ordenação. Aceita `q` ou o nome
  antigo `query` (`q` tem precedência); consulta vazia/ausente lista tudo.
  O contrato antigo de `/search` foi substituído; consumidores devem se ajustar.
  `SearchAsync` removido: as duas rotas delegam ao mesmo `ListAsync`.
  Alias validado contra PostgreSQL: respostas iguais com/sem busca, ordenação
  e paginação, precedência de `q` sobre `query` e erro 400 para pageSize=51.
  `sort`: `recent` (ano DESC, nulos por último), `title` (título ASC),
  `popularity` e `relevance` (popularidade DESC, nulos por último).
  Ausente/desconhecido usa relevance; todas as ordens desempatam por ID.
  `counts` calculado após `q` e antes da paginação; filtros ficam na #19.
  Sem alteração de esquema.
  Build Release aprovado (aviso CS8604 preexistente no SMTP). Testes HTTP contra
  PostgreSQL: GET público 200 com 10 cards, contagens/totalPages e IDs string
  conferidos; pageSize=51 retorna 400; page=2147483647 retorna 200 com lista vazia.
  Validação HTTP realizada em 07/10/2026: 24 requisições aprovadas contra
  PostgreSQL de dev, cobrindo busca por título/autor, caixa/acentos, caracteres
  literais, paginação, contagens, cards de artigos, ordenações, rota legada e
  contrato Swagger. O script auxiliar em Node foi removido; testes manuais
  podem ser repetidos pelo Swagger, sem dependência de Node no back-end.
  Follow-up do front pendente (repo indisponível neste workspace): repontar
  `useCatalogSearch` para `/api/publications`, regenerar OpenAPI/orval e desligar
  `USE_CATALOG_MOCK`. O Swagger da API já expõe o novo contrato.

- **(RF-privacidade) Consentimento + exclusão de conta (LGPD):** `GET`/`PUT /api/users/me/consent` e `DELETE /api/users/me` (autenticado, sob `UserEndpoints`; RN04 por construção). `GET` devolve `UserConsentDto { lgpdConsent, marketingConsent, consentedAt }` (`lgpdConsent = lgpd_consented_at IS NOT NULL`, verdadeiro enquanto a conta existir; `consentedAt = lgpd_consented_at`); `PUT { marketingConsent }` altera só o opcional → 200. **Persistência do marketing**: coluna nova `users.marketing_consent` (`boolean NOT NULL DEFAULT false`, aplicada por admin) — o `register` agora grava o `MarketingConsent` que já recebia (fecha o follow-up). `DELETE /me` (direito à eliminação) apaga, numa transação, todas as tabelas filhas (`user_genre` via SQL — join sem entidade — e `user_preference`/`refresh_token`/`user_avatar`/`annotation`/`highlight`/`word_lookup`/`reading_session`/`shelf` via `ExecuteDelete`) e o usuário → 204; inexistente → 404. E2E validado contra o banco de dev. **Follow-up do front:** desligar `USE_PRIVACY_MOCK` e regenerar o cliente orval.

**Concluído — autenticação e busca (status remoto conferido em 07/10/2026):**

- **#13 (RF29) Logout — fechada, merge no PR #58:** `POST /api/auth/logout` exige JWT e recebe
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
  Testes HTTP com banco em 02/10/2026: logout autenticado 204; refresh após
  logout 401 `INVALID_REFRESH_TOKEN`; logout sem JWT 401.
  Pendente: regeneração do cliente do front
  (repositório do front indisponível neste workspace).

- **#18 (RF11) Busca por palavra-chave — fechada, merge no PR #52:** implementado
  `GET /api/publications/search?query=...&page=1&pageSize=20`.
  Pesquisa título e autores, ignorando maiúsculas/minúsculas e
  acentuação com `ILIKE` e `unaccent`. Reutiliza
  `PagedResult<PublicationSummaryDto>`, com paginação e tratamento
  de caracteres especiais. Script em
  `docs/sql/issue_18_enable_unaccent.sql` (executar como admin;
  requer UNACCENT permitido em azure.extensions no Azure).
  Rota legada retestada contra PostgreSQL em 07/10/2026, junto à #17;
  build Release aprovado. A #17 compartilha a busca com `q` na rota canônica
  e corrige a preservação de espaços e caracteres literais (aguarda merge).
  Pendente: atualização do OpenAPI/cliente do front.

## 10. Documentos relacionados

- `README.md` — setup do banco, execução, e-mail por ambiente, padrão de commits.
- `docs/desenvolvimento.md` — texto de metodologia do artigo (3.1 Tecnologias), também vivo.
