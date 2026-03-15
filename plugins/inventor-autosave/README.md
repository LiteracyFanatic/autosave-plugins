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

```powershell
.\tools\install-inventor-autosave.ps1
```

The installer builds the add-in, deploys it to the Inventor add-ins directory for Inventor 2026, and can restart Inventor unless `-SkipRestart` is supplied.
