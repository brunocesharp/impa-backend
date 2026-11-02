# IMPA — API (impa-backend)

API do IMPA, sistema de gestão da Impacto Papelaria. ASP.NET Core **.NET 10**, PostgreSQL (EF Core + Npgsql).

## Arquitetura

**Monólito modular** (ADR-001): um processo, módulos com contrato público e schema próprio no banco.
Cada módulo segue Clean Architecture em quatro projetos. Um módulo nunca grava no schema de outro (ADR-012).

```
Impa.sln
src/
├── Host/Impa.Host/                 # Composição: registro dos módulos, autenticação, pipeline HTTP
│   ├── Endpoints/                  # Minimal APIs por assunto (auth, configuração, versão): request → MediatR → ResultadoHttp
│   ├── Http/                       # ResultadoHttp: mapeamento único de Result (Ardalis) → HTTP e ErroDto { mensagem }
│   ├── Pipeline/                   # Behaviors do MediatR: LoggingBehavior e ValidationBehavior (ADR-013)
│   ├── Seguranca/                  # Sessão por cookie (ADR-006), autorização por permissão, proteção CSRF
│   └── Persistence/                # HostDbContext (só extensões do PostgreSQL) e suas migrações
├── Shared/Impa.Shared/             # Tipos comuns entre módulos (ainda vazio) — não é módulo de negócio
└── Modules/
    └── Acesso/                     # Identidade e Acesso — schema `acesso`
        ├── Impa.Modules.Acesso.Domain/          # Entidades e regras próprias; não depende de nada externo (ainda vazio)
        ├── Impa.Modules.Acesso.Application/     # Casos de uso (commands/queries MediatR, ADR-013), perfis padrão
        ├── Impa.Modules.Acesso.Infrastructure/  # Identity (Usuario, Perfil, sessão, seed), EF Core (AcessoDbContext, migrações), DI
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

`Usuario` (`IdentityUser<Guid>`) e `Perfil` (`IdentityRole<Guid>`) ficam na **Infrastructure**, não no Domain:
herdam de tipos do ASP.NET Core Identity, e o Domain só pode referenciar `Impa.Shared`. A Application fala com eles
por `ISessaoService` (primitivas: buscar usuário, conferir senha, abrir/encerrar sessão), e a decisão do login fica no handler.

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

Na primeira subida, o seed cria os perfis **Administrador** e **Operador de caixa** e, se ainda não houver administrador,
o primeiro a partir de `IMPA_ADMIN_USUARIO`, `IMPA_ADMIN_SENHA` (mínimo 8 caracteres) e `IMPA_ADMIN_NOME`.

```bash
dotnet run --project src/Host/Impa.Host --launch-profile https
curl -k https://localhost:7065/api/health
```

Use o perfil **https** (`https://localhost:7065`, também escuta `http://localhost:5224`): é para ele que o
`proxy.conf.json` do impa-frontend encaminha `/api` no `ng serve`. O cookie de sessão e o antiforgery são `Secure`,
então login e logout só funcionam por HTTPS; por HTTP os GETs respondem sem token XSRF (o `/api/health` responde 200,
útil para healthchecks internos) e o que altera estado é recusado com 400.

## Testes

```bash
dotnet test
```

Os testes de integração sobem um PostgreSQL com Testcontainers e precisam de um Docker acessível (`DOCKER_HOST`).
