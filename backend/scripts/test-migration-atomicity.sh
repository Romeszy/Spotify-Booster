#!/usr/bin/env bash
set -euo pipefail

PROJECT="${1:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
CLI="${SUPABASE_CLI:-$PROJECT/node_modules/.bin/supabase}"
PROJECT_ID='spotify-booster-commercial-phase-a'
VERSION='29991231235959'
MIGRATION="$PROJECT/supabase/migrations/${VERSION}_atomicity_probe.sql"
PROBE='public.spotify_booster_migration_atomicity_probe'
SENTINEL='SPOTIFY_BOOSTER_ATOMICITY_SENTINEL_7F31A9C2'
SENTINEL_ERROR_PATTERN="ERROR:[[:space:]]+${SENTINEL}[[:space:]]+\\(SQLSTATE[[:space:]]+P0001\\)"
LOG_DIR="$(mktemp -d /tmp/spotify-booster-atomicity.XXXXXX)"
FORBIDDEN_LOG_PATTERN='authorization:[[:space:]]*bearer|refresh_token|@example\.invalid|eyJ[A-Za-z0-9_-]{7,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}|sb_(publishable|secret)_[A-Za-z0-9_-]{20,}|service_role|BEGIN[[:space:]]+(RSA |EC )?PRIVATE KEY|postgres(ql)?://[^[:space:]]+|(database_url|db_password|pgpassword|password)[[:space:]]*[:=][[:space:]]*[^[:space:]]+|"(d|p|q|dp|dq|qi)"[[:space:]]*:[[:space:]]*"[A-Za-z0-9_-]{20,}"'
PROOF_CAPTURED=0

scan_runner_log() {
  [[ ! -f "$LOG_DIR/runner.log" ]] ||
    ! grep -Eqi -- "$FORBIDDEN_LOG_PATTERN" \
      "$LOG_DIR/runner.log" >/dev/null 2>&1
}

cleanup_resources() {
  local cleanup_status=0
  if docker inspect "supabase_db_${PROJECT_ID}" >/dev/null 2>&1; then
    docker exec -i "supabase_db_${PROJECT_ID}" \
      psql -U postgres -d postgres -q -v ON_ERROR_STOP=1 >/dev/null 2>&1 <<SQL || true
drop table if exists ${PROBE};
delete from supabase_migrations.schema_migrations where version = '${VERSION}';
SQL
  fi
  if ! scan_runner_log; then
    printf 'migration_atomicity=log_scan_failed\n' >&2
    cleanup_status=1
  fi
  rm -f -- "$MIGRATION"
  case "$LOG_DIR" in
    /tmp/spotify-booster-atomicity.*) rm -rf -- "$LOG_DIR" ;;
    *)
      printf 'migration_atomicity_cleanup=unsafe_path\n' >&2
      cleanup_status=99
      ;;
  esac
  return "$cleanup_status"
}

on_exit() {
  local exit_code=$?
  local cleanup_code=0
  trap - EXIT INT TERM
  cleanup_resources || cleanup_code=$?
  if (( cleanup_code != 0 )); then
    exit_code=$cleanup_code
  fi
  exit "$exit_code"
}
trap on_exit EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

test "$("$CLI" --version)" = '2.109.1'
test ! -e "$MIGRATION"

cat >"$MIGRATION" <<'SQL'
create table public.spotify_booster_migration_atomicity_probe(id integer);
do $atomicity$
begin
  raise exception using
    errcode = 'P0001',
    message = 'SPOTIFY_BOOSTER_ATOMICITY_SENTINEL_7F31A9C2';
end
$atomicity$;
SQL

if timeout --foreground 120 env SSL_CERT_FILE="${SSL_CERT_FILE:?}" \
  "$CLI" migration up --local --workdir "$PROJECT" \
  >"$LOG_DIR/runner.log" 2>&1; then
  printf 'migration_atomicity=runner_unexpected_success\n' >&2
  exit 1
fi

if ! grep -Eq -- "$SENTINEL_ERROR_PATTERN" "$LOG_DIR/runner.log"; then
  printf 'migration_atomicity=expected_sentinel_missing\n' >&2
  exit 1
fi

if ! scan_runner_log; then
  printf 'migration_atomicity=log_scan_failed\n' >&2
  exit 1
fi

RESULT="$(docker exec -i "supabase_db_${PROJECT_ID}" \
  psql -U postgres -d postgres -Atq -v ON_ERROR_STOP=1 <<SQL
select
  to_regclass('${PROBE}') is not null,
  exists (
    select 1
    from supabase_migrations.schema_migrations
    where version = '${VERSION}'
  );
SQL
)"

test "$RESULT" = 'f|f'
PROOF_CAPTURED=1
test "$PROOF_CAPTURED" = '1'
cleanup_resources
trap - EXIT INT TERM
printf '%s\n' \
  'migration_atomicity_expected_failure=ok' \
  'migration_atomicity_expected_sentinel=ok' \
  'migration_atomicity_log_scan=ok' \
  'migration_atomicity_object_rollback=ok' \
  'migration_atomicity_version_rollback=ok' \
  'migration_atomicity_cleanup=ok'
