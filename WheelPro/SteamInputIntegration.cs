using System.Diagnostics;

namespace WheelPro;

public static class SteamInputIntegration
{
    public static bool IsSteamRunning() => Process.GetProcessesByName("steam").Any();

    public static void OpenControllerSettings()
    {
        Process.Start(new ProcessStartInfo("steam://open/settings/controller") { UseShellExecute = true });
    }

    public static string GetStatus(bool virtualOutputSelected) => IsSteamRunning()
        ? virtualOutputSelected
            ? "Steam is running. Steam Input can use Wheel Pro's virtual controller; keep Steam Input enabled for this game if its native wheel support is limited."
            : "Steam is running. Native wheel input is selected; disable Steam Input for this game if Steam remaps the wheel unexpectedly."
        : "Steam is not running. Steam Input settings will apply automatically when a Steam game is launched.";
}
