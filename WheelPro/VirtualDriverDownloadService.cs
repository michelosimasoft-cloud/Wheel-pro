using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace WheelPro;

/// <summary>Downloads the newest official ViGEmBus setup only after the user asks for it.</summary>
public sealed class VirtualDriverDownloadService
{
    private static readonly HttpClient Client = CreateClient();
    private const string LatestReleaseUrl = "https://api.github.com/repos/nefarius/ViGEmBus/releases/latest";

    public async Task<string> DownloadLatestAsync(CancellationToken cancellationToken = default)
    {
        using var releaseResponse = await Client.GetAsync(LatestReleaseUrl, cancellationToken);
        releaseResponse.EnsureSuccessStatusCode();
        using var release = JsonDocument.Parse(await releaseResponse.Content.ReadAsStringAsync(cancellationToken));
        var asset = release.RootElement.GetProperty("assets").EnumerateArray()
            .Select(item => new { Name = item.GetProperty("name").GetString(), Url = item.GetProperty("browser_download_url").GetString() })
            .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(item.Url) &&
                item.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                (item.Name.Contains("ViGEmBus", StringComparison.OrdinalIgnoreCase) || item.Name.Contains("Setup", StringComparison.OrdinalIgnoreCase)));
        if (asset is null) throw new InvalidOperationException("The official release did not contain a Windows setup file.");

        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WheelPro", "downloads", "virtual-controller-driver");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, asset.Name!);
        await using var source = await Client.GetStreamAsync(asset.Url!, cancellationToken);
        await using var output = File.Create(destination);
        await source.CopyToAsync(output, cancellationToken);
        return destination;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WheelPro", "1.3"));
        return client;
    }
}
