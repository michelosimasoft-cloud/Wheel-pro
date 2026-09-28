using System.Text.Json;
using Velopack;

namespace WheelPro;

public sealed class UpdateService
{
    public const string DefaultUpdateFeed = "https://github.com/michelosimasoft-cloud/Wheel-pro/releases/latest/download/";
    public static string SettingsPath => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WheelPro", "update-source.json");

    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        var source = await ReadSourceAsync(cancellationToken);
        return await new UpdateManager(source).CheckForUpdatesAsync();
    }

    public async Task DownloadAndRestartAsync(UpdateInfo update, CancellationToken cancellationToken = default)
    {
        var source = await ReadSourceAsync(cancellationToken);
        var manager = new UpdateManager(source);
        await manager.DownloadUpdatesAsync(update);
        manager.ApplyUpdatesAndRestart(update);
    }

    private static async Task<string> ReadSourceAsync(CancellationToken cancellationToken)
    {
        if (!System.IO.File.Exists(SettingsPath)) return DefaultUpdateFeed;
        await using var stream = System.IO.File.OpenRead(SettingsPath);
        var configured = (await JsonSerializer.DeserializeAsync<UpdateSettings>(stream, cancellationToken: cancellationToken))?.UpdateSource;
        return string.IsNullOrWhiteSpace(configured) ? DefaultUpdateFeed : configured.Trim();
    }
}

public sealed class UpdateSettings { public string? UpdateSource { get; init; } }
