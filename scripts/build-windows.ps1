[CmdletBinding()]
param(
    [ValidatePattern('^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$')]
    [string]$Version = "1.0.0",

    [ValidateSet("Release")]
    [string]$Configuration = "Release",

    [switch]$SkipTests,

    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifactsDirectory = Join-Path $repositoryRoot "artifacts"
$publishDirectory = Join-Path $artifactsDirectory "publish\win-x64"
$installerDirectory = Join-Path $artifactsDirectory "installer"

if (-not $SkipTests) {
    & (Join-Path $PSScriptRoot "test.ps1") -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "Tests did not pass."
    }
}

New-Item -ItemType Directory -Force -Path $publishDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $installerDirectory | Out-Null

dotnet publish (Join-Path $repositoryRoot "src\CodexUsageCompanion.App\CodexUsageCompanion.App.csproj") `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDirectory `
    -p:Version=$Version `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    throw "Windows publishing failed."
}

$signTool = $env:SIGNTOOL_PATH
$certificateThumbprint = $env:CODE_SIGNING_CERT_THUMBPRINT
if (-not [string]::IsNullOrWhiteSpace($signTool) -and
    -not [string]::IsNullOrWhiteSpace($certificateThumbprint)) {
    & $signTool sign /sha1 $certificateThumbprint /fd SHA256 /td SHA256 `
        /tr "http://timestamp.digicert.com" `
        (Join-Path $publishDirectory "CodexUsageCompanion.App.exe")
    if ($LASTEXITCODE -ne 0) {
        throw "Application signing failed."
    }
}

if (-not $SkipInstaller) {
    $innoCompiler = $env:INNO_SETUP_COMPILER
    if ([string]::IsNullOrWhiteSpace($innoCompiler)) {
        $candidate = Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"
        if (Test-Path -LiteralPath $candidate) {
            $innoCompiler = $candidate
        }
    }

    if ([string]::IsNullOrWhiteSpace($innoCompiler) -or
        -not (Test-Path -LiteralPath $innoCompiler)) {
        throw "Inno Setup 6 was not found. Set INNO_SETUP_COMPILER or use -SkipInstaller."
    }

    & $innoCompiler `
        "/DAppVersion=$Version" `
        "/DPublishDir=$publishDirectory" `
        "/DOutputDir=$installerDirectory" `
        (Join-Path $repositoryRoot "installer\CodexUsageCompanion.iss")
    if ($LASTEXITCODE -ne 0) {
        throw "Installer compilation failed."
    }

    $installer = Get-ChildItem -LiteralPath $installerDirectory -Filter "*.exe" |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $installer) {
        throw "The installer compiler did not produce an executable."
    }

    if (-not [string]::IsNullOrWhiteSpace($signTool) -and
        -not [string]::IsNullOrWhiteSpace($certificateThumbprint)) {
        & $signTool sign /sha1 $certificateThumbprint /fd SHA256 /td SHA256 `
            /tr "http://timestamp.digicert.com" $installer.FullName
        if ($LASTEXITCODE -ne 0) {
            throw "Installer signing failed."
        }
    }
}

$releaseFiles = Get-ChildItem -LiteralPath $installerDirectory -File |
    Where-Object Name -ne "SHA256SUMS.txt"
if ($releaseFiles.Count -gt 0) {
    $checksums = foreach ($file in $releaseFiles) {
        $hash = Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256
        "$($hash.Hash.ToLowerInvariant())  $($file.Name)"
    }
    $checksums | Set-Content -LiteralPath (Join-Path $installerDirectory "SHA256SUMS.txt") -Encoding utf8
}

Write-Host "Windows build ready in $artifactsDirectory"
