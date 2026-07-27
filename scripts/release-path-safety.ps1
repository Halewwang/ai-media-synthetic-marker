function Assert-NoReparsePathComponents {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,
        [Parameter(Mandatory = $true)]
        [string]$CandidatePath,
        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    $fullRoot = [IO.Path]::GetFullPath($RepositoryRoot)
    $fullCandidate = [IO.Path]::GetFullPath($CandidatePath)
    $relative = [IO.Path]::GetRelativePath($fullRoot, $fullCandidate)
    $pathComparison = if ($IsWindows) {
        [StringComparison]::OrdinalIgnoreCase
    }
    else {
        [StringComparison]::Ordinal
    }

    if ($relative -eq '..' -or
        $relative.StartsWith(
            "..$([IO.Path]::DirectorySeparatorChar)",
            $pathComparison) -or
        [IO.Path]::IsPathFullyQualified($relative)) {
        throw "$Description must stay inside the repository root: $fullCandidate"
    }

    if (-not (Test-Path -LiteralPath $fullRoot -PathType Container)) {
        throw "Repository root must be an existing directory: $fullRoot"
    }

    $rootItem = Get-Item -LiteralPath $fullRoot -Force
    if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Repository root must not be a reparse point: $fullRoot"
    }

    if ($relative -eq '.') {
        return
    }

    $current = $fullRoot
    $segments = $relative.Split(
        [char[]](
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar),
        [StringSplitOptions]::RemoveEmptyEntries)
    foreach ($segment in $segments) {
        $current = [IO.Path]::Combine($current, $segment)
        if (-not (Test-Path -LiteralPath $current)) {
            break
        }

        $item = Get-Item -LiteralPath $current -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Description contains a reparse path component: $current"
        }
    }
}
