<#!
.SYNOPSIS
    Reclaims disk space by removing build artifacts and optional caches.

.PARAMETER SolutionRoot
    Directory that contains the solution. Defaults to the repository root.

.PARAMETER IncludeNuGetCache
    Also clears the global NuGet cache via `dotnet nuget locals`.

.PARAMETER IncludeWorkloads
    Also runs `dotnet workload clean` to prune workload packs.
#>
param(
    [string]$SolutionRoot = (Resolve-Path "$PSScriptRoot/.."),
    [switch]$IncludeNuGetCache,
    [switch]$IncludeWorkloads
)

if (-not (Test-Path $SolutionRoot)) {
    throw "Solution root '$SolutionRoot' was not found."
}

Write-Host "Cleaning build artifacts under" $SolutionRoot

$deletedBytes = 0

foreach ($glob in @("bin","obj")) {
    Get-ChildItem -Path $SolutionRoot -Recurse -Directory -Filter $glob -ErrorAction SilentlyContinue |
        Sort-Object FullName -Unique |
        ForEach-Object {
            try {
                $size = (Get-ChildItem -Path $_.FullName -Recurse -File -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum
                if ($size) { $deletedBytes += $size }
                Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction Stop
                Write-Host "Deleted" $_.FullName
            }
            catch {
                Write-Warning "Failed to delete $($_.FullName): $($_.Exception.Message)"
            }
        }
}

if ($IncludeNuGetCache.IsPresent) {
    Write-Host "Clearing NuGet cache..."
    try {
        dotnet nuget locals all --clear | Write-Host
    }
    catch {
        Write-Warning "Failed to clear NuGet cache: $($_.Exception.Message)"
    }
}

if ($IncludeWorkloads.IsPresent) {
    Write-Host "Cleaning unused workloads..."
    try {
        dotnet workload clean | Write-Host
    }
    catch {
        Write-Warning "Failed to clean workloads: $($_.Exception.Message)"
    }
}

$mbFreed = [Math]::Round(($deletedBytes / 1MB), 2)
Write-Host "Approximately" $mbFreed "MB reclaimed from project build folders."
