using System;
using System.IO;
using InventorAutosave.Core;
using InventorAutosave.Core.Logic;
using InventorAutosave.Services;
using InventorAutosave.UI;
using Inventor;
using DrawingColor = System.Drawing.Color;
using FormsFolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;
using FormsMessageBox = System.Windows.Forms.MessageBox;
using FormsMessageBoxButtons = System.Windows.Forms.MessageBoxButtons;
using FormsMessageBoxIcon = System.Windows.Forms.MessageBoxIcon;
using FormsNotifyIcon = System.Windows.Forms.NotifyIcon;
using FormsSystemIcons = System.Drawing.SystemIcons;
using FormsTimer = System.Windows.Forms.Timer;

namespace InventorAutosave;

internal sealed class AutosaveAddInController : IDisposable
{
    private const int TimerHeartbeatMilliseconds = 1000;
    private const string ClientId = "{9DEF1247-47CE-4011-8065-FA0BC8E17110}";
    private const string RibbonTabDisplayName = "Autosave";
    private const string DockableWindowInternalName = "InventorAutosave.StatusWindow";

    private readonly Inventor.Application _application;
    private readonly AutosaveSettingsStore _settingsStore;
    private readonly InventorAutosaveService _autosaveService;
    private readonly SingleFlightGate _singleFlightGate = new();
    private readonly FormsTimer _timer;
    private readonly FormsNotifyIcon _notifyIcon;

    private AutosaveSettings _settings;
    private bool _autoSnapshotsRunning;
    private DateTime? _nextSnapshotDueAtUtc;
    private string _lastStatusMessage = "Idle";
    private UserInterfaceEvents? _userInterfaceEvents;
    private ButtonDefinition? _showTimerButton;
    private DockableWindow? _statusWindow;
    private AutosaveStatusControl? _statusControl;

    public AutosaveAddInController(Inventor.Application application)
    {
        _application = application;
        _settingsStore = new AutosaveSettingsStore();
        _settings = _settingsStore.Load();
        _autosaveService = new InventorAutosaveService(application);
        _timer = new FormsTimer { Interval = TimerHeartbeatMilliseconds };
        _timer.Tick += OnTimerTick;
        _notifyIcon = new FormsNotifyIcon
        {
            Icon = FormsSystemIcons.Application,
            Visible = true,
            Text = "Inventor Autosave",
        };
    }

    public void Initialize()
    {
        CreateCommandDefinitions();
        CreateUserInterface();

        _userInterfaceEvents = _application.UserInterfaceManager.UserInterfaceEvents;
        _userInterfaceEvents.OnResetRibbonInterface += OnResetRibbonInterface;
        UpdateRuntimeState();
    }

    public void Dispose()
    {
        _timer.Stop();

        if (_userInterfaceEvents != null)
        {
            _userInterfaceEvents.OnResetRibbonInterface -= OnResetRibbonInterface;
            _userInterfaceEvents = null;
        }

        if (_showTimerButton != null)
        {
            _showTimerButton.OnExecute -= OnShowTimer;
        }

        if (_statusControl != null)
        {
            _statusControl.BrowseRequested -= OnBrowseRequested;
            _statusControl.SettingsChanged -= OnSettingsChanged;
            _statusControl.StartRequested -= OnStartRequested;
            _statusControl.StopRequested -= OnStopRequested;
            _statusControl.SnapshotNowRequested -= OnSnapshotNowRequested;
        }

        if (_statusWindow != null)
        {
            try
            {
                _statusWindow.Clear();
            }
            catch
            {
            }
        }

        if (_statusControl != null)
        {
            _statusControl.Dispose();
            _statusControl = null;
        }

        _autosaveService.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _timer.Dispose();
    }

    private void CreateCommandDefinitions()
    {
        var controlDefinitions = _application.CommandManager.ControlDefinitions;

        _showTimerButton = CreateOrGetButtonDefinition(
            controlDefinitions,
            "Show Panel",
            "InventorAutosave.ShowTimer",
            "PNL",
            DrawingColor.FromArgb(52, 120, 177));
        _showTimerButton.OnExecute -= OnShowTimer;
        _showTimerButton.OnExecute += OnShowTimer;
    }

    private ButtonDefinition CreateOrGetButtonDefinition(
        ControlDefinitions controlDefinitions,
        string displayName,
        string internalName,
        string iconLabel,
        DrawingColor iconColor)
    {
        try
        {
            return (ButtonDefinition)controlDefinitions[internalName];
        }
        catch
        {
            return controlDefinitions.AddButtonDefinition(
                displayName,
                internalName,
                CommandTypesEnum.kNonShapeEditCmdType,
                ClientId,
                displayName,
                displayName,
                CommandIconFactory.CreateSmallIcon(iconLabel, iconColor),
                CommandIconFactory.CreateLargeIcon(iconLabel, iconColor));
        }
    }

    private void CreateUserInterface()
    {
        var ribbonNames = new[]
        {
            "ZeroDoc",
            "Part",
            "Assembly",
            "Drawing",
            "Presentation",
            "UnknownDocument",
        };

        foreach (var ribbonName in ribbonNames)
        {
            TryCreateRibbonTab(ribbonName);
        }
    }

    private void TryCreateRibbonTab(string ribbonName)
    {
        Ribbon? ribbon;
        try
        {
            ribbon = _application.UserInterfaceManager.Ribbons[ribbonName];
        }
        catch
        {
            return;
        }

        var tabInternalName = $"InventorAutosave.{ribbonName}.Tab";
        RibbonTab ribbonTab;

        try
        {
            ribbonTab = ribbon.RibbonTabs[tabInternalName];
        }
        catch
        {
            ribbonTab = ribbon.RibbonTabs.Add(RibbonTabDisplayName, tabInternalName, ClientId);
        }

        var panelInternalName = $"InventorAutosave.{ribbonName}.Panel";
        RibbonPanel ribbonPanel;
        try
        {
            ribbonPanel = ribbonTab.RibbonPanels[panelInternalName];
        }
        catch
        {
            ribbonPanel = ribbonTab.RibbonPanels.Add("Autosave", panelInternalName, ClientId);
        }

        EnsureButton(ribbonPanel, _showTimerButton);
    }

    private static void EnsureButton(RibbonPanel ribbonPanel, ButtonDefinition? buttonDefinition)
    {
        if (buttonDefinition == null)
        {
            return;
        }

        foreach (CommandControl control in ribbonPanel.CommandControls)
        {
            try
            {
                if (string.Equals(
                    control.InternalName,
                    buttonDefinition.InternalName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            catch
            {
            }
        }

        ribbonPanel.CommandControls.AddButton(buttonDefinition, true);
    }

    private void CreateStatusWindow()
    {
        if (_statusWindow != null && _statusControl != null && !_statusControl.IsDisposed)
        {
            _statusWindow.Visible = true;
            return;
        }

        var dockableWindows = _application.UserInterfaceManager.DockableWindows;
        try
        {
            _statusWindow = dockableWindows[DockableWindowInternalName];
        }
        catch
        {
            _statusWindow = dockableWindows.Add(ClientId, DockableWindowInternalName, "Inventor Autosave");
        }

        _statusControl = new AutosaveStatusControl();
        _statusControl.CreateControl();
        _statusControl.LoadSettings(_settings);
        _statusControl.BrowseRequested += OnBrowseRequested;
        _statusControl.SettingsChanged += OnSettingsChanged;
        _statusControl.StartRequested += OnStartRequested;
        _statusControl.StopRequested += OnStopRequested;
        _statusControl.SnapshotNowRequested += OnSnapshotNowRequested;

        try
        {
            _statusWindow.Clear();
        }
        catch
        {
        }

        _statusWindow.AddChild(_statusControl.Handle.ToInt64());
        _statusWindow.Visible = true;
    }

    private void OnShowTimer(NameValueMap context)
    {
        CreateStatusWindow();
        ShowStatus("Autosave panel shown.");
    }

    private void OnBrowseRequested(object? sender, EventArgs e)
    {
        if (_statusControl == null)
        {
            return;
        }

        using var dialog = new FormsFolderBrowserDialog
        {
            Description = "Choose the project folder to autosave.",
            SelectedPath = _statusControl.TargetDirectory,
            ShowNewFolderButton = false,
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            _statusControl.SetTargetDirectory(dialog.SelectedPath);
        }
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        ApplySettingsFromPanel(showStatusMessage: false);
    }

    private void OnStartRequested(object? sender, EventArgs e)
    {
        ApplySettingsFromPanel(showStatusMessage: false);
        StartSnapshots();
    }

    private void OnStopRequested(object? sender, EventArgs e)
    {
        StopSnapshots();
    }

    private void OnSnapshotNowRequested(object? sender, EventArgs e)
    {
        ApplySettingsFromPanel(showStatusMessage: false);
        ExecuteSnapshot(SnapshotTriggerSource.Manual);
    }

    private void ExecuteSnapshot(SnapshotTriggerSource source)
    {
        if (!ValidateTargetDirectory())
        {
            return;
        }

        if (!_singleFlightGate.TryEnter())
        {
            var triggerText = source == SnapshotTriggerSource.Manual ? "Manual autosave ignored" : "Scheduled autosave skipped";
            ShowStatus($"{triggerText} because another autosave is already running.");
            return;
        }

        try
        {
            _lastStatusMessage = source == SnapshotTriggerSource.Auto
                ? "Running scheduled autosave..."
                : "Running manual autosave...";
            UpdateStatusDisplay();

            var result = _autosaveService.RunSnapshot(
                _settings.TargetDirectory,
                _settings.DebugModeEnabled,
                _settings.KeepSnapshotDirectories);
            var message = BuildCompletionMessage(result);
            var hasWarnings = result.FailedDocuments.Count > 0
                || result.SkippedUnsavedDocuments.Count > 0
                || result.CopyFailures.Count > 0;

            ShowStatus(message);

            if (_settings.DebugModeEnabled)
            {
                ShowDebugDialog(result, source);
            }

            if (hasWarnings)
            {
                ShowWarningDialog(message);
            }
            else if (_settings.NotificationsEnabled)
            {
                ShowSnapshotBalloon(message);
            }
        }
        catch (Exception ex)
        {
            ShowErrorDialog($"Autosave failed: {ex.Message}");
        }
        finally
        {
            _singleFlightGate.Exit();
            UpdateRuntimeState();
        }
    }

    private bool ValidateTargetDirectory()
    {
        if (string.IsNullOrWhiteSpace(_settings.TargetDirectory))
        {
            ShowErrorDialog("Choose a target folder in the autosave panel before starting autosaves.");
            return false;
        }

        if (!Directory.Exists(_settings.TargetDirectory))
        {
            ShowErrorDialog($"Autosave target folder does not exist: {_settings.TargetDirectory}");
            return false;
        }

        return true;
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_autoSnapshotsRunning
            && _nextSnapshotDueAtUtc.HasValue
            && DateTime.UtcNow >= _nextSnapshotDueAtUtc.Value)
        {
            ExecuteSnapshot(SnapshotTriggerSource.Auto);

            if (_autoSnapshotsRunning)
            {
                _nextSnapshotDueAtUtc = SnapshotSchedule.ScheduleNextRunUtc(DateTime.UtcNow, _settings.SnapshotIntervalMinutes);
            }
        }

        UpdateRuntimeState();
    }

    private string BuildCompletionMessage(SnapshotRunResult result)
    {
        var outputPath = BuildSnapshotOutputPath(result);
        var message = $"Autosave completed to {outputPath} ({result.CopiedFileCount} file(s) copied";
        if (result.SavedDocumentCount > 0)
        {
            message += $", {result.SavedDocumentCount} document(s) saved";
        }

        message += ").";

        if (result.SkippedUnsavedDocuments.Count > 0)
        {
            message += $" Skipped unsaved: {string.Join(", ", result.SkippedUnsavedDocuments)}.";
        }

        if (result.FailedDocuments.Count > 0)
        {
            message += $" Failed saves: {string.Join(", ", result.FailedDocuments)}.";
        }

        if (result.CopyFailures.Count > 0)
        {
            message += $" Copy failures: {result.CopyFailures.Count}.";
        }

        return message;
    }

    private static string BuildSnapshotOutputPath(SnapshotRunResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.SnapshotArchivePath)
            && !string.IsNullOrWhiteSpace(result.SnapshotDirectory))
        {
            return $"{result.SnapshotArchivePath} and {result.SnapshotDirectory}";
        }

        if (!string.IsNullOrWhiteSpace(result.SnapshotArchivePath))
        {
            return result.SnapshotArchivePath;
        }

        if (!string.IsNullOrWhiteSpace(result.SnapshotDirectory))
        {
            return result.SnapshotDirectory;
        }

            return "the snapshot output";
    }

    private void UpdateRuntimeState()
    {
        UpdateStatusDisplay();
    }

    private void UpdateStatusDisplay()
    {
        if (_statusControl == null)
        {
            return;
        }

        _statusControl.UpdateState(
            SnapshotSchedule.FormatCountdown(_autoSnapshotsRunning, _nextSnapshotDueAtUtc, DateTime.UtcNow),
            _lastStatusMessage,
            _autoSnapshotsRunning);
    }

    private void ShowStatus(string message)
    {
        _lastStatusMessage = message;
        _application.StatusBarText = message;
        UpdateStatusDisplay();
    }

    private static void ShowDebugDialog(SnapshotRunResult result, SnapshotTriggerSource source)
    {
        using var form = new AutosaveDebugForm(result, source);
        form.ShowDialog();
    }

    private void ShowSnapshotBalloon(string message)
    {
        _notifyIcon.BalloonTipTitle = "Inventor Autosave";
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.ShowBalloonTip(4000);
    }

    private void ShowWarningDialog(string message)
    {
        FormsMessageBox.Show(
            message,
            "Autosave Warning",
            FormsMessageBoxButtons.OK,
            FormsMessageBoxIcon.Warning);
    }

    private void ShowErrorDialog(string message)
    {
        ShowStatus(message);
        FormsMessageBox.Show(
            message,
            "Autosave Error",
            FormsMessageBoxButtons.OK,
            FormsMessageBoxIcon.Error);
    }

    private void StartSnapshots()
    {
        if (!ValidateTargetDirectory())
        {
            return;
        }

        _autoSnapshotsRunning = true;
        _nextSnapshotDueAtUtc = SnapshotSchedule.ScheduleNextRunUtc(DateTime.UtcNow, _settings.SnapshotIntervalMinutes);
        _timer.Start();
        UpdateRuntimeState();
        ShowStatus($"Automatic autosaves started ({_settings.SnapshotIntervalMinutes} minute interval).");
    }

    private void StopSnapshots()
    {
        _autoSnapshotsRunning = false;
        _nextSnapshotDueAtUtc = null;
        _timer.Stop();
        UpdateRuntimeState();
        ShowStatus("Automatic autosaves stopped.");
    }

    private void ApplySettingsFromPanel(bool showStatusMessage)
    {
        if (_statusControl == null)
        {
            return;
        }

        var previousSettings = _settings;
        _settings = _statusControl.BuildSettings();
        _settingsStore.Save(_settings);
        if (_autoSnapshotsRunning
            && previousSettings.SnapshotIntervalMinutes != _settings.SnapshotIntervalMinutes)
        {
            _nextSnapshotDueAtUtc = SnapshotSchedule.ScheduleNextRunUtc(DateTime.UtcNow, _settings.SnapshotIntervalMinutes);
        }

        UpdateRuntimeState();

        if (!showStatusMessage)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.TargetDirectory))
        {
            ShowStatus("Autosave settings updated. Target folder is not configured.");
            return;
        }

        ShowStatus(
            $"Autosave settings updated. Interval: {_settings.SnapshotIntervalMinutes} minute(s). Notifications: {(_settings.NotificationsEnabled ? "on" : "off")}. Keep folders: {(_settings.KeepSnapshotDirectories ? "on" : "off")}. Debug mode: {(_settings.DebugModeEnabled ? "on" : "off")}.");
    }

    private void OnResetRibbonInterface(NameValueMap context)
    {
        CreateUserInterface();
    }
}
