#!/usr/bin/env bash
set -euo pipefail

PROJECT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REPOSITORY="$(cd "$PROJECT/.." && pwd)"
CERT="$PROJECT/supabase/.local/tls/supabase-local.crt"
PROJECT_ID='spotify-booster-commercial-phase-a'
PUBLISHABLE_KEY='sb_publishable_ACJWlzQHlZjBrEguHvfOxg_3BJgxAaH'
TOOL_DIR="$(mktemp -d /tmp/spotify-booster-validation.XXXXXX)"
DOTNET_DIR="$TOOL_DIR/dotnet"
DOTNET_SDK_VERSION='8.0.423'
DOTNET_SDK_ARCHIVE="$TOOL_DIR/dotnet-sdk-${DOTNET_SDK_VERSION}-linux-x64.tar.gz"
DOTNET_SDK_URL="https://builds.dotnet.microsoft.com/dotnet/Sdk/${DOTNET_SDK_VERSION}/dotnet-sdk-${DOTNET_SDK_VERSION}-linux-x64.tar.gz"
DOTNET_SDK_SHA512='e94513dfe42271a85f01e87bd4272aa80b4ec13556f4531754802542225667775242c5e281a94837dae6cc65f7bcc457d2f663f240c0e2b7573fd909e786b1a5'
CLI=''
API_PID=''
API_PORT='5080'
CURRENT_STAGE='initialization'

is_api_port_listening() {
  local socket_status=0
  if (exec 3<>"/dev/tcp/127.0.0.1/$API_PORT") 2>/dev/null; then
    exec 3>&-
    exec 3<&-
    return 0
  else
    socket_status=$?
  fi
  return "$socket_status"
}

wait_for_api_port_release() {
  local attempt
  for attempt in $(seq 1 60); do
    if ! is_api_port_listening; then
      return 0
    fi
    sleep 0.1
  done
  return 1
}

scan_temporary_logs() {
  local grep_status
  local log_list="$TOOL_DIR/log-files.list"
  local log_file
  local forbidden_pattern
  forbidden_pattern='authorization:[[:space:]]*bearer|refresh_token|@example\.invalid|eyJ[A-Za-z0-9_-]{7,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}|sb_(publishable|secret)_[A-Za-z0-9_-]{20,}|service_role|BEGIN[[:space:]]+(RSA |EC )?PRIVATE KEY|postgres(ql)?://[^[:space:]]+|(database_url|db_password|pgpassword|password)[[:space:]]*[:=][[:space:]]*[^[:space:]]+|"(d|p|q|dp|dq|qi)"[[:space:]]*:[[:space:]]*"[A-Za-z0-9_-]{20,}"'

  if ! find "$TOOL_DIR" -type f -name '*.log' -print0 >"$log_list"; then
    return 2
  fi
  while IFS= read -r -d '' log_file; do
    if grep -Eqi -- "$forbidden_pattern" "$log_file" >/dev/null 2>&1; then
      return 1
    else
      grep_status=$?
      if (( grep_status != 1 )); then
        return 2
      fi
    fi
    if grep -Fq -- "$PUBLISHABLE_KEY" "$log_file" >/dev/null 2>&1; then
      return 1
    else
      grep_status=$?
      if (( grep_status != 1 )); then
        return 2
      fi
    fi
  done <"$log_list"
}

stop_runtime() {
  local containers=''
  local runtime_status=0

  if [[ -n "$API_PID" ]]; then
    if kill -0 "$API_PID" >/dev/null 2>&1 &&
       ! kill "$API_PID" >/dev/null 2>&1; then
      printf 'validation_cleanup=api_stop_failed\n' >&2
      runtime_status=1
    fi
    wait "$API_PID" >/dev/null 2>&1 || true
    if kill -0 "$API_PID" >/dev/null 2>&1; then
      printf 'validation_cleanup=api_process_remaining\n' >&2
      runtime_status=1
    else
      API_PID=''
    fi
  fi
  if ! wait_for_api_port_release; then
    printf 'validation_cleanup=api_port_remaining\n' >&2
    runtime_status=1
  fi
  if [[ -n "$CLI" && -x "$CLI" ]]; then
    if "$CLI" stop --project-id "$PROJECT_ID" >/dev/null 2>&1; then
      CLI=''
    else
      printf 'validation_cleanup=supabase_stop_failed\n' >&2
      runtime_status=1
    fi
  fi
  if ! containers="$(docker ps -a \
    --filter label=com.supabase.cli.project="$PROJECT_ID" \
    --format '{{.Names}}')"; then
    printf 'validation_cleanup=docker_inspection_failed\n' >&2
    runtime_status=1
  elif [[ -n "$containers" ]]; then
    printf 'validation_cleanup=containers_remaining\n' >&2
    runtime_status=1
  fi
  return "$runtime_status"
}

remove_tool_directory() {
  case "$TOOL_DIR" in
    /tmp/spotify-booster-validation.*)
      if ! rm -rf -- "$TOOL_DIR" || [[ -e "$TOOL_DIR" ]]; then
        printf 'validation_cleanup=tool_directory_removal_failed\n' >&2
        return 1
      fi
      ;;
    *)
      printf 'validation_cleanup=unsafe_path\n' >&2
      return 99
      ;;
  esac
}

perform_cleanup() {
  local cleanup_status=0
  stop_runtime || cleanup_status=$?
  if ! scan_temporary_logs; then
    printf 'sensitive_log_scan=failed\n' >&2
    cleanup_status=1
  fi
  remove_tool_directory || cleanup_status=$?
  return "$cleanup_status"
}

cleanup() {
  local exit_code=$?
  local cleanup_code=0
  trap - EXIT INT TERM
  perform_cleanup || cleanup_code=$?
  if (( cleanup_code != 0 )); then
    exit_code=$cleanup_code
  fi
  exit "$exit_code"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
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
  --output "$DOTNET_SDK_ARCHIVE" "$DOTNET_SDK_URL"
printf '%s  %s\n' "$DOTNET_SDK_SHA512" "$DOTNET_SDK_ARCHIVE" |
  sha512sum --check --status
mkdir -p -- "$DOTNET_DIR"
tar -xzf "$DOTNET_SDK_ARCHIVE" -C "$DOTNET_DIR"
DOTNET="$DOTNET_DIR/dotnet"
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
test "$("$DOTNET" --version)" = "$DOTNET_SDK_VERSION"

CURRENT_STAGE='supabase_start'
timeout --foreground 180 env SSL_CERT_FILE="$CERT" \
  "$CLI" start --workdir "$PROJECT" >/dev/null 2>&1

CURRENT_STAGE='database_reset'
timeout --foreground 180 env SSL_CERT_FILE="$CERT" \
  "$CLI" db reset --local --workdir "$PROJECT" >/dev/null 2>&1

CURRENT_STAGE='database_lint'
timeout --foreground 180 env SSL_CERT_FILE="$CERT" \
  "$CLI" db lint --local --workdir "$PROJECT" \
  --level error --fail-on error >lint.log 2>&1

CURRENT_STAGE='pgtap'
timeout --foreground 180 env SSL_CERT_FILE="$CERT" \
  "$CLI" test db --local --workdir "$PROJECT" >pgtap.log 2>&1
grep -q 'Result: PASS' pgtap.log
grep -q 'Tests=111' pgtap.log

CURRENT_STAGE='migration_atomicity'
if ! timeout --foreground 180 env \
  SSL_CERT_FILE="$CERT" SUPABASE_CLI="$CLI" \
  bash "$PROJECT/scripts/test-migration-atomicity.sh" "$PROJECT" \
  >atomicity.log 2>&1; then
  grep -E '^migration_atomicity=[a-z0-9_]+$' atomicity.log >&2 || true
  false
fi
grep -q 'migration_atomicity_object_rollback=ok' atomicity.log
grep -q 'migration_atomicity_version_rollback=ok' atomicity.log
grep -q 'migration_atomicity_execution_marker=ok' atomicity.log
grep -q 'migration_atomicity_log_scan=ok' atomicity.log

CURRENT_STAGE='database_reset_after_atomicity'
timeout --foreground 180 env SSL_CERT_FILE="$CERT" \
  "$CLI" db reset --local --workdir "$PROJECT" >/dev/null 2>&1

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
if is_api_port_listening; then
  printf 'api_start=port_in_use\n' >&2
  exit 1
fi
API_DLL="$REPOSITORY/src/SpotifyBooster.Api/bin/Release/net8.0/SpotifyBooster.Api.dll"
test -f "$API_DLL"
env \
  SSL_CERT_FILE="$CERT" \
  DOTNET_CLI_HOME="$TOOL_DIR/dotnet-home" \
  ASPNETCORE_URLS='http://127.0.0.1:5080' \
  Supabase__Origin='https://127.0.0.1:54321/' \
  Supabase__PublishableKey="$PUBLISHABLE_KEY" \
  Supabase__Issuer='https://127.0.0.1:54321/auth/v1' \
  Supabase__Audience='authenticated' \
  Supabase__JwksPath='/auth/v1/.well-known/jwks.json' \
  "$DOTNET" "$API_DLL" >"$TOOL_DIR/api.log" 2>&1 &
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
grep -q 'refresh_direct_parent_recovery=ok' "$TOOL_DIR/e2e.log"
grep -q 'refresh_reuse_family_revocation=ok' "$TOOL_DIR/e2e.log"
grep -q 'readiness_stop_restart=ok' "$TOOL_DIR/e2e.log"
grep -q 'sensitive_log_scan=ok' "$TOOL_DIR/e2e.log"

CURRENT_STAGE='sensitive_log_scan'
if ! scan_temporary_logs; then
  printf 'sensitive_log_scan=failed\n' >&2
  exit 1
fi

CURRENT_STAGE='jwks_tls'
curl --silent --show-error --fail \
  --cacert "$CERT" \
  --output /dev/null \
  https://127.0.0.1:54321/auth/v1/.well-known/jwks.json

CURRENT_STAGE='explicit_cleanup'
cleanup_code=0
perform_cleanup || cleanup_code=$?
trap - EXIT INT TERM
if (( cleanup_code != 0 )); then
  exit "$cleanup_code"
fi

CURRENT_STAGE='complete'
printf '%s\n' \
  'cli=2.109.1' \
  'dotnet_linux=8.0.423' \
  'start=ok' \
  'reset=ok' \
  'lint=ok' \
  'pgtap=111/111' \
  'migration_atomicity=ok' \
  'api_real_process=ok' \
  'auth_api_e2e=ok' \
  'jwks_tls=ok' \
  'sensitive_log_scan=ok' \
  'cleanup=ok'
