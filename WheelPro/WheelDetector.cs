using System.Management;

namespace WheelPro;

public sealed record ConnectedWheel(string Name, string HardwareId);

public static class WheelDetector
{
    private static readonly string[] BrandNames =
    {
        "Thrustmaster", "Logitech", "Fanatec", "MOZA", "Simucube", "Simagic", "Asetek", "Cammus", "Turtle Beach", "HORI", "PXN"
    };

    public static ConnectedWheel? FindMatchingWheel(WheelProfile profile)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID IS NOT NULL");
            foreach (ManagementObject device in searcher.Get())
            {
                var name = device["Name"]?.ToString() ?? string.Empty;
                var hardwareId = device["PNPDeviceID"]?.ToString() ?? string.Empty;
                var isGenericGameController = name.Contains("HID-compliant game controller", StringComparison.OrdinalIgnoreCase) && hardwareId.Contains("HID\\VID_", StringComparison.OrdinalIgnoreCase);
                var matchesHardwareId = profile.HardwareIds?.Any(id => hardwareId.Contains(id, StringComparison.OrdinalIgnoreCase)) == true;
                var matchesSelectedBrand = name.Contains(profile.Brand, StringComparison.OrdinalIgnoreCase);
                var matchesSelectedModel = name.Contains("T98", StringComparison.OrdinalIgnoreCase) || name.Contains(profile.Model.Split(' ')[0], StringComparison.OrdinalIgnoreCase);
                // A selected profile must only open its matching physical device.
                // Unknown Windows HID names can still use the Generic HID profile.
                if (matchesHardwareId || matchesSelectedModel || matchesSelectedBrand || (profile.Brand == "Generic HID" && isGenericGameController))
                    return new ConnectedWheel(name, hardwareId);
            }
            return null;
        }
        catch (ManagementException) { }
        return null;
    }

    public static ConnectedWheel? FindConnectedWheel()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID IS NOT NULL");
            ConnectedWheel? fallback = null;
            foreach (ManagementObject device in searcher.Get())
            {
                var name = device["Name"]?.ToString() ?? string.Empty;
                var hardwareId = device["PNPDeviceID"]?.ToString() ?? string.Empty;
                var isGenericGameController = name.Contains("HID-compliant game controller", StringComparison.OrdinalIgnoreCase) && hardwareId.Contains("HID\\VID_", StringComparison.OrdinalIgnoreCase);
                if (WheelCatalog.FindByHardwareId(hardwareId) is not null || name.Contains("T98", StringComparison.OrdinalIgnoreCase) || BrandNames.Any(brand => name.Contains(brand, StringComparison.OrdinalIgnoreCase)))
                    return new ConnectedWheel(name, hardwareId);
                if (isGenericGameController)
                    fallback ??= new ConnectedWheel(name, hardwareId);
            }
            return fallback;
        }
        catch (ManagementException) { return null; }
    }
}
