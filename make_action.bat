@echo off
REM Release build for .github/workflows/release.yml: same steps as make.bat, with no prompts.
dotnet restore LenovoLegionToolkit.sln || exit /b

IF "%1"=="" (
    echo Usage: make_action.bat ^<version^>
    exit /b 1
)
SET VERSION=%1

for /f %%i in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd"') do set BUILD_DATE=%%i

SET PATH=%PATH%;"C:\Program Files (x86)\Inno Setup 6"

dotnet publish LenovoLegionToolkit.WPF -c release -o build /p:DebugType=None /p:FileVersion=%VERSION% /p:Version=%VERSION% || exit /b
dotnet publish LenovoLegionToolkit.SpectrumTester -c release -o build /p:DebugType=None /p:FileVersion=%VERSION% /p:Version=%VERSION% || exit /b
dotnet publish LenovoLegionToolkit.Probe -c release -o build /p:DebugType=None /p:FileVersion=%VERSION% /p:Version=%VERSION% || exit /b
dotnet publish LenovoLegionToolkit.CLI -c release -o build /p:DebugType=None /p:FileVersion=%VERSION% /p:Version=%VERSION% || exit /b

echo Copying packaging files...
powershell -NoProfile -ExecutionPolicy Bypass -File ".\build_identity_package.ps1" -Version %VERSION% -OutputDir "build" || exit /b

iscc make_installer.iss /DMyAppVersion=%VERSION% /DMyBuildDate=%BUILD_DATE% || exit /b

echo Stamping installer executable...
powershell -NoProfile -ExecutionPolicy Bypass -File ".\sign_installer.ps1" -InstallerPath "build_installer\LenovoLegionToolkitSetup-v%VERSION%_Build%BUILD_DATE%.exe" || exit /b