using System;
using System.IO;
using System.Runtime.Serialization.Json;
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

            using var stream = File.OpenRead(_settingsPath);
            var settings = (AutosaveSettings?)Serializer.ReadObject(stream)
                ?? new AutosaveSettings();
            _logger.LogInformation(
                "Loaded settings from {SettingsPath}. TargetDirectory {TargetDirectory}. IntervalMinutes {SnapshotIntervalMinutes}. NotificationsEnabled {NotificationsEnabled}. KeepSnapshotDirectories {KeepSnapshotDirectories}.",
                _settingsPath,
                settings.TargetDirectory,
                settings.SnapshotIntervalMinutes,
                settings.NotificationsEnabled,
                settings.KeepSnapshotDirectories);
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
                "Saved settings to {SettingsPath}. TargetDirectory {TargetDirectory}. IntervalMinutes {SnapshotIntervalMinutes}. NotificationsEnabled {NotificationsEnabled}. KeepSnapshotDirectories {KeepSnapshotDirectories}.",
                _settingsPath,
                settings.TargetDirectory,
                settings.SnapshotIntervalMinutes,
                settings.NotificationsEnabled,
                settings.KeepSnapshotDirectories);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings to {SettingsPath}.", _settingsPath);
            throw;
        }
    }
}
