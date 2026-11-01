#!/usr/bin/env bash
# Sobe (ou atualiza) a homologação com os commits atuais do impa-backend e do impa-frontend,
# exibidos no rodapé do sistema. Uso, a partir da raiz do impa-backend:
#   deploy/subir.sh deploy/.env [argumentos extras do "docker compose up"]
set -euo pipefail

env_file="${1:?informe o arquivo .env, ex.: deploy/.env}"
shift

raiz="$(cd "$(dirname "$0")/.." && pwd)"
frontend="$(cd "$raiz/deploy" && cd "${CAMINHO_FRONTEND:-../../impa-frontend}" && pwd)"

VERSAO_COMMIT_API="$(git -C "$raiz" rev-parse --short=7 HEAD)"
VERSAO_COMMIT_WEB="$(git -C "$frontend" rev-parse --short=7 HEAD)"
export VERSAO_COMMIT_API VERSAO_COMMIT_WEB
echo "API $VERSAO_COMMIT_API · Web $VERSAO_COMMIT_WEB"

docker compose -f "$raiz/deploy/docker-compose.homolog.yml" --env-file "$env_file" up -d --build "$@"
