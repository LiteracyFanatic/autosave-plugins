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
    public bool DebugModeEnabled { get; set; }

    [DataMember(Order = 5)]
    public bool KeepSnapshotDirectories { get; set; }
}
