using MoergoLayerViz.App.ViewModels;
using MoergoLayerViz.Core.Layout;
using MoergoLayerViz.Core.Settings;
using Xunit;

namespace MoergoLayerViz.Tests;

/// <summary>
/// Endless mouse-layer timeout has no organic way to revert — it must be
/// paired with at least one of (ExitOnTransparentKey, ExitOnEmptyKey) so the
/// user always has a way back. SettingsViewModel owns the cross-field
/// invariant: enabling endless auto-arms transparent-key exit when neither
/// flag is set, and the per-flag setters refuse to disable the last enabled
/// flag while endless is on.
/// </summary>
public class SettingsViewModelMouseLayerClampingTests
{
    private sealed class InMemorySettingsService : ISettingsService
    {
        public UserSettings Current { get; set; } = new();
        public UserSettings Load() => Current;
        public void Save(UserSettings settings) => Current = settings;
    }

    private static SettingsViewModel NewVm(UserSettings? seed = null)
    {
        var settings = new InMemorySettingsService { Current = seed ?? new UserSettings { Keyboard = "GO60" } };
        var main = new MainWindowViewModel(settings);
        // Mouse layer must be enabled for snapshot updates to flow back through
        // the engine without being short-circuited.
        var vm = new SettingsViewModel(settings, main);
        vm.IsMouseLayerEnabled = true;
        return vm;
    }

    [Fact]
    public void EnablingEndless_WithNeitherExitFlag_AutoEnablesTransparent()
    {
        var vm = NewVm();
        Assert.False(vm.IsMouseLayerExitOnTransparentKey);
        Assert.False(vm.IsMouseLayerExitOnEmptyKey);

        vm.IsMouseLayerEndlessTimeout = true;

        Assert.True(vm.IsMouseLayerEndlessTimeout);
        Assert.True(vm.IsMouseLayerExitOnTransparentKey);
        Assert.False(vm.IsMouseLayerExitOnEmptyKey);
    }

    [Fact]
    public void EnablingEndless_WhenEmptyOnly_LeavesEmptyAndDoesNotForceTransparent()
    {
        var vm = NewVm();
        vm.IsMouseLayerExitOnEmptyKey = true;

        vm.IsMouseLayerEndlessTimeout = true;

        Assert.True(vm.IsMouseLayerEndlessTimeout);
        Assert.False(vm.IsMouseLayerExitOnTransparentKey);
        Assert.True(vm.IsMouseLayerExitOnEmptyKey);
    }

    [Fact]
    public void DisablingLastExitFlag_WhileEndless_IsRefused()
    {
        var vm = NewVm();
        vm.IsMouseLayerEndlessTimeout = true; // auto-enables transparent
        Assert.True(vm.IsMouseLayerExitOnTransparentKey);

        vm.IsMouseLayerExitOnTransparentKey = false;

        Assert.True(vm.IsMouseLayerExitOnTransparentKey);
    }

    [Fact]
    public void DisablingOneOfTwoExitFlags_WhileEndless_IsAllowed()
    {
        var vm = NewVm();
        vm.IsMouseLayerExitOnTransparentKey = true;
        vm.IsMouseLayerExitOnEmptyKey = true;
        vm.IsMouseLayerEndlessTimeout = true;

        vm.IsMouseLayerExitOnEmptyKey = false;

        Assert.True(vm.IsMouseLayerEndlessTimeout);
        Assert.True(vm.IsMouseLayerExitOnTransparentKey);
        Assert.False(vm.IsMouseLayerExitOnEmptyKey);
    }

    [Fact]
    public void DisablingEndless_ThenDisablingExitFlag_IsAllowed()
    {
        var vm = NewVm();
        vm.IsMouseLayerEndlessTimeout = true; // auto-enables transparent

        vm.IsMouseLayerEndlessTimeout = false;
        vm.IsMouseLayerExitOnTransparentKey = false;

        Assert.False(vm.IsMouseLayerEndlessTimeout);
        Assert.False(vm.IsMouseLayerExitOnTransparentKey);
    }
}
