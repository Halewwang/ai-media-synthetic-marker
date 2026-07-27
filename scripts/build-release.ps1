[CmdletBinding()]
param(
    [string]$DotNet = "dotnet"
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-path-safety.ps1')
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solution = Join-Path $repoRoot 'Emke.AiMarker.sln'
$releaseProject = Join-Path $repoRoot 'tools/Emke.AiMarker.Release/Emke.AiMarker.Release.csproj'
$appProject = Join-Path $repoRoot 'src/Emke.AiMarker.App/Emke.AiMarker.App.csproj'
$publishDirectory = Join-Path $repoRoot 'build/publish/win-x64'
$outputDirectory = Join-Path $repoRoot 'dist'
$exifTool = Join-Path $repoRoot 'runtime/exiftool/exiftool.exe'

function Remove-OwnedDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$ExpectedParent,
        [Parameter(Mandatory = $true)]
        [string]$ExpectedName,
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot
    )

    Assert-NoReparsePathComponents `
        -RepositoryRoot $RepositoryRoot `
        -CandidatePath $Path `
        -Description 'publish output'
    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $fullPath = [IO.Path]::GetFullPath($Path)
    $fullParent = [IO.Path]::GetFullPath($ExpectedParent)
    if ([IO.Path]::GetDirectoryName($fullPath) -ne $fullParent -or
        [IO.Path]::GetFileName($fullPath) -ne $ExpectedName) {
        throw "Refusing to clean an unowned publish path: $fullPath"
    }

    $rootItem = Get-Item -LiteralPath $fullPath -Force
    if (-not $rootItem.PSIsContainer -or
        (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
        throw "Publish output must be an ordinary directory: $fullPath"
    }

    $unsafeEntry = Get-ChildItem -LiteralPath $fullPath -Force -Recurse |
        Where-Object {
            ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
        } |
        Select-Object -First 1
    if ($null -ne $unsafeEntry) {
        throw "Refusing to clean publish output containing a reparse point: $($unsafeEntry.FullName)"
    }

    Assert-NoReparsePathComponents `
        -RepositoryRoot $RepositoryRoot `
        -CandidatePath $Path `
        -Description 'publish output'
    Remove-Item -LiteralPath $fullPath -Recurse -Force
}

Push-Location $repoRoot
$previousExifTool = $env:EMKE_EXIFTOOL
try {
    & $DotNet restore $solution --locked-mode
    if ($LASTEXITCODE -ne 0) {
        throw "Locked restore failed with exit code $LASTEXITCODE."
    }

    & $DotNet run --project $releaseProject -c Release --no-restore -- `
        fetch-exiftool --repo-root $repoRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Locked ExifTool acquisition failed with exit code $LASTEXITCODE."
    }

    $env:EMKE_EXIFTOOL = (Resolve-Path $exifTool).Path
    & $DotNet test $solution -c Release --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "Release tests failed with exit code $LASTEXITCODE."
    }

    Assert-NoReparsePathComponents `
        -RepositoryRoot $repoRoot `
        -CandidatePath $publishDirectory `
        -Description 'publish output'
    Remove-OwnedDirectory `
        -Path $publishDirectory `
        -ExpectedParent (Join-Path $repoRoot 'build/publish') `
        -ExpectedName 'win-x64' `
        -RepositoryRoot $repoRoot
    Assert-NoReparsePathComponents `
        -RepositoryRoot $repoRoot `
        -CandidatePath $publishDirectory `
        -Description 'publish output'
    & $DotNet publish $appProject -c Release -r win-x64 `
        --self-contained true -o $publishDirectory --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "Windows x64 publish failed with exit code $LASTEXITCODE."
    }

    & $DotNet run --project $releaseProject -c Release --no-build -- `
        package --repo-root $repoRoot `
        --publish-dir $publishDirectory --output-dir $outputDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "Release packaging failed with exit code $LASTEXITCODE."
    }
}
finally {
    $env:EMKE_EXIFTOOL = $previousExifTool
    Pop-Location
}
