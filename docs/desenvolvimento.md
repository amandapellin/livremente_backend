<!--
Documento de apoio à redação do artigo (seção de Metodologia/Desenvolvimento).
Escopo: apenas o tópico "3.1 Tecnologias Utilizadas". Os tópicos "Interface do
aplicativo" e "Repositório do projeto" são tratados em outro material.
Documento VIVO: a cada issue implementada, atualizar a subseção correspondente
e registrar a evolução no apêndice ao final.
-->

# 3. DESENVOLVIMENTO

Esta seção apresenta o aplicativo desenvolvido, sua interface e as tecnologias
empregadas. O presente documento detalha exclusivamente o subtópico **3.1
Tecnologias Utilizadas**; a interface do aplicativo e o repositório do projeto
são descritos em material próprio.

## 3.1 Tecnologias Utilizadas

O LivreMente é uma plataforma web de leitura que reúne, em um único acervo,
livros de domínio público (Project Gutenberg, acessados pela API Gutendex) e
artigos científicos de acesso aberto (repositório arXiv). O sistema foi
estruturado em duas partes independentes e integradas por contrato: uma **API
REST** (back-end), responsável pelas regras de negócio e persistência, e uma
**aplicação de página única (SPA)** (front-end), responsável pela interface.
As subseções a seguir descrevem, para cada tecnologia adotada, tanto sua
natureza quanto o modo como foi empregada na implementação.

### 3.1.1 Linguagem, plataforma e estilo de API

O back-end foi desenvolvido em **C#** sobre o **.NET 9** e o framework
**ASP.NET Core**. Optou-se pelo estilo **Minimal API**, no qual as rotas são
declaradas de forma enxuta e agrupadas por recurso (`MapGroup`), sem o
mecanismo de controladores. Cada manipulador de rota é mantido "fino": recebe a
requisição, delega a lógica a um serviço e traduz o resultado nos códigos HTTP
adequados. Essa abordagem foi escolhida por reduzir cerimônia em uma API de
porte pequeno a médio, mantendo a legibilidade.

### 3.1.2 Documentação e contrato da API (OpenAPI/Swagger)

A documentação da API é gerada automaticamente no padrão **OpenAPI**, com
**Swagger (Swashbuckle)**, disponibilizando uma interface interativa em ambiente
de desenvolvimento. O contrato OpenAPI cumpre, ainda, papel central na
integração entre as camadas: é a partir dele que o cliente HTTP do front-end é
gerado, garantindo que ambos os lados compartilhem os mesmos tipos e rotas.

### 3.1.3 Persistência de dados (PostgreSQL, EF Core e Npgsql)

Os dados são persistidos em um banco relacional **PostgreSQL**, hospedado no
**Azure Database for PostgreSQL (Flexible Server)**. O acesso se dá pelo
**Entity Framework Core 9** com o provedor **Npgsql**, adotando a abordagem
**Database First**: o esquema é definido diretamente no banco e as classes de
modelo são geradas a partir dele. Recursos nativos do PostgreSQL foram
explorados, como os **tipos enumerados (enum)** — mapeados no EF Core por meio
de `HasPostgresEnum` e `MapEnum` — empregados para campos de domínio fechado
(por exemplo, gênero do usuário, tipo de publicação e fonte do acervo). Do ponto
de vista de segurança, adotou-se o princípio do **menor privilégio**,
separando-se o papel de aplicação (com permissões apenas de leitura e escrita de
dados) do papel administrativo (responsável por alterações de esquema).

### 3.1.4 Organização em camadas (arquitetura)

A API foi organizada segundo uma **arquitetura em camadas pragmática**,
composta por três responsabilidades bem delimitadas: objetos de transferência de
dados (*DTOs*), que definem o contrato de entrada e saída sem expor as entidades
de persistência; serviços, que concentram as regras de negócio e o acesso a
dados; e *endpoints*, que apenas orquestram a requisição. Como diretriz
metodológica, padrões de projeto foram adotados com **parcimônia** — somente
quando o benefício justificasse o custo —, evitando-se abstrações desnecessárias
como repositórios sobre o EF Core (que já implementa os padrões *Unit of Work* e
*Repository*).

### 3.1.5 Autenticação e proteção de credenciais (BCrypt)

Para o armazenamento seguro de senhas, utilizou-se o algoritmo **BCrypt**
(biblioteca *BCrypt.Net-Next*), que aplica *hashing* com sal e fator de custo
configurável. O algoritmo foi encapsulado por trás de uma abstração
(`IPasswordHasher`, aplicação do padrão *Strategy*), de modo que sua eventual
substituição não afete as regras de negócio. Essa tecnologia foi introduzida na
implementação do cadastro de usuário (RF01): a senha nunca é armazenada em texto
puro nem retornada nas respostas da API.

No login (RF02), a autenticação emprega **JSON Web Tokens (JWT)**, com a
biblioteca **JwtBearer** do ASP.NET Core. Após validar as credenciais (conferindo
a senha contra o *hash*), o sistema emite um **token de acesso** assinado
(HMAC-SHA256) contendo as *claims* do usuário e uma expiração curta; a geração é
isolada por uma abstração (`IJwtTokenService`). Para manter a sessão sem reenviar
a senha, emite-se também um **refresh token** de longa duração, persistido apenas
como *hash* e **rotacionado** a cada renovação (o token usado é revogado). Por
segurança, credenciais inválidas retornam sempre a mesma resposta genérica (sem
revelar qual campo falhou), e contas ainda não confirmadas são bloqueadas no
login.

### 3.1.6 Envio de e-mail (MailKit e SMTP)

O envio de mensagens de correio eletrônico — utilizado na confirmação de
cadastro (RN01) — foi implementado sobre o protocolo **SMTP** por meio da
biblioteca **MailKit**. O recurso foi igualmente abstraído (`IEmailSender`,
padrão *Strategy*), com duas implementações intercambiáveis por configuração:
uma de desenvolvimento, que apenas registra a mensagem em log, e uma real, que
efetua o envio via SMTP. Assim, a troca entre ambiente de teste (serviço de
captura de e-mails) e ambiente de produção (provedor de envio real) ocorre sem
qualquer alteração de código, apenas por parâmetros de configuração.

### 3.1.7 Integração com fontes de dados externas (Gutendex e arXiv)

O povoamento do acervo é realizado por uma aplicação de console dedicada, que
consome as APIs externas do **Gutendex** (catálogo do Project Gutenberg, em
formato JSON) e do **arXiv** (metadados de artigos, em formato Atom/XML). A
importação trata paginação, repetição em caso de falha e respeito aos limites de
requisição das fontes, além de evitar duplicidades por meio de uma chave única
que combina a fonte e o identificador externo de cada obra. Um dicionário de
tradução converte o vocabulário de categorias exibido ao usuário nos termos
efetivamente utilizados por cada fonte, unificando gêneros de livros e áreas de
conhecimento de artigos.

### 3.1.8 Front-end e geração de cliente a partir do contrato

A interface foi desenvolvida como uma **SPA** em **React** com **TypeScript**,
empacotada pelo **Vite**. A composição visual utiliza a biblioteca de
componentes **Material UI (MUI)**; o gerenciamento de formulários e sua
validação empregam **React Hook Form** e **Zod**; e a comunicação com a API é
mediada pelo **TanStack Query**. Adotou-se abordagem *contract-first*: a partir
do contrato **OpenAPI** da API, a ferramenta **orval** gera automaticamente os
tipos, os *hooks* de requisição e os esquemas de validação, reduzindo a
divergência entre front-end e back-end.

### 3.1.9 Ferramentas de apoio, versionamento e gestão

O código é versionado com **Git** e hospedado no **GitHub**, onde o
acompanhamento do trabalho é feito por *issues* vinculadas a um quadro de
projeto. Segredos de configuração (como a *string* de conexão e credenciais de
e-mail) são mantidos fora do versionamento, por meio do mecanismo de **User
Secrets** em desenvolvimento e de variáveis de ambiente em produção.

---

## Apêndice — Registro de evolução por issue

*Tabela de controle interno (não necessariamente parte do texto final do
artigo). A cada issue concluída, adicionar uma linha relacionando o requisito às
tecnologias empregadas.*

| Issue | Requisito/Regra | Tecnologias e aspectos de implementação |
|---|---|---|
| Setup | — | .NET 9, ASP.NET Core (Minimal API), EF Core 9 + Npgsql, PostgreSQL/Azure, Swagger, CORS |
| Importação do catálogo | — | Aplicação de console; consumo de Gutendex (JSON) e arXiv (Atom/XML); deduplicação por (fonte, id externo) |
| Endpoints de gênero | — | Arquitetura em camadas (DTOs/Services/Endpoints); paginação; `PreferenceCatalog` |
| #5 | RF01 — Cadastro de usuário | BCrypt (`IPasswordHasher`, *Strategy*); transação única (usuário + preferências); enum nativo `gender_enum`; validação e códigos HTTP do contrato |
| #6 | RN01 — Confirmação por e-mail | MailKit/SMTP (`IEmailSender`, *Strategy*); token com *hash* e expiração; confirmação via redirecionamento ao front |
| #7 | RN03 — Consentimento LGPD | Aceite obrigatório validado no cadastro (erro 400 sem consentimento); registro de data/hora em `users.lgpd_consented_at` |
| #8 / #9 | RF02 — Login com JWT (+ refresh) | Autenticação por **JWT** (HS256, `Microsoft.AspNetCore.Authentication.JwtBearer`, `IJwtTokenService` — *Strategy*); senha conferida por *hash*; **refresh token** persistido (só o *hash*) com rotação; 401 genérico (anti-enumeração); gate 403 para conta não confirmada |
| #10 | RF03 — Edição de perfil | Endpoints autenticados (`[Authorize]`) sob `/api/users/me`; identidade do usuário lida do *claim* `sub` do JWT (RN04 por construção, sem IDOR); edição do nome e troca de senha (verifica a senha atual antes de regravar o *hash*); **avatar** enviado por *multipart*, validado por *magic bytes* (PNG/JPG, ≤ 2 MB) e armazenado como `bytea` no banco, servido por endpoint público |
