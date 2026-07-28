# Local PostgreSQL, RLS, and strict Supabase bootstrap

This directory is the Phase A **local development** backend. It is not a
production deployment and contains no production credentials.

## Prerequisites

- Node.js `>=20` (the bootstrap rejects missing, malformed, or older versions
  before invoking npm);
- PowerShell 7, OpenSSL, and Docker Desktop with the engine running;
- an explicit decision by the developer to trust the generated loopback
  certificate in the current user account.

From `backend`:

```powershell
pwsh -NoProfile -File ./scripts/bootstrap-supabase-local.ps1 -SkipStart
# Inspect ./.supabase configuration and the certificate, then explicitly:
pwsh -NoProfile -File ./scripts/bootstrap-supabase-local.ps1 -TrustCertificate
npm run db:reset
npm run db:lint
npm run test:db
```

The script always uses the lock-installed Supabase CLI `2.109.1`; a global CLI
is never substituted. `npm ci` is used, and Docker is checked before any stack
start. The only conforming API origin is `https://127.0.0.1:54321`. There is no
HTTP or permissive-certificate fallback.

## Local secret handling

`supabase/.local/` is ignored by the nested `.gitignore`. It contains only
machine-local TLS and Auth signing material. The bootstrap:

- creates a P-256 loopback certificate only when both certificate and key are
  absent;
- refuses incomplete, expired/not-yet-valid, missing-IP-SAN, or mismatched TLS
  material without overwriting it;
- creates an exact two-byte `[]` signing placeholder with exclusive creation,
  captures CLI output outside the transcript, validates exact ES256/P-256 JWK
  fields and a sign/verify round trip, then replaces the placeholder atomically;
- validates but never overwrites an existing signing key;
- applies `0700` directories and `0600` private files on POSIX. On Windows the
  bootstrap disables file ACL inheritance and grants the current SID and
  `SYSTEM` only when it creates a private file;
- deletes capture, placeholder, and temporary files after any failed signing
  generation.

Never commit, paste into logs, or attach `.local` files. Certificate trust is
not changed unless `-TrustCertificate` is explicitly passed.

## Schema and authorization boundary

Migrations are intentionally split into schema, trigger, and RLS/grant layers.
`profiles` is owner-readable and only `display_name` is owner-writable.
`entitlements` is owner-readable and user-immutable. A `NOLOGIN`, `NOINHERIT`
role models a future server writer and is not granted to Data API roles.

`anon` receives only schema usage needed by PostgREST and execution of the
constant zero-argument `spotify_booster_health_v1()` RPC. It receives no table,
view, sequence, or data-bearing function privilege. The helper schema is not
exposed to `public`, `anon`, or `authenticated`.

The feature allowlist uses the shared contract identifiers:
`AdvancedGain`, `ExpertMode`, `AdvancedBassBoost`, `Equalizer`,
`CustomProfiles`, `GlobalHotkeys`, `TrayAdvancedControls`, `AutoStart`,
`AdvancedDiagnostics`, and `FuturePremiumFeatures`.

## Evidence boundary

`npm audit --package-lock-only --audit-level=high` and static checks can run
without the stack. Reset, lint, pgTAP, hosted RPC, two-user IDOR, Auth password
length, refresh rotation, and TLS/JWKS behavior are runtime evidence and must
not be reported as passing unless Docker and the explicitly trusted strict TLS
origin were actually exercised.
