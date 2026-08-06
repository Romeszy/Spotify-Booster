# Local PostgreSQL, RLS, and strict Supabase bootstrap

This directory is the Phase A **local development** backend. It is not a
production deployment and contains no production credentials.

## Prerequisites

- Node.js `>=20` (the bootstrap rejects missing, malformed, or older versions
  before invoking npm);
- Windows PowerShell 5.1+ or PowerShell 7, OpenSSL, and Docker Desktop with
  the engine running;
- an explicit decision by the developer to trust the generated loopback
  certificate in the current user account.

From `backend`:

```powershell
# Windows PowerShell 5.1+:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./scripts/bootstrap-supabase-local.ps1 -SkipStart
# Inspect ./supabase/config.toml and the certificate, then explicitly:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./scripts/bootstrap-supabase-local.ps1 -TrustCertificate

# With PowerShell 7, use pwsh instead of powershell.exe.
npm run db:reset
npm run db:lint
npm run test:db
npm run test:db:atomicity
npm run gate:local:auth-api
```

The script always uses the lock-installed Supabase CLI `2.109.1`; a global CLI
is never substituted. `npm ci` is used, and Docker is checked before any stack
start. The only conforming API origin is `https://127.0.0.1:54321`. There is no
HTTP or permissive-certificate fallback.

The Linux gate downloads the fixed official .NET SDK `8.0.423` archive and
verifies its pinned SHA-512 before extraction. It does not download or execute
`dotnet-install.sh`. Output from Supabase start/reset is discarded rather than
persisted; every remaining temporary `*.log` file is scanned without echoing
matched content before the temporary directory is removed.

## Local secret handling

`supabase/.local/` is ignored by the nested `.gitignore`. It contains only
machine-local TLS and Auth signing material. The bootstrap:

- creates a P-256 loopback certificate only when both certificate and key are
  absent;
- refuses incomplete, expired/not-yet-valid, missing-IP-SAN, or mismatched TLS
  material without overwriting it;
- creates an exact two-byte `[]` canonical placeholder with exclusive
  creation, then creates an ACL-restricted temporary Supabase project under
  `.local/auth` whose configured `signing_keys_path` points to a separate `[]`
  staging file; CLI `2.109.1` truncates and writes only that staging path while
  both process streams are discarded, never captured;
- validates the staged exact ES256/P-256 JWK fields and a private-sign/public-
  verify round trip, atomically replaces the canonical placeholder, then after
  startup proves that JWKS matches `kid/x/y`, contains no private `d`, and
  verifies through stdin an Auth-issued ES256 JWT before deleting its generated
  local user fixture;
- validates but never overwrites an existing signing key;
- applies `0700` directories and `0600` private files on POSIX. On Windows the
  bootstrap disables file ACL inheritance and grants the current SID and
  `SYSTEM` only; after atomic signing-key replacement it reapplies and verifies
  the final directory and file permissions before accepting the key;
- recursively deletes the private staging project, canonical placeholder,
  JWKS proof file, and other temporary files after failure or completion.

Never commit, paste into logs, or attach `.local` files. Certificate trust is
not changed unless `-TrustCertificate` is explicitly passed. The automated
Linux gate never installs a certificate in a Windows trust store.

## Schema and authorization boundary

Migrations are intentionally split into schema, trigger, and RLS/grant layers.
`profiles` is owner-readable and only `display_name` is owner-writable.
`entitlements` is owner-readable and user-immutable. A `NOLOGIN`, `NOINHERIT`
role models a future server writer and is not granted to Data API roles.

`anon` receives only schema usage needed by PostgREST and execution of the
constant zero-argument `spotify_booster_health_v1()` RPC. It receives no table,
view, sequence, or data-bearing function privilege. The helper schema is not
exposed to `public`, `anon`, or `authenticated`.

`authenticated` alone may execute `spotify_booster_me_v1()`. The function has
no arguments, runs as `SECURITY INVOKER`, and obtains identity only from
`auth.uid()` so a client cannot choose a `user_id`.

The feature allowlist uses the shared contract identifiers:
`AdvancedGain`, `ExpertMode`, `AdvancedBassBoost`, `Equalizer`,
`CustomProfiles`, `GlobalHotkeys`, `TrayAdvancedControls`, `AutoStart`,
`AdvancedDiagnostics`, and `FuturePremiumFeatures`.

## Evidence boundary

`npm audit --package-lock-only --audit-level=high` and static checks can run
without the stack. Reset, lint, pgTAP, real RPC, two-user IDOR, Auth password
length, refresh rotation, migration atomicity, real ASP.NET `/v1/me`,
outage/recovery readiness, and TLS/JWKS behavior are runtime evidence and must
not be reported as passing unless Docker and the explicitly trusted strict TLS
origin were actually exercised.

The atomicity probe is fail-closed. Before running the disposable migration,
the test creates a PostgreSQL sequence outside the migration transaction. The
migration creates a probe table, raises the dedicated `P0001` exception, then
catches it to verify its exact SQLSTATE and message. Only that verified handler
advances the sequence before rethrowing. The gate accepts atomicity only when
the runner fails, the non-transactional sequence advance remains visible, and
both the probe table and migration-version row are absent. A TLS, CLI, parser,
unrelated SQL, or pre-execution failure cannot satisfy this combined proof.
