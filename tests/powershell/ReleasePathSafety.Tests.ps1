[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Import-Module Microsoft.PowerShell.Management -ErrorAction Stop
Import-Module Microsoft.PowerShell.Utility -ErrorAction Stop
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$helperPath = Join-Path $repositoryRoot 'scripts/release-path-safety.ps1'
if (-not (Test-Path -LiteralPath $helperPath -PathType Leaf)) {
    throw "FAIL: release path safety helper is missing: $helperPath"
}

. $helperPath

$testRoot = Join-Path ([IO.Path]::GetTempPath()) (
    "emke-release-path-safety-$([Guid]::NewGuid().ToString('N'))")
$sentinelBytes = [byte[]](0x45, 0x4D, 0x4B, 0x45, 0x00, 0xFF)

function Assert-ReparseAncestorRejected {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('build', 'publish')]
        [string]$LinkedComponent
    )

    $caseRoot = Join-Path $testRoot "repo-$LinkedComponent"
    $externalRoot = Join-Path $testRoot "external-$LinkedComponent"
    $externalBuild = Join-Path $externalRoot 'build'
    $externalPublish = Join-Path $externalBuild 'publish'
    $externalLeaf = Join-Path $externalPublish 'win-x64'
    New-Item -ItemType Directory -Path $caseRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $externalLeaf -Force | Out-Null
    $sentinel = Join-Path $externalLeaf 'sentinel.bin'
    [IO.File]::WriteAllBytes($sentinel, $sentinelBytes)

    if ($LinkedComponent -eq 'build') {
        New-Item -ItemType SymbolicLink `
            -Path (Join-Path $caseRoot 'build') `
            -Target $externalBuild | Out-Null
    }
    else {
        $build = Join-Path $caseRoot 'build'
        New-Item -ItemType Directory -Path $build | Out-Null
        New-Item -ItemType SymbolicLink `
            -Path (Join-Path $build 'publish') `
            -Target $externalPublish | Out-Null
    }

    $publish = Join-Path $caseRoot 'build/publish/win-x64'
    $rejected = $false
    try {
        Assert-NoReparsePathComponents `
            -RepositoryRoot $caseRoot `
            -CandidatePath $publish `
            -Description 'publish output'
    }
    catch {
        $rejected = $true
    }

    if (-not $rejected) {
        throw "FAIL: $LinkedComponent reparse ancestor was accepted."
    }

    if (-not (Test-Path -LiteralPath $sentinel -PathType Leaf)) {
        throw "FAIL: external sentinel was deleted for $LinkedComponent."
    }

    $actual = [IO.File]::ReadAllBytes($sentinel)
    if (-not [Linq.Enumerable]::SequenceEqual[byte]($sentinelBytes, $actual)) {
        throw "FAIL: external sentinel was modified for $LinkedComponent."
    }
}

try {
    New-Item -ItemType Directory -Path $testRoot | Out-Null
    Assert-ReparseAncestorRejected -LinkedComponent build
    Assert-ReparseAncestorRejected -LinkedComponent publish
    Write-Output 'PASS: build and build/publish reparse ancestors rejected.'
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
