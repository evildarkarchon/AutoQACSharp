#Requires -Version 5.1
<#
.SYNOPSIS
    Builds, tests, and publishes a self-contained Release folder for AutoQAC.

.DESCRIPTION
    Produces an unpackaged, self-contained WinUI 3 publish layout under
    artifacts/publish/win-x64/ and verifies required files are present.

.PARAMETER OutputDir
    Publish output directory. Defaults to artifacts/publish/win-x64.

.PARAMETER SkipTests
    Skip dotnet test after the Release build.

.PARAMETER SkipLaunchSmoke
    Skip the short process-start smoke check against AutoQAC.exe.

.EXAMPLE
    .\scripts\Publish-Release.ps1
#>
[CmdletBinding()]
param(
    [string] $OutputDir,
    [switch] $SkipTests,
    [switch] $SkipLaunchSmoke
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = if ([string]::IsNullOrWhiteSpace($PSScriptRoot)) {
    Split-Path -Parent $MyInvocation.MyCommand.Path
} else {
    $PSScriptRoot
}

$repoRoot = (Resolve-Path (Join-Path $scriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "artifacts\publish\win-x64"
}
$solution = Join-Path $repoRoot "AutoQACSharp.slnx"
$project = Join-Path $repoRoot "AutoQAC\AutoQAC.csproj"
$resolvedOutput = if ([System.IO.Path]::IsPathRooted($OutputDir)) {
    [System.IO.Path]::GetFullPath($OutputDir)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir))
}

function Write-Step([string] $Message) {
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Assert-PathExists([string] $Path, [string] $Description) {
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Missing $Description`: $Path"
    }

    Write-Host "  OK  $Description"
}

function Assert-NoAvaloniaArtifacts([string] $Directory) {
    $avaloniaDlls = @(Get-ChildItem -LiteralPath $Directory -Filter "Avalonia*.dll" -File -ErrorAction SilentlyContinue)
    if ($avaloniaDlls.Length -gt 0) {
        throw "Found unexpected Avalonia artifacts in publish output: $($avaloniaDlls.Name -join ', ')"
    }

    Write-Host "  OK  No Avalonia artifacts"
}

function Assert-SelfContainedRuntime([string] $Directory) {
    $hasCoreClr = Test-Path -LiteralPath (Join-Path $Directory "coreclr.dll")
    $hasWinAppRuntime = @(Get-ChildItem -LiteralPath $Directory -Filter "Microsoft.WindowsAppRuntime*.dll" -File -ErrorAction SilentlyContinue).Length -gt 0

    if (-not ($hasCoreClr -or $hasWinAppRuntime)) {
        throw "Publish output does not look self-contained (expected coreclr.dll or Microsoft.WindowsAppRuntime*.dll)."
    }

    Write-Host "  OK  Self-contained runtime files present"
}

function Test-LaunchSmoke([string] $ExecutablePath) {
    Write-Step "Launch smoke check"
    $process = Start-Process -FilePath $ExecutablePath -PassThru
    try {
        Start-Sleep -Seconds 3
        if ($process.HasExited) {
            throw "AutoQAC.exe exited during launch smoke check with code $($process.ExitCode)."
        }

        Write-Host "  OK  AutoQAC.exe started successfully"
    }
    finally {
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
    }
}

Push-Location $repoRoot
try {
    Write-Step "Restore solution"
    dotnet restore $solution
    if ($LASTEXITCODE -ne 0) { throw "Restore failed with exit code $LASTEXITCODE." }

    Write-Step "Build Release"
    dotnet build $solution -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Release build failed with exit code $LASTEXITCODE." }

    if (-not $SkipTests) {
        Write-Step "Run Release tests"
        dotnet test $solution -c Release --no-build
        if ($LASTEXITCODE -ne 0) { throw "Release tests failed with exit code $LASTEXITCODE." }
    }

    if (Test-Path -LiteralPath $resolvedOutput) {
        Write-Step "Clear existing publish output"
        Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
    }

    Write-Step "Publish self-contained Release"
    dotnet publish $project `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -o $resolvedOutput `
        --no-build
    if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE." }

    Write-Step "Verify publish layout"
    $exePath = Join-Path $resolvedOutput "AutoQAC.exe"
    Assert-PathExists $exePath "main executable"
    Assert-PathExists (Join-Path $resolvedOutput "AutoQAC Data\AutoQAC Main.yaml") "bundled main config"
    Assert-PathExists (Join-Path $resolvedOutput "AutoQAC Data\AutoQAC Settings.yaml") "bundled settings template"
    Assert-PathExists (Join-Path $resolvedOutput "Assets\AutoQAC.ico") "application icon"
    Assert-SelfContainedRuntime $resolvedOutput
    Assert-NoAvaloniaArtifacts $resolvedOutput

    if (-not $SkipLaunchSmoke) {
        Test-LaunchSmoke $exePath
    }

    Write-Host ""
    Write-Host "Publish complete." -ForegroundColor Green
    Write-Host "Output: $resolvedOutput"
    Write-Host "Distribute by zipping this folder for xcopy-style deployment."
}
finally {
    Pop-Location
}
