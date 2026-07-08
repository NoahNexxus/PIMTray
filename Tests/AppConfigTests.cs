using System.Text.Json;
using Xunit;

namespace PIMTray.Tests;

public class AppConfigTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"pimtray-test-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void Load_CreatesDefaultsWithOneConnection_WhenFileMissing()
    {
        var cfg = AppConfig.Load(_path);

        Assert.True(File.Exists(_path));
        Assert.Single(cfg.Connections);
        Assert.Equal("Default", cfg.Connections[0].Name);
        Assert.False(string.IsNullOrWhiteSpace(cfg.Connections[0].Id));
        Assert.Equal(new[] { 1, 2, 4, 8 }, cfg.Pim.DurationOptionsHours);
    }

    [Fact]
    public void Load_MigratesLegacySingleAzureAdFormat_ToConnectionsList()
    {
        var legacyJson = """
        {
          "AzureAd": {
            "TenantId": "contoso.onmicrosoft.com",
            "ClientId": "11111111-1111-1111-1111-111111111111",
            "RedirectUri": "http://localhost"
          },
          "Pim": {
            "DefaultDurationHours": 2,
            "DurationOptionsHours": [1, 2, 4]
          }
        }
        """;
        File.WriteAllText(_path, legacyJson);

        var cfg = AppConfig.Load(_path);

        Assert.Single(cfg.Connections);
        Assert.Equal("Default", cfg.Connections[0].Name);
        Assert.Equal("contoso.onmicrosoft.com", cfg.Connections[0].TenantId);
        Assert.Equal("11111111-1111-1111-1111-111111111111", cfg.Connections[0].ClientId);
        Assert.Equal(2, cfg.Pim.DefaultDurationHours);

        // Migration should persist the upgraded format back to disk.
        var onDisk = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(_path));
        Assert.True(onDisk.TryGetProperty("Connections", out _));
    }

    [Fact]
    public void Load_ReadsMultiConnectionFormat_Unchanged()
    {
        var cfg = new AppConfig
        {
            Connections = new List<ConnectionConfig>
            {
                new() { Id = "a", Name = "Prod", TenantId = "tenant-a", ClientId = "client-a" },
                new() { Id = "b", Name = "Sandbox", TenantId = "tenant-b", ClientId = "client-b" }
            }
        };
        AppConfig.Save(cfg, _path);

        var loaded = AppConfig.Load(_path);

        Assert.Equal(2, loaded.Connections.Count);
        Assert.Equal("Prod", loaded.Connections[0].Name);
        Assert.Equal("Sandbox", loaded.Connections[1].Name);
    }
}
