using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using InventorAutosave.Core;
using InventorAutosave.Core.Logic;

namespace InventorAutosave.UI;

internal sealed class AutosaveDebugForm : Form
{
    public AutosaveDebugForm(SnapshotRunResult result, SnapshotTriggerSource source)
    {
        Text = "Autosave Debug";
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MinimumSize = new Size(760, 520);
        Size = new Size(900, 620);

        var summaryTextBox = new TextBox
        {
            Dock = DockStyle.Top,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Height = 140,
            Font = new Font("Segoe UI", 9f, FontStyle.Regular),
            Text = BuildSummary(result, source),
        };

        var tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
        };
        tabControl.TabPages.Add(CreateListPage(
            $"Dirty Detected ({result.DirtyDocumentsDetected.Count})",
            result.DirtyDocumentsDetected,
            "No eligible dirty files were detected."));
        tabControl.TabPages.Add(CreateListPage(
            $"Saved ({result.SavedDocuments.Count})",
            result.SavedDocuments,
            "No files were saved."));

        var diffEmptyText = string.IsNullOrWhiteSpace(result.PreviousSnapshotPath)
            ? "No previous snapshot was available for hash comparison."
            : "No files differed from the previous snapshot.";
        tabControl.TabPages.Add(CreateListPage(
            $"Hash Diff ({result.FilesDifferentFromPreviousSnapshot.Count})",
            result.FilesDifferentFromPreviousSnapshot,
            diffEmptyText));

        var closeButton = new Button
        {
            Text = "Close",
            DialogResult = DialogResult.OK,
            Dock = DockStyle.Bottom,
            Height = 34,
        };

        AcceptButton = closeButton;
        CancelButton = closeButton;

        Controls.Add(tabControl);
        Controls.Add(summaryTextBox);
        Controls.Add(closeButton);
    }

    private static TabPage CreateListPage(string title, IReadOnlyList<string> items, string emptyText)
    {
        var listBox = new ListBox
        {
            Dock = DockStyle.Fill,
            HorizontalScrollbar = true,
            Font = new Font("Consolas", 9f, FontStyle.Regular),
        };

        if (items.Count == 0)
        {
            listBox.Items.Add(emptyText);
        }
        else
        {
            foreach (var item in items)
            {
                listBox.Items.Add(item);
            }
        }

        var page = new TabPage(title);
        page.Controls.Add(listBox);
        return page;
    }

    private static string BuildSummary(SnapshotRunResult result, SnapshotTriggerSource source)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Trigger: {source}");
        builder.AppendLine(string.IsNullOrWhiteSpace(result.SnapshotArchivePath)
            ? "Snapshot archive: not created"
            : $"Snapshot archive: {result.SnapshotArchivePath}");
        builder.AppendLine(string.IsNullOrWhiteSpace(result.SnapshotDirectory)
            ? "Snapshot directory: not kept"
            : $"Snapshot directory: {result.SnapshotDirectory}");
        builder.AppendLine(string.IsNullOrWhiteSpace(result.PreviousSnapshotPath)
            ? "Previous snapshot: none"
            : $"Previous snapshot: {result.PreviousSnapshotPath}");
        builder.AppendLine($"Dirty detected: {result.DirtyDocumentsDetected.Count}");
        builder.AppendLine($"Saved: {result.SavedDocuments.Count}");
        builder.AppendLine($"Hash differences: {result.FilesDifferentFromPreviousSnapshot.Count}");

        if (result.FailedDocuments.Count > 0)
        {
            builder.AppendLine($"Failed saves: {string.Join(", ", result.FailedDocuments)}");
        }

        if (result.SkippedUnsavedDocuments.Count > 0)
        {
            builder.AppendLine($"Skipped unsaved: {string.Join(", ", result.SkippedUnsavedDocuments)}");
        }

        if (result.CopyFailures.Count > 0)
        {
            builder.AppendLine($"Copy failures: {string.Join(" | ", result.CopyFailures)}");
        }

        return builder.ToString();
    }
}
