# Windows development and build setup

## Requirements

- Windows 11 x64.
- .NET 8 SDK.
- Git.
- Deno 2.x for Edge Function checks.
- Inno Setup 6 for an installer build.
- Windows SDK `signtool.exe` and a code-signing certificate for a public release.
- Optional: Supabase CLI 2.x and Docker Desktop for local database validation.

The current workstation audit is recorded in [prerequisites.md](prerequisites.md).

## 1. Install the tools

Run these commands in **Windows Terminal → PowerShell**:

```powershell
winget install --id Git.Git --exact
winget install --id Microsoft.DotNet.SDK.8 --exact
winget install --id DenoLand.Deno --exact
winget install --id JRSoftware.InnoSetup --exact
```

Success ends with an installed/successfully installed message. If `winget` is unavailable, update **App Installer** from Microsoft Store or download the tools from their official sites. Restart the terminal after installation.

Verify in a new PowerShell window:

```powershell
git --version
dotnet --version
deno --version
```

Expected output includes a Git version, a .NET `8.0.x` SDK, and Deno `2.x`. If a command is not recognized, restart Windows or add the tool's documented install directory to your user `PATH`; do not change system-wide settings just for this project.

## 2. Clone and restore

Run in the parent folder where you want the project:

```powershell
git clone https://github.com/OWNER/CodexUsageCompanion.git
Set-Location .\CodexUsageCompanion
dotnet restore .\CodexUsageCompanion.sln
```

Replace `OWNER` with the actual GitHub owner once the repository has a remote. Restore succeeds with `Restore completed` or `All projects are up-to-date for restore`. If it fails, confirm internet access and that the .NET 8 SDK—not only a runtime—is installed.

## 3. Build and test

Run from the repository root:

```powershell
dotnet build .\CodexUsageCompanion.sln --configuration Debug
.\scripts\test.ps1 -Configuration Debug
```

Success reports zero build errors/warnings and passing .NET/Deno tests. If Deno is missing, the helper warns and skips only Edge Function checks; install Deno and rerun before a release. For a failing test, copy the first failure name and sanitized stack trace—never local settings or credentials.

## 4. Run from source

```powershell
dotnet run --project .\src\CodexUsageCompanion.App
```

Run from the repository root. Success opens the status window and creates a tray icon. Mock mode is the safe default. Closing the window keeps the app in the system tray; choose **Exit** from the tray menu to stop it. If startup fails, follow [Troubleshooting](troubleshooting.md) and inspect only the sanitized application log.

## 5. Create a self-contained application

```powershell
.\scripts\build-windows.ps1 -Version 1.0.0 -SkipInstaller
```

Run from the repository root. Success ends with `Windows build ready` and writes x64 output to `artifacts\publish\win-x64`. It includes the .NET runtime, so the target machine does not need the SDK. If publishing fails, rerun `scripts\test.ps1` and resolve its first error.

## 6. Create the installer

Install Inno Setup 6, then run:

```powershell
.\scripts\build-windows.ps1 -Version 1.0.0
```

If Inno Setup is not in its default location:

```powershell
$env:INNO_SETUP_COMPILER = 'C:\Tools\Inno Setup 6\ISCC.exe'
.\scripts\build-windows.ps1 -Version 1.0.0
```

Run from the repository root. Success creates `artifacts\installer\CodexUsageCompanion-1.0.0-win-x64-setup.exe` and `SHA256SUMS.txt`. If the compiler is not found, set the path as shown above. The installer is per-user and installs under `%LOCALAPPDATA%\Programs\Codex Usage Companion`. It never deletes `%LOCALAPPDATA%\CodexUsageCompanion` during uninstall.

## Optional signing

Set `SIGNTOOL_PATH` and `CODE_SIGNING_CERT_THUMBPRINT` before running the build. The script signs both the application and final installer with SHA-256 and a timestamp. The public release workflow refuses to publish if its signing secret is absent.

## Local data

No build or upgrade should move mutable data into the source or install directory. The exact active location is visible in the status window and can be opened through diagnostics/troubleshooting steps.

## Configure and test Start with Windows

Open **Settings → Monitoring**, enable **Start with Windows**, and save. Sign out of Windows and back in, or inspect the current-user Run entry:

```powershell
Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name CodexUsageCompanion
```

Success shows one quoted executable path followed by `--background`. Disable the setting and rerun the command; PowerShell should report that the property does not exist. The app never enables startup silently.
