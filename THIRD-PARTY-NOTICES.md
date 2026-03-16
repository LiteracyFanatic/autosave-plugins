# Third-Party Notices

The source code in this repository is licensed under MIT. Third-party dependencies remain under their own licenses and terms.

Release assets produced by `plugins/inventor-autosave/tools/build-release-assets.ps1` bundle:

- `LICENSE`
- `THIRD-PARTY-NOTICES.md`
- `licenses/MIT.txt`
- `licenses/Apache-2.0.txt`
- `licenses/Autodesk.Inventor.Sdk-1.0.3-LICENSE.txt`
- `licenses/WixToolset-6.0.2-OSMFEULA.txt`

## Runtime Packages Bundled With Inventor Autosave

| Package | Version | License | Bundled text | Upstream |
| --- | --- | --- | --- | --- |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.0 | MIT | `licenses/MIT.txt` | https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection.Abstractions/10.0.0 |
| Microsoft.Extensions.Logging.Abstractions | 10.0.0 | MIT | `licenses/MIT.txt` | https://www.nuget.org/packages/Microsoft.Extensions.Logging.Abstractions/10.0.0 |
| Serilog | 4.3.1 | Apache-2.0 | `licenses/Apache-2.0.txt` | https://www.nuget.org/packages/Serilog/4.3.1 |
| Serilog.Sinks.File | 7.0.0 | Apache-2.0 | `licenses/Apache-2.0.txt` | https://www.nuget.org/packages/Serilog.Sinks.File/7.0.0 |
| System.Diagnostics.DiagnosticSource | 10.0.0 | MIT | `licenses/MIT.txt` | https://www.nuget.org/packages/System.Diagnostics.DiagnosticSource/10.0.0 |

The runtime package list above comes from the published `InventorAutosave.deps.json` output for the plugin.

## Direct Build-Time And Test-Time Dependencies

These packages are required to build the installer, compile the Inventor add-in, or run tests. They are not redistributed as runtime assemblies with the plugin, but their license information is still provided here for transparency.

| Package | Version | Scope | License or terms | Bundled text | Upstream |
| --- | --- | --- | --- | --- | --- |
| Autodesk.Inventor.Sdk | 1.0.3 | Build | Autodesk package `LICENSE.txt` | `licenses/Autodesk.Inventor.Sdk-1.0.3-LICENSE.txt` | https://www.nuget.org/packages/Autodesk.Inventor.Sdk/1.0.3 |
| WixToolset.Sdk | 6.0.2 | Build | NuGet binary release `OSMFEULA.txt` | `licenses/WixToolset-6.0.2-OSMFEULA.txt` | https://www.nuget.org/packages/WixToolset.Sdk/6.0.2 |
| WixToolset.UI.wixext | 6.0.2 | Build | NuGet binary release `OSMFEULA.txt` | `licenses/WixToolset-6.0.2-OSMFEULA.txt` | https://www.nuget.org/packages/WixToolset.UI.wixext/6.0.2 |
| WixToolset.Util.wixext | 6.0.2 | Build | NuGet binary release `OSMFEULA.txt` | `licenses/WixToolset-6.0.2-OSMFEULA.txt` | https://www.nuget.org/packages/WixToolset.Util.wixext/6.0.2 |
| coverlet.collector | 6.0.2 | Test | MIT | `licenses/MIT.txt` | https://www.nuget.org/packages/coverlet.collector/6.0.2 |
| Microsoft.NET.Test.Sdk | 17.12.0 | Test | MIT | `licenses/MIT.txt` | https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/17.12.0 |
| xunit | 2.9.2 | Test | Apache-2.0 | `licenses/Apache-2.0.txt` | https://www.nuget.org/packages/xunit/2.9.2 |
| xunit.runner.visualstudio | 2.8.2 | Test | Apache-2.0 | `licenses/Apache-2.0.txt` | https://www.nuget.org/packages/xunit.runner.visualstudio/2.8.2 |

## Notes

- Autodesk and WiX package terms are defined by the vendor-provided files shipped in those NuGet packages. Those documents are bundled with release assets so end users can inspect them without fetching the packages separately.
- Nothing in this notice changes the upstream license terms for those packages.
