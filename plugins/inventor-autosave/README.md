# Inventor Autosave

Inventor Autosave is a Windows add-in for Autodesk Inventor 2026 that saves dirty open documents in place on a timed or manual cadence.

## What The Add-In Does

- saves dirty open Inventor documents that already have a file path
- skips unsaved documents so autosave never opens a Save As prompt
- tracks repeated automatic saves on a configurable timer that defaults to 5 minutes
- lets you trigger an immediate manual save with **Save Now**
- prompts before interrupting active edit environments or modal commands

Autosave does not create project snapshot folders, ZIP archives, copied project folders, or git-managed save artifacts.

## Main Features

### Timed Autosave

You can start and stop automatic saves from the panel inside Inventor. When autosave is running, the add-in shows the countdown to the next save and uses the configured interval in minutes.

### Manual Save Now

The **Save Now** button runs an immediate save without waiting for the timer. If automatic autosave is already running, the manual save restarts the timer from that point.

### Modal Command Handling

Some Inventor saves require resolving an active edit environment or command first. When that happens, the add-in asks whether to:

- **Save Now**: exit or stop the active edit state, then save in place
- **Ignore**: skip that document for the current autosave run
- **Delay**: defer that document for the configured delay interval

Choosing **Ignore** or **Delay** affects only that document. Autosave continues evaluating the other open documents in the same run.

### Warnings For Skipped Files

The add-in can warn when an unsaved document has no file name yet and cannot be autosaved. If that warning is disabled, unsaved documents are skipped silently.

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

### Schedule

- **Autosave interval**: timer interval in minutes
- **Show notifications**: whether completion balloons are shown

### Modal Commands

- **Delay option (minutes)**: how long delayed documents wait before retry

### Warnings

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
