# Inventor Autosave

Inventor Autosave is a Windows add-in for Autodesk Inventor 2026. It can save dirty open documents, copy the project folder into timestamped snapshots, create a zip archive for each run, and optionally keep the unpacked snapshot directory.

## Projects

- `src/InventorAutosave`: Windows COM add-in and WinForms UI
- `src/InventorAutosave.Core`: shared autosave and snapshot logic
- `tests/InventorAutosave.Core.Tests`: automated tests for the core logic

## Build

```powershell
dotnet build .\src\InventorAutosave\InventorAutosave.csproj -c Release
```

## Test

```powershell
dotnet test .\tests\InventorAutosave.Core.Tests\InventorAutosave.Core.Tests.csproj
```

## Install

For end users, install from the downloadable MSI attached to each GitHub release.

Release assets include:

- `inventor-autosave-<version>.msi`: per-user installer for Inventor Autosave
- `inventor-autosave-<version>.zip`: raw published plugin payload
- `SHA256SUMS.txt`: checksums for the release assets

The installer payload and zip also include the project MIT `LICENSE`, a generated `THIRD-PARTY-NOTICES.md`, and generated per-package third-party license texts under `licenses\`.

## Developer Install

For local development and testing from a checkout:

```powershell
.\tools\install-inventor-autosave.ps1
```

The PowerShell installer builds the add-in, deploys it to the Inventor add-ins directory for Inventor 2026, and can restart Inventor unless `-SkipRestart` is supplied.

## Build Release Assets

On Windows, the release payload and MSI can be built from the repo with:

```powershell
.\tools\build-release-assets.ps1 -Version 0.1.0 -AssetVersionLabel 0.1.0
```

That build restores the local `nuget-license` tool, refreshes the repository `THIRD-PARTY-NOTICES.md`, generates the release payload notice/license texts, and will fail if the required compliance artifacts cannot be produced.
