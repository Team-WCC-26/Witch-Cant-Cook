[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

Push-Location (Join-Path $PSScriptRoot '..\..')
try {
    $assetPaths = @(':(glob)ProjectSettings/**/*.asset', ':(glob)Assets/**/*.asset')
    $workingMatches = @(& git grep --untracked --exclude-standard -l -w -e WCC_TEST_TOOLS -- @assetPaths 2>$null)
    if ($LASTEXITCODE -gt 1) { throw 'Failed to inspect working-tree test symbols.' }
    $stagedMatches = @(& git grep --cached -l -w -e WCC_TEST_TOOLS -- @assetPaths 2>$null)
    if ($LASTEXITCODE -gt 1) { throw 'Failed to inspect staged test symbols.' }

    if ($workingMatches.Count -gt 0 -or $stagedMatches.Count -gt 0) {
        Write-Host 'WCC_TEST_TOOLS is still present in settings or build profiles.' -ForegroundColor Red
        foreach ($matchPath in $workingMatches) { Write-Host "Working tree: $matchPath" }
        foreach ($matchPath in $stagedMatches) { Write-Host "Staged: $matchPath" }
        Write-Host 'Disable WCC_TEST_TOOLS in Unity, save, and stage the corrected settings before committing.'
        exit 1
    }

    Write-Host 'PASS: WCC_TEST_TOOLS is absent from working and staged settings/build profiles.' -ForegroundColor Green
    exit 0
}
finally {
    Pop-Location
}
