using Microsoft.Win32;
using System.Diagnostics;
using System.IO;

namespace WheelPro;

public static class ExclusiveInputIntegration
{
    public const string OfficialDownloadUrl = "https://github.com/nefarius/HidHide/releases/latest";

    public static bool IsInstalled => FindCli() is not null;

    public static string EnableFor(ConnectedWheel wheel)
    {
        var cli = FindCli();
        if (cli is null)
            return "Exclusive input support is not installed. Install HidHide from its official publisher, restart Windows, then reopen Wheel Pro.";
        var app = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(app)) return "Wheel Pro could not determine its installed application path.";
        try
        {
            var start = new ProcessStartInfo(cli)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("--app-reg"); start.ArgumentList.Add(app);
            start.ArgumentList.Add("--dev-hide"); start.ArgumentList.Add(wheel.HardwareId);
            start.ArgumentList.Add("--cloak-on");
            using var process = Process.Start(start) ?? throw new InvalidOperationException("HidHide CLI did not start.");
            if (!process.WaitForExit(10000))
            {
                process.Kill(entireProcessTree: true);
                return "Exclusive mode configuration timed out. Open HidHide Configuration Client and verify that its driver is running.";
            }
            var error = process.StandardError.ReadToEnd().Trim();
            return process.ExitCode == 0
                ? "Exclusive controller mode is active. Games see only Wheel Pro's Xbox controller; the physical wheel remains visible to Wheel Pro."
                : $"Exclusive mode could not be enabled: {error}";
        }
        catch (Exception ex) { return $"Exclusive mode could not be enabled: {ex.Message}"; }
    }

    private static string? FindCli()
    {
        var candidates = new List<string>();
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(@"SOFTWARE\Nefarius Software Solutions e.U.\Nefarius Software Solutions e.U. HidHide");
            if (key?.GetValue("Path") is string installedPath)
            {
                candidates.Add(Path.Combine(installedPath, "HidHideCLI.exe"));
                candidates.Add(Path.Combine(installedPath, "x64", "HidHideCLI.exe"));
            }
        }
        catch { }
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Nefarius Software Solutions", "HidHide", "HidHideCLI.exe"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Nefarius Software Solutions", "HidHide", "x64", "HidHideCLI.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }
}
