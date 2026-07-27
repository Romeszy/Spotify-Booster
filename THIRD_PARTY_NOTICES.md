# Third-party notices

Versions resolved on 27 July 2026 from NuGet. Exact dependency graphs are locked in each project’s `packages.lock.json`.

These notices identify dependencies and licenses; they do not replace the license texts distributed by their authors.

## Runtime dependencies

### NAudio

| Package | Version | Use | License |
|---|---:|---|---|
| `NAudio.Wasapi` | 2.3.0 | WASAPI capture/output and Core Audio endpoints | MIT |
| `NAudio.Core` | 2.3.0 | Wave formats, sample providers and WDL resampler | MIT |

Copyright © Mark Heath and contributors.

- Project: <https://github.com/naudio/NAudio>
- Package: <https://www.nuget.org/packages/NAudio.Wasapi/2.3.0>
- License: <https://github.com/naudio/NAudio/blob/v2.3.0/license.txt>

The smaller `NAudio.Wasapi` package is used instead of the full `NAudio` metapackage to avoid unused ASIO, MIDI, WinMM and WinForms modules.

### Microsoft.Extensions and supporting Microsoft packages

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

The 10.0.10 packages explicitly support `net8.0`. The application still targets .NET 8 as required; migration to .NET 10 LTS is tracked because .NET 8 support ends on 10 November 2026.

## Test-only dependencies

These packages are used to build/run tests and are not application features.

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

### Microsoft test platform

Direct package:

- `Microsoft.NET.Test.Sdk` 18.8.1

Resolved test platform packages:

- `Microsoft.CodeCoverage` 18.8.1
- `Microsoft.TestPlatform.ObjectModel` 18.8.1
- `Microsoft.TestPlatform.TestHost` 18.8.1
- `Microsoft.Testing.Platform` 1.9.1
- `Microsoft.Testing.Platform.MSBuild` 1.9.1
- `Microsoft.Testing.Extensions.Telemetry` 1.9.1
- `Microsoft.Testing.Extensions.TrxReport.Abstractions` 1.9.1
- `Microsoft.ApplicationInsights` 2.23.0

License: MIT.

- Test platform: <https://github.com/microsoft/vstest>
- Microsoft Testing Platform: <https://github.com/microsoft/testfx>
- License: <https://github.com/microsoft/vstest/blob/main/LICENSE>

`Microsoft.Testing.Extensions.Telemetry` is a transitive **test-runner** dependency. It is not referenced by the WPF application. The validation commands used for this delivery set `DOTNET_CLI_TELEMETRY_OPTOUT=1`; the repository does not currently enforce that environment variable through a script.

The NuGet deprecation audit reports `Microsoft.NETCore.Platforms` 5.0.0, `System.Security.AccessControl` 5.0.0 and, for the DSP test project, `System.Diagnostics.DiagnosticSource` 5.0.0 as legacy transitive test dependencies. They are not runtime dependencies of Spotify Booster. No direct or transitive package was reported as vulnerable by the NuGet audit on 27 July 2026.

### Microsoft/System compatibility packages used by tests

- `Microsoft.Bcl.AsyncInterfaces` 6.0.0
- `Microsoft.NETCore.Platforms` 5.0.0
- `Microsoft.Win32.Registry` 5.0.0
- `System.Collections.Immutable` 8.0.0
- `System.Diagnostics.DiagnosticSource` 5.0.0/10.0.10
- `System.Reflection.Metadata` 8.0.0
- `System.Security.AccessControl` 5.0.0
- `System.Security.Principal.Windows` 5.0.0

Copyright © Microsoft Corporation and contributors. License: MIT.

- License: <https://github.com/dotnet/runtime/blob/main/LICENSE.TXT>

## Commercial-use note

MIT and Apache-2.0 are permissive licenses commonly compatible with commercial distribution when their notice and license obligations are followed. This file is an engineering inventory, not legal advice. A release process should generate an SBOM, bundle the full license texts and obtain legal review for the product’s use of Spotify audio/content.
