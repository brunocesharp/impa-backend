# API do IMPA. Contexto de build: a raiz do impa-backend (ver docker-compose.homolog.yml).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restauração em camada própria: só refaz quando um .csproj muda.
COPY global.json Directory.Build.props ./
COPY src/Shared/Impa.Shared/*.csproj src/Shared/Impa.Shared/
COPY src/Modules/Acesso/Impa.Modules.Acesso.Domain/*.csproj src/Modules/Acesso/Impa.Modules.Acesso.Domain/
COPY src/Modules/Acesso/Impa.Modules.Acesso.Contracts/*.csproj src/Modules/Acesso/Impa.Modules.Acesso.Contracts/
COPY src/Modules/Acesso/Impa.Modules.Acesso.Application/*.csproj src/Modules/Acesso/Impa.Modules.Acesso.Application/
COPY src/Modules/Acesso/Impa.Modules.Acesso.Infrastructure/*.csproj src/Modules/Acesso/Impa.Modules.Acesso.Infrastructure/
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
