# API do IMPA. Contexto de build: a raiz do impa-backend (ver docker-compose.homolog.yml).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restauração em camada própria: só refaz quando um .csproj muda.
# Todo projeto novo em src/ (ex.: um módulo novo) precisa de uma linha aqui; sem ela, o publish falha
# com NETSDK1004 ("project.assets.json not found").
COPY global.json Directory.Build.props ./
COPY src/Shared/Impa.Shared/*.csproj src/Shared/Impa.Shared/
COPY src/Modules/Acesso/Impa.Modules.Acesso.Domain/*.csproj src/Modules/Acesso/Impa.Modules.Acesso.Domain/
COPY src/Modules/Acesso/Impa.Modules.Acesso.Contracts/*.csproj src/Modules/Acesso/Impa.Modules.Acesso.Contracts/
COPY src/Modules/Acesso/Impa.Modules.Acesso.Application/*.csproj src/Modules/Acesso/Impa.Modules.Acesso.Application/
COPY src/Modules/Acesso/Impa.Modules.Acesso.Infrastructure/*.csproj src/Modules/Acesso/Impa.Modules.Acesso.Infrastructure/
COPY src/Modules/Configuracao/Impa.Modules.Configuracao.Domain/*.csproj src/Modules/Configuracao/Impa.Modules.Configuracao.Domain/
COPY src/Modules/Configuracao/Impa.Modules.Configuracao.Contracts/*.csproj src/Modules/Configuracao/Impa.Modules.Configuracao.Contracts/
COPY src/Modules/Configuracao/Impa.Modules.Configuracao.Application/*.csproj src/Modules/Configuracao/Impa.Modules.Configuracao.Application/
COPY src/Modules/Configuracao/Impa.Modules.Configuracao.Infrastructure/*.csproj src/Modules/Configuracao/Impa.Modules.Configuracao.Infrastructure/
COPY src/Modules/Pessoas/Impa.Modules.Pessoas.Domain/*.csproj src/Modules/Pessoas/Impa.Modules.Pessoas.Domain/
COPY src/Modules/Pessoas/Impa.Modules.Pessoas.Contracts/*.csproj src/Modules/Pessoas/Impa.Modules.Pessoas.Contracts/
COPY src/Modules/Pessoas/Impa.Modules.Pessoas.Application/*.csproj src/Modules/Pessoas/Impa.Modules.Pessoas.Application/
COPY src/Modules/Pessoas/Impa.Modules.Pessoas.Infrastructure/*.csproj src/Modules/Pessoas/Impa.Modules.Pessoas.Infrastructure/
COPY src/Modules/Financeiro/Impa.Modules.Financeiro.Domain/*.csproj src/Modules/Financeiro/Impa.Modules.Financeiro.Domain/
COPY src/Modules/Financeiro/Impa.Modules.Financeiro.Contracts/*.csproj src/Modules/Financeiro/Impa.Modules.Financeiro.Contracts/
COPY src/Modules/Financeiro/Impa.Modules.Financeiro.Application/*.csproj src/Modules/Financeiro/Impa.Modules.Financeiro.Application/
COPY src/Modules/Financeiro/Impa.Modules.Financeiro.Infrastructure/*.csproj src/Modules/Financeiro/Impa.Modules.Financeiro.Infrastructure/
COPY src/Host/Impa.Host/*.csproj src/Host/Impa.Host/
RUN dotnet restore src/Host/Impa.Host/Impa.Host.csproj

COPY src/ src/
# Commit do git (o contexto não tem .git): vai para o InformationalVersion, exibido no rodapé (GET /api/versao).
ARG VERSAO_COMMIT=
RUN dotnet publish src/Host/Impa.Host/Impa.Host.csproj -c Release --no-restore -o /app     -p:SourceRevisionId=${VERSAO_COMMIT}

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Chaves do Data Protection (criptografia do cookie de sessão), num volume: sobrevivem a novas versões.
RUN mkdir -p /keys && chown app:app /keys

COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DataProtection__KeysPath=/keys
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Impa.Host.dll"]
