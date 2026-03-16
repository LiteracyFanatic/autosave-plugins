# Third-Party Notices

Release assets produced by `plugins/inventor-autosave/tools/build-release-assets.ps1` generate and bundle:

- `LICENSE`
- `THIRD-PARTY-NOTICES.md`
- `licenses/` with per-package license texts produced from `nuget-license`

The generated notice file is based on:

- the published `InventorAutosave.deps.json` runtime dependency graph for bundled plugin assemblies
- the Inventor add-in project package graph
- the installer package graph
- the test project package graph

Packages that expose license terms through embedded package files instead of SPDX identifiers are bundled as extracted text files during the release build.
