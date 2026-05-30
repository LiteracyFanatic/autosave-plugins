using System;
using InventorAutosave.Core;
using InventorAutosave.Core.Logic;
using InventorAutosave.Services;
using InventorAutosave.UI;
using Inventor;
using Microsoft.Extensions.Logging;
using DrawingColor = System.Drawing.Color;
using FormsDialogResult = System.Windows.Forms.DialogResult;
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
    private readonly ILogger<AutosaveAddInController> _logger;
    private readonly AutosaveSettingsStore _settingsStore;
    private readonly InventorAutosaveService _autosaveService;
    private readonly SingleFlightGate _singleFlightGate = new();
    private readonly FormsTimer _timer;
    private readonly FormsNotifyIcon _notifyIcon;

    private AutosaveSettings _settings;
    private bool _automaticAutosavesRunning;
    private DateTime? _nextAutosaveDueAtUtc;
    private string _lastStatusMessage = "Idle";
    private UserInterfaceEvents? _userInterfaceEvents;
    private ButtonDefinition? _showTimerButton;
    private DockableWindow? _statusWindow;
    private AutosaveStatusControl? _statusControl;

    public AutosaveAddInController(Inventor.Application application, ILoggerFactory loggerFactory)
    {
        _application = application;
        _logger = loggerFactory.CreateLogger<AutosaveAddInController>();
        _settingsStore = new AutosaveSettingsStore(loggerFactory.CreateLogger<AutosaveSettingsStore>());
        _settings = _settingsStore.Load();
        _autosaveService = new InventorAutosaveService(
            application,
            loggerFactory.CreateLogger<InventorAutosaveService>(),
            PromptForModalSave);
        _timer = new FormsTimer { Interval = TimerHeartbeatMilliseconds };
        _timer.Tick += OnTimerTick;
        _notifyIcon = new FormsNotifyIcon
        {
            Icon = FormsSystemIcons.Application,
            Visible = true,
            Text = "Inventor Autosave",
        };

        _logger.LogInformation(
            "Controller created. IntervalMinutes {AutosaveIntervalMinutes}. NotificationsEnabled {NotificationsEnabled}. DeferredSaveMinutes {DeferredSaveMinutes}. WarnAboutUnsavedFiles {WarnAboutUnsavedFiles}.",
            _settings.AutosaveIntervalMinutes,
            _settings.NotificationsEnabled,
            _settings.DeferredSaveMinutes,
            _settings.WarnAboutUnsavedFiles);
    }

    public void Initialize()
    {
        _logger.LogInformation("Initializing autosave controller.");
        CreateCommandDefinitions();
        CreateUserInterface();

        _userInterfaceEvents = _application.UserInterfaceManager.UserInterfaceEvents;
        _userInterfaceEvents.OnResetRibbonInterface += OnResetRibbonInterface;
        UpdateRuntimeState();
        _logger.LogInformation("Autosave controller initialized.");
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing autosave controller.");
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
            _statusControl.SettingsChanged -= OnSettingsChanged;
            _statusControl.StartRequested -= OnStartRequested;
            _statusControl.StopRequested -= OnStopRequested;
            _statusControl.AutosaveNowRequested -= OnAutosaveNowRequested;
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
        _logger.LogInformation("Autosave controller disposed.");
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
        foreach (Ribbon ribbon in _application.UserInterfaceManager.Ribbons)
        {
            TryCreateRibbonTab(ribbon);
        }
    }

    private void TryCreateRibbonTab(Ribbon ribbon)
    {
        var ribbonKey = SanitizeInternalNameSegment(GetRibbonInternalName(ribbon));
        if (string.IsNullOrWhiteSpace(ribbonKey))
        {
            return;
        }

        var tabInternalName = $"InventorAutosave.{ribbonKey}.Tab";
        RibbonTab ribbonTab;

        try
        {
            ribbonTab = ribbon.RibbonTabs[tabInternalName];
        }
        catch
        {
            try
            {
                ribbonTab = ribbon.RibbonTabs.Add(RibbonTabDisplayName, tabInternalName, ClientId);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not create autosave ribbon tab for ribbon {RibbonKey}.", ribbonKey);
                return;
            }
        }

        var panelInternalName = $"InventorAutosave.{ribbonKey}.Panel";
        RibbonPanel ribbonPanel;
        try
        {
            ribbonPanel = ribbonTab.RibbonPanels[panelInternalName];
        }
        catch
        {
            try
            {
                ribbonPanel = ribbonTab.RibbonPanels.Add("Autosave", panelInternalName, ClientId);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not create autosave ribbon panel for ribbon {RibbonKey}.", ribbonKey);
                return;
            }
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
        _statusControl.SettingsChanged += OnSettingsChanged;
        _statusControl.StartRequested += OnStartRequested;
        _statusControl.StopRequested += OnStopRequested;
        _statusControl.AutosaveNowRequested += OnAutosaveNowRequested;

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
        _logger.LogInformation("Autosave panel shown.");
        ShowStatus("Autosave panel shown.");
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        ApplySettingsFromPanel(showStatusMessage: false);
    }

    private void OnStartRequested(object? sender, EventArgs e)
    {
        ApplySettingsFromPanel(showStatusMessage: false);
        StartAutosaves();
    }

    private void OnStopRequested(object? sender, EventArgs e)
    {
        StopAutosaves();
    }

    private void OnAutosaveNowRequested(object? sender, EventArgs e)
    {
        ApplySettingsFromPanel(showStatusMessage: false);
        ExecuteAutosave(AutosaveTriggerSource.Manual);
    }

    private void ExecuteAutosave(AutosaveTriggerSource source)
    {
        _logger.LogInformation(
            "Autosave requested. Source {Source}. NotificationsEnabled {NotificationsEnabled}. DeferredSaveMinutes {DeferredSaveMinutes}.",
            source,
            _settings.NotificationsEnabled,
            _settings.DeferredSaveMinutes);

        if (!_singleFlightGate.TryEnter())
        {
            var triggerText = source == AutosaveTriggerSource.Manual ? "Manual autosave ignored" : "Scheduled autosave skipped";
            _logger.LogWarning("Autosave request skipped because another autosave is already running. Source {Source}.", source);
            ShowStatus($"{triggerText} because another autosave is already running.");
            return;
        }

        try
        {
            _lastStatusMessage = source == AutosaveTriggerSource.Auto
                ? "Running scheduled autosave..."
                : "Running manual autosave...";
            UpdateStatusDisplay();

            var result = _autosaveService.RunAutosave(_settings.DeferredSaveMinutes);
            LogAutosaveResult(source, result);
            var message = BuildCompletionMessage(result);
            var hasWarnings = AutosaveCompletionPolicy.ShouldShowWarning(result, _settings.WarnAboutUnsavedFiles);

            ShowStatus(message);

            if (hasWarnings)
            {
                ShowWarningDialog(message);
            }
            else if (_settings.NotificationsEnabled)
            {
                ShowAutosaveBalloon(message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Autosave failed. Source {Source}.", source);
            ShowErrorDialog($"Autosave failed: {ex.Message}");
        }
        finally
        {
            if (source == AutosaveTriggerSource.Manual && _automaticAutosavesRunning)
            {
                _nextAutosaveDueAtUtc = AutosaveSchedule.ScheduleNextRunUtc(DateTime.UtcNow, _settings.AutosaveIntervalMinutes);
                _logger.LogInformation(
                    "Manual save restarted autosave timer. NextAutosaveDueAtUtc {NextAutosaveDueAtUtc}.",
                    _nextAutosaveDueAtUtc.Value);
            }

            _singleFlightGate.Exit();
            UpdateRuntimeState();
        }
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_automaticAutosavesRunning
            && _nextAutosaveDueAtUtc.HasValue
            && DateTime.UtcNow >= _nextAutosaveDueAtUtc.Value)
        {
            _logger.LogInformation(
                "Scheduled autosave is due. DueAtUtc {DueAtUtc}. CurrentTimeUtc {CurrentTimeUtc}.",
                _nextAutosaveDueAtUtc.Value,
                DateTime.UtcNow);
            ExecuteAutosave(AutosaveTriggerSource.Auto);

            if (_automaticAutosavesRunning)
            {
                _nextAutosaveDueAtUtc = AutosaveSchedule.ScheduleNextRunUtc(DateTime.UtcNow, _settings.AutosaveIntervalMinutes);
                _logger.LogDebug("Next scheduled autosave set to {NextAutosaveDueAtUtc}.", _nextAutosaveDueAtUtc.Value);
            }
        }

        UpdateRuntimeState();
    }

    private string BuildCompletionMessage(AutosaveRunResult result)
    {
        var message = $"Autosave completed ({result.SavedDocumentCount} document(s) saved).";

        if (result.FailedDocuments.Count > 0)
        {
            message += $" Failed saves: {string.Join(", ", result.FailedDocuments)}.";
        }

        if (_settings.WarnAboutUnsavedFiles
            && result.SkippedUnsavedDocuments.Count > 0)
        {
            message += $" Unsaved files were skipped: {string.Join(", ", result.SkippedUnsavedDocuments)}.";
        }

        if (result.IgnoredDocuments.Count > 0)
        {
            message += $" Ignored saves: {string.Join(", ", result.IgnoredDocuments)}.";
        }

        if (result.DelayedDocuments.Count > 0)
        {
            message += $" Delayed saves: {string.Join(", ", result.DelayedDocuments)}.";
        }

        return message;
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
            AutosaveSchedule.FormatCountdown(_automaticAutosavesRunning, _nextAutosaveDueAtUtc, DateTime.UtcNow),
            _lastStatusMessage,
            _automaticAutosavesRunning);
    }

    private void ShowStatus(string message)
    {
        _lastStatusMessage = message;
        _logger.LogInformation("Status updated: {StatusMessage}", message);
        _application.StatusBarText = message;
        UpdateStatusDisplay();
    }

    private void ShowAutosaveBalloon(string message)
    {
        _logger.LogDebug("Showing notification balloon. Message: {Message}", message);
        _notifyIcon.BalloonTipTitle = "Inventor Autosave";
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.ShowBalloonTip(4000);
    }

    private void ShowWarningDialog(string message)
    {
        _logger.LogWarning("Showing warning dialog. Message: {Message}", message);
        FormsMessageBox.Show(
            message,
            "Autosave Warning",
            FormsMessageBoxButtons.OK,
            FormsMessageBoxIcon.Warning);
    }

    private void ShowErrorDialog(string message)
    {
        ShowStatus(message);
        _logger.LogError("Showing error dialog. Message: {Message}", message);
        FormsMessageBox.Show(
            message,
            "Autosave Error",
            FormsMessageBoxButtons.OK,
            FormsMessageBoxIcon.Error);
    }

    private void StartAutosaves()
    {
        _automaticAutosavesRunning = true;
        _nextAutosaveDueAtUtc = AutosaveSchedule.ScheduleNextRunUtc(DateTime.UtcNow, _settings.AutosaveIntervalMinutes);
        _timer.Start();
        UpdateRuntimeState();
        _logger.LogInformation(
            "Automatic autosaves started. IntervalMinutes {AutosaveIntervalMinutes}. NextAutosaveDueAtUtc {NextAutosaveDueAtUtc}.",
            _settings.AutosaveIntervalMinutes,
            _nextAutosaveDueAtUtc.Value);
        ShowStatus($"Automatic autosaves started ({_settings.AutosaveIntervalMinutes} minute interval).");
    }

    private void StopAutosaves()
    {
        _automaticAutosavesRunning = false;
        _nextAutosaveDueAtUtc = null;
        _timer.Stop();
        UpdateRuntimeState();
        _logger.LogInformation("Automatic autosaves stopped.");
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
        _logger.LogInformation(
            "Settings updated from panel. IntervalMinutes {AutosaveIntervalMinutes}. NotificationsEnabled {NotificationsEnabled}. DeferredSaveMinutes {DeferredSaveMinutes}. WarnAboutUnsavedFiles {WarnAboutUnsavedFiles}.",
            _settings.AutosaveIntervalMinutes,
            _settings.NotificationsEnabled,
            _settings.DeferredSaveMinutes,
            _settings.WarnAboutUnsavedFiles);
        if (_automaticAutosavesRunning
            && previousSettings.AutosaveIntervalMinutes != _settings.AutosaveIntervalMinutes)
        {
            _nextAutosaveDueAtUtc = AutosaveSchedule.ScheduleNextRunUtc(DateTime.UtcNow, _settings.AutosaveIntervalMinutes);
            _logger.LogInformation(
                "Autosave interval changed while running. NextAutosaveDueAtUtc {NextAutosaveDueAtUtc}.",
                _nextAutosaveDueAtUtc.Value);
        }

        UpdateRuntimeState();

        if (!showStatusMessage)
        {
            return;
        }

        ShowStatus(
            $"Autosave settings updated. Interval: {_settings.AutosaveIntervalMinutes} minute(s). Notifications: {(_settings.NotificationsEnabled ? "on" : "off")}. Delay: {_settings.DeferredSaveMinutes} minute(s). Unsaved warnings: {(_settings.WarnAboutUnsavedFiles ? "on" : "off")}.");
    }

    private void OnResetRibbonInterface(NameValueMap context)
    {
        CreateUserInterface();
        _logger.LogInformation("Ribbon interface reset detected. Autosave UI recreated.");
    }

    private void LogAutosaveResult(AutosaveTriggerSource source, AutosaveRunResult result)
    {
        _logger.LogInformation(
            "Autosave completed. Source {Source}. DirtyDetectedCount {DirtyDetectedCount}. SavedDocumentCount {SavedDocumentCount}. FailedDocumentCount {FailedDocumentCount}. SkippedUnsavedCount {SkippedUnsavedCount}. IgnoredDocumentCount {IgnoredDocumentCount}. DelayedDocumentCount {DelayedDocumentCount}.",
            source,
            result.DirtyDocumentsDetected.Count,
            result.SavedDocuments.Count,
            result.FailedDocuments.Count,
            result.SkippedUnsavedDocuments.Count,
            result.IgnoredDocuments.Count,
            result.DelayedDocuments.Count);

        if (result.DirtyDocumentsDetected.Count > 0)
        {
            _logger.LogDebug("Dirty documents detected: {DirtyDocuments}", string.Join(" | ", result.DirtyDocumentsDetected));
        }

        if (result.SavedDocuments.Count > 0)
        {
            _logger.LogDebug("Saved documents: {SavedDocuments}", string.Join(" | ", result.SavedDocuments));
        }

        if (result.SkippedUnsavedDocuments.Count > 0)
        {
            _logger.LogWarning("Skipped unsaved documents: {SkippedUnsavedDocuments}", string.Join(" | ", result.SkippedUnsavedDocuments));
        }

        if (result.FailedDocuments.Count > 0)
        {
            _logger.LogWarning("Documents that could not be saved: {FailedDocuments}", string.Join(" | ", result.FailedDocuments));
        }

        if (result.IgnoredDocuments.Count > 0)
        {
            _logger.LogInformation("Documents skipped by user choice: {IgnoredDocuments}", string.Join(" | ", result.IgnoredDocuments));
        }

        if (result.DelayedDocuments.Count > 0)
        {
            _logger.LogInformation("Documents delayed by user choice: {DelayedDocuments}", string.Join(" | ", result.DelayedDocuments));
        }
    }

    private AutosavePromptChoice PromptForModalSave(string documentLabel, int delayMinutes)
    {
        using var prompt = new EditEnvironmentSavePromptForm(documentLabel, delayMinutes);
        return prompt.ShowDialog() switch
        {
            FormsDialogResult.Yes => AutosavePromptChoice.SaveNow,
            FormsDialogResult.Retry => AutosavePromptChoice.Delay,
            _ => AutosavePromptChoice.Ignore,
        };
    }

    private static string GetRibbonInternalName(Ribbon ribbon)
    {
        try
        {
            return ribbon.InternalName;
        }
        catch
        {
        }

        return string.Empty;
    }

    private static string SanitizeInternalNameSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value.ToCharArray();
        for (var index = 0; index < chars.Length; index++)
        {
            if (!char.IsLetterOrDigit(chars[index]) && chars[index] != '_' && chars[index] != '.')
            {
                chars[index] = '_';
            }
        }

        return new string(chars);
    }
}
