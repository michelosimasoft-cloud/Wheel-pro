[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$CertificateThumbprint = $env:WHEELPRO_SIGNING_THUMBPRINT,
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "WheelPro\WheelPro.csproj"
$icon = Join-Path $repoRoot "WheelPro\Assets\WheelPro.ico"
$artifacts = Join-Path $repoRoot "artifacts"
$publish = Join-Path $artifacts "publish"
$releases = Join-Path $artifacts "releases"
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$vpk = (Get-Command vpk -ErrorAction Stop).Source
$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version

& $dotnet restore $project -r $Runtime --configfile (Join-Path $repoRoot "NuGet.Config")
if ($LASTEXITCODE) { throw "dotnet restore failed with exit code $LASTEXITCODE" }
& $dotnet publish $project -c $Configuration -r $Runtime --self-contained -o $publish --no-restore
if ($LASTEXITCODE) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

function Sign-File([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) { return }
    $signtool = (Get-Command signtool.exe -ErrorAction Stop).Source
    & $signtool sign /sha1 $CertificateThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $Path
    if ($LASTEXITCODE) { throw "Signing failed for $Path" }
}

Sign-File (Join-Path $publish "WheelPro.exe")
New-Item -ItemType Directory -Force -Path $releases | Out-Null
& $vpk pack --packId WheelPro --packVersion $version --packDir $publish --mainExe WheelPro.exe --icon $icon --outputDir $releases
if ($LASTEXITCODE) { throw "Velopack failed with exit code $LASTEXITCODE" }
Get-ChildItem $releases -Filter *.exe | ForEach-Object { Sign-File $_.FullName }
Get-ChildItem $releases -File | Get-FileHash -Algorithm SHA256 | ForEach-Object {
    "{0}  {1}" -f $_.Hash.ToLowerInvariant(), (Split-Path $_.Path -Leaf)
} | Set-Content (Join-Path $releases "SHA256SUMS.txt")

Write-Host "Wheel Pro $version release created in $releases"
