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
├── Shared/Impa.Shared/             # Tipos comuns entre módulos (Documento CPF/CNPJ, mensageria) — não é módulo de negócio
└── Modules/
    └── Acesso/                     # Identidade e Acesso — schema `acesso`
        ├── Impa.Modules.Acesso.Domain/          # Entidades e regras próprias; não depende de nada externo (ainda vazio)
        ├── Impa.Modules.Acesso.Application/     # Casos de uso (commands/queries MediatR, ADR-013), perfis padrão
        ├── Impa.Modules.Acesso.Infrastructure/  # Identity (Usuario, Perfil, sessão, seed), EF Core (AcessoDbContext, migrações), DI
        └── Impa.Modules.Acesso.Contracts/       # Contrato público: o único projeto que outros módulos podem referenciar
    └── Pessoas/                    # Clientes e fornecedores (story 3-7) — schema `pessoas`
        ├── Impa.Modules.Pessoas.Domain/         # Cliente, Fornecedor (RN-14.1, RN-14.2)
        ├── Impa.Modules.Pessoas.Application/    # Casos de uso, busca (RN-14.3), exclusão só sem histórico (RN-14.4)
        ├── Impa.Modules.Pessoas.Infrastructure/ # PessoasDbContext, repositórios, migrações, DI
        └── Impa.Modules.Pessoas.Contracts/      # IPessoasConsultas (PDV, Compras, Financeiro) e a porta IHistoricoDaPessoa
    └── Financeiro/                 # Contas a pagar (story 11-1) — schema `financeiro`
        ├── Impa.Modules.Financeiro.Domain/         # CategoriaFinanceira, Conta, Parcelamento (RN-23.1 a RN-23.3)
        ├── Impa.Modules.Financeiro.Application/    # Casos de uso; implementa IHistoricoDaPessoa (fornecedor com conta)
        ├── Impa.Modules.Financeiro.Infrastructure/ # FinanceiroDbContext, repositórios, migrações (com as categorias iniciais), DI
        └── Impa.Modules.Financeiro.Contracts/      # Contrato público (ainda vazio)
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

### Permissões (story 1.3)

- **Catálogo no código, perfis no banco.** `Permissoes.Catalogo` (`Impa.Modules.Acesso.Contracts`) lista cada permissão
  com grupo e descrição (é o que a tela de perfis mostra). Cada perfil (role do Identity) guarda as suas como claims
  `permissao` em `acesso.perfis_permissoes`; cada usuário tem um perfil. Permissão nova entra no catálogo e o seed a dá ao
  **Administrador** (perfil protegido: não pode ser alterado nem excluído). Os demais perfis são do administrador da loja.
- **Proteger um endpoint:** `.RequirePermissao(Permissoes.X)` — sem sessão 401, sem a permissão 403. A verificação usa o
  perfil **atual** do usuário no banco (cache de 30 s, invalidado quando um perfil ou o perfil de um usuário muda), não o
  que estava no tíquete do login. Uma instância da API só: com várias, a invalidação teria de ser distribuída.
- **Campo que depende de permissão:** marque a propriedade do DTO com `[property: ExigePermissao(Permissoes.X)]`; sem a
  permissão o campo é **omitido do JSON** (não vem `null`). Ex.: o custo dos itens.
- **Convenção por funcionalidade** — os endpoints existentes e futuros usam:

  | Funcionalidade | Permissão |
  |----------------|-----------|
  | Custo dos itens (campo nos DTOs de item, épico 3) | `catalogo.custo.ver` (`ExigePermissao`) |
  | Contas a pagar e financeiro (`/api/financeiro/categorias`, `/api/financeiro/contas`): consultar / lançar, alterar, excluir, pagar e estornar | `financeiro.ver` / `financeiro.editar` (+ `financeiro.ver`) |
  | Importação de planilhas | `importacao.executar` |
  | Alterar a configuração fiscal | `configuracao.fiscal` |
  | Ver a configuração (`GET /api/configuracao/empresa`) | `configuracao.ver` |
  | Usuários (`/api/usuarios`) | `usuarios.gerenciar` |
  | Perfis (`/api/perfis`) | `perfis.gerenciar` |
  | Clientes (`/api/pessoas/clientes`): consultar / cadastrar e alterar / inativar, reativar e excluir | `clientes.ver` / `clientes.editar` (+ `clientes.ver`) / `clientes.inativar` |
  | Fornecedores (`/api/pessoas/fornecedores`) | `fornecedores.gerenciar` |

  O teste `PerfisTests` (cenário 1.5) exercita essas permissões com endpoints de exemplo registrados só nos testes
  (`IEndpointsAdicionais`).
- **Último administrador:** desativar ou trocar o perfil do único administrador ativo é recusado (409, "É preciso manter
  pelo menos um administrador ativo"); a checagem roda sob uma trava (`pg_advisory_xact_lock`).

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
