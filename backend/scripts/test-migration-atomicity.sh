#!/usr/bin/env bash
set -euo pipefail

PROJECT="${1:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
CLI="${SUPABASE_CLI:-$PROJECT/node_modules/.bin/supabase}"
PROJECT_ID='spotify-booster-commercial-phase-a'
VERSION='29991231235959'
MIGRATION="$PROJECT/supabase/migrations/${VERSION}_atomicity_probe.sql"
PROBE='public.spotify_booster_migration_atomicity_probe'
SIGNAL='public.spotify_booster_migration_atomicity_signal'
LOG_DIR="$(mktemp -d /tmp/spotify-booster-atomicity.XXXXXX)"
FORBIDDEN_LOG_PATTERN='authorization:[[:space:]]*bearer|refresh_token|@example\.invalid|eyJ[A-Za-z0-9_-]{7,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}|sb_(publishable|secret)_[A-Za-z0-9_-]{20,}|service_role|BEGIN[[:space:]]+(RSA |EC )?PRIVATE KEY|postgres(ql)?://[^[:space:]]+|(database_url|db_password|pgpassword|password)[[:space:]]*[:=][[:space:]]*[^[:space:]]+|"(d|p|q|dp|dq|qi)"[[:space:]]*:[[:space:]]*"[A-Za-z0-9_-]{20,}"'
PROOF_CAPTURED=0

scan_runner_log() {
  local grep_status
  local log_list="$LOG_DIR/log-files.list"
  local log_file

  if ! find "$LOG_DIR" -type f -name '*.log' -print0 >"$log_list"; then
    return 2
  fi
  while IFS= read -r -d '' log_file; do
    if grep -Eqi -- "$FORBIDDEN_LOG_PATTERN" "$log_file" >/dev/null 2>&1; then
      return 1
    else
      grep_status=$?
      if (( grep_status != 1 )); then
        return 2
      fi
    fi
  done <"$log_list"
}

cleanup_resources() {
  local cleanup_status=0
  local cleanup_state=''
  if docker inspect "supabase_db_${PROJECT_ID}" >/dev/null 2>&1; then
    if ! docker exec -i "supabase_db_${PROJECT_ID}" \
      psql -U postgres -d postgres -q -v ON_ERROR_STOP=1 >/dev/null 2>&1 <<SQL
drop table if exists ${PROBE};
drop sequence if exists ${SIGNAL};
delete from supabase_migrations.schema_migrations where version = '${VERSION}';
SQL
    then
      printf 'migration_atomicity=database_cleanup_failed\n' >&2
      cleanup_status=1
    elif ! cleanup_state="$(docker exec -i "supabase_db_${PROJECT_ID}" \
      psql -U postgres -d postgres -Atq -v ON_ERROR_STOP=1 <<SQL
select
  to_regclass('${PROBE}') is null,
  to_regclass('${SIGNAL}') is null,
  not exists (
    select 1
    from supabase_migrations.schema_migrations
    where version = '${VERSION}'
  );
SQL
)"; then
      printf 'migration_atomicity=database_cleanup_verification_failed\n' >&2
      cleanup_status=1
    elif [[ "$cleanup_state" != 't|t|t' ]]; then
      printf 'migration_atomicity=database_cleanup_incomplete\n' >&2
      cleanup_status=1
    fi
  else
    printf 'migration_atomicity=database_cleanup_unavailable\n' >&2
    cleanup_status=1
  fi
  if ! scan_runner_log; then
    printf 'migration_atomicity=log_scan_failed\n' >&2
    cleanup_status=1
  fi
  if ! rm -f -- "$MIGRATION" || [[ -e "$MIGRATION" ]]; then
    printf 'migration_atomicity=migration_cleanup_failed\n' >&2
    cleanup_status=1
  fi
  case "$LOG_DIR" in
    /tmp/spotify-booster-atomicity.*)
      if ! rm -rf -- "$LOG_DIR" || [[ -e "$LOG_DIR" ]]; then
        printf 'migration_atomicity=log_cleanup_failed\n' >&2
        cleanup_status=1
      fi
      ;;
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

INITIAL_STATE="$(docker exec -i "supabase_db_${PROJECT_ID}" \
  psql -U postgres -d postgres -Atq -v ON_ERROR_STOP=1 <<SQL
select
  to_regclass('${PROBE}') is null,
  to_regclass('${SIGNAL}') is null,
  not exists (
    select 1
    from supabase_migrations.schema_migrations
    where version = '${VERSION}'
  );
SQL
)"
test "$INITIAL_STATE" = 't|t|t'

docker exec -i "supabase_db_${PROJECT_ID}" \
  psql -U postgres -d postgres -q -v ON_ERROR_STOP=1 >/dev/null <<SQL
create sequence ${SIGNAL};
SQL

cat >"$MIGRATION" <<'SQL'
create table public.spotify_booster_migration_atomicity_probe(id integer);
do $atomicity$
declare
  caught_message text;
  caught_sqlstate text;
begin
  begin
    raise exception using
      errcode = 'P0001',
      message = 'SPOTIFY_BOOSTER_ATOMICITY_SENTINEL_7F31A9C2';
  exception
    when sqlstate 'P0001' then
      get stacked diagnostics
        caught_message = message_text,
        caught_sqlstate = returned_sqlstate;
      if caught_message is distinct from
          'SPOTIFY_BOOSTER_ATOMICITY_SENTINEL_7F31A9C2'
        or caught_sqlstate is distinct from 'P0001' then
        raise exception 'unexpected atomicity probe exception';
      end if;
      perform nextval('public.spotify_booster_migration_atomicity_signal');
      raise;
  end;
end
$atomicity$;
SQL

if timeout --foreground 120 env SSL_CERT_FILE="${SSL_CERT_FILE:?}" \
  "$CLI" migration up --local --workdir "$PROJECT" \
  >"$LOG_DIR/runner.log" 2>&1; then
  printf 'migration_atomicity=runner_unexpected_success\n' >&2
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
  ),
  (
    select is_called and last_value = 1
    from ${SIGNAL}
  );
SQL
)"

test "$RESULT" = 'f|f|t'
PROOF_CAPTURED=1
test "$PROOF_CAPTURED" = '1'
cleanup_code=0
cleanup_resources || cleanup_code=$?
trap - EXIT INT TERM
if (( cleanup_code != 0 )); then
  printf 'migration_atomicity=cleanup_failed\n' >&2
  exit "$cleanup_code"
fi
printf '%s\n' \
  'migration_atomicity_expected_failure=ok' \
  'migration_atomicity_execution_marker=ok' \
  'migration_atomicity_log_scan=ok' \
  'migration_atomicity_object_rollback=ok' \
  'migration_atomicity_version_rollback=ok' \
  'migration_atomicity_cleanup=ok'
