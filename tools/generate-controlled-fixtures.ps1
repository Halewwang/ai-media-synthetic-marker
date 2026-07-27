$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Import-Module Microsoft.PowerShell.Management -ErrorAction Stop
Import-Module Microsoft.PowerShell.Utility -ErrorAction Stop

$expectedFfmpegVersion = '7.1.1'
$expectedExifToolVersion = '13.59'
$existingSubject = 'emke-existing-fixture-subject'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$fixturesParent = Join-Path $repositoryRoot 'tests/fixtures'
$controlledDirectory = Join-Path $fixturesParent 'controlled'
$stageDirectory = Join-Path $fixturesParent ".controlled-stage-$([Guid]::NewGuid().ToString('N'))"
$backupDirectory = Join-Path $fixturesParent ".controlled-backup-$([Guid]::NewGuid().ToString('N'))"

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory)]
        [string] $Executable,

        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    $global:LASTEXITCODE = 0
    & $Executable @Arguments
    if ($global:LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${global:LASTEXITCODE}: $Executable $($Arguments -join ' ')"
    }
}

function Assert-GeneratorVersions {
    $ffmpegCommand = Get-Command ffmpeg -CommandType Application -ErrorAction Stop
    $global:LASTEXITCODE = 0
    $ffmpegVersionLine = (& $ffmpegCommand.Source -version | Select-Object -First 1)
    if ($global:LASTEXITCODE -ne 0) {
        throw "Unable to query FFmpeg version (exit code ${global:LASTEXITCODE})."
    }
    if ($ffmpegVersionLine -notmatch '^ffmpeg version 7\.1\.1(?:\s|$)') {
        throw "FFmpeg 7.1.1 is required to regenerate controlled fixtures. Actual: $ffmpegVersionLine"
    }

    if ([string]::IsNullOrWhiteSpace($env:EMKE_EXIFTOOL)) {
        throw 'EMKE_EXIFTOOL must point to an executable ExifTool 13.59 entry point.'
    }
    if (-not (Test-Path -LiteralPath $env:EMKE_EXIFTOOL -PathType Leaf)) {
        throw "EMKE_EXIFTOOL does not exist: $env:EMKE_EXIFTOOL"
    }

    $exifToolPath = (Resolve-Path -LiteralPath $env:EMKE_EXIFTOOL).Path
    $global:LASTEXITCODE = 0
    $exifToolVersion = (& $exifToolPath -ver | Out-String).Trim()
    if ($global:LASTEXITCODE -ne 0) {
        throw "Unable to query ExifTool version (exit code ${global:LASTEXITCODE})."
    }
    if ($exifToolVersion -cne $expectedExifToolVersion) {
        throw "ExifTool 13.59 is required to regenerate controlled fixtures. Actual: $exifToolVersion"
    }

    return [ordered]@{
        Ffmpeg = $ffmpegCommand.Source
        ExifTool = $exifToolPath
    }
}

function New-FixtureRecord {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [string[]] $GenerationCommands
    )

    $path = Join-Path $stageDirectory $Name
    $info = Get-Item -LiteralPath $path
    $sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    return [ordered]@{
        path = $Name
        byte_length = $info.Length
        sha256 = $sha256
        generation_commands = $GenerationCommands
    }
}

$generators = Assert-GeneratorVersions
New-Item -ItemType Directory -Path $fixturesParent -Force | Out-Null
New-Item -ItemType Directory -Path $stageDirectory | Out-Null

$pngCommand = 'ffmpeg -hide_banner -loglevel error -f lavfi -i color=c=white:s=16x16:d=1 -frames:v 1 -y fixture.png'
$jpgCommand = 'ffmpeg -hide_banner -loglevel error -f lavfi -i color=c=white:s=16x16:d=1 -frames:v 1 -q:v 2 -y fixture.jpg'
$jpegCommand = 'Copy-Item fixture.jpg fixture.jpeg'
$mp4Command = 'ffmpeg -hide_banner -loglevel error -f lavfi -i color=c=black:s=16x16:d=1 -an -c:v libx264 -pix_fmt yuv420p -movflags +faststart -metadata title=EMKE-Controlled-Test-Fixture -y fixture.mp4'
$metadataCommand = 'exiftool -overwrite_original -XMP-dc:Subject=emke-existing-fixture-subject fixture.jpg fixture.jpeg fixture.png fixture.mp4'

try {
    Push-Location $stageDirectory
    try {
        Invoke-CheckedCommand $generators.Ffmpeg @(
            '-hide_banner', '-loglevel', 'error',
            '-f', 'lavfi', '-i', 'color=c=white:s=16x16:d=1',
            '-frames:v', '1', '-y', 'fixture.png'
        )
        Invoke-CheckedCommand $generators.Ffmpeg @(
            '-hide_banner', '-loglevel', 'error',
            '-f', 'lavfi', '-i', 'color=c=white:s=16x16:d=1',
            '-frames:v', '1', '-q:v', '2', '-y', 'fixture.jpg'
        )
        Copy-Item -LiteralPath 'fixture.jpg' -Destination 'fixture.jpeg'
        Invoke-CheckedCommand $generators.Ffmpeg @(
            '-hide_banner', '-loglevel', 'error',
            '-f', 'lavfi', '-i', 'color=c=black:s=16x16:d=1',
            '-an', '-c:v', 'libx264', '-pix_fmt', 'yuv420p',
            '-movflags', '+faststart',
            '-metadata', 'title=EMKE-Controlled-Test-Fixture',
            '-y', 'fixture.mp4'
        )
        Invoke-CheckedCommand $generators.ExifTool @(
            '-overwrite_original',
            "-XMP-dc:Subject=$existingSubject",
            'fixture.jpg',
            'fixture.jpeg',
            'fixture.png',
            'fixture.mp4'
        )
    }
    finally {
        Pop-Location
    }

    $manifest = [ordered]@{
        schema_version = 1
        generator_versions = [ordered]@{
            ffmpeg = $expectedFfmpegVersion
            exiftool = $expectedExifToolVersion
        }
        files = @(
            (New-FixtureRecord 'fixture.jpeg' @($jpegCommand, $metadataCommand))
            (New-FixtureRecord 'fixture.jpg' @($jpgCommand, $metadataCommand))
            (New-FixtureRecord 'fixture.mp4' @($mp4Command, $metadataCommand))
            (New-FixtureRecord 'fixture.png' @($pngCommand, $metadataCommand))
        )
    }
    $manifestJson = $manifest | ConvertTo-Json -Depth 5
    $manifestPath = Join-Path $stageDirectory 'fixture-manifest.json'
    [IO.File]::WriteAllText(
        $manifestPath,
        "$manifestJson`n",
        [Text.UTF8Encoding]::new($false))

    if (Test-Path -LiteralPath $controlledDirectory) {
        Move-Item -LiteralPath $controlledDirectory -Destination $backupDirectory
    }
    try {
        Move-Item -LiteralPath $stageDirectory -Destination $controlledDirectory
    }
    catch {
        if (Test-Path -LiteralPath $backupDirectory) {
            Move-Item -LiteralPath $backupDirectory -Destination $controlledDirectory
        }
        throw
    }
    if (Test-Path -LiteralPath $backupDirectory) {
        Remove-Item -LiteralPath $backupDirectory -Recurse -Force
    }
}
finally {
    if (Test-Path -LiteralPath $stageDirectory) {
        Remove-Item -LiteralPath $stageDirectory -Recurse -Force
    }
}

Write-Host "Controlled fixtures generated with FFmpeg $expectedFfmpegVersion and ExifTool $expectedExifToolVersion."
