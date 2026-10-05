#!/usr/bin/env bash
# P4-10 — serveur PostgreSQL 17.10 JETABLE dédié aux tests de redémarrage (critère de sortie 10).
#
# Les tests l'arrêtent et le redémarrent réellement (arrêt rapide = SIGINT au processus postmaster, comme un
# redémarrage de service) : il ne doit être partagé avec aucun autre test.
#
# Usage : start-restart-server.sh <dossier-de-sortie>     (CI Linux, ou Git Bash + Docker Desktop en local)
# Produit : restart.env (KEY=VALUE, compatible $GITHUB_ENV). Mot de passe généré à chaque exécution, jamais committé.
set -euo pipefail

IMAGE="${MMV_RESTART_IMAGE:-postgres:17.10}"
OUT="${1:?Usage : start-restart-server.sh <dossier-de-sortie>}"
PORT="${MMV_RESTART_PORT:-5436}"
NAME=mmv-pg-restart
USER_NAME=mmv_restart
PASSWORD="$(openssl rand -hex 24)"

export MSYS_NO_PATHCONV=1
mkdir -p "$OUT"

docker rm -f "$NAME" >/dev/null 2>&1 || true
docker run -d --name "$NAME" -p "$PORT:5432" \
  -e POSTGRES_USER="$USER_NAME" -e POSTGRES_PASSWORD="$PASSWORD" -e POSTGRES_DB=postgres \
  "$IMAGE" >/dev/null

for _ in $(seq 1 60); do
  if docker exec "$NAME" pg_isready -h 127.0.0.1 -U "$USER_NAME" -d postgres >/dev/null 2>&1; then
    break
  fi
  sleep 1
done
docker exec "$NAME" pg_isready -h 127.0.0.1 -U "$USER_NAME" -d postgres >/dev/null

cat > "$OUT/restart.env" <<EOF
MMV_TEST_POSTGRESQL_RESTART_CONNECTION_STRING=Host=localhost;Port=$PORT;Username=$USER_NAME;Password=$PASSWORD;Database=postgres
MMV_TEST_POSTGRESQL_RESTART_CONTAINER=$NAME
EOF

if [ -n "${GITHUB_ACTIONS:-}" ]; then
  echo "::add-mask::$PASSWORD"
fi
echo "Serveur de redémarrage prêt ($NAME, port $PORT) ; variables écrites dans $OUT/restart.env."
