namespace WheelPro;

public sealed class SavedWheel
{
    public SavedWheel(string brand, string model, bool favourite, string cachePath, int presetCount = 0)
    {
        Brand = brand;
        Model = model;
        Favourite = favourite;
        CachePath = cachePath;
        PresetCount = presetCount;
    }

    public string Brand { get; }
    public string Model { get; }
    public bool Favourite { get; set; }
    public string CachePath { get; }
    public int PresetCount { get; }
    public string Status => PresetCount > 0
        ? $"{PresetCount} saved preset{(PresetCount == 1 ? string.Empty : "s")}{(Favourite ? " · favourite" : string.Empty)}"
        : Favourite ? "Favourite - profile cached" : string.IsNullOrWhiteSpace(CachePath) ? "Catalogue wheel - select to configure" : "Profile cached";
}
