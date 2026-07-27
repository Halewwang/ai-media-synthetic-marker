[CmdletBinding()]
param(
    [string]$DotNet = "dotnet",
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ReleaseToolArguments
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repoRoot 'tools/Emke.AiMarker.Release/Emke.AiMarker.Release.csproj'

& $DotNet run --project $project -c Release -- `
    fetch-exiftool --repo-root $repoRoot @ReleaseToolArguments
if ($LASTEXITCODE -ne 0) {
    throw "fetch-exiftool failed with exit code $LASTEXITCODE."
}
