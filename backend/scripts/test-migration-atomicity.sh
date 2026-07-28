#!/usr/bin/env bash
set -euo pipefail

PROJECT="${1:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
CLI="${SUPABASE_CLI:-$PROJECT/node_modules/.bin/supabase}"
PROJECT_ID='spotify-booster-commercial-phase-a'
VERSION='29991231235959'
MIGRATION="$PROJECT/supabase/migrations/${VERSION}_atomicity_probe.sql"
PROBE='public.spotify_booster_migration_atomicity_probe'
LOG_DIR="$(mktemp -d /tmp/spotify-booster-atomicity.XXXXXX)"
PROOF_CAPTURED=0

cleanup() {
  if docker inspect "supabase_db_${PROJECT_ID}" >/dev/null 2>&1; then
    docker exec -i "supabase_db_${PROJECT_ID}" \
      psql -U postgres -d postgres -q -v ON_ERROR_STOP=1 >/dev/null 2>&1 <<SQL || true
drop table if exists ${PROBE};
delete from supabase_migrations.schema_migrations where version = '${VERSION}';
SQL
  fi
  rm -f -- "$MIGRATION"
  case "$LOG_DIR" in
    /tmp/spotify-booster-atomicity.*) rm -rf -- "$LOG_DIR" ;;
    *) printf 'migration_atomicity_cleanup=unsafe_path\n' >&2; return 99 ;;
  esac
}
trap cleanup EXIT INT TERM

test "$("$CLI" --version)" = '2.109.1'
test ! -e "$MIGRATION"

cat >"$MIGRATION" <<'SQL'
create table public.spotify_booster_migration_atomicity_probe(id integer);
select public.spotify_booster_force_expected_migration_failure();
SQL

if timeout --foreground 120 env SSL_CERT_FILE="${SSL_CERT_FILE:?}" \
  "$CLI" migration up --local --workdir "$PROJECT" \
  >"$LOG_DIR/runner.log" 2>&1; then
  printf 'migration_atomicity=runner_unexpected_success\n' >&2
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
cleanup
trap - EXIT INT TERM
printf '%s\n' \
  'migration_atomicity_expected_failure=ok' \
  'migration_atomicity_object_rollback=ok' \
  'migration_atomicity_version_rollback=ok' \
  'migration_atomicity_cleanup=ok'
