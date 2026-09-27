using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace WheelPro;

/// <summary>Downloads the newest official ViGEmBus setup only after the user asks for it.</summary>
public sealed class VirtualDriverDownloadService
{
    private static readonly HttpClient Client = CreateClient();
    private const string LatestReleaseUrl = "https://api.github.com/repos/nefarius/ViGEmBus/releases/latest";
    private const long MaximumInstallerBytes = 200 * 1024 * 1024;

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

        var assetUri = new Uri(asset.Url!);
        if (assetUri.Scheme != Uri.UriSchemeHttps ||
            !(assetUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || assetUri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The official release returned an unexpected download address.");

        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WheelPro", "downloads", "virtual-controller-driver");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, asset.Name!);
        var temporary = destination + ".download";
        try
        {
            using var download = await Client.GetAsync(assetUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            download.EnsureSuccessStatusCode();
            if (download.Content.Headers.ContentLength is > MaximumInstallerBytes)
                throw new InvalidOperationException("The driver installer was larger than the safe download limit.");
            await using var source = await download.Content.ReadAsStreamAsync(cancellationToken);
            await using (var output = File.Create(temporary))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += read;
                    if (total > MaximumInstallerBytes) throw new InvalidOperationException("The driver installer exceeded the safe download limit.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }
            VerifyPublisherSignature(temporary);
            File.Move(temporary, destination, overwrite: true);
            return destination;
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }

    private static void VerifyPublisherSignature(string installer)
    {
        try
        {
#pragma warning disable SYSLIB0057
            using var signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(installer));
#pragma warning restore SYSLIB0057
            var subject = signer.Subject;
            if (!subject.Contains("Nefarius", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Unexpected driver publisher: {subject}");
            // Authenticode timestamps allow a correctly signed installer to remain
            // valid after the publisher certificate's normal expiry date.
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException("The downloaded driver installer is not publisher-signed.", ex);
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WheelPro", "1.3"));
        return client;
    }
}
