# Building

## Requirements

- Windows 10 or 11, x64
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (`dotnet --list-sdks` should show `9.0.x`)
- For installers only: [Inno Setup 6](https://jrsoftware.org/isinfo.php) and the Windows SDK (MakeAppx/SignTool)

## Build and test

Build individual projects. The solution also contains a Windows packaging project (`.wapproj`) that needs Visual Studio's MSBuild.

```powershell
dotnet build LenovoLegionToolkit.WPF\LenovoLegionToolkit.WPF.csproj -c Release
dotnet build LenovoLegionToolkit.CLI\LenovoLegionToolkit.CLI.csproj -c Release
dotnet test  LenovoLegionToolkit.Tests\LenovoLegionToolkit.Tests.csproj -c Release
```

Build output goes to `BuildLLT\bin\` (configured in `Directory.Build.props`).

## Dependencies

The five `LLT.*` packages are pinned to the versions that were current when v2.36.0.0 was released. Newer versions removed APIs this code uses. Change them deliberately, not by floating versions.

## Installer and signing

`make.bat` (local) and `make_action.bat` (release workflow) publish all projects, build the identity package, compile the Inno Setup installer (`make_installer.iss`, shared by both) and sign it. Both need the version as their first argument (`make.bat 2.36.26.0`), and the installer script refuses to build without one. A plain `dotnet build` gets the version in `Directory.Build.props`.

Signing matters for more than trust prompts: the app manifest requests `uiAccess="true"`, and **Windows only starts uiAccess executables that are signed by a trusted certificate and installed in a protected folder** (such as Program Files). The official certificate's private key is not part of this repository, so to produce a runnable installer you need your own:

1. Delete the repository's `LenovoLegionToolkit.cer` (it is the official project's public certificate; the script only generates a new pair when neither file exists). Then run `generate_certificate.ps1 -Password <password>` once. It creates `LenovoLegionToolkit.pfx` (git-ignored) and a matching `LenovoLegionToolkit.cer`.
2. Set `LLT_CERT_PASSWORD` and run `make.bat <version>`.
3. The installer adds your `.cer` to the machine's trusted roots so the signed app can start.

Notes:
- The update check only looks at this fork's releases and opens the release page; it never downloads or installs anything.
- `LLT_UNSIGNED_BUILD=1` skips installer signing. That is fine for compiling and testing, but the resulting app won't start because of `uiAccess`.

`make.bat` with your own certificate is how this fork's installers are built and installed. The release workflow below runs the same steps but has not run yet.

## CI

`.github/workflows/ci.yml` (this fork, runs on `main`):
- **Every push and pull request to `main`**, and on demand. It runs on `windows-latest` with .NET 9.
- **Steps:** restore each project (never the `.sln`, whose `.wapproj` the dotnet CLI can't build), build the app and `llt` with warnings as errors, then run every test. The test results are uploaded as the `test-results` artifact, also when a step fails, and each failed test's name and message is shown as an annotation on the run (readable without signing in).
- **No signing, no secrets, no installer.** Warnings are errors only in CI, so a new SDK warning can't stop a local `make.bat`.

`.github/workflows/release.yml` (this fork):
- **Runs only when a tag `vX.Y.Z` or `vX.Y.Z.W` is pushed** on a commit of `main`.
- **Needs two repository secrets** added by the owner: `LLT_CERT_PFX` (the `.pfx`, base64-encoded) and `LLT_CERT_PASSWORD`. Without them it stops before building.
- **Steps:** run every test, then `make_action.bat <version>`. The key file is deleted afterwards even if a step fails. The app and installer must be signed by the committed `LenovoLegionToolkit.cer`, or the run fails.
- **Creates a draft release** with the installer and its SHA-256. Nothing is public until the draft is published by hand.

All actions are pinned to full commit SHAs, with the version in a comment.
