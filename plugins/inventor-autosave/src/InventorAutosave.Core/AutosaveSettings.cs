using System.Runtime.Serialization;

namespace InventorAutosave.Core;

[DataContract]
internal sealed class AutosaveSettings
{
    [DataMember(Order = 1)]
    public bool NotificationsEnabled { get; set; } = true;

    [DataMember(Order = 2)]
    public int AutosaveIntervalMinutes { get; set; } = AutosaveDefaults.DefaultIntervalMinutes;

    [DataMember(Order = 3)]
    public int DeferredSaveMinutes { get; set; } = AutosaveDefaults.DefaultDeferredSaveMinutes;

    [DataMember(Order = 4)]
    public bool WarnAboutUnsavedFiles { get; set; } = true;
}
