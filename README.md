# IMPA — API (impa-backend)

API do IMPA, sistema de gestão da Impacto Papelaria. ASP.NET Core **.NET 10**, PostgreSQL (EF Core + Npgsql).

## Arquitetura

**Monólito modular** (ADR-001): um processo, módulos com contrato público e schema próprio no banco.
Cada módulo segue Clean Architecture em quatro projetos. Um módulo nunca grava no schema de outro (ADR-012).

```
Impa.sln
src/
├── Host/Impa.Host/                 # Composição: registro dos módulos, autenticação, pipeline HTTP
│   └── Persistence/                # HostDbContext (só extensões do PostgreSQL) e suas migrações
├── Shared/Impa.Shared/             # Tipos comuns (resultado, erros etc.) — não é módulo de negócio
└── Modules/
    └── Acesso/                     # Identidade e Acesso — schema `acesso`
        ├── Impa.Modules.Acesso.Domain/          # Entidades e regras; não depende de nada externo
        ├── Impa.Modules.Acesso.Application/     # Casos de uso (comandos/consultas)
        ├── Impa.Modules.Acesso.Infrastructure/  # EF Core (AcessoDbContext, migrações), DI do módulo
        └── Impa.Modules.Acesso.Contracts/       # Contrato público: o único projeto que outros módulos podem referenciar
tests/
├── Impa.Architecture.Tests/        # Regras de dependência entre módulos e camadas
├── Impa.Modules.Acesso.Tests/      # Unitários do módulo Acesso
└── Impa.Api.IntegrationTests/      # WebApplicationFactory + PostgreSQL em container (Testcontainers)
deploy/                             # Dockerfile, compose de homologação, Caddyfile (story 1-1, T8)
```

### Regras de dependência (verificadas em `Impa.Architecture.Tests`)

| Projeto | Pode referenciar |
|---------|------------------|
| `*.Domain` | `Impa.Shared` |
| `*.Contracts` | `Impa.Shared` |
| `*.Application` | Domain e Contracts do próprio módulo; `*.Contracts` de outros módulos |
| `*.Infrastructure` | Application do próprio módulo (e o que ela traz) |
| `Impa.Host` | Infrastructure de cada módulo (raiz de composição) |

### Banco

- Um schema por módulo (`acesso`, ...), cada um com a própria tabela `__EFMigrationsHistory`.
- O Host tem o schema `host` só para o histórico das migrações de banco (extensões `unaccent` e `pg_trgm`).
- As migrações são aplicadas na inicialização (`Database:MigrateOnStartup`, padrão `true`): Host primeiro, depois os módulos.

Criar migração de um módulo:

```bash
dotnet ef migrations add {Nome} -p src/Modules/Acesso/Impa.Modules.Acesso.Infrastructure -s src/Host/Impa.Host -c AcessoDbContext -o Persistence/Migrations
```

## Rodar localmente

Pré-requisito: PostgreSQL acessível com a string `ConnectionStrings:Impa` (`appsettings.Development.json`).

```bash
dotnet run --project src/Host/Impa.Host
curl http://localhost:5224/api/health
```

## Testes

```bash
dotnet test
```

Os testes de integração sobem um PostgreSQL com Testcontainers e precisam de um Docker acessível (`DOCKER_HOST`).
