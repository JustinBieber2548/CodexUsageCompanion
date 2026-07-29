[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot

Push-Location $repositoryRoot
try {
    dotnet restore CodexUsageCompanion.sln
    if ($LASTEXITCODE -ne 0) {
        throw "Dependency restore failed."
    }

    dotnet build CodexUsageCompanion.sln --configuration $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "The .NET build failed."
    }

    dotnet test CodexUsageCompanion.sln --configuration $Configuration --no-build --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        throw "The .NET tests failed."
    }

    $deno = Get-Command deno -ErrorAction SilentlyContinue
    if ($null -eq $deno) {
        Write-Warning "Deno is not installed; Edge Function checks were skipped."
    }
    else {
        & $deno.Source fmt --check supabase/functions
        if ($LASTEXITCODE -ne 0) {
            throw "Deno formatting checks failed."
        }

        & $deno.Source check `
            supabase/functions/ingest-usage/index.ts `
            supabase/functions/telegram-webhook/index.ts `
            supabase/functions/notification-check/index.ts
        if ($LASTEXITCODE -ne 0) {
            throw "Deno type checks failed."
        }

        & $deno.Source test --allow-env supabase/functions
        if ($LASTEXITCODE -ne 0) {
            throw "Edge Function tests failed."
        }
    }
}
finally {
    Pop-Location
}
