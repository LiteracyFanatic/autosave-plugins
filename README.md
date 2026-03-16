# Inventor Autosave

Inventor Autosave is a Windows add-in for Autodesk Inventor 2026 that helps you capture intermediate project states over time by saving dirty documents and creating timestamped snapshots of a project folder.

### What It Does

- saves dirty open Inventor documents before a snapshot is taken
- creates a timestamped ZIP snapshot of the selected target folder
- can optionally keep the extracted snapshot folder in addition to the ZIP
- runs repeated automatic snapshots on a configurable timer
- lets you trigger an immediate manual snapshot with **Save Now**

The add-in always excludes its own `snapshots` folder from source files so it does not recurse into previous backups.

### Main Features

#### Timed autosave

You can start and stop automatic snapshots from the panel inside Inventor. While autosave is running, the add-in shows the countdown to the next snapshot and uses the configured interval in minutes.

#### Manual Save Now

The **Save Now** button runs an immediate snapshot without waiting for the timer. If automatic autosaves are already running, a manual snapshot restarts the timer from that point.

#### Edit environment handling

Some saves require Inventor to close an active edit environment first. The add-in supports two behaviors:

- **Close the edit environment and save automatically**
- **Ask me whether to save, ignore, or delay**

When prompting is enabled, **Delay** is only available while automatic autosaves are running. Choosing **Ignore** or **Delay** cancels the entire snapshot so you do not get a partial backup.

#### Snapshot capture rules

- ZIP archives are always created
- you can also keep the extracted snapshot folder
- the `snapshots` folder is always excluded from source files
- additional ignore patterns can be configured one per line

This makes it possible to include non-Inventor files from the working folder while still excluding generated or unwanted content.

#### Warnings for skipped files

The add-in can warn when:

- an open file is outside the configured target folder
- an unsaved document has no file name yet and cannot be autosaved

If those warnings are disabled, those files are skipped silently and the snapshot continues.

#### Notifications and logs

The panel can show desktop notifications when autosaves complete. Runtime logs are written under:

```text
%LOCALAPPDATA%\InventorAutosave\logs\
```

Typical files include:

- `inventor-autosave-YYYYMMDD.log`
- `startup-trace.log`

### Settings Available In The Panel

The current Inventor Autosave panel is grouped into these sections:

#### Target

- **Folder to snapshot**: the root folder copied into each snapshot

#### Schedule

- **Autosave interval**: timer interval in minutes
- **Show notifications**: whether completion balloons are shown

#### Save Behavior

- **If saving must exit an edit environment**: choose auto-close or prompt mode
- **Delay option (minutes)**: the delay used by prompt mode

#### Capture

- **Keep extracted snapshot folders in addition to ZIP archives**
- **Ignore patterns (one per line)**: glob-style patterns excluded from snapshots

#### Warnings

- **Warn when open files outside the target folder are skipped**
- **Warn when unsaved files are skipped**

## Install

For end users, install from the downloadable MSI attached to each GitHub release.

Release assets include:

- `inventor-autosave-<version>.msi`
- `inventor-autosave-<version>.zip`
- `SHA256SUMS.txt`

## Repository Layout

- `plugins/inventor-autosave`: the add-in source
- `plugins/inventor-autosave/src/InventorAutosave`: WinForms add-in and UI
- `plugins/inventor-autosave/src/InventorAutosave.Core`: shared autosave and snapshot logic
- `plugins/inventor-autosave/tests/InventorAutosave.Core.Tests`: automated tests
