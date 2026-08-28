# Wheel Pro releases and automatic updates

Wheel Pro uses Velopack for Windows installers, background downloads, and the refresh-to-install prompt.

## Configure the live update feed

Create `%LOCALAPPDATA%\WheelPro\update-source.json` on the installed machine:

```json
{ "UpdateSource": "https://your-domain.example/wheelpro/releases" }
```

The URL must host the Velopack release assets, including `releases.win-x64.json` and the generated `.nupkg` files. A GitHub Releases repository can also be used after its URL is configured through a GitHub-specific update source in a production deployment.

## Publish a new release

1. Increase `<Version>` in `WheelPro/WheelPro.csproj`.
2. Publish self-contained: `dotnet publish WheelPro/WheelPro.csproj -c Release -r win-x64 --self-contained -o artifacts/publish`.
3. Package it with Velopack `vpk` and upload every generated release asset to the update feed.

Do not store profiles or settings inside the installed application directory; updates replace that directory. Wheel Pro stores its device cache under `%LOCALAPPDATA%\WheelPro`.
