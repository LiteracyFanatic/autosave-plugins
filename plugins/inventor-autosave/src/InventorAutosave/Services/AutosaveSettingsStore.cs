using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using InventorAutosave.Core;
using Microsoft.Extensions.Logging;

namespace InventorAutosave.Services;

internal sealed class AutosaveSettingsStore
{
    private static readonly DataContractJsonSerializer Serializer = new(typeof(AutosaveSettings));

    private readonly string _settingsPath;
    private readonly ILogger<AutosaveSettingsStore> _logger;

    public AutosaveSettingsStore(ILogger<AutosaveSettingsStore> logger)
    {
        _logger = logger;
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "InventorAutosave");
        _settingsPath = Path.Combine(root, "settings.json");
    }

    public AutosaveSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                _logger.LogInformation("Settings file not found. Using defaults. Path: {SettingsPath}", _settingsPath);
                return new AutosaveSettings();
            }

            var settingsJson = File.ReadAllText(_settingsPath);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(settingsJson));
            var settings = (AutosaveSettings?)Serializer.ReadObject(stream)
                ?? new AutosaveSettings();
            settings.IgnorePatterns ??= AutosaveDefaults.CreateDefaultIgnorePatterns();
            if (settings.DeferredSaveMinutes < 1)
            {
                settings.DeferredSaveMinutes = AutosaveDefaults.DefaultDeferredSaveMinutes;
            }
            if (!settingsJson.Contains("\"WarnAboutFilesOutsideTargetDirectory\"", StringComparison.Ordinal))
            {
                settings.WarnAboutFilesOutsideTargetDirectory = true;
            }

            if (!settingsJson.Contains("\"WarnAboutUnsavedFiles\"", StringComparison.Ordinal))
            {
                settings.WarnAboutUnsavedFiles = true;
            }
            _logger.LogInformation(
                "Loaded settings from {SettingsPath}. TargetDirectory {TargetDirectory}. IntervalMinutes {SnapshotIntervalMinutes}. NotificationsEnabled {NotificationsEnabled}. KeepSnapshotDirectories {KeepSnapshotDirectories}. IgnorePatternCount {IgnorePatternCount}. EditEnvironmentSaveBehavior {EditEnvironmentSaveBehavior}. DeferredSaveMinutes {DeferredSaveMinutes}. WarnAboutFilesOutsideTargetDirectory {WarnAboutFilesOutsideTargetDirectory}. WarnAboutUnsavedFiles {WarnAboutUnsavedFiles}.",
                _settingsPath,
                settings.TargetDirectory,
                settings.SnapshotIntervalMinutes,
                settings.NotificationsEnabled,
                settings.KeepSnapshotDirectories,
                settings.IgnorePatterns.Length,
                settings.EditEnvironmentSaveBehavior,
                settings.DeferredSaveMinutes,
                settings.WarnAboutFilesOutsideTargetDirectory,
                settings.WarnAboutUnsavedFiles);
            return settings;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load settings from {SettingsPath}. Using defaults.", _settingsPath);
            return new AutosaveSettings();
        }
    }

    public void Save(AutosaveSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var stream = File.Create(_settingsPath);
            Serializer.WriteObject(stream, settings);
            _logger.LogInformation(
                "Saved settings to {SettingsPath}. TargetDirectory {TargetDirectory}. IntervalMinutes {SnapshotIntervalMinutes}. NotificationsEnabled {NotificationsEnabled}. KeepSnapshotDirectories {KeepSnapshotDirectories}. IgnorePatternCount {IgnorePatternCount}. EditEnvironmentSaveBehavior {EditEnvironmentSaveBehavior}. DeferredSaveMinutes {DeferredSaveMinutes}. WarnAboutFilesOutsideTargetDirectory {WarnAboutFilesOutsideTargetDirectory}. WarnAboutUnsavedFiles {WarnAboutUnsavedFiles}.",
                _settingsPath,
                settings.TargetDirectory,
                settings.SnapshotIntervalMinutes,
                settings.NotificationsEnabled,
                settings.KeepSnapshotDirectories,
                settings.IgnorePatterns?.Length ?? 0,
                settings.EditEnvironmentSaveBehavior,
                settings.DeferredSaveMinutes,
                settings.WarnAboutFilesOutsideTargetDirectory,
                settings.WarnAboutUnsavedFiles);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings to {SettingsPath}.", _settingsPath);
            throw;
        }
    }
}
