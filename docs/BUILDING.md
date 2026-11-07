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

`make.bat` (local) and `make_action.bat` (CI) publish all projects, build the identity package, compile the Inno Setup installer and sign it.

Signing matters for more than trust prompts: the app manifest requests `uiAccess="true"`, and **Windows only starts uiAccess executables that are signed by a trusted certificate and installed in a protected folder** (such as Program Files). The official certificate's private key is not part of this repository, so to produce a runnable installer you need your own:

1. Delete the repository's `LenovoLegionToolkit.cer` (it is the official project's public certificate; the script only generates a new pair when neither file exists). Then run `generate_certificate.ps1 -Password <password>` once. It creates `LenovoLegionToolkit.pfx` (git-ignored) and a matching `LenovoLegionToolkit.cer`.
2. Set `LLT_CERT_PASSWORD` and run `make.bat <version>`.
3. The installer adds your `.cer` to the machine's trusted roots so the signed app can start.

Notes:
- The updater pins the **official** installer's certificate thumbprint (`UpdateChecker.TRUSTED_SIGNATURE_THUMBPRINT`) and downloads official releases. Self-built installers are therefore not accepted as updates, and official updates replace this mod (see README).
- An unsigned build skips signing (`LLT_UNSIGNED_BUILD=1`, used by CI for pull requests). It is fine for compiling and testing, but the resulting app won't start because of `uiAccess`.

This signing flow is the upstream project's process. It has not been re-tested end-to-end in this fork.

## CI

`.github/workflows/build.yml`:
- **Pushes and manual runs** restore the signing key from secrets and build a signed installer.
- **Pull requests** never receive secrets and build unsigned (`make_action.bat raw` with `LLT_UNSIGNED_BUILD=1`).
- All actions are pinned to full commit SHAs, with the version in a comment.
