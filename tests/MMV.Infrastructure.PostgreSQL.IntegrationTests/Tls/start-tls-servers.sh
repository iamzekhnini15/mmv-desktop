#!/usr/bin/env bash
# P4-8 (ADR-PROD-DB-010, D-14) — serveurs PostgreSQL TLS JETABLES pour les tests d'intégration.
#
# Produit, dans le dossier passé en argument :
#   * une autorité de test (ca.crt) et une autorité ÉTRANGÈRE (wrong-ca.crt) ;
#   * un certificat serveur valide pour DNS:localhost UNIQUEMENT (aucune IP en SAN : 127.0.0.1 doit échouer
#     en VerifyFull) et un certificat signé par la bonne autorité mais EXPIRÉ ;
#   * deux conteneurs : TLS valide (port 5433) et TLS expiré (port 5434), SCRAM-SHA-256, pg_hba « hostssl » ;
#   * tls.env : les variables MMV_TEST_POSTGRESQL_TLS_* (format KEY=VALUE, compatible $GITHUB_ENV).
#
# Tout est éphémère : clés et mot de passe administrateur sont générés à chaque exécution, jamais committés.
# Usage : start-tls-servers.sh <dossier-de-sortie>      (CI Linux, ou Git Bash + Docker Desktop en local)
set -euo pipefail

IMAGE="${MMV_TLS_IMAGE:-postgres:17.10}"
OUT="${1:?Usage : start-tls-servers.sh <dossier-de-sortie>}"
VALID_PORT="${MMV_TLS_VALID_PORT:-5433}"
EXPIRED_PORT="${MMV_TLS_EXPIRED_PORT:-5434}"
ADMIN_USER=mmv_tls_admin
ADMIN_PASSWORD="$(openssl rand -hex 24)"

export MSYS_NO_PATHCONV=1
mkdir -p "$OUT"
cd "$OUT"
native() { if command -v cygpath >/dev/null 2>&1; then cygpath -m "$1"; else printf '%s' "$1"; fi; }

openssl req -x509 -newkey rsa:2048 -nodes -keyout ca.key -out ca.crt -days 3 -subj "/CN=MMV IT Test CA" 2>/dev/null
openssl req -x509 -newkey rsa:2048 -nodes -keyout wrong-ca.key -out wrong-ca.crt -days 3 -subj "/CN=MMV IT Foreign CA" 2>/dev/null

printf 'subjectAltName=DNS:localhost\nbasicConstraints=CA:FALSE\nkeyUsage=digitalSignature,keyEncipherment\nextendedKeyUsage=serverAuth\n' > san.ext

openssl req -newkey rsa:2048 -nodes -keyout server.key -out server.csr -subj "/CN=localhost" 2>/dev/null
openssl x509 -req -in server.csr -CA ca.crt -CAkey ca.key -CAcreateserial -out server.crt -days 3 -extfile san.ext 2>/dev/null

# Certificat expiré : `openssl ca` est le seul moyen portable (OpenSSL 3.0) de fixer des dates passées.
mkdir -p ca-db/newcerts
: > ca-db/index.txt
echo 1000 > ca-db/serial
cat > ca.cnf <<EOF
[ ca ]
default_ca = test_ca
[ test_ca ]
dir = $(native "$PWD")/ca-db
database = \$dir/index.txt
new_certs_dir = \$dir/newcerts
serial = \$dir/serial
default_md = sha256
policy = policy_any
[ policy_any ]
commonName = supplied
EOF
openssl req -newkey rsa:2048 -nodes -keyout expired.key -out expired.csr -subj "/CN=localhost" 2>/dev/null
openssl ca -batch -config ca.cnf -cert ca.crt -keyfile ca.key -in expired.csr -out expired.crt \
  -startdate 20200101000000Z -enddate 20200102000000Z -notext -extfile san.ext 2>/dev/null

# pg_hba : réseau en TLS uniquement, SCRAM partout, sauf des rôles-sondes NOMMÉS qui prouvent les refus
# (md5 côté client, audit de provisioning). Aucune règle « all » non TLS.
cat > pg_hba.conf <<'EOF'
local   all all                          trust
hostssl all mmv_tls_md5_probe     all    md5
hostssl all mmv_tls_insecure_md5  all    md5
host    all mmv_tls_insecure_nossl all   scram-sha-256
hostssl all all                   all    scram-sha-256
EOF

start_server() {
  local name="$1" port="$2" cert="$3" key="$4"
  local stage="stage-$name"
  docker rm -f "$name" >/dev/null 2>&1 || true
  mkdir -p "$stage"
  cp "$cert" "$stage/server.crt"
  cp "$key" "$stage/server.key"
  cp pg_hba.conf "$stage/pg_hba.conf"
  docker create --name "$name" -p "$port:5432" \
    -e POSTGRES_USER="$ADMIN_USER" -e POSTGRES_PASSWORD="$ADMIN_PASSWORD" -e POSTGRES_DB=postgres \
    --entrypoint bash "$IMAGE" -c '
      set -e
      mkdir -p /etc/mmv-tls
      cp /mmv-tls-src/* /etc/mmv-tls/
      chown -R postgres:postgres /etc/mmv-tls
      chmod 600 /etc/mmv-tls/server.key
      exec docker-entrypoint.sh postgres -c ssl=on \
        -c ssl_cert_file=/etc/mmv-tls/server.crt -c ssl_key_file=/etc/mmv-tls/server.key \
        -c hba_file=/etc/mmv-tls/pg_hba.conf -c password_encryption=scram-sha-256' >/dev/null
  docker cp "$(native "$PWD/$stage")/." "$name:/mmv-tls-src" >/dev/null
  docker start "$name" >/dev/null
}

wait_ready() {
  local name="$1"
  for _ in $(seq 1 90); do
    # TCP seulement : le serveur temporaire d'initialisation n'écoute que sur la socket Unix.
    if docker exec "$name" pg_isready -h 127.0.0.1 -U "$ADMIN_USER" -d postgres >/dev/null 2>&1; then
      return 0
    fi
    sleep 1
  done
  docker logs "$name" | tail -50
  echo "Serveur $name non prêt." >&2
  return 1
}

start_server mmv-pg-tls "$VALID_PORT" server.crt server.key
start_server mmv-pg-tls-expired "$EXPIRED_PORT" expired.crt expired.key
wait_ready mmv-pg-tls
wait_ready mmv-pg-tls-expired

CA="$(native "$PWD/ca.crt")"
cat > tls.env <<EOF
MMV_TEST_POSTGRESQL_TLS_CONNECTION_STRING=Host=localhost;Port=$VALID_PORT;Username=$ADMIN_USER;Password=$ADMIN_PASSWORD;Database=postgres;SSL Mode=VerifyFull;Root Certificate=$CA
MMV_TEST_POSTGRESQL_TLS_WRONG_CA=$(native "$PWD/wrong-ca.crt")
MMV_TEST_POSTGRESQL_TLS_EXPIRED_PORT=$EXPIRED_PORT
EOF

if [ -n "${GITHUB_ACTIONS:-}" ]; then
  echo "::add-mask::$ADMIN_PASSWORD"
fi
docker exec mmv-pg-tls psql -U "$ADMIN_USER" -d postgres -Atc "SELECT version(), current_setting('ssl'), current_setting('password_encryption')"
echo "Serveurs TLS prêts ; variables écrites dans $(native "$PWD/tls.env")."
