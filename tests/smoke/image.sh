#!/usr/bin/env bash
# Test rapide de l'image Home-GED construite : démarre l'image avec les mêmes contraintes que
# deploy/docker-compose.yml (lecture seule, aucune capacité, pas d'élévation de droits) devant un
# PostgreSQL jetable, puis vérifie le healthcheck, les en-têtes de sécurité et l'utilisateur.
#
#   tests/smoke/image.sh <image>      (ex. : tests/smoke/image.sh home-ged:ci)
# shellcheck disable=SC2015 # ok/ko renvoient toujours 0 : « A && ok || ko » est sûr ici.
set -euo pipefail

IMAGE=${1:?Usage : $0 <image>}
RUN=homeged-smoke-$$
PORT=${SMOKE_PORT:-18080}
failures=0

ok() { printf '  \033[32mOK\033[0m   %s\n' "$*"; }
ko() { printf '  \033[31mKO\033[0m   %s\n' "$*"; failures=$((failures + 1)); }

cleanup() {
  if ((failures)); then docker logs "$RUN-app" 2>&1 | tail -n 40 || true; fi
  docker rm -f "$RUN-app" "$RUN-db" > /dev/null 2>&1 || true
  docker network rm "$RUN" > /dev/null 2>&1 || true
}
trap cleanup EXIT

docker network create "$RUN" > /dev/null
docker run -d --name "$RUN-db" --network "$RUN" --network-alias db \
  -e POSTGRES_DB=homeged -e POSTGRES_USER=homeged -e POSTGRES_PASSWORD=smoke \
  --health-cmd 'pg_isready -U homeged -d homeged' --health-interval 2s \
  docker.io/library/postgres:18-alpine > /dev/null

for _ in $(seq 30); do
  [[ $(docker inspect -f '{{.State.Health.Status}}' "$RUN-db") == healthy ]] && break
  sleep 2
done

# Paperless volontairement injoignable : l'application doit démarrer quand même (vivacité).
docker run -d --name "$RUN-app" --network "$RUN" -p "127.0.0.1:$PORT:8080" \
  --read-only --tmpfs /tmp --cap-drop ALL --security-opt no-new-privileges:true \
  -e Paperless__BaseUrl=http://paperless.invalid:8000 \
  -e 'Database__ConnectionString=Host=db;Database=homeged;Username=homeged;Password=smoke' \
  "$IMAGE" > /dev/null

echo "Image $IMAGE :"
status=starting
for _ in $(seq 45); do
  status=$(docker inspect -f '{{.State.Health.Status}}' "$RUN-app")
  [[ $status == healthy || $(docker inspect -f '{{.State.Running}}' "$RUN-app") == false ]] && break
  sleep 2
done
[[ $status == healthy ]] && ok "healthcheck Docker : healthy" || ko "healthcheck Docker : $status"

code=$(curl -s -o /dev/null -w '%{http_code}' "http://127.0.0.1:$PORT/health" || true)
[[ $code == 200 ]] && ok "/health : 200" || ko "/health : $code"

code=$(curl -s -o /dev/null -w '%{http_code}' "http://127.0.0.1:$PORT/api/documents" || true)
[[ $code == 401 ]] && ok "/api/documents sans session : 401" || ko "/api/documents sans session : $code (401 attendu)"

headers=$(curl -s -D - -o /dev/null "http://127.0.0.1:$PORT/" | tr -d '\r')
for expected in \
  "content-security-policy: default-src 'self'" \
  "x-content-type-options: nosniff" \
  "referrer-policy: no-referrer" \
  "x-frame-options: " \
  "cross-origin-opener-policy: same-origin"; do
  grep -qi "^$expected" <<< "$headers" && ok "en-tête ${expected%%:*}" || ko "en-tête manquant : $expected"
done
grep -qi '^server:' <<< "$headers" && ko "en-tête Server présent (version du serveur divulguée)" || ok "pas d'en-tête Server"

uid=$(docker top "$RUN-app" -o pid,uid | awk 'NR == 2 {print $2}')
[[ -n $uid && $uid != 0 && $uid != root ]] && ok "processus non root (uid $uid)" || ko "processus lancé en root ($uid)"

if ((failures)); then
  echo "$failures vérification(s) en échec."
  exit 1
fi
echo "Toutes les vérifications sont passées."
