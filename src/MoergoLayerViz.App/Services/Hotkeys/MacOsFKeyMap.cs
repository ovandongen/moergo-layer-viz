namespace MoergoLayerViz.App.Services.Hotkeys;

/// <summary>
/// Bidirectional map between human F-key indices (F1–F20) and the Carbon
/// virtual key codes used by <c>RegisterEventHotKey</c> and reported by
/// <c>CopySymbolicHotKeys</c>. F21–F24 have no documented Carbon VK and
/// are intentionally absent — they can't be registered.
/// </summary>
internal static class MacOsFKeyMap
{
    private static readonly Dictionary<int, int> FIndexToVk = new()
    {
        { 1, 0x7A }, { 2, 0x78 }, { 3, 0x63 }, { 4, 0x76 },
        { 5, 0x60 }, { 6, 0x61 }, { 7, 0x62 }, { 8, 0x64 },
        { 9, 0x65 }, { 10, 0x6D }, { 11, 0x67 }, { 12, 0x6F },
        { 13, 0x69 }, { 14, 0x6B }, { 15, 0x71 }, { 16, 0x6A },
        { 17, 0x40 }, { 18, 0x4F }, { 19, 0x50 }, { 20, 0x5A },
    };

    private static readonly Dictionary<int, int> VkToFIndex =
        FIndexToVk.ToDictionary(kv => kv.Value, kv => kv.Key);

    public static int? GetCarbonVk(int fIndex) =>
        FIndexToVk.TryGetValue(fIndex, out var vk) ? vk : null;

    public static int? GetFIndex(int carbonVk) =>
        VkToFIndex.TryGetValue(carbonVk, out var f) ? f : null;

    public static IEnumerable<int> SupportedFIndices => FIndexToVk.Keys;
}
