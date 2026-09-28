# Wheel Pro releases and automatic updates

Wheel Pro uses Velopack for Windows installers, background downloads, and the refresh-to-install prompt.

## Automatic updates

Installed builds check the public Wheel Pro GitHub Release feed when the main window opens. When a newer version exists, the user is notified and chooses whether to download, install, and restart. Updates are never installed silently.

The built-in feed is `https://github.com/michelosimasoft-cloud/Wheel-pro/releases/latest/download/`. To use a mirror or test feed, create `%LOCALAPPDATA%\WheelPro\update-source.json` on the installed machine:

```json
{ "UpdateSource": "https://your-domain.example/wheelpro/releases" }
```

The URL must host the Velopack release assets, including `releases.win.json` and the generated `.nupkg` files.

## Publish a new release

1. Increase `<Version>` in `WheelPro/WheelPro.csproj`.
2. Install the matching Velopack CLI once: `dotnet tool install -g vpk --version 1.2.0`.
3. Run `./scripts/build-release.ps1`. The script restores from the repository config, publishes self-contained, packages with Velopack, and writes SHA-256 checksums.
4. Upload every generated release asset to the update feed.

For trusted Windows distribution, install the organisation's Authenticode certificate in the build account and set `WHEELPRO_SIGNING_THUMBPRINT` before running the script. The application and generated installer are then signed and RFC-3161 timestamped. Never commit a PFX file or certificate password. The GitHub workflow intentionally produces unsigned review artifacts until a protected signing service or certificate secret is configured.

The first-run wizard checks for a visible wheel and ViGEmBus. Manufacturer drivers remain separate because they are vendor-owned and may require their own licence, administrator prompt, or restart.

Do not store profiles or settings inside the installed application directory; updates replace that directory. Wheel Pro stores its device cache under `%LOCALAPPDATA%\WheelPro`.
