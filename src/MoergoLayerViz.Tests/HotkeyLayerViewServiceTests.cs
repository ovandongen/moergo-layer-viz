using MoergoLayerViz.App.Services;
using MoergoLayerViz.App.Services.Hotkeys;
using MoergoLayerViz.Core.Settings;
using Xunit;

namespace MoergoLayerViz.Tests;

public class HotkeyLayerViewServiceTests
{
    /// <summary>
    /// Records every registration the service makes plus the order of
    /// register/unregister calls. <see cref="FailKeys"/> lets a test inject
    /// per-key registration failures (mimics "key already owned" or an
    /// unsupported name) without touching the real OS hotkey API.
    /// </summary>
    private sealed class FakeRegistry : INativeHotkeyRegistry
    {
        private int _nextToken = 1;
        public List<(int Token, string Key, string Mods, Action OnPress)> Registered { get; } = new();
        public List<int> Unregistered { get; } = new();
        public HashSet<string> FailKeys { get; } = new();

        public HotkeyRegistration TryRegister(string keyName, string modifiersName, Action onPress)
        {
            if (FailKeys.Contains(keyName))
                return HotkeyRegistration.Failed($"forced failure for {keyName}");
            var token = _nextToken++;
            Registered.Add((token, keyName, modifiersName, onPress));
            return HotkeyRegistration.Ok(token);
        }

        public void Unregister(int token)
        {
            Unregistered.Add(token);
            Registered.RemoveAll(r => r.Token == token);
        }

        public void UnregisterAll()
        {
            foreach (var r in Registered) Unregistered.Add(r.Token);
            Registered.Clear();
        }

        public void Dispose() { }
    }

    [Fact]
    public void ApplyBindings_RegistersEachBinding_AndReportsSuccess()
    {
        var registry = new FakeRegistry();
        var service = new HotkeyLayerViewService(registry, _ => { });

        var results = service.ApplyBindings(new[]
        {
            new HotkeyLayerBinding("F18", "None", 1),
            new HotkeyLayerBinding("F19", "None", 2),
        });

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Success));
        Assert.Equal(2, registry.Registered.Count);
        Assert.Equal("F18", registry.Registered[0].Key);
        Assert.Equal("F19", registry.Registered[1].Key);
    }

    [Fact]
    public void ApplyBindings_SurfacesPerBindingFailures()
    {
        var registry = new FakeRegistry();
        registry.FailKeys.Add("F19");
        var service = new HotkeyLayerViewService(registry, _ => { });

        var results = service.ApplyBindings(new[]
        {
            new HotkeyLayerBinding("F18", "None", 1),
            new HotkeyLayerBinding("F19", "None", 2),
        });

        Assert.True(results[0].Success);
        Assert.False(results[1].Success);
        Assert.NotNull(results[1].ErrorMessage);
        Assert.Single(registry.Registered);
    }

    [Fact]
    public void ApplyBindings_ReleasesPriorBindingsBeforeRegistering()
    {
        var registry = new FakeRegistry();
        var service = new HotkeyLayerViewService(registry, _ => { });

        service.ApplyBindings(new[] { new HotkeyLayerBinding("F18", "None", 1) });
        var firstToken = registry.Registered[0].Token;

        service.ApplyBindings(new[] { new HotkeyLayerBinding("F19", "None", 2) });

        Assert.Contains(firstToken, registry.Unregistered);
        Assert.Single(registry.Registered);
        Assert.Equal("F19", registry.Registered[0].Key);
    }

    [Fact]
    public void PressInvokesCallbackWithBoundLayerIndex()
    {
        var registry = new FakeRegistry();
        int? toggled = null;
        var service = new HotkeyLayerViewService(registry, layer => toggled = layer);

        service.ApplyBindings(new[]
        {
            new HotkeyLayerBinding("F18", "None", 1),
            new HotkeyLayerBinding("F19", "None", 3),
        });

        registry.Registered.First(r => r.Key == "F19").OnPress();
        Assert.Equal(3, toggled);

        registry.Registered.First(r => r.Key == "F18").OnPress();
        Assert.Equal(1, toggled);
    }

    [Fact]
    public void Dispose_UnregistersEveryActiveBinding()
    {
        var registry = new FakeRegistry();
        var service = new HotkeyLayerViewService(registry, _ => { });
        service.ApplyBindings(new[]
        {
            new HotkeyLayerBinding("F18", "None", 1),
            new HotkeyLayerBinding("F19", "None", 2),
        });

        service.Dispose();

        Assert.Empty(registry.Registered);
        Assert.Equal(2, registry.Unregistered.Count);
    }

    [Fact]
    public void ApplyBindings_AfterDispose_ReturnsEmpty_AndDoesNotRegister()
    {
        var registry = new FakeRegistry();
        var service = new HotkeyLayerViewService(registry, _ => { });
        service.Dispose();

        var results = service.ApplyBindings(new[] { new HotkeyLayerBinding("F18", "None", 1) });

        Assert.Empty(results);
        Assert.Empty(registry.Registered);
    }
}
