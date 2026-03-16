# Inventor Autosave

Inventor Autosave is a Windows add-in for Autodesk Inventor 2026 that helps you capture intermediate project states over time by saving open documents and creating timestamped snapshots of a project folder.

## What The Add-In Does

- saves dirty open Inventor documents before a snapshot is taken
- creates a timestamped ZIP snapshot of the selected target folder
- can optionally keep the extracted snapshot folder in addition to the ZIP
- tracks repeated automatic snapshots on a configurable timer
- lets you trigger an immediate manual snapshot with **Save Now**

The snapshot always excludes the add-in's own `snapshots` folder so it does not recurse into previous backups.

## Main Features

### Timed Autosave

You can start and stop automatic snapshots from the panel inside Inventor. When autosave is running, the add-in shows the countdown to the next snapshot and uses the configured interval in minutes.

### Manual Save Now

The **Save Now** button runs an immediate snapshot without waiting for the timer. If automatic autosave is already running, the manual snapshot restarts the timer from that point.

### Edit Environment Handling

Some Inventor saves require closing an active edit environment first. The add-in supports two behaviors:

- **Close the edit environment and save automatically**
- **Ask me whether to save, ignore, or delay**

When prompting is enabled, **Delay** is only offered while automatic autosaves are running. Choosing **Ignore** or **Delay** cancels the entire snapshot so you do not get a partial backup.

### Snapshot Capture Rules

- ZIP archives are always created
- you can also keep the extracted snapshot folder
- the `snapshots` folder is always excluded from source files
- additional ignore patterns can be configured one per line

This makes it possible to include non-Inventor files from the working folder while still excluding generated or unwanted content.

### Warnings For Skipped Files

The add-in can warn when:

- an open file is outside the configured target folder
- an unsaved document has no file name yet and cannot be autosaved

If those warnings are disabled, those documents are skipped silently and the snapshot continues.

### Notifications And Logs

The panel can show desktop notifications when autosaves complete. Runtime logs are written under:

```text
%LOCALAPPDATA%\InventorAutosave\logs\
```

Typical files include:

- `inventor-autosave-YYYYMMDD.log`
- `startup-trace.log`

## Settings In The Panel

The Inventor panel is grouped into the following sections:

### Target

- **Folder to snapshot**: the root folder copied into each snapshot

### Schedule

- **Autosave interval**: timer interval in minutes
- **Show notifications**: whether completion balloons are shown

### Save Behavior

- **If saving must exit an edit environment**: choose auto-close or prompt mode
- **Delay option (minutes)**: the delay used by the prompt mode

### Capture

- **Keep extracted snapshot folders in addition to ZIP archives**
- **Ignore patterns (one per line)**: glob-style patterns excluded from snapshots

### Warnings

- **Warn when open files outside the target folder are skipped**
- **Warn when unsaved files are skipped**

## Install

For end users, install from the downloadable MSI attached to each GitHub release.

Release assets include:

- `inventor-autosave-<version>.msi`
- `inventor-autosave-<version>.zip`
- `SHA256SUMS.txt`

## Developer Install

For local development and testing from a checkout:

```powershell
.\tools\install-inventor-autosave.ps1
```

The PowerShell installer builds the add-in, deploys it to the Inventor add-ins directory for Inventor 2026, and can restart Inventor unless `-SkipRestart` is supplied.

## Build

```powershell
dotnet build .\src\InventorAutosave\InventorAutosave.csproj -c Release
```

## Test

```powershell
dotnet test .\tests\InventorAutosave.Core.Tests\InventorAutosave.Core.Tests.csproj
```

## Build Release Assets

On Windows, the release payload and MSI can be built from the repo with:

```powershell
.\tools\build-release-assets.ps1 -Version 0.1.0 -AssetVersionLabel 0.1.0
```
