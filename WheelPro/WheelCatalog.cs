namespace WheelPro;

public sealed record WheelProfile(
    string Brand, string Model, bool HasForceFeedback, bool RequiresVendorDriver, string Rotation,
    int PedalCount = 2, bool HasHShifter = false, int ButtonCount = 12);

public static class WheelCatalog
{
    public static string GetOfficialSupportUrl(string brand) => brand.ToUpperInvariant() switch
    {
        "THRUSTMASTER" => "https://support.thrustmaster.com/en/",
        "LOGITECH" => "https://support.logi.com/hc/en-us",
        "FANATEC" => "https://fanatec.com/us-en/downloads",
        "MOZA" => "https://support.mozaracing.com/en/support/solutions/articles/70000628053-downloads",
        "SIMUCUBE" => "https://simucube.com/en-us/support/",
        "ASETEK" => "https://www.asetek.com/simsports/support/",
        "CAMMUS" => "https://cammusracing.com/support/",
        "SIMAGIC" => "https://en.simagic.com/download",
        "VRS" => "https://vrs.racing/downloads",
        "TURTLE BEACH" => "https://support.turtlebeach.com/",
        "HORI" => "https://stores.horiusa.com/support/",
        _ => "https://www.google.com/search?q=" + Uri.EscapeDataString(brand + " official wheel driver support")
    };

    private static readonly WheelProfile[] Profiles =
    {
        new("Thrustmaster", "T98 Ferrari 296 GTB", false, false, "240 degree", 2, false, 13),
        new("Thrustmaster", "T80 Ferrari 488 GTB", false, false, "240 degree", 2, false, 13),
        new("Thrustmaster", "T128", false, false, "270 degree", 2, false, 13),
        new("Thrustmaster", "T150 / TMX", false, true, "1080 degree", 2, false, 13),
        new("Thrustmaster", "T248", true, true, "900 degree", 3, false, 25),
        new("Thrustmaster", "T300RS / TX Racing Wheel", true, true, "1080 degree", 3, false, 25),
        new("Thrustmaster", "T300 Ferrari Integral Racing Wheel Alcantara Edition", true, true, "1080 degree", 3, false, 25),
        new("Thrustmaster", "TS-PC Racer", true, true, "1080 degree", 3, true, 25),
        new("Thrustmaster", "TS-XW Racer Sparco P310", true, true, "1080 degree", 3, true, 25),
        new("Thrustmaster", "T-GT II", true, true, "1080 degree", 3, true, 25),
        new("Thrustmaster", "Ferrari SF1000 Add-On Wheel", true, true, "1080 degree", 3, true, 25),
        new("Thrustmaster", "T818 Direct Drive", true, true, "1180 degree", 3, true, 30),
        new("Thrustmaster", "Ferrari 458 Spider Racing Wheel", false, false, "240 degree", 2, false, 13),
        new("Thrustmaster", "Ferrari 599XX EVO 30 Wheel Add-On", true, true, "1080 degree", 3, true, 25),
        new("Logitech", "G29 / G920 / G923", true, true, "900 degree", 3, false, 18),
        new("Fanatec", "CSL DD / ClubSport", true, true, "up to 2520 degree", 3, true, 24),
        new("MOZA", "R3 / R5 / R9 / R12", true, true, "up to 360 degree", 3, true, 22),
        new("Simucube", "2 Sport / 2 Pro", true, true, "up to 360 degree", 3, true, 24),
        new("Asetek", "La Prima / Forte", true, true, "up to 360 degree", 3, true, 22),
        new("Cammus", "C5 / C12", true, true, "up to 360 degree", 3, true, 18),
        new("PXN", "V3 Pro / V9 / V10", false, false, "180 to 900 degree", 3, true, 12),
        new("HORI", "Racing Wheel Apex", false, false, "270 degree"),
        new("Turtle Beach", "VelocityOne Race", true, true, "900 degree")
        ,new("Simagic", "Alpha Mini / Alpha", true, true, "up to 360 degree")
        ,new("VRS", "DirectForce Pro", true, true, "up to 360 degree")
        ,new("Fantech", "Generic racing controller", false, false, "unknown rotation")
        ,new("Mad Catz", "Pro Racing Force Feedback Wheel", true, true, "900 degree")
        ,new("Cobra", "GT Racing Wheel", false, false, "unknown rotation")
        ,new("Speedlink", "Trailblazer Racing Wheel", false, false, "180 degree", 2, false, 12)
        ,new("Superdrive", "GS850-X / SV950", false, false, "270 degree", 3, true, 12)
        ,new("FR-TEC", "Hurricane Wheel", false, false, "180 degree", 2, false, 12)
        ,new("Genius", "Speed Wheel 3", false, false, "180 degree", 2, false, 12)
        ,new("Hama", "V18 Racing Wheel", false, false, "180 degree", 2, false, 12)
        ,new("Cube Controls", "Formula Sport / GT Pro", true, true, "up to 360 degree", 3, true, 28)
        ,new("Leoxz", "XF1 Pro", true, true, "up to 360 degree", 3, true, 24)
        ,new("Conspit", "Ares / Apollo", true, true, "up to 360 degree", 3, true, 24)
        ,new("P1Sim", "Eau Rouge / Arnage", true, true, "up to 360 degree", 3, true, 28)
        ,new("Heusinkveld", "Sprint / Ultimate Pedals", false, true, "pedal set", 3, false, 0)
    };

    public static WheelProfile Find(string search)
    {
        var match = Profiles.FirstOrDefault(profile =>
            $"{profile.Brand} {profile.Model}".Contains(search, StringComparison.OrdinalIgnoreCase) ||
            search.Contains(profile.Brand, StringComparison.OrdinalIgnoreCase));
        return match ?? new WheelProfile("Generic HID", string.IsNullOrWhiteSpace(search) ? "Wheel" : search, false, false, "unknown rotation");
    }

    public static IEnumerable<string> Suggest(string search)
    {
        var brands = Profiles.Select(profile => profile.Brand).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(brand => brand.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(brand => brand.StartsWith(search, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .Select(brand => $"Brand: {brand}");
        var models = Profiles.Where(profile => $"{profile.Brand} {profile.Model}".Contains(search, StringComparison.OrdinalIgnoreCase))
            .Select(profile => $"{profile.Brand} {profile.Model}");
        return brands.Concat(models).Distinct(StringComparer.OrdinalIgnoreCase).Take(8);
    }

    public static IEnumerable<WheelProfile> Search(string search)
    {
        var normalizedSearch = Normalize(search);
        if (string.IsNullOrWhiteSpace(normalizedSearch)) return Profiles;
        return Profiles.Where(profile => Normalize($"{profile.Brand} {profile.Model}").Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string value) => new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}
