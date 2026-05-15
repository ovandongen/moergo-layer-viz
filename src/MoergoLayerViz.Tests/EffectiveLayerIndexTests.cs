using System.IO;
using Avalonia.Threading;
using MoergoLayerViz.App.ViewModels;
using MoergoLayerViz.Core.Layout;
using MoergoLayerViz.Core.Settings;
using Xunit;

namespace MoergoLayerViz.Tests;

public class EffectiveLayerIndexTests
{
    // ToggleLayerViewOverride dispatches its mutation via Dispatcher.UIThread.Post
    // (so native hotkey callbacks land on the UI thread). xUnit doesn't pump the
    // Avalonia dispatcher, so we drain queued jobs manually after each call.
    private static void PumpDispatcher() => Dispatcher.UIThread.RunJobs();

    private sealed class InMemorySettingsService : ISettingsService
    {
        public UserSettings Current { get; set; } = new();
        public UserSettings Load() => Current;
        public void Save(UserSettings settings) => Current = settings;
    }

    private static MainWindowViewModel MakeVmWithLayout()
    {
        var settings = new InMemorySettingsService
        {
            Current = new UserSettings { Keyboard = "GO60" }
        };
        var vm = new MainWindowViewModel(settings);
        vm.LoadLayoutFromPath(Path.Combine(AppContext.BaseDirectory, "Go60.json"));
        return vm;
    }

    [Fact]
    public void EffectiveLayerIndex_StartsAtZero_WhenNoOverride()
    {
        var vm = MakeVmWithLayout();
        Assert.Null(vm.LayerViewOverride);
        Assert.Equal(0, vm.EffectiveLayerIndex);
    }

    [Fact]
    public void ToggleLayerViewOverride_SetsAndClearsOverride()
    {
        var vm = MakeVmWithLayout();

        vm.ToggleLayerViewOverride(2);
        PumpDispatcher();
        Assert.Equal(2, vm.LayerViewOverride);
        Assert.Equal(2, vm.EffectiveLayerIndex);

        vm.ToggleLayerViewOverride(2);
        PumpDispatcher();
        Assert.Null(vm.LayerViewOverride);
        Assert.Equal(0, vm.EffectiveLayerIndex);
    }

    [Fact]
    public void ToggleLayerViewOverride_SwitchesBetweenLayers_WithoutIntermediateRevert()
    {
        var vm = MakeVmWithLayout();

        vm.ToggleLayerViewOverride(1);
        PumpDispatcher();
        Assert.Equal(1, vm.LayerViewOverride);

        vm.ToggleLayerViewOverride(3);
        PumpDispatcher();
        Assert.Equal(3, vm.LayerViewOverride);
        Assert.Equal(3, vm.EffectiveLayerIndex);
    }

    [Fact]
    public void SelectKeyboard_ClearsActiveOverride()
    {
        var vm = MakeVmWithLayout();
        vm.ToggleLayerViewOverride(2);
        PumpDispatcher();
        Assert.Equal(2, vm.LayerViewOverride);

        vm.SelectKeyboardCommand.Execute(new Glove80Profile());

        Assert.Null(vm.LayerViewOverride);
    }
}
