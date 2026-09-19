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

- [.NET SDK 9 (LTS)](https://dotnet.microsoft.com/download) ou superior
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
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=<host>;Port=5432;Database=livre_mente_dev;Username=livremente_app;Password=<senha>;SSL Mode=Require;Trust Server Certificate=true"
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

- **Gutendex**: importa o catálogo completo (~79 mil livros), ordenado por popularidade (mais baixados primeiro), pulando itens com `copyright: true` (RN de domínio público).
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
