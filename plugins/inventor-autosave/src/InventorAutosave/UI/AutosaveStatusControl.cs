using System;
using System.Drawing;
using System.Windows.Forms;
using InventorAutosave.Core;

namespace InventorAutosave.UI;

internal sealed class AutosaveStatusControl : UserControl
{
    private readonly Label _countdownValueLabel;
    private readonly Label _statusValueLabel;
    private readonly TextBox _targetDirectoryTextBox;
    private readonly NumericUpDown _intervalNumericUpDown;
    private readonly CheckBox _notificationsCheckBox;
    private readonly CheckBox _keepDirectoriesCheckBox;
    private readonly Button _startButton;
    private readonly Button _stopButton;
    private bool _isLoadingSettings;

    public AutosaveStatusControl()
    {
        Dock = DockStyle.Fill;
        Padding = new Padding(10);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 5,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var titleLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            Text = "Inventor Autosave",
        };

        _countdownValueLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Consolas", 20f, FontStyle.Bold),
            Text = "Stopped",
            Margin = new Padding(0, 8, 0, 8),
        };

        _statusValueLabel = CreateValueLabel();
        _statusValueLabel.MaximumSize = new Size(360, 0);

        var settingsGroup = new GroupBox
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Text = "Settings",
            Padding = new Padding(10),
        };

        var settingsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 6,
        };
        settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        settingsLayout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Target folder",
            Margin = new Padding(0, 0, 0, 4),
        }, 0, 0);
        settingsLayout.SetColumnSpan(settingsLayout.Controls[settingsLayout.Controls.Count - 1], 3);

        _targetDirectoryTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 8, 8),
        };
        _targetDirectoryTextBox.TextChanged += (_, _) => RaiseSettingsChanged();
        settingsLayout.Controls.Add(_targetDirectoryTextBox, 0, 1);
        settingsLayout.SetColumnSpan(_targetDirectoryTextBox, 2);

        var browseButton = new Button
        {
            AutoSize = true,
            Text = "Browse...",
            Margin = new Padding(0, 0, 0, 8),
        };
        browseButton.Click += (_, _) => BrowseRequested?.Invoke(this, EventArgs.Empty);
        settingsLayout.Controls.Add(browseButton, 2, 1);

        settingsLayout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Interval (minutes)",
            Margin = new Padding(0, 0, 0, 4),
        }, 0, 2);

        _intervalNumericUpDown = new NumericUpDown
        {
            Width = 90,
            Minimum = 1,
            Maximum = 1440,
            Margin = new Padding(0, 0, 8, 8),
        };
        settingsLayout.Controls.Add(_intervalNumericUpDown, 0, 3);

        _notificationsCheckBox = new CheckBox
        {
            AutoSize = true,
            Text = "Show notifications",
            Margin = new Padding(0, 2, 8, 8),
        };
        settingsLayout.Controls.Add(_notificationsCheckBox, 1, 3);

        _intervalNumericUpDown.ValueChanged += (_, _) => RaiseSettingsChanged();
        _notificationsCheckBox.CheckedChanged += (_, _) => RaiseSettingsChanged();

        _keepDirectoriesCheckBox = new CheckBox
        {
            AutoSize = true,
            Text = "Also keep regular folders",
            Margin = new Padding(0, 2, 0, 8),
        };
        _keepDirectoriesCheckBox.CheckedChanged += (_, _) => RaiseSettingsChanged();
        settingsLayout.Controls.Add(_keepDirectoriesCheckBox, 0, 4);
        settingsLayout.SetColumnSpan(_keepDirectoriesCheckBox, 3);

        var actionPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
            WrapContents = true,
        };

        _startButton = new Button
        {
            AutoSize = true,
            Text = "Start",
        };
        _startButton.Click += (_, _) => StartRequested?.Invoke(this, EventArgs.Empty);
        actionPanel.Controls.Add(_startButton);

        _stopButton = new Button
        {
            AutoSize = true,
            Text = "Stop",
        };
        _stopButton.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);
        actionPanel.Controls.Add(_stopButton);

        var snapshotNowButton = new Button
        {
            AutoSize = true,
            Text = "Autosave Now",
        };
        snapshotNowButton.Click += (_, _) => SnapshotNowRequested?.Invoke(this, EventArgs.Empty);
        actionPanel.Controls.Add(snapshotNowButton);

        settingsLayout.Controls.Add(actionPanel, 0, 5);
        settingsLayout.SetColumnSpan(actionPanel, 3);

        settingsGroup.Controls.Add(settingsLayout);

        root.Controls.Add(titleLabel, 0, 0);
        root.Controls.Add(_countdownValueLabel, 0, 1);
        root.Controls.Add(CreatePairPanel("Status", _statusValueLabel), 0, 2);
        root.Controls.Add(settingsGroup, 0, 3);

        Controls.Add(root);
    }

    public event EventHandler? BrowseRequested;

    public event EventHandler? SettingsChanged;

    public event EventHandler? StartRequested;

    public event EventHandler? StopRequested;

    public event EventHandler? SnapshotNowRequested;

    public string TargetDirectory => _targetDirectoryTextBox.Text;

    public void LoadSettings(AutosaveSettings settings)
    {
        _isLoadingSettings = true;
        _targetDirectoryTextBox.Text = settings.TargetDirectory ?? string.Empty;
        _intervalNumericUpDown.Value = settings.SnapshotIntervalMinutes >= 1
            ? settings.SnapshotIntervalMinutes
            : AutosaveDefaults.DefaultIntervalMinutes;
        _notificationsCheckBox.Checked = settings.NotificationsEnabled;
        _keepDirectoriesCheckBox.Checked = settings.KeepSnapshotDirectories;
        _isLoadingSettings = false;
    }

    public AutosaveSettings BuildSettings()
    {
        return new AutosaveSettings
        {
            TargetDirectory = _targetDirectoryTextBox.Text.Trim(),
            NotificationsEnabled = _notificationsCheckBox.Checked,
            SnapshotIntervalMinutes = Decimal.ToInt32(_intervalNumericUpDown.Value),
            KeepSnapshotDirectories = _keepDirectoriesCheckBox.Checked,
        };
    }

    public void SetTargetDirectory(string targetDirectory)
    {
        _targetDirectoryTextBox.Text = targetDirectory;
    }

    public void UpdateState(string countdown, string status, bool isRunning)
    {
        _countdownValueLabel.Text = countdown;
        _statusValueLabel.Text = status;
        _startButton.Enabled = !isRunning;
        _stopButton.Enabled = isRunning;
    }

    private void RaiseSettingsChanged()
    {
        if (_isLoadingSettings)
        {
            return;
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static Control CreatePairPanel(string label, Control valueControl)
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 8),
        };

        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            ForeColor = SystemColors.GrayText,
            Text = label.ToUpperInvariant(),
        });
        panel.Controls.Add(valueControl);
        return panel;
    }

    private static Label CreateValueLabel()
    {
        return new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 9f, FontStyle.Regular),
        };
    }
}
