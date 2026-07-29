[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RepositoryRoot,
    [Parameter(Mandatory = $true)]
    [string]$StageDirectory,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [Parameter(Mandatory = $true)]
    [string]$InnoCompiler
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-path-safety.ps1')

$appVersion = '2.0.1'
$rootName = "emke-ai-marker-v$appVersion-windows-x64"
$zipName = "$rootName.zip"
$setupName = "$rootName-setup.exe"
$repoRoot = [IO.Path]::GetFullPath($RepositoryRoot)
$stage = [IO.Path]::GetFullPath($StageDirectory)
$output = [IO.Path]::GetFullPath($OutputDirectory)
$compiler = [IO.Path]::GetFullPath($InnoCompiler)
$buildRoot = Join-Path $repoRoot 'build'
$expectedStage = Join-Path $buildRoot "stage/$rootName"
$installerScript = Join-Path $repoRoot 'packaging/installer/Emke.AiMarker.iss'
$compilerLockPath = Join-Path $repoRoot 'packaging/inno-setup.lock.json'
$zipPath = Join-Path $output $zipName
$setupPath = Join-Path $output $setupName
$checksumPath = Join-Path $output 'SHA256SUMS.txt'

function Assert-OrdinaryTree {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "$Description must be an existing directory: $Path"
    }

    $rootItem = Get-Item -LiteralPath $Path -Force
    if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Description must not be a reparse point: $Path"
    }

    $unsafe = Get-ChildItem -LiteralPath $Path -Force -Recurse |
        Where-Object {
            ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
        } |
        Select-Object -First 1
    if ($null -ne $unsafe) {
        throw "$Description contains a reparse point: $($unsafe.FullName)"
    }
}

function Invoke-CheckedProcess {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,
        [int]$TimeoutSeconds = 180
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "Failed to start process: $FilePath"
        }

        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            try {
                $process.Kill($true)
                $process.WaitForExit()
            }
            catch {
                # Timeout remains the primary failure.
            }
            throw "Process timed out after $TimeoutSeconds seconds: $FilePath"
        }

        $outputText = $stdout.GetAwaiter().GetResult()
        $errorText = $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            throw "Process failed with exit code $($process.ExitCode): $FilePath`n$errorText"
        }

        if (-not [string]::IsNullOrWhiteSpace($outputText)) {
            Write-Host $outputText.Trim()
        }
    }
    finally {
        $process.Dispose()
    }
}

function Assert-ExactReport {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string[]]$Expected
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Self-test report is missing: $Path"
    }

    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Self-test report must be an ordinary file: $Path"
    }

    $actual = @(Get-Content -LiteralPath $Path | Where-Object { $_.Length -gt 0 })
    if ($actual.Count -ne $Expected.Count) {
        throw "Self-test report line count mismatch: $Path"
    }

    for ($index = 0; $index -lt $Expected.Count; $index++) {
        if ($actual[$index] -cne $Expected[$index]) {
            throw "Self-test report mismatch at line $($index + 1): $Path"
        }
    }
}

function Remove-OwnedOperationRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $fullPath = [IO.Path]::GetFullPath($Path)
    if ([IO.Path]::GetDirectoryName($fullPath) -ne $buildRoot -or
        -not [IO.Path]::GetFileName($fullPath).StartsWith(
            '.installer-',
            [StringComparison]::Ordinal)) {
        throw "Refusing to remove unowned installer path: $fullPath"
    }

    Assert-NoReparsePathComponents `
        -RepositoryRoot $repoRoot `
        -CandidatePath $fullPath `
        -Description 'installer operation root'
    Assert-OrdinaryTree -Path $fullPath -Description 'installer operation root'
    Remove-Item -LiteralPath $fullPath -Recurse -Force
}

if (-not (Test-Path -LiteralPath $repoRoot -PathType Container)) {
    throw "Repository root does not exist: $repoRoot"
}
Assert-NoReparsePathComponents `
    -RepositoryRoot $repoRoot `
    -CandidatePath $repoRoot `
    -Description 'repository root'
if ($stage -ne [IO.Path]::GetFullPath($expectedStage)) {
    throw "Stage must be the exact validated release stage: $expectedStage"
}
Assert-NoReparsePathComponents `
    -RepositoryRoot $repoRoot `
    -CandidatePath $stage `
    -Description 'release stage'
Assert-OrdinaryTree -Path $stage -Description 'release stage'

Assert-NoReparsePathComponents `
    -RepositoryRoot $repoRoot `
    -CandidatePath $output `
    -Description 'installer output'
if (-not (Test-Path -LiteralPath $output)) {
    [IO.Directory]::CreateDirectory($output) | Out-Null
}
Assert-OrdinaryTree -Path $output -Description 'installer output'

if (-not [IO.Path]::IsPathFullyQualified($compiler) -or
    -not (Test-Path -LiteralPath $compiler -PathType Leaf) -or
    [IO.Path]::GetFileName($compiler) -cne 'ISCC.exe') {
    throw "InnoCompiler must be a fully-qualified existing ISCC.exe: $compiler"
}
$compilerItem = Get-Item -LiteralPath $compiler -Force
if (($compilerItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "InnoCompiler must be an ordinary file: $compiler"
}

$compilerLock = Get-Content -LiteralPath $compilerLockPath -Raw |
    ConvertFrom-Json
if ([string]$compilerLock.version -cne '6.7.3') {
    throw "Inno Setup compiler lock must require version 6.7.3."
}
$compilerVersion = [string]$compilerItem.VersionInfo.FileVersion
if (-not $compilerVersion.StartsWith('6.7.3', [StringComparison]::Ordinal)) {
    $matchingUninstallers = @(
        Get-ChildItem -LiteralPath $compilerItem.DirectoryName -Filter 'unins*.exe' -File |
            Where-Object {
                ([string]$_.VersionInfo.ProductVersion).StartsWith(
                    '6.7.3',
                    [StringComparison]::Ordinal)
            })
    if ($matchingUninstallers.Count -ne 1) {
        throw "ISCC.exe is not installed beside exactly one Inno Setup 6.7.3 uninstaller."
    }
}

if (-not (Test-Path -LiteralPath $installerScript -PathType Leaf)) {
    throw "Installer definition is missing: $installerScript"
}
if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) {
    throw "Validated portable ZIP is missing: $zipPath"
}

$operationRoot = Join-Path $buildRoot ".installer-$([Guid]::NewGuid().ToString('N'))"
$candidateOutput = Join-Path $operationRoot 'candidate'
$installRoot = Join-Path $operationRoot 'installed'
$headlessReport = Join-Path $operationRoot 'installed-self-test.txt'
$uiReport = Join-Path $operationRoot 'installed-ui-self-test.txt'
$candidateSetup = Join-Path $candidateOutput $setupName

[IO.Directory]::CreateDirectory($operationRoot) | Out-Null
[IO.Directory]::CreateDirectory($candidateOutput) | Out-Null
try {
    Assert-NoReparsePathComponents `
        -RepositoryRoot $repoRoot `
        -CandidatePath $operationRoot `
        -Description 'installer operation root'

    Invoke-CheckedProcess `
        -FilePath $compiler `
        -Arguments @(
            '/Qp',
            "/DStageDir=$stage",
            "/DAppVersion=$appVersion",
            "/DOutputDir=$candidateOutput",
            $installerScript) `
        -WorkingDirectory $repoRoot

    $candidateFiles = @(
        Get-ChildItem -LiteralPath $candidateOutput -Filter $setupName -File)
    if ($candidateFiles.Count -ne 1 -or
        $candidateFiles[0].FullName -ne $candidateSetup) {
        throw "Expected exactly one compiled Setup: $candidateSetup"
    }
    if ([string]$candidateFiles[0].VersionInfo.FileVersion -cne '2.0.1.0') {
        throw "Setup file version must be exactly 2.0.1.0."
    }

    Invoke-CheckedProcess `
        -FilePath $candidateSetup `
        -Arguments @(
            '/VERYSILENT',
            '/SUPPRESSMSGBOXES',
            '/NORESTART',
            '/NOICONS',
            "/DIR=$installRoot") `
        -WorkingDirectory $operationRoot

    Assert-NoReparsePathComponents `
        -RepositoryRoot $repoRoot `
        -CandidatePath $installRoot `
        -Description 'temporary install'
    Assert-OrdinaryTree -Path $installRoot -Description 'temporary install'

    foreach ($stageFile in Get-ChildItem -LiteralPath $stage -File -Recurse) {
        $relativePath = [IO.Path]::GetRelativePath($stage, $stageFile.FullName)
        $installedFile = Join-Path $installRoot $relativePath
        if (-not (Test-Path -LiteralPath $installedFile -PathType Leaf)) {
            throw "Installed payload is missing stage file: $relativePath"
        }
        $installedItem = Get-Item -LiteralPath $installedFile -Force
        if (($installedItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Installed payload file is a reparse point: $relativePath"
        }
        $stageHash = (Get-FileHash -LiteralPath $stageFile.FullName -Algorithm SHA256).Hash
        $installedHash = (Get-FileHash -LiteralPath $installedFile -Algorithm SHA256).Hash
        if ($stageHash -cne $installedHash) {
            throw "Installed payload hash mismatch: $relativePath"
        }
    }

    $installedApp = Join-Path $installRoot 'EMKE AI Marker.exe'
    Invoke-CheckedProcess `
        -FilePath $installedApp `
        -Arguments @('--self-test', '--report', $headlessReport) `
        -WorkingDirectory $installRoot
    Assert-ExactReport `
        -Path $headlessReport `
        -Expected @(
            'AppVersion=2.0.1',
            'Runtime=.NET 10',
            'ExifTool=13.59',
            'Result=ok')

    Invoke-CheckedProcess `
        -FilePath $installedApp `
        -Arguments @('--ui-self-test', '--report', $uiReport) `
        -WorkingDirectory $installRoot
    Assert-ExactReport `
        -Path $uiReport `
        -Expected @(
            'AppVersion=2.0.1',
            'MainWindow=shown',
            'Result=ok')

    $uninstallers = @(
        Get-ChildItem -LiteralPath $installRoot -Filter 'unins*.exe' -File)
    if ($uninstallers.Count -ne 1) {
        throw "Expected exactly one uninstaller in the temporary install."
    }
    Invoke-CheckedProcess `
        -FilePath $uninstallers[0].FullName `
        -Arguments @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') `
        -WorkingDirectory $operationRoot

    $uninstallDeadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
    while ((Test-Path -LiteralPath $installRoot) -and
        [DateTimeOffset]::UtcNow -lt $uninstallDeadline) {
        Start-Sleep -Milliseconds 200
    }
    if (Test-Path -LiteralPath $installRoot) {
        throw "Temporary install directory remained after uninstall: $installRoot"
    }

    if (Test-Path -LiteralPath $setupPath) {
        $existingSetup = Get-Item -LiteralPath $setupPath -Force
        if (($existingSetup.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing to replace a Setup reparse point: $setupPath"
        }
        Remove-Item -LiteralPath $setupPath -Force
    }
    Move-Item -LiteralPath $candidateSetup -Destination $setupPath

    $artifacts = @(
        Get-Item -LiteralPath $zipPath,
            $setupPath |
            Sort-Object Name)
    $checksumLines = foreach ($artifact in $artifacts) {
        $hash = (Get-FileHash -LiteralPath $artifact.FullName -Algorithm SHA256).
            Hash.ToLowerInvariant()
        "$hash  $($artifact.Name)"
    }
    $temporaryChecksum = Join-Path $output (
        ".SHA256SUMS.txt.$([Guid]::NewGuid().ToString('N')).tmp")
    try {
        [IO.File]::WriteAllText(
            $temporaryChecksum,
            ($checksumLines -join "`n") + "`n",
            [Text.UTF8Encoding]::new($false))
        Move-Item `
            -LiteralPath $temporaryChecksum `
            -Destination $checksumPath `
            -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporaryChecksum) {
            Remove-Item -LiteralPath $temporaryChecksum -Force
        }
    }
}
finally {
    if (Test-Path -LiteralPath $installRoot) {
        try {
            $leftoverUninstallers = @(
                Get-ChildItem -LiteralPath $installRoot -Filter 'unins*.exe' -File)
            if ($leftoverUninstallers.Count -eq 1) {
                Invoke-CheckedProcess `
                    -FilePath $leftoverUninstallers[0].FullName `
                    -Arguments @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') `
                    -WorkingDirectory $operationRoot `
                    -TimeoutSeconds 30
            }
        }
        catch {
            Write-Warning "Best-effort temporary uninstall failed: $($_.Exception.Message)"
        }
    }

    Remove-OwnedOperationRoot -Path $operationRoot
}
