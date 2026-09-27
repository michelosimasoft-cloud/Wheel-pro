using System.IO;
using System.Text.Json;

namespace WheelPro;

public sealed class GameSessionSettings
{
    public string? GameExecutable { get; set; }
    public int Platform { get; set; }
    public int OutputMode { get; set; }
}

public static class GameSessionSettingsStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WheelPro", "game-session.json");

    public static GameSessionSettings Load()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<GameSessionSettings>(File.ReadAllText(SettingsPath)) ?? new GameSessionSettings()
                : new GameSessionSettings();
        }
        catch { return new GameSessionSettings(); }
    }

    public static void Save(GameSessionSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
