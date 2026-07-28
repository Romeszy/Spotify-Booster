# Third-party notices

Package versions were verified from the repository’s `packages.lock.json` files on 28 July 2026. These notices identify dependencies and license families; they do not replace the complete license texts that a distributor must bundle.

## Runtime dependencies

### NAudio

| Package | Version | Use | License |
|---|---:|---|---|
| `NAudio.Wasapi` | 2.3.0 | WASAPI capture/output and Core Audio endpoints | MIT |
| `NAudio.Core` | 2.3.0 | Wave formats, sample providers and WDL resampling | MIT |

Copyright © Mark Heath and contributors.

- Project: <https://github.com/naudio/NAudio>
- Package: <https://www.nuget.org/packages/NAudio.Wasapi/2.3.0>
- License: <https://github.com/naudio/NAudio/blob/v2.3.0/license.txt>

Spotify Booster references the smaller `NAudio.Wasapi` package rather than the full `NAudio` metapackage. `NAudio.Core` is resolved transitively.

The MIT license is permissive, but redistribution must preserve its copyright and permission notice.

### Microsoft.Extensions and supporting packages

Direct packages:

- `Microsoft.Extensions.Configuration.Json` 10.0.10
- `Microsoft.Extensions.DependencyInjection` 10.0.10
- `Microsoft.Extensions.Logging` 10.0.10
- `Microsoft.Extensions.Logging.Debug` 10.0.10

Resolved runtime support packages, all version 10.0.10:

- `Microsoft.Extensions.Configuration`
- `Microsoft.Extensions.Configuration.Abstractions`
- `Microsoft.Extensions.Configuration.FileExtensions`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.FileProviders.Abstractions`
- `Microsoft.Extensions.FileProviders.Physical`
- `Microsoft.Extensions.FileSystemGlobbing`
- `Microsoft.Extensions.Logging.Abstractions`
- `Microsoft.Extensions.Options`
- `Microsoft.Extensions.Primitives`
- `System.Diagnostics.DiagnosticSource`
- `System.IO.Pipelines`
- `System.Text.Encodings.Web`
- `System.Text.Json`

Copyright © Microsoft Corporation and contributors. License: MIT.

- Repositories: <https://github.com/dotnet/runtime>, <https://github.com/dotnet/extensions>
- License: <https://github.com/dotnet/runtime/blob/main/LICENSE.TXT>

The 10.0.10 packages support `net8.0`. Spotify Booster still targets .NET 8. .NET 8 support ends on 10 November 2026, so a maintained release must migrate or otherwise address the unsupported runtime after that date.

### Microsoft.AspNetCore.Authentication.JwtBearer

| Package | Version | Use | License |
|---|---:|---|---|
| `Microsoft.AspNetCore.Authentication.JwtBearer` | 8.0.29 | ASP.NET Core JWT authentication foundation for the Phase A API | MIT |

Copyright © .NET Foundation and contributors.

- Upstream: <https://github.com/dotnet/aspnetcore/tree/v8.0.29>
- Package: <https://www.nuget.org/packages/Microsoft.AspNetCore.Authentication.JwtBearer/8.0.29>
- License: <https://github.com/dotnet/aspnetcore/blob/v8.0.29/LICENSE.txt>

This package is used by the API project only. It is not included as an
authentication feature in the WPF desktop application.

### .NET Desktop Runtime

Framework-dependent packages require the Microsoft .NET Desktop Runtime 8 on the target machine. Self-contained packages include runtime files produced by the .NET SDK.

Distributors must review the applicable Microsoft .NET distribution terms and include the required notices for the exact published output. The repository’s NuGet inventory alone is not a complete runtime notice set.

## Test-only dependencies

These packages build or run tests; they are not application features.

### xUnit.net

Direct packages:

- `xunit.v3` 3.2.2
- `xunit.runner.visualstudio` 3.1.5

Resolved xUnit packages:

- `xunit.analyzers` 1.27.0
- `xunit.v3.assert` 3.2.2
- `xunit.v3.common` 3.2.2
- `xunit.v3.core.mtp-v1` 3.2.2
- `xunit.v3.extensibility.core` 3.2.2
- `xunit.v3.mtp-v1` 3.2.2
- `xunit.v3.runner.common` 3.2.2
- `xunit.v3.runner.inproc.console` 3.2.2

Copyright © .NET Foundation and contributors. License: Apache License 2.0.

- Project: <https://github.com/xunit/xunit>
- License: <https://github.com/xunit/xunit/blob/main/LICENSE>

Apache-2.0 is permissive but includes notice, attribution and patent-license conditions. A distributor must include the license text and any applicable notices.

### Microsoft test platform

Direct package:

- `Microsoft.NET.Test.Sdk` 18.8.1

Resolved packages:

- `Microsoft.CodeCoverage` 18.8.1
- `Microsoft.TestPlatform.ObjectModel` 18.8.1
- `Microsoft.TestPlatform.TestHost` 18.8.1
- `Microsoft.Testing.Platform` 1.9.1
- `Microsoft.Testing.Platform.MSBuild` 1.9.1
- `Microsoft.Testing.Extensions.Telemetry` 1.9.1
- `Microsoft.Testing.Extensions.TrxReport.Abstractions` 1.9.1
- `Microsoft.ApplicationInsights` 2.23.0

License family: MIT.

- Test platform: <https://github.com/microsoft/vstest>
- Microsoft Testing Platform: <https://github.com/microsoft/testfx>
- License: <https://github.com/microsoft/vstest/blob/main/LICENSE>

`Microsoft.Testing.Extensions.Telemetry` is a transitive test-runner dependency. It is not referenced by the WPF application and does not add application telemetry.

### Microsoft.AspNetCore.Mvc.Testing

| Package | Version | Use | License |
|---|---:|---|---|
| `Microsoft.AspNetCore.Mvc.Testing` | 8.0.29 | Hosted ASP.NET Core API integration tests | MIT |

Copyright © .NET Foundation and contributors.

- Upstream: <https://github.com/dotnet/aspnetcore/tree/v8.0.29>
- Package: <https://www.nuget.org/packages/Microsoft.AspNetCore.Mvc.Testing/8.0.29>
- License: <https://github.com/dotnet/aspnetcore/blob/v8.0.29/LICENSE.txt>

This package is test-only and is not a runtime dependency of the desktop or
deployed API.

### Microsoft/System compatibility packages used by tests

- `Microsoft.Bcl.AsyncInterfaces` 6.0.0
- `Microsoft.NETCore.Platforms` 5.0.0
- `Microsoft.Win32.Registry` 5.0.0
- `System.Collections.Immutable` 8.0.0
- `System.Diagnostics.DiagnosticSource` 5.0.0 or 10.0.10
- `System.Reflection.Metadata` 8.0.0
- `System.Security.AccessControl` 5.0.0
- `System.Security.Principal.Windows` 5.0.0

Copyright © Microsoft Corporation and contributors. License family: MIT.

- License: <https://github.com/dotnet/runtime/blob/main/LICENSE.TXT>

Some version 5.0.0 transitive test dependencies have previously been reported as legacy/deprecated. They are not runtime dependencies of Spotify Booster. Vulnerability and deprecation audits must be rerun for the final release; this notice does not claim a fresh security result.

## External software not distributed

### Supabase CLI

| Package | Version | Use | License |
|---|---:|---|---|
| npm package `supabase` | 2.109.1 | Local database/Auth/TLS development and pgTAP execution | MIT |

Copyright © Supabase, Inc. and contributors.

- Upstream: <https://github.com/supabase/cli/tree/v2.109.1>
- Package: <https://www.npmjs.com/package/supabase/v/2.109.1>
- License: <https://github.com/supabase/cli/blob/v2.109.1/LICENSE>

The exact version is a development-only dependency in
`backend/package-lock.json`. The bootstrap uses the lock-installed binary and
does not substitute a global CLI. Supabase CLI, its platform binary,
`node_modules`, Docker images and local `.local` TLS/signing material must not
be included in WPF release artifacts.

### NSIS

NSIS 3.12 is the verified build-time dependency used to create `SpotifyBooster-Setup-x64.exe`. It is not a runtime dependency of the portable application. The generated installer contains NSIS installer components, so the distribution includes the applicable NSIS zlib/libpng-style license notice.

- Project: <https://nsis.sourceforge.io/>
- License: <https://nsis.sourceforge.io/License>

The generated portable and installer include `LICENSE.txt`, this notice file and the `licenses/` directory. The release script rebuilds them from the final clean commit. They remain unsigned because no Authenticode certificate is configured.

### VB-CABLE

VB-CABLE is optional external driver software installed manually by the user. Spotify Booster may open the fixed vendor URL <https://vb-audio.com/Cable/>, but it does not download, install, link, bundle or redistribute VB-CABLE.

Its license and commercial-use conditions are controlled by VB-Audio. They are not covered by the MIT/Apache notices above.

### Spotify

Spotify Desktop is external software. This project is not affiliated with or endorsed by Spotify. Spotify names and marks belong to their respective owner. No Spotify SDK, API or package is included.

## Project artwork

The application icon and its active/inactive local variants are original project assets, not third-party icon-library or Spotify assets. They are outside this third-party inventory and remain subject to the repository’s `LICENSE.txt`.

## Distribution requirements

Before distributing a build:

1. generate an SBOM from the final published files;
2. include the complete MIT and Apache-2.0 texts and applicable notices;
3. include the .NET runtime notices required by the chosen publish mode;
4. verify that no VB-CABLE installer or driver is present;
5. rerun vulnerability and deprecation audits;
6. obtain legal review where required.

MIT and Apache-2.0 are commonly compatible with commercial distribution when their conditions are met. This file is an engineering inventory, not legal advice.
