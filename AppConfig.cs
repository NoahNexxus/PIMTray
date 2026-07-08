using System.Text.Json;

namespace PIMTray;

public sealed class AppConfig
{
    public List<ConnectionConfig> Connections { get; set; } = new();
    public PimConfig Pim { get; set; } = new();

    public static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PIMTray");

    public static string ConfigPath =>
        Path.Combine(ConfigDirectory, "appsettings.json");

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };

    public static AppConfig Load() => Load(ConfigPath);

    // Path is a parameter (rather than always the hardcoded user-profile location) so
    // load/save/migration logic can be exercised against a temp file in tests.
    public static AppConfig Load(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (!File.Exists(path))
        {
            var defaults = BuildDefaults();
            Save(defaults, path);
            return defaults;
        }

        var json = File.ReadAllText(path);
        var cfg = TryMigrateLegacy(json, path) ?? JsonSerializer.Deserialize<AppConfig>(json, ReadOptions);

        if (cfg is null || cfg.Connections.Count == 0)
        {
            var defaults = BuildDefaults();
            Save(defaults, path);
            return defaults;
        }

        return cfg;
    }

    // Reads the pre-multi-account format ({ "AzureAd": {...}, "Pim": {...} }) and
    // upgrades it in place to a single-item Connections list so existing users
    // don't lose their configured app registration or have to re-sign-in.
    private static AppConfig? TryMigrateLegacy(string json, string path)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("Connections", out _)) return null;
        if (!doc.RootElement.TryGetProperty("AzureAd", out var azureAdEl)) return null;

        var legacyConnection = JsonSerializer.Deserialize<ConnectionConfig>(azureAdEl.GetRawText(), ReadOptions)
            ?? new ConnectionConfig();
        legacyConnection.Id = Guid.NewGuid().ToString("N");
        legacyConnection.Name = "Default";

        var pim = doc.RootElement.TryGetProperty("Pim", out var pimEl)
            ? JsonSerializer.Deserialize<PimConfig>(pimEl.GetRawText(), ReadOptions) ?? new PimConfig()
            : new PimConfig();

        var migrated = new AppConfig
        {
            Connections = new List<ConnectionConfig> { legacyConnection },
            Pim = pim
        };
        Save(migrated, path);
        return migrated;
    }

    public static void Save(AppConfig cfg) => Save(cfg, ConfigPath);

    public static void Save(AppConfig cfg, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(cfg, WriteOptions);
        File.WriteAllText(path, json);
    }

    private static AppConfig BuildDefaults() => new()
    {
        Connections = new List<ConnectionConfig>
        {
            new ConnectionConfig
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "Default",
                TenantId = "common",
                ClientId = "14d82eec-204b-4c2f-b7e8-296a70dab67e",
                RedirectUri = "http://localhost"
            }
        },
        Pim = new PimConfig
        {
            DefaultDurationHours = 1,
            DurationOptionsHours = new[] { 1, 2, 4, 8 }
        }
    };
}

public sealed class ConnectionConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string TenantId { get; set; } = "common";
    public string ClientId { get; set; } = "";
    public string RedirectUri { get; set; } = "http://localhost";
}

public sealed class PimConfig
{
    public int DefaultDurationHours { get; set; } = 1;
    public int[] DurationOptionsHours { get; set; } = new[] { 1, 2, 4, 8 };
}
