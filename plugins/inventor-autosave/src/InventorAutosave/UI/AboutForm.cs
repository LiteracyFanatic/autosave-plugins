using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using InventorAutosave.Services;

namespace InventorAutosave.UI;

internal sealed class AboutForm : Form
{
    private const string RepositoryUrl = "https://github.com/LiteracyFanatic/autosave-plugins";

    private static readonly AboutPackage[] FallbackRuntimePackages =
    {
        new("Microsoft.Extensions.DependencyInjection.Abstractions", "10.0.0", "MIT"),
        new("Microsoft.Extensions.Logging.Abstractions", "10.0.0", "MIT"),
        new("Serilog", "4.3.1", "Apache-2.0"),
        new("Serilog.Sinks.File", "7.0.0", "Apache-2.0"),
        new("System.Diagnostics.DiagnosticSource", "10.0.0", "MIT"),
    };

    public AboutForm()
    {
        Text = "About Inventor Autosave";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        MinimumSize = new System.Drawing.Size(640, 460);
        Width = 680;
        Height = 520;
        Padding = new Padding(12);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new System.Drawing.Font("Segoe UI", 12f, System.Drawing.FontStyle.Bold),
            Text = "Inventor Autosave",
            Margin = new Padding(0, 0, 0, 6),
        }, 0, 0);

        var versionPanel = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = new Padding(0, 0, 0, 6),
        };
        versionPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        versionPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        versionPanel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = $"Build: {AutosaveLogManager.GetBuildVersion()}",
            Margin = new Padding(0, 0, 8, 0),
        }, 0, 0);

        var copyVersionButton = new Button
        {
            AutoSize = true,
            Text = "Copy Version",
            Margin = new Padding(0),
        };
        copyVersionButton.Click += (_, _) => CopyText($"Build: {AutosaveLogManager.GetBuildVersion()}");
        versionPanel.Controls.Add(copyVersionButton, 1, 0);
        root.Controls.Add(versionPanel, 0, 1);

        root.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(620, 0),
            Text = "Inventor Autosave is an Autodesk Inventor add-in that saves dirty documents and captures timestamped project-folder snapshots to track how a project evolves over time.",
            Margin = new Padding(0, 0, 0, 10),
        }, 0, 2);

        var repoPanel = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = new Padding(0, 0, 0, 12),
        };
        repoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        repoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        repoPanel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = RepositoryUrl,
            Margin = new Padding(0, 0, 8, 0),
        }, 0, 0);

        var openRepositoryButton = new Button
        {
            AutoSize = true,
            Text = "Open Repository",
            Margin = new Padding(0),
        };
        openRepositoryButton.Click += (_, _) => OpenRepository();
        repoPanel.Controls.Add(openRepositoryButton, 1, 0);
        root.Controls.Add(repoPanel, 0, 3);

        var packagesGroup = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "Runtime Packages And Licenses",
            Padding = new Padding(10),
        };

        var packageList = new ListView
        {
            Dock = DockStyle.Fill,
            FullRowSelect = true,
            GridLines = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            HideSelection = false,
            MultiSelect = false,
            View = View.Details,
        };
        packageList.Columns.Add("Package", 330);
        packageList.Columns.Add("Version", 90);
        packageList.Columns.Add("License", 120);

        foreach (var package in LoadRuntimePackages())
        {
            var item = new ListViewItem(package.Name);
            item.SubItems.Add(package.Version);
            item.SubItems.Add(package.License);
            packageList.Items.Add(item);
        }

        packageList.HandleCreated += (_, _) => ResizePackageColumns(packageList);
        packageList.ClientSizeChanged += (_, _) => ResizePackageColumns(packageList);

        packagesGroup.Controls.Add(packageList);
        root.Controls.Add(packagesGroup, 0, 4);

        var closeButton = new Button
        {
            AutoSize = true,
            DialogResult = DialogResult.OK,
            Text = "Close",
        };
        AcceptButton = closeButton;
        CancelButton = closeButton;

        var footer = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0),
            WrapContents = false,
        };
        footer.Controls.Add(closeButton);
        root.Controls.Add(footer, 0, 5);

        Controls.Add(root);
    }

    private static IReadOnlyList<AboutPackage> LoadRuntimePackages()
    {
        try
        {
            var noticesPath = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md");
            if (!File.Exists(noticesPath))
            {
                return FallbackRuntimePackages;
            }

            var packages = new List<AboutPackage>();
            var inRuntimeSection = false;

            foreach (var rawLine in File.ReadLines(noticesPath))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    if (inRuntimeSection)
                    {
                        break;
                    }

                    inRuntimeSection = line.Equals("## Runtime Packages Bundled With Inventor Autosave", StringComparison.Ordinal);
                    continue;
                }

                if (!inRuntimeSection || !line.StartsWith("|", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.Contains("---", StringComparison.Ordinal))
                {
                    continue;
                }

                var columns = line.Split('|')
                    .Select(value => value.Trim())
                    .Where(value => value.Length > 0)
                    .ToArray();
                if (columns.Length < 3 || columns[0].Equals("Package", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                packages.Add(new AboutPackage(columns[0], columns[1], columns[2]));
            }

            return packages.Count > 0 ? packages : FallbackRuntimePackages;
        }
        catch
        {
            return FallbackRuntimePackages;
        }
    }

    private static void ResizePackageColumns(ListView packageList)
    {
        if (packageList.Columns.Count != 3 || packageList.ClientSize.Width <= 0)
        {
            return;
        }

        var versionWidth = 90;
        var licenseWidth = 120;
        var packageWidth = Math.Max(180, packageList.ClientSize.Width - versionWidth - licenseWidth - SystemInformation.VerticalScrollBarWidth - 4);

        packageList.Columns[0].Width = packageWidth;
        packageList.Columns[1].Width = versionWidth;
        packageList.Columns[2].Width = Math.Max(100, packageList.ClientSize.Width - packageWidth - versionWidth - 4);
    }

    private static void OpenRepository()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = RepositoryUrl,
                UseShellExecute = true,
            });
        }
        catch
        {
        }
    }

    private static void CopyText(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
        }
    }

    private readonly record struct AboutPackage(string Name, string Version, string License);
}
