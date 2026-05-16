using MoergoLayerViz.App.Services;
using MoergoLayerViz.Core.Layout;
using MoergoLayerViz.Core.Settings;
using Xunit;
using ZmkHidProtocol.ActiveWindow;

namespace MoergoLayerViz.Tests;

/// <summary>
/// Covers the AutoSwitchEngine state machine via direct manipulation of the
/// ObservableProperty <c>ActiveWindow</c> — the IActiveWindowMonitor is left
/// null so the focus-change path (which goes through Dispatcher.UIThread.Post,
/// unavailable in xUnit's threading context) stays out of the test surface.
/// Exit-tap *trigger* behavior also uses the dispatcher and is exercised only
/// indirectly (persistence + clearing); the rule-match state machine is the
/// part with real coverage debt.
/// </summary>
public class AutoSwitchEngineTests
{
    private sealed class InMemorySettingsService : ISettingsService
    {
        public UserSettings Current { get; set; } = new();
        public UserSettings Load() => Current;
        public void Save(UserSettings settings) => Current = settings;
    }

    private static AppLayerRule Rule(string match, int layer) => new(match, layer);
    private static ActiveWindowInfo Window(string process) => new(process, null, null);

    /// <summary>
    /// Build an engine with sensible defaults — rules in `initialRules`, the
    /// engine enabled by default, no active-window monitor. Tests then drive
    /// state by assigning <c>engine.ActiveWindow</c> and reading from the
    /// captured push list.
    /// </summary>
    private static (AutoSwitchEngine engine, List<int> pushes, InMemorySettingsService settings) NewEngine(
        IReadOnlyList<AppLayerRule>? initialRules = null,
        AutoSwitchFallbackMode fallback = AutoSwitchFallbackMode.Base,
        Func<int>? getActiveLayer = null,
        bool enabled = true,
        IKeyboardProfile? profile = null,
        int? mouseLayerIndex = null)
    {
        profile ??= new Go60Profile();
        var settings = new InMemorySettingsService
        {
            Current = new UserSettings
            {
                AutoSwitchKeyboardLayer = enabled,
                AppLayerRules = initialRules is null
                    ? new Dictionary<string, List<AppLayerRule>>()
                    : new Dictionary<string, List<AppLayerRule>> { [profile.Id] = initialRules.ToList() },
                AutoSwitchFallback = new Dictionary<string, AutoSwitchFallbackMode> { [profile.Id] = fallback },
                MouseLayer = mouseLayerIndex is null
                    ? new Dictionary<string, MouseLayerSettings>()
                    : new Dictionary<string, MouseLayerSettings>
                    {
                        [profile.Id] = new MouseLayerSettings(Enabled: true, MouseLayerIndex: mouseLayerIndex),
                    },
            },
        };
        var engine = new AutoSwitchEngine(
            settings,
            activeWindowMonitor: null,
            getActiveLayer ?? (() => 0),
            profile);
        var pushes = new List<int>();
        engine.PushLayerRequested += pushes.Add;
        return (engine, pushes, settings);
    }

    // ─── Idle ↔ InSession transitions ────────────────────────────────────

    [Fact]
    public void Idle_NoMatch_DoesNothing()
    {
        var (engine, pushes, _) = NewEngine(new[] { Rule("Code", 3) });
        engine.ActiveWindow = Window("firefox");

        Assert.Empty(pushes);
        Assert.Null(engine.MatchedAppLayerRule);
    }

    [Fact]
    public void Idle_MatchingRule_FiresAndSnapshotsPreRuleLayer()
    {
        var (engine, pushes, _) = NewEngine(new[] { Rule("Code", 3) }, getActiveLayer: () => 1);
        engine.ActiveWindow = Window("Code");

        Assert.Equal(new[] { 3 }, pushes);
        Assert.Equal(3, engine.MatchedAppLayerRule!.LayerIndex);
    }

    [Fact]
    public void Idle_MatchWhenActiveLayerEqualsMouseLayer_CapturesBaseAsPreRule()
    {
        // ff97b24: the mouse layer can never be the AutoSwitch fallback target.
        // When the user clicks an app while the mouse-push has already swapped
        // the layer to the configured mouse layer, the engine must snapshot 0
        // (base) instead of the live active layer.
        var activeLayer = 4;
        var (engine, pushes, _) = NewEngine(
            new[] { Rule("Code", 3) },
            fallback: AutoSwitchFallbackMode.Previous,
            getActiveLayer: () => activeLayer,
            mouseLayerIndex: 4);

        engine.ActiveWindow = Window("Code");
        Assert.Equal(new[] { 3 }, pushes);

        // Focus moves away; previous-fallback would normally push the captured
        // pre-rule layer. Because that capture was forced to 0, the fallback
        // pushes 0 instead of the stale mouse layer (4).
        activeLayer = 3;  // pretend HID acked the rule push
        engine.ActiveWindow = Window("firefox");

        Assert.Equal(new[] { 3, 0 }, pushes);
    }

    [Fact]
    public void InSession_MatchClearsWithoutUserOverride_RevertsToBase()
    {
        var activeLayer = 1;
        var (engine, pushes, _) = NewEngine(
            new[] { Rule("Code", 3) },
            fallback: AutoSwitchFallbackMode.Base,
            getActiveLayer: () => activeLayer);

        engine.ActiveWindow = Window("Code");
        activeLayer = 3;  // ack
        engine.ActiveWindow = Window("firefox");

        Assert.Equal(new[] { 3, 0 }, pushes);
    }

    [Fact]
    public void InSession_MatchClearsWithoutUserOverride_RevertsToPreviousLayer()
    {
        var activeLayer = 1;
        var (engine, pushes, _) = NewEngine(
            new[] { Rule("Code", 3) },
            fallback: AutoSwitchFallbackMode.Previous,
            getActiveLayer: () => activeLayer);

        engine.ActiveWindow = Window("Code");
        activeLayer = 3;
        engine.ActiveWindow = Window("firefox");

        Assert.Equal(new[] { 3, 1 }, pushes);
    }

    [Fact]
    public void InSession_MatchClearsAfterUserOverride_DoesNotRevert()
    {
        // User manually changed the layer after the rule fired — that's a
        // signal we shouldn't yank it back when focus changes.
        var activeLayer = 1;
        var (engine, pushes, _) = NewEngine(
            new[] { Rule("Code", 3) },
            getActiveLayer: () => activeLayer);

        engine.ActiveWindow = Window("Code");
        activeLayer = 5;  // user manually picked a different layer
        engine.ActiveWindow = Window("firefox");

        Assert.Equal(new[] { 3 }, pushes);  // only the original rule push
    }

    [Fact]
    public void InSession_MatchClears_FallbackBaseRevertsToZero()
    {
        // Active layer is acked to match LastPushedLayer (so userOverride is
        // false); fallback Base pushes 0.
        var activeLayer = 0;
        var (engine, pushes, _) = NewEngine(
            new[] { Rule("Code", 3) },
            fallback: AutoSwitchFallbackMode.Base,
            getActiveLayer: () => activeLayer);

        engine.ActiveWindow = Window("Code");
        activeLayer = 3;  // ack push so the user-override check passes
        engine.ActiveWindow = Window("firefox");

        Assert.Equal(new[] { 3, 0 }, pushes);
    }

    [Fact]
    public void InSession_NewMatchingRule_FiresAndUpdatesSession()
    {
        var activeLayer = 0;
        var (engine, pushes, _) = NewEngine(
            new[] { Rule("Code", 3), Rule("firefox", 5) },
            getActiveLayer: () => activeLayer);

        engine.ActiveWindow = Window("Code");
        activeLayer = 3;
        engine.ActiveWindow = Window("firefox");

        Assert.Equal(new[] { 3, 5 }, pushes);
    }

    [Fact]
    public void InSession_SameMatchAgain_DoesNotRePush()
    {
        var (engine, pushes, _) = NewEngine(new[] { Rule("Code", 3) });
        engine.ActiveWindow = Window("Code");
        engine.ActiveWindow = Window("Code - file.cs");  // same matched rule

        Assert.Equal(new[] { 3 }, pushes);
    }

    // ─── Disabled ────────────────────────────────────────────────────────

    [Fact]
    public void Disabled_NoMatchesFire()
    {
        var (engine, pushes, _) = NewEngine(new[] { Rule("Code", 3) }, enabled: false);
        engine.ActiveWindow = Window("Code");

        Assert.Empty(pushes);
    }

    [Fact]
    public void EnableWhileMatched_FiresImmediately()
    {
        var (engine, pushes, _) = NewEngine(new[] { Rule("Code", 3) }, enabled: false);
        engine.ActiveWindow = Window("Code");
        Assert.Empty(pushes);

        engine.IsEnabled = true;

        Assert.Equal(new[] { 3 }, pushes);
    }

    [Fact]
    public void DisableMidSession_ClearsStateButDoesNotPush()
    {
        var activeLayer = 0;
        var (engine, pushes, _) = NewEngine(
            new[] { Rule("Code", 3) },
            getActiveLayer: () => activeLayer);

        engine.ActiveWindow = Window("Code");
        activeLayer = 3;
        engine.IsEnabled = false;

        // Disabling alone doesn't issue a revert push — it just stops the engine.
        Assert.Equal(new[] { 3 }, pushes);
    }

    // ─── Rule mutations ─────────────────────────────────────────────────

    [Fact]
    public void ApplyAppLayerRules_DeletingActiveRule_RevertsToFallback()
    {
        var activeLayer = 1;
        var (engine, pushes, settings) = NewEngine(
            new[] { Rule("Code", 3) },
            getActiveLayer: () => activeLayer);

        engine.ActiveWindow = Window("Code");
        activeLayer = 3;

        engine.ApplyAppLayerRules(Array.Empty<AppLayerRule>());

        // Match recomputed; new match = null while InSession → revert push.
        Assert.Equal(new[] { 3, 0 }, pushes);
        Assert.Empty(settings.Current.AppLayerRules);
    }

    [Fact]
    public void ApplyAppLayerRules_AddingNewMatchingRule_Fires()
    {
        var (engine, pushes, _) = NewEngine(initialRules: null);
        engine.ActiveWindow = Window("Code");
        Assert.Empty(pushes);

        engine.ApplyAppLayerRules(new[] { Rule("Code", 7) });

        Assert.Equal(new[] { 7 }, pushes);
    }

    [Fact]
    public void ApplyAppLayerRules_PersistsToSettingsKeyedByProfile()
    {
        var profile = new Go60Profile();
        var (engine, _, settings) = NewEngine(profile: profile);

        engine.ApplyAppLayerRules(new[] { Rule("vim", 2), Rule("emacs", 4) });

        Assert.True(settings.Current.AppLayerRules.ContainsKey(profile.Id));
        Assert.Equal(2, settings.Current.AppLayerRules[profile.Id].Count);
    }

    [Fact]
    public void ApplyAppLayerRules_EmptyList_RemovesProfileEntry()
    {
        var profile = new Go60Profile();
        var (engine, _, settings) = NewEngine(new[] { Rule("vim", 2) }, profile: profile);
        Assert.True(settings.Current.AppLayerRules.ContainsKey(profile.Id));

        engine.ApplyAppLayerRules(Array.Empty<AppLayerRule>());

        Assert.False(settings.Current.AppLayerRules.ContainsKey(profile.Id));
    }

    // ─── Profile switching ──────────────────────────────────────────────

    [Fact]
    public void SetActiveProfile_ClearsSessionAndReloadsRules()
    {
        var go60 = new Go60Profile();
        var glove = new Glove80Profile();
        var settings = new InMemorySettingsService
        {
            Current = new UserSettings
            {
                AutoSwitchKeyboardLayer = true,
                AppLayerRules = new Dictionary<string, List<AppLayerRule>>
                {
                    [go60.Id] = new() { Rule("Code", 3) },
                    [glove.Id] = new() { Rule("firefox", 5) },
                },
            },
        };
        var engine = new AutoSwitchEngine(settings, null, () => 0, go60);
        var pushes = new List<int>();
        engine.PushLayerRequested += pushes.Add;

        engine.ActiveWindow = Window("Code");
        Assert.Equal(new[] { 3 }, pushes);

        engine.SetActiveProfile(glove);

        // Rules now reflect Glove80; session was cleared so "Code" no longer
        // matches anything (Glove80's rule is for firefox).
        Assert.DoesNotContain(engine.AppLayerRules, r => r.ProcessMatch == "Code");
        Assert.Single(engine.AppLayerRules, r => r.ProcessMatch == "firefox");
    }

    [Fact]
    public void SetActiveProfile_LoadsFallbackForNewProfile()
    {
        var go60 = new Go60Profile();
        var glove = new Glove80Profile();
        var settings = new InMemorySettingsService
        {
            Current = new UserSettings
            {
                AutoSwitchFallback = new Dictionary<string, AutoSwitchFallbackMode>
                {
                    [go60.Id] = AutoSwitchFallbackMode.Base,
                    [glove.Id] = AutoSwitchFallbackMode.Previous,
                },
            },
        };
        var engine = new AutoSwitchEngine(settings, null, () => 0, go60);
        Assert.Equal(AutoSwitchFallbackMode.Base, engine.FallbackMode);

        engine.SetActiveProfile(glove);

        Assert.Equal(AutoSwitchFallbackMode.Previous, engine.FallbackMode);
    }

    [Fact]
    public void SetActiveProfile_LoadsExitTapKeyForNewProfile()
    {
        var go60 = new Go60Profile();
        var glove = new Glove80Profile();
        var settings = new InMemorySettingsService
        {
            Current = new UserSettings
            {
                AutoSwitchExitKey = new Dictionary<string, int>
                {
                    [go60.Id] = 12,
                    [glove.Id] = 47,
                },
            },
        };
        var engine = new AutoSwitchEngine(settings, null, () => 0, go60);
        Assert.Equal(12, engine.ExitTapKey);

        engine.SetActiveProfile(glove);

        Assert.Equal(47, engine.ExitTapKey);
    }

    // ─── Fallback persistence ───────────────────────────────────────────

    [Fact]
    public void FallbackMode_Setter_PersistsPerProfile()
    {
        var profile = new Go60Profile();
        var (engine, _, settings) = NewEngine(profile: profile);

        engine.FallbackMode = AutoSwitchFallbackMode.Previous;

        Assert.Equal(AutoSwitchFallbackMode.Previous, settings.Current.AutoSwitchFallback[profile.Id]);
    }

    // ─── Exit-tap key persistence ───────────────────────────────────────

    [Fact]
    public void SetExitTapKey_ValidIndex_PersistsAndExposes()
    {
        var profile = new Go60Profile();
        var (engine, _, settings) = NewEngine(profile: profile);

        engine.SetExitTapKey(42);

        Assert.Equal(42, engine.ExitTapKey);
        Assert.Equal(42, settings.Current.AutoSwitchExitKey[profile.Id]);
    }

    [Fact]
    public void SetExitTapKey_Null_ClearsAndRemovesPersistedEntry()
    {
        var profile = new Go60Profile();
        var settings = new InMemorySettingsService
        {
            Current = new UserSettings
            {
                AutoSwitchExitKey = new Dictionary<string, int> { [profile.Id] = 5 },
            },
        };
        var engine = new AutoSwitchEngine(settings, null, () => 0, profile);
        Assert.Equal(5, engine.ExitTapKey);

        engine.SetExitTapKey(null);

        Assert.Null(engine.ExitTapKey);
        Assert.False(settings.Current.AutoSwitchExitKey.ContainsKey(profile.Id));
    }

    [Fact]
    public void SetExitTapKey_NegativeIndex_TreatedAsClear()
    {
        var profile = new Go60Profile();
        var (engine, _, settings) = NewEngine(profile: profile);
        engine.SetExitTapKey(7);
        Assert.Equal(7, engine.ExitTapKey);

        engine.SetExitTapKey(-1);

        Assert.Null(engine.ExitTapKey);
        Assert.False(settings.Current.AutoSwitchExitKey.ContainsKey(profile.Id));
    }

    // ─── Exit-on-transparent persistence ────────────────────────────────

    [Fact]
    public void ExitOnTransparentKey_Setter_PersistsPerProfile()
    {
        var profile = new Go60Profile();
        var (engine, _, settings) = NewEngine(profile: profile);

        engine.ExitOnTransparentKey = true;

        Assert.True(engine.ExitOnTransparentKey);
        Assert.True(settings.Current.AutoSwitchExitOnTransparent[profile.Id]);
    }

    [Fact]
    public void ExitOnTransparentKey_SetterFalse_RemovesPersistedEntry()
    {
        var profile = new Go60Profile();
        var settings = new InMemorySettingsService
        {
            Current = new UserSettings
            {
                AutoSwitchExitOnTransparent = new Dictionary<string, bool> { [profile.Id] = true },
            },
        };
        var engine = new AutoSwitchEngine(settings, null, () => 0, profile);
        Assert.True(engine.ExitOnTransparentKey);

        engine.ExitOnTransparentKey = false;

        Assert.False(engine.ExitOnTransparentKey);
        Assert.False(settings.Current.AutoSwitchExitOnTransparent.ContainsKey(profile.Id));
    }

    [Fact]
    public void SetActiveProfile_LoadsExitOnTransparentForNewProfile()
    {
        var go60 = new Go60Profile();
        var glove = new Glove80Profile();
        var settings = new InMemorySettingsService
        {
            Current = new UserSettings
            {
                AutoSwitchExitOnTransparent = new Dictionary<string, bool>
                {
                    [go60.Id] = false,
                    [glove.Id] = true,
                },
            },
        };
        var engine = new AutoSwitchEngine(settings, null, () => 0, go60);
        Assert.False(engine.ExitOnTransparentKey);

        engine.SetActiveProfile(glove);

        Assert.True(engine.ExitOnTransparentKey);
    }

    // ─── Exit-on-empty persistence (parallel to trans) ──────────────────

    [Fact]
    public void ExitOnEmptyKey_Setter_PersistsPerProfile()
    {
        var profile = new Go60Profile();
        var (engine, _, settings) = NewEngine(profile: profile);

        engine.ExitOnEmptyKey = true;

        Assert.True(engine.ExitOnEmptyKey);
        Assert.True(settings.Current.AutoSwitchExitOnEmpty[profile.Id]);
    }

    [Fact]
    public void ExitOnEmptyKey_SetterFalse_RemovesPersistedEntry()
    {
        var profile = new Go60Profile();
        var settings = new InMemorySettingsService
        {
            Current = new UserSettings
            {
                AutoSwitchExitOnEmpty = new Dictionary<string, bool> { [profile.Id] = true },
            },
        };
        var engine = new AutoSwitchEngine(settings, null, () => 0, profile);
        Assert.True(engine.ExitOnEmptyKey);

        engine.ExitOnEmptyKey = false;

        Assert.False(engine.ExitOnEmptyKey);
        Assert.False(settings.Current.AutoSwitchExitOnEmpty.ContainsKey(profile.Id));
    }

    [Fact]
    public void ExitOnTransparentAndEmpty_AreIndependent()
    {
        var profile = new Go60Profile();
        var (engine, _, settings) = NewEngine(profile: profile);

        engine.ExitOnTransparentKey = true;
        engine.ExitOnEmptyKey = true;
        Assert.True(engine.ExitOnTransparentKey);
        Assert.True(engine.ExitOnEmptyKey);

        engine.ExitOnTransparentKey = false;

        Assert.False(engine.ExitOnTransparentKey);
        Assert.True(engine.ExitOnEmptyKey);
        Assert.True(settings.Current.AutoSwitchExitOnEmpty[profile.Id]);
        Assert.False(settings.Current.AutoSwitchExitOnTransparent.ContainsKey(profile.Id));
    }
}
