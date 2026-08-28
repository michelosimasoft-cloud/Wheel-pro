using System.IO;

namespace WheelPro;

public sealed record GameControllerProfile(string Name, int OutputMode, string Description);

public static class BuiltInProfiles
{
    public static bool ApplyWheelDefaults(WheelProfile wheel, IDictionary<string, uint> buttons, IDictionary<string, string> axes, IDictionary<string, long> pedalTravel, IDictionary<string, int> pedalDirection, IDictionary<string, long> steeringTravel)
    {
        if (!wheel.Brand.Equals("Thrustmaster", StringComparison.OrdinalIgnoreCase) || !wheel.Model.Contains("T98", StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var binding in new Dictionary<string, uint>
        {
            ["GearDown"] = 1, ["GearUp"] = 2, ["Triangle"] = 4, ["Circle"] = 8, ["Square"] = 16, ["Cross"] = 32,
            ["Share"] = 64, ["Options"] = 128, ["R2"] = 256, ["L2"] = 512, ["L3"] = 1024, ["R3"] = 2048, ["PS"] = 4096
        }) buttons[binding.Key] = binding.Value;
        axes["Accelerator"] = "Y"; axes["Brake"] = "Z";
        pedalTravel["Accelerator"] = 65535; pedalTravel["Brake"] = 65535;
        pedalDirection["Accelerator"] = -1; pedalDirection["Brake"] = -1;
        steeringTravel["SteerLeft"] = 27669; steeringTravel["SteerRight"] = 26495;
        return true;
    }

    public static GameControllerProfile? FindGameProfile(string executable)
    {
        var game = Path.GetFileNameWithoutExtension(executable);
        if (game.Contains("carx", StringComparison.OrdinalIgnoreCase))
            return new GameControllerProfile("CarX Street", 1, "Virtual Xbox controller: steering, pedals, paddles, face buttons and D-pad are ready for gameplay.");
        if (game.Contains("assettocorsa", StringComparison.OrdinalIgnoreCase) || game.Contains("iracing", StringComparison.OrdinalIgnoreCase) || game.Contains("rFactor", StringComparison.OrdinalIgnoreCase) || game.Contains("beamng", StringComparison.OrdinalIgnoreCase) || game.Contains("wrc", StringComparison.OrdinalIgnoreCase) || game.Contains("f1_", StringComparison.OrdinalIgnoreCase))
            return new GameControllerProfile(game, 0, "Native wheel/HID profile: the game can use the wheel directly, including vendor force feedback where supported.");
        return null;
    }
}
