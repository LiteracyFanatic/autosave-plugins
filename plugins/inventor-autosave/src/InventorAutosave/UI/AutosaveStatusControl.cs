using System;
using System.Drawing;
using System.Linq;
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
    private readonly ComboBox _editEnvironmentBehaviorComboBox;
    private readonly NumericUpDown _deferredSaveMinutesNumericUpDown;
    private readonly TextBox _ignorePatternsTextBox;
    private readonly CheckBox _warnOutsideTargetFilesCheckBox;
    private readonly CheckBox _warnUnsavedFilesCheckBox;
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
            AutoSize = false,
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

        _startButton = new Button
        {
            AutoSize = true,
            Text = "Start",
        };
        _startButton.Click += (_, _) => StartRequested?.Invoke(this, EventArgs.Empty);

        _stopButton = new Button
        {
            AutoSize = true,
            Text = "Stop",
        };
        _stopButton.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);

        _targetDirectoryTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 8, 0),
        };
        _targetDirectoryTextBox.TextChanged += (_, _) => RaiseSettingsChanged();

        _intervalNumericUpDown = new NumericUpDown
        {
            Width = 90,
            Minimum = 1,
            Maximum = 1440,
        };
        _intervalNumericUpDown.ValueChanged += (_, _) => RaiseSettingsChanged();

        _notificationsCheckBox = new CheckBox
        {
            AutoSize = true,
            Text = "Show notifications",
        };
        _notificationsCheckBox.CheckedChanged += (_, _) => RaiseSettingsChanged();

        _keepDirectoriesCheckBox = new CheckBox
        {
            AutoSize = true,
            Text = "Keep extracted snapshot folders in addition to ZIP archives",
        };
        _keepDirectoriesCheckBox.CheckedChanged += (_, _) => RaiseSettingsChanged();

        _editEnvironmentBehaviorComboBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _editEnvironmentBehaviorComboBox.Items.AddRange(
            new object[]
            {
                "Close the edit environment and save automatically",
                "Ask me whether to save, ignore, or delay",
            });
        _editEnvironmentBehaviorComboBox.SelectedIndex = 0;
        _editEnvironmentBehaviorComboBox.SelectedIndexChanged += (_, _) =>
        {
            UpdateDeferredSaveControlState();
            RaiseSettingsChanged();
        };

        _deferredSaveMinutesNumericUpDown = new NumericUpDown
        {
            Width = 90,
            Minimum = 1,
            Maximum = 1440,
        };
        _deferredSaveMinutesNumericUpDown.ValueChanged += (_, _) => RaiseSettingsChanged();

        _ignorePatternsTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            AcceptsReturn = true,
            Height = 72,
        };
        _ignorePatternsTextBox.TextChanged += (_, _) => RaiseSettingsChanged();

        _warnOutsideTargetFilesCheckBox = new CheckBox
        {
            AutoSize = true,
            Text = "Warn when open files outside the target folder are skipped",
        };
        _warnOutsideTargetFilesCheckBox.CheckedChanged += (_, _) => RaiseSettingsChanged();

        _warnUnsavedFilesCheckBox = new CheckBox
        {
            AutoSize = true,
            Text = "Warn when unsaved files are skipped",
        };
        _warnUnsavedFilesCheckBox.CheckedChanged += (_, _) => RaiseSettingsChanged();

        var actionPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 0, 0, 10),
            WrapContents = true,
        };
        actionPanel.Controls.Add(_startButton);
        actionPanel.Controls.Add(_stopButton);

        var snapshotNowButton = new Button
        {
            AutoSize = true,
            Text = "Save Now",
        };
        snapshotNowButton.Click += (_, _) => SnapshotNowRequested?.Invoke(this, EventArgs.Empty);
        actionPanel.Controls.Add(snapshotNowButton);

        var aboutButton = new Button
        {
            AutoSize = true,
            Text = "About",
        };
        aboutButton.Click += (_, _) => ShowAboutDialog();
        actionPanel.Controls.Add(aboutButton);

        var settingsScrollPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Margin = new Padding(0),
        };

        var settingsSections = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = new Padding(0),
        };
        settingsSections.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        settingsSections.Controls.Add(CreateTargetGroup(), 0, 0);
        settingsSections.Controls.Add(CreateScheduleGroup(), 0, 1);
        settingsSections.Controls.Add(CreateSaveBehaviorGroup(), 0, 2);
        settingsSections.Controls.Add(CreateCaptureGroup(), 0, 3);
        settingsSections.Controls.Add(CreateWarningsGroup(), 0, 4);
        settingsScrollPanel.Controls.Add(settingsSections);

        root.Controls.Add(titleLabel, 0, 0);
        root.Controls.Add(_countdownValueLabel, 0, 1);
        root.Controls.Add(CreatePairPanel("Status", _statusValueLabel), 0, 2);
        root.Controls.Add(actionPanel, 0, 3);
        root.Controls.Add(settingsScrollPanel, 0, 4);

        Controls.Add(root);
        UpdateDeferredSaveControlState();
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
        _editEnvironmentBehaviorComboBox.SelectedIndex = settings.EditEnvironmentSaveBehavior == EditEnvironmentSaveBehavior.Prompt ? 1 : 0;
        _deferredSaveMinutesNumericUpDown.Value = settings.DeferredSaveMinutes >= 1
            ? settings.DeferredSaveMinutes
            : AutosaveDefaults.DefaultDeferredSaveMinutes;
        _ignorePatternsTextBox.Text = string.Join(
            Environment.NewLine,
            settings.IgnorePatterns ?? AutosaveDefaults.CreateDefaultIgnorePatterns());
        _warnOutsideTargetFilesCheckBox.Checked = settings.WarnAboutFilesOutsideTargetDirectory;
        _warnUnsavedFilesCheckBox.Checked = settings.WarnAboutUnsavedFiles;
        UpdateDeferredSaveControlState();
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
            EditEnvironmentSaveBehavior = _editEnvironmentBehaviorComboBox.SelectedIndex == 1
                ? EditEnvironmentSaveBehavior.Prompt
                : EditEnvironmentSaveBehavior.AutoCloseAndSave,
            DeferredSaveMinutes = Decimal.ToInt32(_deferredSaveMinutesNumericUpDown.Value),
            IgnorePatterns = _ignorePatternsTextBox.Lines
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray(),
            WarnAboutFilesOutsideTargetDirectory = _warnOutsideTargetFilesCheckBox.Checked,
            WarnAboutUnsavedFiles = _warnUnsavedFilesCheckBox.Checked,
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

    private void UpdateDeferredSaveControlState()
    {
        if (_editEnvironmentBehaviorComboBox.SelectedIndex == 1)
        {
            _deferredSaveMinutesNumericUpDown.Enabled = true;
            return;
        }

        _deferredSaveMinutesNumericUpDown.Enabled = false;
    }

    private GroupBox CreateTargetGroup()
    {
        var browseButton = new Button
        {
            AutoSize = true,
            Text = "Browse...",
            Margin = new Padding(0),
        };
        browseButton.Click += (_, _) => BrowseRequested?.Invoke(this, EventArgs.Empty);

        var layout = CreateSectionLayout(3);
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        layout.Controls.Add(CreateSectionLabel("Folder to snapshot"), 0, 0);
        layout.SetColumnSpan(layout.Controls[layout.Controls.Count - 1], 3);
        layout.Controls.Add(_targetDirectoryTextBox, 0, 1);
        layout.SetColumnSpan(_targetDirectoryTextBox, 2);
        layout.Controls.Add(browseButton, 2, 1);

        return CreateSectionGroup("Target", layout);
    }

    private GroupBox CreateScheduleGroup()
    {
        var layout = CreateSectionLayout(2);
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        layout.Controls.Add(CreateSectionLabel("Autosave interval"), 0, 0);
        layout.Controls.Add(_intervalNumericUpDown, 1, 0);
        layout.Controls.Add(_notificationsCheckBox, 0, 1);
        layout.SetColumnSpan(_notificationsCheckBox, 2);

        return CreateSectionGroup("Schedule", layout);
    }

    private GroupBox CreateSaveBehaviorGroup()
    {
        var layout = CreateSectionLayout(2);
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        layout.Controls.Add(CreateSectionLabel("If saving must exit an edit environment"), 0, 0);
        layout.SetColumnSpan(layout.Controls[layout.Controls.Count - 1], 2);
        layout.Controls.Add(_editEnvironmentBehaviorComboBox, 0, 1);
        layout.SetColumnSpan(_editEnvironmentBehaviorComboBox, 2);
        layout.Controls.Add(CreateSectionLabel("Delay option (minutes)"), 0, 2);
        layout.Controls.Add(_deferredSaveMinutesNumericUpDown, 1, 2);
        layout.Controls.Add(
            CreateHelpLabel("Delay is only available while automatic autosaves are running."),
            0,
            3);
        layout.SetColumnSpan(layout.Controls[layout.Controls.Count - 1], 2);

        return CreateSectionGroup("Save Behavior", layout);
    }

    private GroupBox CreateCaptureGroup()
    {
        var layout = CreateSectionLayout(1);
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        layout.Controls.Add(_keepDirectoriesCheckBox, 0, 0);
        layout.Controls.Add(
            CreateHelpLabel("ZIP archives are always created. The snapshots folder is always excluded from source files."),
            0,
            1);
        layout.Controls.Add(CreateSectionLabel("Ignore patterns (one per line)"), 0, 2);
        layout.Controls.Add(_ignorePatternsTextBox, 0, 3);

        return CreateSectionGroup("Capture", layout);
    }

    private GroupBox CreateWarningsGroup()
    {
        var layout = CreateSectionLayout(1);
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        layout.Controls.Add(_warnOutsideTargetFilesCheckBox, 0, 0);
        layout.Controls.Add(_warnUnsavedFilesCheckBox, 0, 1);
        layout.Controls.Add(
            CreateHelpLabel("If these warnings are off, those files are silently skipped and the snapshot continues."),
            0,
            2);

        return CreateSectionGroup("Warnings", layout);
    }

    private static GroupBox CreateSectionGroup(string title, Control content)
    {
        var group = new GroupBox
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Padding = new Padding(10),
            Text = title,
            Margin = new Padding(0, 0, 0, 8),
        };
        group.Controls.Add(content);
        return group;
    }

    private static TableLayoutPanel CreateSectionLayout(int rowCount)
    {
        return new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = rowCount,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
        };
    }

    private static Label CreateSectionLabel(string text)
    {
        return new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4),
            Text = text,
        };
    }

    private static Label CreateHelpLabel(string text)
    {
        return new Label
        {
            AutoSize = true,
            MaximumSize = new Size(340, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 8),
            Text = text,
        };
    }

    private void ShowAboutDialog()
    {
        using var aboutForm = new AboutForm();
        aboutForm.ShowDialog(FindForm());
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
