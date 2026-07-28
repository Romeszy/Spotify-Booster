#!/usr/bin/env bash
set -euo pipefail

PROJECT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REPOSITORY="$(cd "$PROJECT/.." && pwd)"
CERT="$PROJECT/supabase/.local/tls/supabase-local.crt"
PROJECT_ID='spotify-booster-commercial-phase-a'
PUBLISHABLE_KEY='sb_publishable_ACJWlzQHlZjBrEguHvfOxg_3BJgxAaH'
TOOL_DIR="$(mktemp -d /tmp/spotify-booster-validation.XXXXXX)"
DOTNET_DIR="$TOOL_DIR/dotnet"
CLI=''
API_PID=''
CURRENT_STAGE='initialization'

cleanup() {
  if [[ -n "$API_PID" ]]; then
    kill "$API_PID" >/dev/null 2>&1 || true
    wait "$API_PID" >/dev/null 2>&1 || true
  fi
  if [[ -n "$CLI" && -x "$CLI" ]]; then
    "$CLI" stop --project-id "$PROJECT_ID" >/dev/null 2>&1 || true
  fi
  case "$TOOL_DIR" in
    /tmp/spotify-booster-validation.*) rm -rf -- "$TOOL_DIR" ;;
    *) printf 'validation_cleanup=unsafe_path\n' >&2; return 99 ;;
  esac
}
trap cleanup EXIT INT TERM
trap 'printf "validation_stage=%s:failed\n" "$CURRENT_STAGE" >&2' ERR

test -f "$CERT"
CURRENT_STAGE='node_version_check'
NODE_VERSION_OUTPUT="$(node --version)"
if [[ ! "$NODE_VERSION_OUTPUT" =~ ^v([0-9]+)\.[0-9]+\.[0-9]+$ ]] ||
   (( BASH_REMATCH[1] < 20 )); then
  printf 'node_version=unsupported\n' >&2
  exit 1
fi

cd "$TOOL_DIR"
CURRENT_STAGE='supabase_cli_install'
cp -- "$PROJECT/package.json" "$PROJECT/package-lock.json" "$TOOL_DIR/"
npm ci --offline --ignore-scripts=false >npm-ci.log 2>&1

CLI="$TOOL_DIR/node_modules/.bin/supabase"
test "$("$CLI" --version)" = '2.109.1'

CURRENT_STAGE='dotnet_linux_install'
curl --fail --silent --show-error --location \
  --output dotnet-install.sh https://dot.net/v1/dotnet-install.sh
bash dotnet-install.sh --version 8.0.423 --install-dir "$DOTNET_DIR" \
  --no-path >dotnet-install.log 2>&1
DOTNET="$DOTNET_DIR/dotnet"
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
test "$("$DOTNET" --version)" = '8.0.423'

CURRENT_STAGE='supabase_start'
timeout --foreground 180 env SSL_CERT_FILE="$CERT" \
  "$CLI" start --workdir "$PROJECT" >start.log 2>&1

CURRENT_STAGE='database_reset'
timeout --foreground 180 env SSL_CERT_FILE="$CERT" \
  "$CLI" db reset --local --workdir "$PROJECT" >reset.log 2>&1

CURRENT_STAGE='database_lint'
timeout --foreground 180 env SSL_CERT_FILE="$CERT" \
  "$CLI" db lint --local --workdir "$PROJECT" \
  --level error --fail-on error >lint.log 2>&1

CURRENT_STAGE='pgtap'
timeout --foreground 180 env SSL_CERT_FILE="$CERT" \
  "$CLI" test db --local --workdir "$PROJECT" >pgtap.log 2>&1
grep -q 'Result: PASS' pgtap.log
grep -q 'Tests=110' pgtap.log

CURRENT_STAGE='migration_atomicity'
timeout --foreground 180 env \
  SSL_CERT_FILE="$CERT" SUPABASE_CLI="$CLI" \
  bash "$PROJECT/scripts/test-migration-atomicity.sh" "$PROJECT" \
  >atomicity.log 2>&1
grep -q 'migration_atomicity_object_rollback=ok' atomicity.log
grep -q 'migration_atomicity_version_rollback=ok' atomicity.log

CURRENT_STAGE='database_reset_after_atomicity'
timeout --foreground 180 env SSL_CERT_FILE="$CERT" \
  "$CLI" db reset --local --workdir "$PROJECT" >reset-after-atomicity.log 2>&1

cd "$REPOSITORY"
CURRENT_STAGE='dotnet_restore'
timeout --foreground 300 "$DOTNET" restore \
  src/SpotifyBooster.Api/SpotifyBooster.Api.csproj \
  --locked-mode >"$TOOL_DIR/dotnet-restore.log" 2>&1
CURRENT_STAGE='dotnet_build'
timeout --foreground 300 "$DOTNET" build \
  src/SpotifyBooster.Api/SpotifyBooster.Api.csproj \
  --configuration Release --no-restore >"$TOOL_DIR/dotnet-build.log" 2>&1

CURRENT_STAGE='api_start'
env \
  SSL_CERT_FILE="$CERT" \
  DOTNET_CLI_HOME="$TOOL_DIR/dotnet-home" \
  ASPNETCORE_URLS='http://127.0.0.1:5080' \
  Supabase__Origin='https://127.0.0.1:54321/' \
  Supabase__PublishableKey="$PUBLISHABLE_KEY" \
  Supabase__Issuer='https://127.0.0.1:54321/auth/v1' \
  Supabase__Audience='authenticated' \
  Supabase__JwksPath='/auth/v1/.well-known/jwks.json' \
  "$DOTNET" run \
  --project src/SpotifyBooster.Api/SpotifyBooster.Api.csproj \
  --configuration Release --no-build >"$TOOL_DIR/api.log" 2>&1 &
API_PID="$!"

for _ in $(seq 1 60); do
  if curl --fail --silent --show-error \
    http://127.0.0.1:5080/health/live >/dev/null 2>&1; then
    break
  fi
  if ! kill -0 "$API_PID" >/dev/null 2>&1; then
    printf 'api_start=process_exited\n' >&2
    grep -E \
      '(^Unhandled exception|Exception:|error [A-Z]+[0-9]+|Failed to|Could not|cannot|Address already)' \
      "$TOOL_DIR/api.log" | head -n 20 >&2 || true
    exit 1
  fi
  sleep 0.5
done
if ! curl --fail --silent --show-error \
  http://127.0.0.1:5080/health/live >/dev/null; then
  printf 'api_start=timeout\n' >&2
  exit 1
fi

CURRENT_STAGE='auth_api_e2e'
if ! timeout --foreground 240 env \
  SUPABASE_CA_FILE="$CERT" \
  SPOTIFY_BOOSTER_API_ORIGIN='http://127.0.0.1:5080' \
  SPOTIFY_BOOSTER_PUBLISHABLE_KEY="$PUBLISHABLE_KEY" \
  SPOTIFY_BOOSTER_API_LOG="$TOOL_DIR/api.log" \
  SUPABASE_CLI="$CLI" \
  node "$PROJECT/scripts/e2e-auth-api.mjs" >"$TOOL_DIR/e2e.log" 2>&1; then
  grep -E '^auth_api_e2e_failed:[a-z0-9_]+$' "$TOOL_DIR/e2e.log" >&2 || true
  LAST_READINESS_RESULT="$(
    grep -Eo 'result changed: (ready|timeout|transport|http_status|invalid_shape)' \
      "$TOOL_DIR/api.log" | tail -n 1 | sed 's/.*: //' || true
  )"
  printf 'readiness_last_result=%s\n' \
    "${LAST_READINESS_RESULT:-unobserved}" >&2
  false
fi
CURRENT_STAGE='auth_api_evidence'
grep -q 'auth_password_boundary=ok' "$TOOL_DIR/e2e.log"
grep -q 'refresh_reuse_family_revocation=ok' "$TOOL_DIR/e2e.log"
grep -q 'readiness_stop_restart=ok' "$TOOL_DIR/e2e.log"
grep -q 'sensitive_log_scan=ok' "$TOOL_DIR/e2e.log"

CURRENT_STAGE='sensitive_log_scan'
if grep -Eqi \
  'authorization:[[:space:]]*bearer|refresh_token|@example\.invalid|eyJ[A-Za-z0-9_-]{7,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}' \
  "$TOOL_DIR/api.log" "$TOOL_DIR/e2e.log"; then
  printf 'sensitive_log_scan=failed\n' >&2
  exit 1
fi

CURRENT_STAGE='jwks_tls'
curl --silent --show-error --fail \
  --cacert "$CERT" \
  --output /dev/null \
  https://127.0.0.1:54321/auth/v1/.well-known/jwks.json

CURRENT_STAGE='explicit_cleanup'
kill "$API_PID"
wait "$API_PID" || true
API_PID=''
"$CLI" stop --project-id "$PROJECT_ID" >/dev/null
test -z "$(docker ps -a \
  --filter label=com.supabase.cli.project="$PROJECT_ID" \
  --format '{{.Names}}')"

CURRENT_STAGE='complete'
printf '%s\n' \
  'cli=2.109.1' \
  'dotnet_linux=8.0.423' \
  'start=ok' \
  'reset=ok' \
  'lint=ok' \
  'pgtap=110/110' \
  'migration_atomicity=ok' \
  'api_real_process=ok' \
  'auth_api_e2e=ok' \
  'jwks_tls=ok' \
  'sensitive_log_scan=ok' \
  'cleanup=ok'
