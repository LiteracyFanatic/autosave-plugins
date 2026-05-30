using System;
using System.Drawing;
using System.Windows.Forms;

namespace InventorAutosave.UI;

internal sealed class EditEnvironmentSavePromptForm : Form
{
    public EditEnvironmentSavePromptForm(string documentLabel, int delayMinutes)
    {
        Text = "Autosave Action Required";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        var root = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
        };

        root.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(360, 0),
            Text = $"\"{documentLabel}\" is in an active edit environment or command that must be resolved before it can be autosaved.",
            Margin = new Padding(0, 0, 0, 8),
        }, 0, 0);

        root.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(360, 0),
            Text = "Autosave could not save quietly. Choose whether to save now, delay this document, or ignore it for the current autosave without showing a warning.",
            Margin = new Padding(0, 0, 0, 12),
        }, 0, 1);

        var buttonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0),
        };

        var saveNowButton = new Button
        {
            AutoSize = true,
            Text = "Save Now",
            DialogResult = DialogResult.Yes,
        };
        AcceptButton = saveNowButton;
        buttonPanel.Controls.Add(saveNowButton);

        var ignoreButton = new Button
        {
            AutoSize = true,
            Text = "Ignore",
            DialogResult = DialogResult.No,
        };
        buttonPanel.Controls.Add(ignoreButton);

        var delayButton = new Button
        {
            AutoSize = true,
            Text = $"Delay {delayMinutes} min",
            DialogResult = DialogResult.Retry,
        };
        buttonPanel.Controls.Add(delayButton);

        CancelButton = ignoreButton;
        root.Controls.Add(buttonPanel, 0, 2);

        Controls.Add(root);
    }
}
