using System.Runtime.Serialization;

namespace InventorAutosave.Core;

[DataContract]
internal sealed class AutosaveSettings
{
    [DataMember(Order = 1)]
    public string TargetDirectory { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public bool NotificationsEnabled { get; set; } = true;

    [DataMember(Order = 3)]
    public int SnapshotIntervalMinutes { get; set; } = AutosaveDefaults.DefaultIntervalMinutes;

    [DataMember(Order = 4)]
    public bool KeepSnapshotDirectories { get; set; }

    [DataMember(Order = 5)]
    public string[] IgnorePatterns { get; set; } = AutosaveDefaults.CreateDefaultIgnorePatterns();

    [DataMember(Order = 6)]
    public EditEnvironmentSaveBehavior EditEnvironmentSaveBehavior { get; set; }

    [DataMember(Order = 7)]
    public int DeferredSaveMinutes { get; set; } = AutosaveDefaults.DefaultDeferredSaveMinutes;

    [DataMember(Order = 8)]
    public bool WarnAboutFilesOutsideTargetDirectory { get; set; } = true;

    [DataMember(Order = 9)]
    public bool WarnAboutUnsavedFiles { get; set; } = true;
}
