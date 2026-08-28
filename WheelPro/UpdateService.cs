using System.Text.Json;
using Velopack;

namespace WheelPro;

public sealed class UpdateService
{
    public static string SettingsPath => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WheelPro", "update-source.json");

    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        var source = await ReadSourceAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(source)) return null;
        return await new UpdateManager(source).CheckForUpdatesAsync();
    }

    public async Task DownloadAndRestartAsync(UpdateInfo update, CancellationToken cancellationToken = default)
    {
        var source = await ReadSourceAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(source)) return;
        var manager = new UpdateManager(source);
        await manager.DownloadUpdatesAsync(update);
        manager.ApplyUpdatesAndRestart(update);
    }

    private static async Task<string?> ReadSourceAsync(CancellationToken cancellationToken)
    {
        if (!System.IO.File.Exists(SettingsPath)) return null;
        await using var stream = System.IO.File.OpenRead(SettingsPath);
        return (await JsonSerializer.DeserializeAsync<UpdateSettings>(stream, cancellationToken: cancellationToken))?.UpdateSource;
    }
}

public sealed class UpdateSettings { public string? UpdateSource { get; init; } }
