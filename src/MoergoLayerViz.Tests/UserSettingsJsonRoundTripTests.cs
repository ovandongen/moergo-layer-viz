using System.Text.Json;
using MoergoLayerViz.Core.Settings;
using Xunit;

namespace MoergoLayerViz.Tests;

/// <summary>
/// Pins the forward/backward-compat properties of <see cref="UserSettings"/>'
/// JSON shape that the HID-only refactor relied on but never had explicit
/// coverage for: silently dropping keys removed in the refactor, tolerating
/// missing per-profile dictionary entries, and round-tripping the
/// post-refactor schema without losing fields.
/// </summary>
public class UserSettingsJsonRoundTripTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void Load_IgnoresLegacyLayerSourceKey()
    {
        // Phase 1/2 of the HID-only refactor dropped UserSettings.LayerSource.
        // Old settings files in the wild still carry it; we must not blow up.
        var json = """{"SchemaVersion": 1, "LayerSource": "SharpHook", "Keyboard": "GO60"}""";

        var parsed = JsonSerializer.Deserialize<UserSettings>(json, Options);

        Assert.NotNull(parsed);
        Assert.Equal("GO60", parsed!.Keyboard);
    }

    [Fact]
    public void Load_IgnoresLegacyManualLayerSignalsKey()
    {
        // Same as above — the macro-signal subsystem was removed.
        var json = """
            {
                "SchemaVersion": 1,
                "ManualLayerSignals": {"GO60": [{"Signal": "F18", "LayerIndex": 2}]},
                "Keyboard": "Glove80"
            }
            """;

        var parsed = JsonSerializer.Deserialize<UserSettings>(json, Options);

        Assert.NotNull(parsed);
        Assert.Equal("Glove80", parsed!.Keyboard);
    }

    [Fact]
    public void Load_MissingMouseLayerDictionary_ResolvesToEmpty()
    {
        // Settings files written before Phase 4 of the refactor have no
        // MouseLayer key. The default initializer should kick in.
        var json = """{"SchemaVersion": 1, "Keyboard": "GO60"}""";

        var parsed = JsonSerializer.Deserialize<UserSettings>(json, Options);

        Assert.NotNull(parsed);
        Assert.NotNull(parsed!.MouseLayer);
        Assert.Empty(parsed.MouseLayer);
    }

    [Fact]
    public void Load_MissingPerProfileMouseLayerEntry_DoesNotPopulateDefault()
    {
        // Engine treats "no entry for this profile" as a disabled default —
        // the JSON should preserve that absence (not auto-populate).
        var json = """
            {
                "SchemaVersion": 1,
                "MouseLayer": {"Glove80": {"Enabled": true, "MouseLayerIndex": 4, "IdleTimeoutMs": 600}}
            }
            """;

        var parsed = JsonSerializer.Deserialize<UserSettings>(json, Options);

        Assert.NotNull(parsed);
        Assert.True(parsed!.MouseLayer.ContainsKey("Glove80"));
        Assert.False(parsed.MouseLayer.ContainsKey("GO60"));
    }

    [Fact]
    public void Load_TolratesUnknownAdHocKey()
    {
        // System.Text.Json's default behaviour drops unknown keys; pinning it
        // so a future "preserve unknown" change doesn't quietly break old
        // settings round-trips.
        var json = """{"SchemaVersion": 1, "SomeFutureKey": "ignored", "Keyboard": "GO60"}""";

        var parsed = JsonSerializer.Deserialize<UserSettings>(json, Options);

        Assert.NotNull(parsed);
        Assert.Equal("GO60", parsed!.Keyboard);
    }

    [Fact]
    public void RoundTrip_PreservesMouseLayerSettings()
    {
        var original = new UserSettings
        {
            Keyboard = "Glove80",
            MouseLayer = new Dictionary<string, MouseLayerSettings>
            {
                ["GO60"] = new(Enabled: true, MouseLayerIndex: 3, IdleTimeoutMs: 750),
                ["Glove80"] = new(Enabled: false, MouseLayerIndex: null, IdleTimeoutMs: 500),
            },
        };

        var json = JsonSerializer.Serialize(original, Options);
        var rehydrated = JsonSerializer.Deserialize<UserSettings>(json, Options);

        Assert.NotNull(rehydrated);
        Assert.Equal(original.MouseLayer["GO60"], rehydrated!.MouseLayer["GO60"]);
        Assert.Equal(original.MouseLayer["Glove80"], rehydrated.MouseLayer["Glove80"]);
    }

    [Fact]
    public void RoundTrip_PreservesAutoSwitchAndHotkeySettings()
    {
        var original = new UserSettings
        {
            HotkeyKey = "F18",
            HotkeyModifiers = "Ctrl+Alt",
            AutoSwitchKeyboardLayer = true,
            ColorTrayIconByActiveLayer = false,
            ModifierStyle = "Windows",
            AutoSwitchExitKey = new Dictionary<string, int> { ["GO60"] = 42 },
            AutoSwitchFallback = new Dictionary<string, AutoSwitchFallbackMode>
            {
                ["GO60"] = AutoSwitchFallbackMode.Previous,
            },
        };

        var json = JsonSerializer.Serialize(original, Options);
        var rehydrated = JsonSerializer.Deserialize<UserSettings>(json, Options);

        Assert.NotNull(rehydrated);
        Assert.Equal("F18", rehydrated!.HotkeyKey);
        Assert.Equal("Ctrl+Alt", rehydrated.HotkeyModifiers);
        Assert.True(rehydrated.AutoSwitchKeyboardLayer);
        Assert.False(rehydrated.ColorTrayIconByActiveLayer);
        Assert.Equal("Windows", rehydrated.ModifierStyle);
        Assert.Equal(42, rehydrated.AutoSwitchExitKey["GO60"]);
        Assert.Equal(AutoSwitchFallbackMode.Previous, rehydrated.AutoSwitchFallback["GO60"]);
    }

    [Fact]
    public void SettingsService_LoadFromMissingFile_ReturnsDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mlv-settings-{Guid.NewGuid()}.json");
        try
        {
            var svc = new SettingsService(path);
            var loaded = svc.Load();

            Assert.NotNull(loaded);
            Assert.Equal(UserSettings.CurrentSchemaVersion, loaded.SchemaVersion);
            Assert.Equal("GO60", loaded.Keyboard);
            Assert.Empty(loaded.MouseLayer);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SettingsService_LoadFromFutureSchemaVersion_BacksUpAndReturnsDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mlv-settings-{Guid.NewGuid()}.json");
        var backup = $"{path}.v999.bak";
        try
        {
            File.WriteAllText(path,
                """{"SchemaVersion": 999, "Keyboard": "Glove80"}""");
            var svc = new SettingsService(path);
            var loaded = svc.Load();

            Assert.Equal("GO60", loaded.Keyboard);  // default, not "Glove80"
            Assert.True(File.Exists(backup), "future-versioned settings file must be backed up");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(backup)) File.Delete(backup);
        }
    }

    [Fact]
    public void SettingsService_LoadFromCorruptJson_ReturnsDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mlv-settings-{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(path, "{ this is not valid json");
            var svc = new SettingsService(path);
            var loaded = svc.Load();

            Assert.Equal("GO60", loaded.Keyboard);
            Assert.Empty(loaded.MouseLayer);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SettingsService_SaveThenLoad_RoundTripsAllPostRefactorFields()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mlv-settings-{Guid.NewGuid()}.json");
        try
        {
            var svc = new SettingsService(path);
            var saved = new UserSettings
            {
                Keyboard = "Glove80",
                HotkeyKey = "F19",
                ColorTrayIconByActiveLayer = false,
                MouseLayer = new Dictionary<string, MouseLayerSettings>
                {
                    ["Glove80"] = new(Enabled: true, MouseLayerIndex: 5, IdleTimeoutMs: 800),
                },
            };
            svc.Save(saved);
            var loaded = svc.Load();

            Assert.Equal(saved.Keyboard, loaded.Keyboard);
            Assert.Equal(saved.HotkeyKey, loaded.HotkeyKey);
            Assert.Equal(saved.ColorTrayIconByActiveLayer, loaded.ColorTrayIconByActiveLayer);
            Assert.Equal(saved.MouseLayer["Glove80"], loaded.MouseLayer["Glove80"]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
