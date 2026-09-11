# LivreMente — Backend

API REST do LivreMente, plataforma de leitura que integra livros em domínio público (Project Gutenberg, via Gutendex) e artigos científicos de acesso aberto (arXiv). Este repositório contém o backend, em ASP.NET Core.

Repositório do frontend: [livremente](https://github.com/amandapellin/livremente)

## Tecnologias

- ASP.NET Core (C#)
- Entity Framework Core + Npgsql (driver PostgreSQL), abordagem **Database First**
- PostgreSQL (hospedado no Azure Database for PostgreSQL Flexible Server)
- Autenticação via JWT
- OpenAPI/Swagger para documentação da API

## Pré-requisitos

- [.NET SDK 8 (LTS)](https://dotnet.microsoft.com/download) ou superior
- Acesso ao servidor PostgreSQL do projeto no Azure (peça a credencial de aplicação a quem já configurou o banco — não é a mesma senha do administrador do Azure)
- [pgAdmin](https://www.pgadmin.org/) (opcional, útil para inspecionar o banco diretamente)

## Configuração do banco de dados

O banco já está criado, com as 13 tabelas do dicionário de dados prontas, porém ainda **sem dados**. A importação inicial do catálogo (Gutendex e arXiv) precisa ser executada manualmente.
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
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=<host>;Port=5432;Database=livremente_db;Username=livremente_app;Password=<senha>;SSL Mode=Require;Trust Server Certificate=true"
```

O `SSL Mode=Require` é obrigatório — o Azure Flexible Server recusa conexões sem SSL.

### 3. Confirme se seu IP está liberado no firewall do Azure

Se a aplicação não conseguir conectar (timeout, não erro de autenticação), o mais provável é seu IP não estar liberado. Confira no portal do Azure, em **Settings > Networking** do recurso do PostgreSQL, e adicione seu IP atual se necessário.

### 4. (Só se o schema do banco mudar) Regenere os modelos

Como o projeto é Database First, os modelos C# são gerados a partir do banco, não o contrário. Se alguma tabela for alterada diretamente no banco, regenere com:

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

## Variáveis/segredos necessários

| Nome | Onde configurar | Descrição |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | User Secrets (dev) / variável de ambiente (produção) | String de conexão do PostgreSQL |
| `Jwt:Key` | User Secrets (dev) / variável de ambiente (produção) | Chave usada para assinar os tokens JWT |
| `Jwt:Issuer` / `Jwt:Audience` | `appsettings.json` (não sensível) | Metadados do token JWT |

## Fluxo de contribuição

- Branches: `feature/rf01-cadastro-usuario` (código do requisito + descrição curta)
- Pull requests devem referenciar a issue correspondente (`Closes #12`) e passar por revisão da outra desenvolvedora antes do merge
- Board de acompanhamento: [Project "Livremente"](https://github.com/users/amandapellin/projects/1)
