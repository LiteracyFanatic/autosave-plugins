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

function Convert-ToSdkVersion
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

function Convert-ToSafeFileName
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    return ($Value -replace '[^0-9A-Za-z._-]', '-').Trim('-')
}

function Get-OptionalPackageProperty
{
    param(
        [Parameter(Mandatory = $true)]
        $Package,
        [Parameter(Mandatory = $true)]
        [string]$PropertyName
    )

    $property = $Package.PSObject.Properties[$PropertyName]
    if ($null -eq $property)
    {
        return ""
    }

    return [string]$property.Value
}

function Restore-LicenseTool
{
    Push-Location $repoRoot
    try
    {
        & dotnet tool restore
        if ($LASTEXITCODE -ne 0)
        {
            throw "dotnet tool restore failed with exit code $LASTEXITCODE."
        }
    }
    finally
    {
        Pop-Location
    }
}

function Invoke-NuGetLicenseReport
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$InputPath,
        [Parameter(Mandatory = $true)]
        [string]$OutputPath,
        [Parameter(Mandatory = $true)]
        [string]$DownloadDirectory,
        [string]$TargetFramework
    )

    Ensure-Directory -DirectoryPath (Split-Path -Parent $OutputPath)
    Ensure-Directory -DirectoryPath $DownloadDirectory

    $arguments = @(
        "tool",
        "run",
        "nuget-license",
        "--",
        "-i",
        $InputPath,
        "-t",
        "-o",
        "JsonPretty",
        "-fo",
        $OutputPath,
        "-d",
        $DownloadDirectory
    )

    if (-not [string]::IsNullOrWhiteSpace($TargetFramework))
    {
        $arguments += @("-f", $TargetFramework)
    }

    Push-Location $repoRoot
    try
    {
        & dotnet @arguments
        $exitCode = $LASTEXITCODE
    }
    finally
    {
        Pop-Location
    }

    if (-not (Test-Path -LiteralPath $OutputPath))
    {
        throw "nuget-license did not produce an output file for '$InputPath'. Exit code: $exitCode."
    }

    if ($exitCode -ne 0)
    {
        Write-Warning "nuget-license returned exit code $exitCode for '$InputPath'. Continuing because it still produced a report."
    }

    return (Get-Content -LiteralPath $OutputPath -Raw | ConvertFrom-Json)
}

function Get-RuntimePackageKeysFromDepsFile
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$DepsFilePath
    )

    if (-not (Test-Path -LiteralPath $DepsFilePath))
    {
        throw "Published dependency manifest not found: $DepsFilePath"
    }

    $deps = Get-Content -LiteralPath $DepsFilePath -Raw | ConvertFrom-Json
    $runtimePackageKeys = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)

    foreach ($property in $deps.libraries.PSObject.Properties)
    {
        if ($property.Value.type -eq "package")
        {
            $runtimePackageKeys.Add($property.Name) | Out-Null
        }
    }

    return $runtimePackageKeys
}

function Resolve-LicenseDisplayName
{
    param(
        [Parameter(Mandatory = $true)]
        $Package
    )

    $license = (Get-OptionalPackageProperty -Package $Package -PropertyName "License").Trim()
    if ([string]::IsNullOrWhiteSpace($license))
    {
        return "See bundled license text"
    }

    if ($license.Length -gt 80 -or $license.Contains("`n") -or $license.Contains("`r") -or $license -match '^\s*https?://')
    {
        return "See bundled license text"
    }

    return $license
}

function Resolve-UpstreamUrl
{
    param(
        [Parameter(Mandatory = $true)]
        $Package
    )

    $packageId = Get-OptionalPackageProperty -Package $Package -PropertyName "PackageId"
    $packageVersion = Get-OptionalPackageProperty -Package $Package -PropertyName "PackageVersion"

    return "https://www.nuget.org/packages/$packageId/$packageVersion"
}

function Get-ProjectSdkPackage
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectFilePath,
        [Parameter(Mandatory = $true)]
        $FallbackLicensePackage
    )

    [xml]$projectXml = Get-Content -LiteralPath $ProjectFilePath -Raw
    $sdkAttribute = [string]$projectXml.Project.Sdk
    if ([string]::IsNullOrWhiteSpace($sdkAttribute) -or -not $sdkAttribute.Contains("/"))
    {
        return $null
    }

    $sdkParts = $sdkAttribute.Split("/", 2)
    if ($sdkParts.Length -ne 2)
    {
        return $null
    }

    return [pscustomobject]@{
        PackageId = $sdkParts[0]
        PackageVersion = $sdkParts[1]
        License = Get-OptionalPackageProperty -Package $FallbackLicensePackage -PropertyName "License"
        LicenseUrl = Get-OptionalPackageProperty -Package $FallbackLicensePackage -PropertyName "LicenseUrl"
        LicenseInformationOrigin = Get-OptionalPackageProperty -Package $FallbackLicensePackage -PropertyName "LicenseInformationOrigin"
    }
}

function Copy-PackageLicenseArtifact
{
    param(
        [Parameter(Mandatory = $true)]
        $Package,
        [Parameter(Mandatory = $true)]
        [string[]]$DownloadDirectories,
        [Parameter(Mandatory = $true)]
        [string]$LicensesDestination
    )

    $packageId = Get-OptionalPackageProperty -Package $Package -PropertyName "PackageId"
    $packageVersion = Get-OptionalPackageProperty -Package $Package -PropertyName "PackageVersion"
    $safeBaseName = "$(Convert-ToSafeFileName -Value $packageId)-$(Convert-ToSafeFileName -Value $packageVersion)"

    foreach ($downloadDirectory in $DownloadDirectories)
    {
        if (-not (Test-Path -LiteralPath $downloadDirectory))
        {
            continue
        }

        $downloadedLicenseFile = Get-ChildItem -LiteralPath $downloadDirectory -File -Filter "$packageId`__$packageVersion.*" -ErrorAction SilentlyContinue |
            Select-Object -First 1

        if ($null -ne $downloadedLicenseFile)
        {
            $destinationFileName = "$safeBaseName$($downloadedLicenseFile.Extension.ToLowerInvariant())"
            $destinationPath = Join-Path $LicensesDestination $destinationFileName
            Copy-Item -LiteralPath $downloadedLicenseFile.FullName -Destination $destinationPath -Force
            return "licenses/$destinationFileName"
        }
    }

    $licenseText = Get-OptionalPackageProperty -Package $Package -PropertyName "License"
    if (-not [string]::IsNullOrWhiteSpace($licenseText))
    {
        $destinationFileName = "$safeBaseName.txt"
        $destinationPath = Join-Path $LicensesDestination $destinationFileName
        Set-Content -LiteralPath $destinationPath -Value $licenseText
        return "licenses/$destinationFileName"
    }

    throw "Unable to resolve a bundled license artifact for package '$packageId/$packageVersion'."
}

function Add-LicensePackagesToMap
{
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$PackageMap,
        [Parameter(Mandatory = $true)]
        $Packages,
        [Parameter(Mandatory = $true)]
        [string]$RequestedScope,
        [Parameter(Mandatory = $true)]
        [System.Collections.Generic.HashSet[string]]$RuntimePackageKeys,
        [Parameter(Mandatory = $true)]
        [string]$DownloadDirectory
    )

    foreach ($package in $Packages)
    {
        $packageId = Get-OptionalPackageProperty -Package $package -PropertyName "PackageId"
        $packageVersion = Get-OptionalPackageProperty -Package $package -PropertyName "PackageVersion"
        $packageKey = "$packageId/$packageVersion"
        $effectiveScope = if ($RequestedScope -eq "Plugin")
        {
            if ($RuntimePackageKeys.Contains($packageKey))
            {
                "Runtime"
            }
            else
            {
                "Build"
            }
        }
        else
        {
            $RequestedScope
        }

        if (-not $PackageMap.ContainsKey($packageKey))
        {
            $PackageMap[$packageKey] = [ordered]@{
                Package = $package
                Scopes = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
                DownloadDirectories = New-Object System.Collections.Generic.List[string]
            }
        }

        $PackageMap[$packageKey].Scopes.Add($effectiveScope) | Out-Null
        if (-not $PackageMap[$packageKey].DownloadDirectories.Contains($DownloadDirectory))
        {
            $PackageMap[$packageKey].DownloadDirectories.Add($DownloadDirectory)
        }
    }
}

function New-ThirdPartyNoticesContent
{
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$PackageMap,
        [Parameter(Mandatory = $true)]
        [hashtable]$BundledLicensePaths,
        [string]$BundledTextPrefix = ""
    )

    $orderedPackages = $PackageMap.Keys | Sort-Object | ForEach-Object { $PackageMap[$_] }
    $runtimePackages = @($orderedPackages | Where-Object { $_.Scopes.Contains("Runtime") })
    $buildAndTestPackages = @($orderedPackages | Where-Object { -not $_.Scopes.Contains("Runtime") })

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# Third-Party Notices")
    $lines.Add("")
    $lines.Add("The source code in this repository is licensed under MIT. Third-party dependencies remain under their own licenses and terms.")
    $lines.Add("")
    $lines.Add("Release assets produced by plugins/inventor-autosave/tools/build-release-assets.ps1 bundle:")
    $lines.Add("")
    $lines.Add("- LICENSE")
    $lines.Add("- THIRD-PARTY-NOTICES.md")
    $lines.Add("- licenses/ with per-package license texts generated by nuget-license")
    $lines.Add("")
    $lines.Add("## Runtime Packages Bundled With Inventor Autosave")
    $lines.Add("")
    $lines.Add("| Package | Version | License | Bundled text | Upstream |")
    $lines.Add("| --- | --- | --- | --- | --- |")

    foreach ($entry in $runtimePackages)
    {
        $package = $entry.Package
        $packageId = Get-OptionalPackageProperty -Package $package -PropertyName "PackageId"
        $packageVersion = Get-OptionalPackageProperty -Package $package -PropertyName "PackageVersion"
        $bundledPath = "$BundledTextPrefix$($BundledLicensePaths["$packageId/$packageVersion"])"
        $licenseLabel = Resolve-LicenseDisplayName -Package $package
        $upstreamUrl = Resolve-UpstreamUrl -Package $package
        $lines.Add("| $packageId | $packageVersion | $licenseLabel | $bundledPath | $upstreamUrl |")
    }

    $lines.Add("")
    $lines.Add("The runtime package list above comes from the published InventorAutosave.deps.json output for the plugin.")
    $lines.Add("")
    $lines.Add("## Build-Time And Test-Time Dependencies")
    $lines.Add("")
    $lines.Add("These packages are required to compile the add-in, build the installer, or run tests. They are not redistributed as runtime assemblies with the plugin, but their license information is bundled here for transparency.")
    $lines.Add("")
    $lines.Add("| Package | Version | Scope | License or terms | Bundled text | Upstream |")
    $lines.Add("| --- | --- | --- | --- | --- | --- |")

    foreach ($entry in $buildAndTestPackages)
    {
        $package = $entry.Package
        $packageId = Get-OptionalPackageProperty -Package $package -PropertyName "PackageId"
        $packageVersion = Get-OptionalPackageProperty -Package $package -PropertyName "PackageVersion"
        $scopeLabels = @($entry.Scopes | Sort-Object) -join ", "
        $bundledPath = "$BundledTextPrefix$($BundledLicensePaths["$packageId/$packageVersion"])"
        $licenseLabel = Resolve-LicenseDisplayName -Package $package
        $upstreamUrl = Resolve-UpstreamUrl -Package $package
        $lines.Add("| $packageId | $packageVersion | $scopeLabels | $licenseLabel | $bundledPath | $upstreamUrl |")
    }

    $lines.Add("")
    $lines.Add("## Notes")
    $lines.Add("")
    $lines.Add("- Dependency metadata and license texts are generated during the release build with the local nuget-license tool manifest.")
    $lines.Add("- Packages that expose license terms through embedded package files rather than SPDX identifiers are bundled as extracted text files.")

    return ($lines -join [Environment]::NewLine)
}

function Copy-ComplianceFiles
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$PayloadDirectory
    )

    $licensesDestination = Join-Path $PayloadDirectory "licenses"
    $noticePath = Join-Path $PayloadDirectory "THIRD-PARTY-NOTICES.md"
    $repositoryNoticePath = Join-Path $repoRoot "THIRD-PARTY-NOTICES.md"
    $temporaryLicenseRoot = Join-Path ([Path]::GetTempPath()) ("inventor-autosave-license-" + [Guid]::NewGuid().ToString("N"))
    $pluginLicenseJson = Join-Path $temporaryLicenseRoot "plugin\licenses.json"
    $pluginDownloadDirectory = Join-Path $temporaryLicenseRoot "plugin\downloaded"
    $testLicenseJson = Join-Path $temporaryLicenseRoot "tests\licenses.json"
    $testDownloadDirectory = Join-Path $temporaryLicenseRoot "tests\downloaded"
    $installerLicenseJson = Join-Path $temporaryLicenseRoot "installer\licenses.json"
    $installerDownloadDirectory = Join-Path $temporaryLicenseRoot "installer\downloaded"
    $testProjectPath = Join-Path $pluginRoot "tests\InventorAutosave.Core.Tests\InventorAutosave.Core.Tests.csproj"
    $depsFilePath = Join-Path $PayloadDirectory "InventorAutosave.deps.json"

    Ensure-Directory -DirectoryPath $licensesDestination
    Copy-RequiredFile -SourcePath (Join-Path $repoRoot "LICENSE") -DestinationPath (Join-Path $PayloadDirectory "LICENSE")
    Restore-LicenseTool

    try
    {
        $runtimePackageKeys = Get-RuntimePackageKeysFromDepsFile -DepsFilePath $depsFilePath
        $pluginPackages = Invoke-NuGetLicenseReport `
            -InputPath $projectPath `
            -OutputPath $pluginLicenseJson `
            -DownloadDirectory $pluginDownloadDirectory `
            -TargetFramework "net8.0-windows"
        $testPackages = Invoke-NuGetLicenseReport `
            -InputPath $testProjectPath `
            -OutputPath $testLicenseJson `
            -DownloadDirectory $testDownloadDirectory `
            -TargetFramework "net8.0"
        $installerPackages = Invoke-NuGetLicenseReport `
            -InputPath $installerProjectPath `
            -OutputPath $installerLicenseJson `
            -DownloadDirectory $installerDownloadDirectory

        $packageMap = @{}
        Add-LicensePackagesToMap -PackageMap $packageMap -Packages $pluginPackages -RequestedScope "Plugin" -RuntimePackageKeys $runtimePackageKeys -DownloadDirectory $pluginDownloadDirectory
        Add-LicensePackagesToMap -PackageMap $packageMap -Packages $testPackages -RequestedScope "Test" -RuntimePackageKeys $runtimePackageKeys -DownloadDirectory $testDownloadDirectory
        Add-LicensePackagesToMap -PackageMap $packageMap -Packages $installerPackages -RequestedScope "Build" -RuntimePackageKeys $runtimePackageKeys -DownloadDirectory $installerDownloadDirectory

        $installerFallbackLicensePackage = @($installerPackages | Select-Object -First 1)
        if ($installerFallbackLicensePackage.Count -gt 0)
        {
            $installerSdkPackage = Get-ProjectSdkPackage -ProjectFilePath $installerProjectPath -FallbackLicensePackage $installerFallbackLicensePackage[0]
            if ($null -ne $installerSdkPackage)
            {
                Add-LicensePackagesToMap -PackageMap $packageMap -Packages @($installerSdkPackage) -RequestedScope "Build" -RuntimePackageKeys $runtimePackageKeys -DownloadDirectory $installerDownloadDirectory
            }
        }

        $bundledLicensePaths = @{}
        foreach ($packageKey in ($packageMap.Keys | Sort-Object))
        {
            $entry = $packageMap[$packageKey]
            $bundledLicensePaths[$packageKey] = Copy-PackageLicenseArtifact `
                -Package $entry.Package `
                -DownloadDirectories ([string[]]$entry.DownloadDirectories) `
                -LicensesDestination $licensesDestination
        }

        $payloadNoticesContent = New-ThirdPartyNoticesContent -PackageMap $packageMap -BundledLicensePaths $bundledLicensePaths
        $repositoryNoticesContent = New-ThirdPartyNoticesContent -PackageMap $packageMap -BundledLicensePaths $bundledLicensePaths -BundledTextPrefix "release asset "
        Set-Content -LiteralPath $noticePath -Value $payloadNoticesContent
        Set-Content -LiteralPath $repositoryNoticePath -Value $repositoryNoticesContent
    }
    finally
    {
        Remove-IfExists -PathToRemove $temporaryLicenseRoot
    }
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

$sdkVersion = Convert-ToSdkVersion -RawVersion $Version
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
& dotnet publish $projectPath -c $Configuration -o $payloadDirectory "-p:Version=$sdkVersion"
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
