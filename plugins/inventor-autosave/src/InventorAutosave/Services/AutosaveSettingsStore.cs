using System;
using System.IO;
using System.Runtime.Serialization.Json;
using InventorAutosave.Core;

namespace InventorAutosave.Services;

internal sealed class AutosaveSettingsStore
{
    private static readonly DataContractJsonSerializer Serializer = new(typeof(AutosaveSettings));

    private readonly string _settingsPath;

    public AutosaveSettingsStore()
    {
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
                return new AutosaveSettings();
            }

            using var stream = File.OpenRead(_settingsPath);
            return (AutosaveSettings?)Serializer.ReadObject(stream)
                ?? new AutosaveSettings();
        }
        catch
        {
            return new AutosaveSettings();
        }
    }

    public void Save(AutosaveSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = File.Create(_settingsPath);
        Serializer.WriteObject(stream, settings);
    }
}
