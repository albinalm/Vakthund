using Vakthund.UI.Enums;

namespace Vakthund.UI.Options;

public class UiOptions
{
    public int MaxStoredAuditEntries { get; set; } = 50_000;
    public StorageMode StorageMode { get; set; } = StorageMode.Memory;
    public string StoragePath { get; set; } = "";
    public string Retention { get; set; } = "";

    public TimeSpan? RetentionPeriod
    {
        get
        {
            string raw = Retention.Trim();
            if (raw.Length < 2) return null;

            char suffix = raw[^1];
            if (!int.TryParse(raw[..^1], out int value) || value <= 0) return null;

            return suffix switch
            {
                'm' => TimeSpan.FromMinutes(value),
                'h' => TimeSpan.FromHours(value),
                'd' => TimeSpan.FromDays(value),
                _ => null
            };
        }
    }
}
