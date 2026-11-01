# Deploy do IMPA: homologação

Três contêineres, num só `docker compose`:

| Serviço | Imagem | Papel |
|---------|--------|-------|
| `web` | Caddy + build do Angular (`Dockerfile.web`) | HTTPS automático, serve a SPA e encaminha `/api/*` para a API |
| `api` | ASP.NET Core .NET 10 (`Dockerfile`) | API; aplica migrações e seed na inicialização |
| `postgres` | `postgres:18-alpine` | Banco, só na rede interna do compose |

Volumes: `postgres-dados` (banco), `chaves-sessao` (chaves que criptografam o cookie de sessão; sem elas, cada
nova versão derrubaria as sessões), `caddy-dados`/`caddy-config` (certificados).

## Pré-requisitos

1. **DNS (pessoa):** registro `A` de `homolog.impactopapelaria.com.br` apontando para o IP da VPS. O domínio raiz
   não é alterado. Conferir com `nslookup homolog.impactopapelaria.com.br` antes da primeira subida: sem o DNS,
   o Caddy não consegue o certificado.
2. **VPS:** Docker Engine com o plugin `docker compose` (v2.17 ou mais, por causa de `additional_contexts`);
   portas **80** e **443** (TCP e UDP) liberadas no firewall.
3. **Recursos da VPS:** conferir antes da primeira subida (`nproc`, `free -h`, `df -h`) e anotar abaixo. O build
   do Angular e o do .NET rodam na própria VPS; com menos de 2 GB de memória, use swap ou faça o build fora.

   | Data | CPU | Memória | Disco livre |
   |------|-----|---------|-------------|
   | _a conferir_ | | | |

4. **Os dois repositórios lado a lado** (o compose procura o frontend em `../../impa-frontend`, a partir de `deploy/`):

   ```bash
   mkdir -p ~/impa && cd ~/impa
   git clone {url}/impa-backend.git
   git clone {url}/impa-frontend.git
   ```

## Primeira subida

```bash
cd ~/impa/impa-backend
cp deploy/.env.example deploy/.env
nano deploy/.env        # DOMINIO, POSTGRES_PASSWORD, IMPA_ADMIN_*, MEDIATR_LICENSE_KEY
chmod 600 deploy/.env

docker compose -f deploy/docker-compose.homolog.yml --env-file deploy/.env up -d --build
```

Conferir:

```bash
curl -fsS https://homolog.impactopapelaria.com.br/api/health      # Healthy
```

Depois, abrir `https://homolog.impactopapelaria.com.br` e entrar com `IMPA_ADMIN_USUARIO` / `IMPA_ADMIN_SENHA`.
O administrador só é criado se ainda não houver nenhum: mudar as variáveis depois não altera a senha.

## Atualização

```bash
cd ~/impa/impa-backend && git pull
cd ~/impa/impa-frontend && git pull
cd ~/impa/impa-backend
docker compose -f deploy/docker-compose.homolog.yml --env-file deploy/.env up -d --build
docker image prune -f
```

As migrações do banco rodam sozinhas quando a API sobe. As sessões abertas continuam valendo (chaves no volume).
O `web` (Caddy) só sobe depois que o `healthcheck` da `api` (`GET /api/health` por HTTP dentro do container) responde
200, então não há 502 enquanto a API inicia; o `ps` mostra `(healthy)`.

## Logs e situação

```bash
docker compose -f deploy/docker-compose.homolog.yml --env-file deploy/.env ps
docker compose -f deploy/docker-compose.homolog.yml --env-file deploy/.env logs -f api
docker compose -f deploy/docker-compose.homolog.yml --env-file deploy/.env logs -f web     # certificados, acessos
```

## Testar localmente (mesma configuração)

Com `DOMINIO=localhost`, o Caddy usa um certificado interno (o navegador avisa; aceite para testar).
Se 80/443 estiverem ocupadas, use `PORTA_HTTP=8080` e `PORTA_HTTPS=8443` e abra `https://localhost:8443`.

```bash
docker compose -f deploy/docker-compose.homolog.yml --env-file deploy/.env.local up -d --build
```

Os testes ponta a ponta do impa-frontend (Playwright) rodam contra esse ambiente (ver o README do impa-frontend).

## Problemas comuns

- **`web` não sobe e a `api` fica `(unhealthy)`:** a API não terminou as migrações ou não alcança o banco; veja `logs api`.
- **Certificado não sai:** DNS ainda não propagou ou porta 80/443 fechada. `logs web` mostra o motivo.
- **Login recusado com "token de segurança ausente ou inválido":** o navegador está numa aba antiga; recarregue.
- **Todos deslogados depois de atualizar:** o volume `chaves-sessao` foi removido (`down -v`). Nunca use `-v` em homologação: também apaga o banco.
