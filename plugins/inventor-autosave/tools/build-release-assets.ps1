using namespace System.IO
using namespace System.IO.Compression
using namespace System.Security.Cryptography
using namespace System.Text
using namespace System.Security

[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Version = "0.0.0",
    [string]$AssetVersionLabel,
    [string]$OutputRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $PSCommandPath
$pluginRoot = [Path]::GetFullPath((Join-Path $scriptDir ".."))
$repoRoot = [Path]::GetFullPath((Join-Path $pluginRoot "..\.."))
$projectPath = Join-Path $pluginRoot "src\InventorAutosave\InventorAutosave.csproj"
$installerProjectPath = Join-Path $pluginRoot "installer\InventorAutosave.Setup.wixproj"

if ([string]::IsNullOrWhiteSpace($OutputRoot))
{
    $OutputRoot = Join-Path $pluginRoot "artifacts"
}

if ([string]::IsNullOrWhiteSpace($AssetVersionLabel))
{
    $AssetVersionLabel = $Version
}

function Convert-ToMsiVersion
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$RawVersion
    )

    if ($RawVersion -match '^[vV]?(\d+)\.(\d+)\.(\d+)$')
    {
        return "$($Matches[1]).$($Matches[2]).$($Matches[3])"
    }

    throw "Version '$RawVersion' must match vMAJOR.MINOR.PATCH or MAJOR.MINOR.PATCH."
}

function Convert-ToAssetLabel
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$RawLabel
    )

    $sanitized = $RawLabel -replace '[^0-9A-Za-z._-]', '-'
    $sanitized = $sanitized.Trim('-')

    if ([string]::IsNullOrWhiteSpace($sanitized))
    {
        throw "AssetVersionLabel '$RawLabel' does not contain any filesystem-safe characters."
    }

    return $sanitized.ToLowerInvariant()
}

function Remove-IfExists
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$PathToRemove
    )

    if (Test-Path -LiteralPath $PathToRemove)
    {
        Remove-Item -LiteralPath $PathToRemove -Recurse -Force
    }
}

function Ensure-Directory
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$DirectoryPath
    )

    New-Item -ItemType Directory -Path $DirectoryPath -Force | Out-Null
}

function Convert-ToIdentifier
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Prefix,
        [Parameter(Mandatory = $true)]
        [string]$Seed
    )

    $hashBytes = [MD5]::HashData([Encoding]::UTF8.GetBytes($Seed))
    $hashText = ([Convert]::ToHexString($hashBytes)).ToLowerInvariant()
    return "$Prefix$hashText"
}

function Convert-ToDeterministicGuid
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Seed
    )

    $hashBytes = [MD5]::HashData([Encoding]::UTF8.GetBytes($Seed))
    return [Guid]::new($hashBytes).ToString().ToUpperInvariant()
}

function Escape-Xml
{
    param(
        [AllowEmptyString()]
        [string]$Value
    )

    return [SecurityElement]::Escape($Value)
}

function Get-NuGetGlobalPackagesPath
{
    $nuGetLocalsOutput = & dotnet nuget locals global-packages --list
    if ($LASTEXITCODE -ne 0)
    {
        throw "Failed to determine the NuGet global packages location."
    }

    foreach ($line in $nuGetLocalsOutput)
    {
        if ($line -match '^\s*global-packages:\s*(.+?)\s*$')
        {
            return [Path]::GetFullPath($Matches[1])
        }
    }

    throw "dotnet nuget locals output did not include a global-packages entry."
}

function Copy-RequiredFile
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePath,
        [Parameter(Mandatory = $true)]
        [string]$DestinationPath
    )

    if (-not (Test-Path -LiteralPath $SourcePath))
    {
        throw "Required file not found: $SourcePath"
    }

    $destinationDirectory = Split-Path -Parent $DestinationPath
    if (-not [string]::IsNullOrWhiteSpace($destinationDirectory))
    {
        Ensure-Directory -DirectoryPath $destinationDirectory
    }

    Copy-Item -LiteralPath $SourcePath -Destination $DestinationPath -Force
}

function Copy-RequiredDirectory
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceDirectory,
        [Parameter(Mandatory = $true)]
        [string]$DestinationDirectory
    )

    if (-not (Test-Path -LiteralPath $SourceDirectory))
    {
        throw "Required directory not found: $SourceDirectory"
    }

    Ensure-Directory -DirectoryPath $DestinationDirectory
    Get-ChildItem -LiteralPath $SourceDirectory -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $DestinationDirectory -Recurse -Force
    }
}

function Get-PackagePath
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$GlobalPackagesPath,
        [Parameter(Mandatory = $true)]
        [string]$PackageId,
        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    $packagePath = Join-Path $GlobalPackagesPath ($PackageId.ToLowerInvariant())
    $packagePath = Join-Path $packagePath $Version

    if (-not (Test-Path -LiteralPath $packagePath))
    {
        throw "NuGet package '$PackageId/$Version' was not found in the global packages folder '$GlobalPackagesPath'."
    }

    return $packagePath
}

function Copy-PackageLicenseFile
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$GlobalPackagesPath,
        [Parameter(Mandatory = $true)]
        [string]$PackageId,
        [Parameter(Mandatory = $true)]
        [string]$Version,
        [Parameter(Mandatory = $true)]
        [string]$PackageRelativePath,
        [Parameter(Mandatory = $true)]
        [string]$DestinationPath
    )

    $packagePath = Get-PackagePath -GlobalPackagesPath $GlobalPackagesPath -PackageId $PackageId -Version $Version
    $sourcePath = Join-Path $packagePath $PackageRelativePath
    Copy-RequiredFile -SourcePath $sourcePath -DestinationPath $DestinationPath
}

function Copy-ComplianceFiles
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$PayloadDirectory
    )

    $licensesDestination = Join-Path $PayloadDirectory "licenses"
    Ensure-Directory -DirectoryPath $licensesDestination

    Copy-RequiredFile -SourcePath (Join-Path $repoRoot "LICENSE") -DestinationPath (Join-Path $PayloadDirectory "LICENSE")
    Copy-RequiredFile -SourcePath (Join-Path $repoRoot "THIRD-PARTY-NOTICES.md") -DestinationPath (Join-Path $PayloadDirectory "THIRD-PARTY-NOTICES.md")
    Copy-RequiredDirectory -SourceDirectory (Join-Path $repoRoot "licenses") -DestinationDirectory $licensesDestination

    $globalPackagesPath = Get-NuGetGlobalPackagesPath

    Copy-PackageLicenseFile `
        -GlobalPackagesPath $globalPackagesPath `
        -PackageId "Autodesk.Inventor.Sdk" `
        -Version "1.0.3" `
        -PackageRelativePath "LICENSE.txt" `
        -DestinationPath (Join-Path $licensesDestination "Autodesk.Inventor.Sdk-1.0.3-LICENSE.txt")

    Copy-PackageLicenseFile `
        -GlobalPackagesPath $globalPackagesPath `
        -PackageId "WixToolset.Sdk" `
        -Version "6.0.2" `
        -PackageRelativePath "OSMFEULA.txt" `
        -DestinationPath (Join-Path $licensesDestination "WixToolset-6.0.2-OSMFEULA.txt")
}

function New-DirectoryTree
{
    param(
        [string[]]$RelativeDirectories
    )

    $nodes = @{}
    $nodes[""] = [ordered]@{
        Id = "INSTALLFOLDER"
        Name = ""
        Children = New-Object System.Collections.Generic.List[string]
        Components = New-Object System.Collections.Generic.List[object]
    }

    foreach ($relativeDirectory in $RelativeDirectories)
    {
        $currentPath = ""
        foreach ($segment in ($relativeDirectory -split '[\\/]'))
        {
            if ([string]::IsNullOrWhiteSpace($segment))
            {
                continue
            }

            $nextPath = if ([string]::IsNullOrWhiteSpace($currentPath)) { $segment } else { Join-Path $currentPath $segment }
            if (-not $nodes.Contains($nextPath))
            {
                $nodes[$nextPath] = [ordered]@{
                    Id = Convert-ToIdentifier -Prefix "DIR_" -Seed $nextPath
                    Name = $segment
                    Children = New-Object System.Collections.Generic.List[string]
                    Components = New-Object System.Collections.Generic.List[object]
                }

                $nodes[$currentPath].Children.Add($nextPath)
            }

            $currentPath = $nextPath
        }
    }

    return $nodes
}

function Write-DirectoryNode
{
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$Nodes,
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [int]$IndentLevel
    )

    $indent = '  ' * $IndentLevel
    $node = $Nodes[$Path]
    $lines = New-Object System.Collections.Generic.List[string]

    foreach ($component in $node.Components)
    {
        $filePath = Escape-Xml -Value $component.FilePath
        $registryName = Escape-Xml -Value $component.RegistryName

        foreach ($line in @(
            "$indent<Component Id=`"$($component.ComponentId)`" Guid=`"{$($component.Guid)}`">",
            "$indent  <File Id=`"$($component.FileId)`" Source=`"$filePath`" />",
            "$indent  <RegistryValue Root=`"HKCU`" Key=`"Software\AutosavePlugins\InventorAutosave\Components`" Name=`"$registryName`" Type=`"integer`" Value=`"1`" KeyPath=`"yes`" />",
            "$indent</Component>"
        ))
        {
            $lines.Add($line)
        }
    }

    foreach ($childPath in $node.Children)
    {
        $childNode = $Nodes[$childPath]
        $childName = Escape-Xml -Value $childNode.Name

        $lines.Add("$indent<Directory Id=`"$($childNode.Id)`" Name=`"$childName`">")
        $lines.Add((Write-DirectoryNode -Nodes $Nodes -Path $childPath -IndentLevel ($IndentLevel + 1)))
        $lines.Add("$indent</Directory>")
    }

    return ($lines -join [Environment]::NewLine)
}

function Write-DirectoryCleanupMarkup
{
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$Nodes
    )

    $lines = New-Object System.Collections.Generic.List[string]

    $lines.Add('        <RemoveFile Id="RemoveFilesFromInstallFolder" Directory="INSTALLFOLDER" Name="*" On="uninstall" />')
    $lines.Add('        <RemoveFolder Id="RemoveInstallFolder" Directory="INSTALLFOLDER" On="uninstall" />')

    $orderedPaths = $Nodes.Keys |
        Where-Object { $_ -ne "" } |
        Sort-Object `
            @{ Expression = { ($_ -split '[\\/]').Count }; Descending = $true }, `
            @{ Expression = { $_ }; Descending = $false }

    foreach ($path in $orderedPaths)
    {
        $directoryId = $Nodes[$path].Id
        $removeFilesId = Convert-ToIdentifier -Prefix "RMF_" -Seed $path
        $removeFolderId = Convert-ToIdentifier -Prefix "RMD_" -Seed $path

        $lines.Add("        <RemoveFile Id=`"$removeFilesId`" Directory=`"$directoryId`" Name=`"*`" On=`"uninstall`" />")
        $lines.Add("        <RemoveFolder Id=`"$removeFolderId`" Directory=`"$directoryId`" On=`"uninstall`" />")
    }

    $lines.Add('        <RemoveFolder Id="RemoveAddinsFolder" Directory="ADDINSFOLDER" On="uninstall" />')
    $lines.Add('        <RemoveFolder Id="RemoveInventorFolder" Directory="INVENTORFOLDER" On="uninstall" />')
    $lines.Add('        <RemoveFolder Id="RemoveAutodeskFolder" Directory="AUTODESKFOLDER" On="uninstall" />')

    return ($lines -join [Environment]::NewLine)
}

function New-PayloadManifest
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$PayloadDirectory,
        [Parameter(Mandatory = $true)]
        [string]$ManifestPath
    )

    $payloadFiles = Get-ChildItem -LiteralPath $PayloadDirectory -File -Recurse | Sort-Object FullName
    if ($payloadFiles.Count -eq 0)
    {
        throw "No payload files found in $PayloadDirectory"
    }

    $relativeDirectories = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
    $componentIds = New-Object System.Collections.Generic.List[string]
    $nodes = $null

    foreach ($payloadFile in $payloadFiles)
    {
        $relativePath = [Path]::GetRelativePath($PayloadDirectory, $payloadFile.FullName)
        $relativeDirectory = [Path]::GetDirectoryName($relativePath)
        if (-not [string]::IsNullOrWhiteSpace($relativeDirectory))
        {
            $relativeDirectories.Add($relativeDirectory) | Out-Null
        }
    }

    $nodes = New-DirectoryTree -RelativeDirectories ([string[]]$relativeDirectories)

    foreach ($payloadFile in $payloadFiles)
    {
        $relativePath = [Path]::GetRelativePath($PayloadDirectory, $payloadFile.FullName)
        $relativeDirectory = [Path]::GetDirectoryName($relativePath)
        if ($null -eq $relativeDirectory)
        {
            $relativeDirectory = ""
        }

        $componentId = Convert-ToIdentifier -Prefix "CMP_" -Seed $relativePath
        $componentIds.Add($componentId) | Out-Null

        $nodes[$relativeDirectory].Components.Add([ordered]@{
            ComponentId = $componentId
            FileId = Convert-ToIdentifier -Prefix "FIL_" -Seed $relativePath
            FilePath = $payloadFile.FullName
            Guid = Convert-ToDeterministicGuid -Seed "InventorAutosave:$relativePath"
            RegistryName = ($relativePath -replace '[\\/:]', '_')
        })
    }

    $directoryMarkup = Write-DirectoryNode -Nodes $nodes -Path "" -IndentLevel 2
    $directoryCleanupMarkup = Write-DirectoryCleanupMarkup -Nodes $nodes
    $componentRefMarkup = ($componentIds | ForEach-Object { "    <ComponentRef Id=`"$_`" />" }) -join [Environment]::NewLine

    $manifest = @(
        '<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs" xmlns:util="http://wixtoolset.org/schemas/v4/wxs/util">',
        '  <Fragment>',
        '    <DirectoryRef Id="INSTALLFOLDER">',
        '      <Component Id="CleanupFolders" Guid="{DD757673-53C4-43A9-921B-9AED97D7F3DB}">',
        '        <RegistryValue Root="HKCU" Key="Software\AutosavePlugins\InventorAutosave" Name="Installed" Type="integer" Value="1" KeyPath="yes" />',
        '        <RegistryValue Root="HKCU" Key="Software\AutosavePlugins\InventorAutosave" Name="InstallFolder" Type="string" Value="[INSTALLFOLDER]" />',
        '        <util:RemoveFolderEx Property="INSTALLFOLDERPATH" On="uninstall" />',
        $directoryCleanupMarkup,
        '      </Component>',
        $directoryMarkup,
        '    </DirectoryRef>',
        '  </Fragment>',
        '  <Fragment>',
        '    <ComponentGroup Id="PayloadComponents">',
        '      <ComponentRef Id="CleanupFolders" />',
        $componentRefMarkup,
        '    </ComponentGroup>',
        '  </Fragment>',
        '</Wix>'
    ) -join [Environment]::NewLine

    Set-Content -LiteralPath $ManifestPath -Value $manifest
}

$msiVersion = Convert-ToMsiVersion -RawVersion $Version
$safeAssetLabel = Convert-ToAssetLabel -RawLabel $AssetVersionLabel

$outputRootFullPath = [Path]::GetFullPath($OutputRoot)
$stagingRoot = Join-Path $outputRootFullPath "staging"
$payloadDirectory = Join-Path $stagingRoot "payload"
$generatedPayloadManifest = Join-Path $stagingRoot "GeneratedPayload.wxs"
$installerOutputDirectory = Join-Path $stagingRoot "installer"
$assetsDirectory = Join-Path $outputRootFullPath "release"

Remove-IfExists -PathToRemove $stagingRoot
Remove-IfExists -PathToRemove $assetsDirectory

New-Item -ItemType Directory -Path $payloadDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $installerOutputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $assetsDirectory -Force | Out-Null

Write-Host "Publishing Inventor Autosave..."
& dotnet publish $projectPath -c $Configuration -o $payloadDirectory "-p:Version=$Version"
if ($LASTEXITCODE -ne 0)
{
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Write-Host "Restoring installer dependencies..."
& dotnet restore $installerProjectPath
if ($LASTEXITCODE -ne 0)
{
    throw "dotnet restore failed for the installer project with exit code $LASTEXITCODE."
}

$requiredFiles = @(
    "InventorAutosave.dll",
    "InventorAutosave.Inventor.addin"
)

foreach ($requiredFile in $requiredFiles)
{
    $requiredPath = Join-Path $payloadDirectory $requiredFile
    if (-not (Test-Path -LiteralPath $requiredPath))
    {
        throw "Expected published output not found: $requiredPath"
    }
}

Get-ChildItem -LiteralPath $payloadDirectory -Filter *.pdb -File | Remove-Item -Force

Write-Host "Bundling license and notice files..."
Copy-ComplianceFiles -PayloadDirectory $payloadDirectory

Write-Host "Generating WiX payload manifest..."
New-PayloadManifest -PayloadDirectory $payloadDirectory -ManifestPath $generatedPayloadManifest

$zipPath = Join-Path $assetsDirectory "inventor-autosave-$safeAssetLabel.zip"
$msiPath = Join-Path $assetsDirectory "inventor-autosave-$safeAssetLabel.msi"
$checksumsPath = Join-Path $assetsDirectory "SHA256SUMS.txt"

Write-Host "Creating payload zip..."
Compress-Archive -Path (Join-Path $payloadDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "Building MSI installer..."
& dotnet build $installerProjectPath -c $Configuration `
    "-p:PayloadDir=$payloadDirectory" `
    "-p:GeneratedPayloadWxs=$generatedPayloadManifest" `
    "-p:InstallerVersion=$msiVersion" `
    "-p:OutputPath=$installerOutputDirectory" `
    "-p:OutputName=inventor-autosave-$safeAssetLabel"
if ($LASTEXITCODE -ne 0)
{
    throw "Installer build failed with exit code $LASTEXITCODE."
}

$builtMsiPath = Join-Path $installerOutputDirectory "inventor-autosave-$safeAssetLabel.msi"
if (-not (Test-Path -LiteralPath $builtMsiPath))
{
    throw "Expected installer output not found: $builtMsiPath"
}

Copy-Item -LiteralPath $builtMsiPath -Destination $msiPath -Force

$hashEntries = @()
foreach ($assetPath in @($msiPath, $zipPath))
{
    $hash = Get-FileHash -LiteralPath $assetPath -Algorithm SHA256
    $hashEntries += "{0} *{1}" -f $hash.Hash.ToLowerInvariant(), (Split-Path -Leaf $assetPath)
}

Set-Content -LiteralPath $checksumsPath -Value $hashEntries

Write-Host ""
Write-Host "Release assets created:"
Get-ChildItem -LiteralPath $assetsDirectory -File | Sort-Object Name | ForEach-Object {
    Write-Host "  $($_.FullName)"
}
