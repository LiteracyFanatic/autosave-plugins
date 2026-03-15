using namespace System.IO

[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$InventorVersion = "2026",
    [string]$ProjectPath,
    [string]$TargetDirectory,
    [switch]$SkipBuild,
    [switch]$SkipRestart,
    [switch]$SkipRefresh
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$pluginName = "InventorAutosave"
$targetFramework = "net8.0-windows"

$scriptDir = Split-Path -Parent $PSCommandPath
$pluginRoot = [Path]::GetFullPath((Join-Path $scriptDir ".."))

if ([string]::IsNullOrWhiteSpace($ProjectPath))
{
    $ProjectPath = Join-Path $pluginRoot "src\$pluginName\$pluginName.csproj"
}

$ProjectPath = [Path]::GetFullPath($ProjectPath)

if (-not (Test-Path -LiteralPath $ProjectPath))
{
    throw "Project file not found: $ProjectPath"
}

if ([string]::IsNullOrWhiteSpace($TargetDirectory))
{
    $TargetDirectory = Join-Path $env:APPDATA "Autodesk\Inventor $InventorVersion\Addins\$pluginName"
}

$projectDirectory = Split-Path -Parent $ProjectPath
$outputDirectory = Join-Path $projectDirectory "bin\$Configuration\$targetFramework"

if ($SkipRefresh)
{
    Write-Warning "-SkipRefresh is deprecated. Use -SkipRestart instead."
    $SkipRestart = $true
}

function Invoke-Build
{
    if (Test-Path -LiteralPath $outputDirectory)
    {
        Write-Host "Cleaning build output at $outputDirectory"
        Remove-Item -LiteralPath $outputDirectory -Recurse -Force
    }

    Write-Host "Building $pluginName ($Configuration)..."
    & dotnet build $ProjectPath -c $Configuration
    if ($LASTEXITCODE -ne 0)
    {
        throw "dotnet build failed with exit code $LASTEXITCODE."
    }
}

function Assert-OutputExists
{
    $requiredFiles = @(
        "$pluginName.dll",
        "$pluginName.Inventor.addin"
    )

    foreach ($requiredFile in $requiredFiles)
    {
        $requiredPath = Join-Path $outputDirectory $requiredFile
        if (-not (Test-Path -LiteralPath $requiredPath))
        {
            throw "Expected build output not found: $requiredPath"
        }
    }
}

function Copy-PluginOutput
{
    Write-Host "Deploying to $TargetDirectory"

    New-Item -ItemType Directory -Path $TargetDirectory -Force | Out-Null

    Get-ChildItem -LiteralPath $TargetDirectory -Force | Remove-Item -Recurse -Force
    Get-ChildItem -LiteralPath $outputDirectory -File | Copy-Item -Destination $TargetDirectory -Force
}

function Resolve-InventorExecutablePath
{
    $candidatePaths = @(
        (Join-Path $env:ProgramFiles "Autodesk\Inventor $InventorVersion\Bin\Inventor.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Autodesk\Inventor $InventorVersion\Bin\Inventor.exe")
    )

    foreach ($candidatePath in $candidatePaths)
    {
        if (-not [string]::IsNullOrWhiteSpace($candidatePath) -and (Test-Path -LiteralPath $candidatePath))
        {
            return $candidatePath
        }
    }

    return $null
}

function Stop-Inventor
{
    Write-Host "Stopping Inventor..."
    Stop-Process -Name "Inventor" -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}

function Start-Inventor
{
    param(
        [string]$ExecutablePath
    )

    if ($SkipRestart)
    {
        Write-Host "Skipping Inventor restart."
        return
    }

    if ([string]::IsNullOrWhiteSpace($ExecutablePath))
    {
        Write-Warning "Inventor executable path could not be resolved. Start Inventor manually to load the plugin."
        return
    }

    Write-Host "Starting Inventor..."
    Start-Process -FilePath $ExecutablePath | Out-Null
}

if (-not $SkipBuild)
{
    Invoke-Build
}
else
{
    Write-Host "Skipping build."
}

Assert-OutputExists
$inventorExecutablePath = Resolve-InventorExecutablePath
if (-not $SkipRestart)
{
    Stop-Inventor
}
Copy-PluginOutput
Start-Inventor -ExecutablePath $inventorExecutablePath

Write-Host ""
Write-Host "Done."
Write-Host "Installed files:"
Get-ChildItem -LiteralPath $TargetDirectory -File | Sort-Object Name | ForEach-Object {
    Write-Host "  $($_.Name)"
}
