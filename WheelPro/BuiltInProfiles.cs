using System.IO;

namespace WheelPro;

public sealed record GameControllerProfile(string Name, int OutputMode, string Description, double SteeringGain = 1);

public static class BuiltInProfiles
{
    private static readonly (string Pattern, string Name)[] VirtualXboxGames =
    {
        ("forzahorizon5", "Forza Horizon 5"), ("forzahorizon4", "Forza Horizon 4"),
        ("forzamotorsport", "Forza Motorsport"), ("forza_steamworks_release_final", "Forza"),
        ("needforspeedunbound", "Need for Speed Unbound"), ("nfsheat", "Need for Speed Heat"),
        ("needforspeedpayback", "Need for Speed Payback"), ("needforspeed", "Need for Speed"),
        ("nfs16", "Need for Speed 2016"), ("nfs14", "Need for Speed Rivals"),
        ("f1_", "EA Sports F1"), ("easportswrc", "EA Sports WRC"), ("wrc", "EA Sports WRC"),
        ("gridlegends", "GRID Legends"), ("grid", "GRID"),
        ("burnoutparadise", "Burnout Paradise"), ("carx", "CarX Street")
    };

    public static bool ApplyWheelDefaults(WheelProfile wheel, IDictionary<string, uint> buttons, IDictionary<string, string> axes, IDictionary<string, long> pedalTravel, IDictionary<string, int> pedalDirection, IDictionary<string, long> steeringTravel)
    {
        if (!wheel.Brand.Equals("Thrustmaster", StringComparison.OrdinalIgnoreCase) || !wheel.Model.Contains("T98", StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var binding in new Dictionary<string, uint>
        {
            ["GearDown"] = 1, ["GearUp"] = 2, ["Triangle"] = 4, ["Circle"] = 8, ["Square"] = 16, ["Cross"] = 32,
            ["Share"] = 64, ["Options"] = 128, ["R2"] = 256, ["L2"] = 512, ["L3"] = 1024, ["R3"] = 2048, ["PS"] = 4096
        }) buttons[binding.Key] = binding.Value;
        steeringTravel["SteerLeft"] = 27669; steeringTravel["SteerRight"] = 26495;
        return true;
    }

    public static GameControllerProfile? FindGameProfile(string executable)
    {
        var game = Path.GetFileNameWithoutExtension(executable);
        if (game.Equals("speed", StringComparison.OrdinalIgnoreCase) || game.Equals("speed2", StringComparison.OrdinalIgnoreCase) ||
            game.Equals("nfsmw", StringComparison.OrdinalIgnoreCase) || game.Equals("nfsc", StringComparison.OrdinalIgnoreCase))
            return new GameControllerProfile("Classic Need for Speed", 1,
                "Compatibility Xbox profile: calibrated wheel input is exposed through XInput.", 1.25);
        var match = VirtualXboxGames.FirstOrDefault(entry => game.Contains(entry.Pattern, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(match.Pattern))
        {
            var gain = match.Name switch
            {
                "Need for Speed" or "Need for Speed Unbound" or "Need for Speed Heat" or "Need for Speed Payback" or "Need for Speed 2016" or "Need for Speed Rivals" => 1.2,
                "Forza Horizon 5" or "Forza Horizon 4" or "Forza Motorsport" or "Forza" => 1.1,
                "GRID Legends" or "GRID" or "CarX Street" or "Burnout Paradise" => 1.15,
                _ => 1.05
            };
            return new GameControllerProfile(match.Name, 1,
                "Compatibility Xbox profile: Wheel Pro exposes calibrated steering and pedals as XInput before the game starts.", gain);
        }

        if (game.Contains("assettocorsa", StringComparison.OrdinalIgnoreCase) || game.Contains("iracing", StringComparison.OrdinalIgnoreCase) ||
            game.Contains("rfactor", StringComparison.OrdinalIgnoreCase) || game.Contains("beamng", StringComparison.OrdinalIgnoreCase))
            return new GameControllerProfile(game, 0, "Native wheel/HID profile for a game with direct wheel support.");
        return null;
    }

    public static bool IsSupportedGameExecutable(string executable) => FindGameProfile(executable) is not null;
}
