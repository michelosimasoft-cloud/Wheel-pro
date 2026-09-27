using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WheelPro;

public sealed record RunningGame(string Executable, string DisplayName, int PlatformIndex);

public static class RunningGameDetector
{
    private static readonly string[] IgnoredProcesses =
    {
        "wheelpro", "explorer", "dwm", "taskmgr", "applicationframehost", "shellexperiencehost",
        "startmenuexperiencehost", "searchhost", "steam", "steamwebhelper", "eadesktop", "eabackgroundservice",
        "epicgameslauncher", "goggalaxy", "upc", "ubisoftconnect", "gamingservices", "gamebar", "gamebarftserver"
    };

    public static RunningGame? FindActiveGame()
    {
        var foreground = GetForegroundWindow();
        if (foreground != IntPtr.Zero)
        {
            GetWindowThreadProcessId(foreground, out var processId);
            try
            {
                using var process = Process.GetProcessById((int)processId);
                if (FromProcess(process) is { } foregroundGame) return foregroundGame;
            }
            catch { }
        }
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (BuiltInProfiles.IsSupportedGameExecutable(process.ProcessName))
                    return FromProcess(process, true);
            }
            catch { }
            finally { process.Dispose(); }
        }
        return null;
    }

    private static RunningGame? FromProcess(Process process, bool allowKnownWithoutWindow = false)
    {
        var name = process.ProcessName;
        if (IgnoredProcesses.Any(ignored => name.Equals(ignored, StringComparison.OrdinalIgnoreCase))) return null;
        string? path;
        try { path = process.MainModule?.FileName; }
        catch { path = null; }
        var known = BuiltInProfiles.IsSupportedGameExecutable(name);
        var gameLocation = path?.Contains("\\steamapps\\common\\", StringComparison.OrdinalIgnoreCase) == true ||
                           path?.Contains("\\EA Games\\", StringComparison.OrdinalIgnoreCase) == true ||
                           path?.Contains("\\XboxGames\\", StringComparison.OrdinalIgnoreCase) == true ||
                           path?.Contains("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase) == true ||
                           path?.Contains("\\Epic Games\\", StringComparison.OrdinalIgnoreCase) == true ||
                           path?.Contains("\\GOG Galaxy\\Games\\", StringComparison.OrdinalIgnoreCase) == true ||
                           path?.Contains("\\Ubisoft Game Launcher\\games\\", StringComparison.OrdinalIgnoreCase) == true;
        if (!known && !gameLocation && !allowKnownWithoutWindow) return null;
        var executable = path ?? $"{name}.exe";
        var platform = path?.Contains("steamapps", StringComparison.OrdinalIgnoreCase) == true ? 1
            : path?.Contains("EA Games", StringComparison.OrdinalIgnoreCase) == true ? 2 : 0;
        var display = BuiltInProfiles.FindGameProfile(name)?.Name ?? process.MainWindowTitle;
        if (string.IsNullOrWhiteSpace(display)) display = name;
        return new RunningGame(executable, display, platform);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
