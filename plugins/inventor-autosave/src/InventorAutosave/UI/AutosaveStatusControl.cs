using System;
using System.Drawing;
using System.Windows.Forms;
using InventorAutosave.Core;

namespace InventorAutosave.UI;

internal sealed class AutosaveStatusControl : UserControl
{
    private readonly Label _countdownValueLabel;
    private readonly Label _statusValueLabel;
    private readonly NumericUpDown _intervalNumericUpDown;
    private readonly CheckBox _notificationsCheckBox;
    private readonly NumericUpDown _deferredSaveMinutesNumericUpDown;
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

        _deferredSaveMinutesNumericUpDown = new NumericUpDown
        {
            Width = 90,
            Minimum = 1,
            Maximum = 1440,
        };
        _deferredSaveMinutesNumericUpDown.ValueChanged += (_, _) => RaiseSettingsChanged();

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

        var autosaveNowButton = new Button
        {
            AutoSize = true,
            Text = "Save Now",
        };
        autosaveNowButton.Click += (_, _) => AutosaveNowRequested?.Invoke(this, EventArgs.Empty);
        actionPanel.Controls.Add(autosaveNowButton);

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
        settingsSections.Controls.Add(CreateScheduleGroup(), 0, 0);
        settingsSections.Controls.Add(CreateModalCommandGroup(), 0, 1);
        settingsSections.Controls.Add(CreateWarningsGroup(), 0, 2);
        settingsScrollPanel.Controls.Add(settingsSections);

        root.Controls.Add(titleLabel, 0, 0);
        root.Controls.Add(_countdownValueLabel, 0, 1);
        root.Controls.Add(CreatePairPanel("Status", _statusValueLabel), 0, 2);
        root.Controls.Add(actionPanel, 0, 3);
        root.Controls.Add(settingsScrollPanel, 0, 4);

        Controls.Add(root);
    }

    public event EventHandler? SettingsChanged;

    public event EventHandler? StartRequested;

    public event EventHandler? StopRequested;

    public event EventHandler? AutosaveNowRequested;

    public void LoadSettings(AutosaveSettings settings)
    {
        _isLoadingSettings = true;
        _intervalNumericUpDown.Value = settings.AutosaveIntervalMinutes >= 1
            ? settings.AutosaveIntervalMinutes
            : AutosaveDefaults.DefaultIntervalMinutes;
        _notificationsCheckBox.Checked = settings.NotificationsEnabled;
        _deferredSaveMinutesNumericUpDown.Value = settings.DeferredSaveMinutes >= 1
            ? settings.DeferredSaveMinutes
            : AutosaveDefaults.DefaultDeferredSaveMinutes;
        _warnUnsavedFilesCheckBox.Checked = settings.WarnAboutUnsavedFiles;
        _isLoadingSettings = false;
    }

    public AutosaveSettings BuildSettings()
    {
        return new AutosaveSettings
        {
            NotificationsEnabled = _notificationsCheckBox.Checked,
            AutosaveIntervalMinutes = Decimal.ToInt32(_intervalNumericUpDown.Value),
            DeferredSaveMinutes = Decimal.ToInt32(_deferredSaveMinutesNumericUpDown.Value),
            WarnAboutUnsavedFiles = _warnUnsavedFilesCheckBox.Checked,
        };
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

    private GroupBox CreateModalCommandGroup()
    {
        var layout = CreateSectionLayout(3);
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        layout.Controls.Add(
            CreateHelpLabel("When autosave cannot save quietly because an edit command is active, choose whether to save now, delay this document, or ignore it for the current run."),
            0,
            0);
        layout.SetColumnSpan(layout.Controls[layout.Controls.Count - 1], 2);
        layout.Controls.Add(CreateSectionLabel("Delay option (minutes)"), 0, 1);
        layout.Controls.Add(_deferredSaveMinutesNumericUpDown, 1, 1);
        layout.Controls.Add(
            CreateHelpLabel("Save Now may exit the active edit or stop the active command before saving. Ignore skips only that document and does not show a warning."),
            0,
            2);
        layout.SetColumnSpan(layout.Controls[layout.Controls.Count - 1], 2);

        return CreateSectionGroup("Active Commands and Edits", layout);
    }

    private GroupBox CreateWarningsGroup()
    {
        var layout = CreateSectionLayout(1);
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        layout.Controls.Add(_warnUnsavedFilesCheckBox, 0, 0);

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
