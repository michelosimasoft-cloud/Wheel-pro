[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PublishDirectory,
    [ValidateSet("self-contained", "framework-dependent")]
    [string]$Deployment = "self-contained"
)

$ErrorActionPreference = "Stop"
$publish = (Resolve-Path $PublishDirectory).Path
$selfContained = $Deployment -eq "self-contained"
$required = @("WheelPro.exe", "WheelPro.dll", "WheelPro.deps.json", "WheelPro.runtimeconfig.json")
if ($selfContained) { $required += @("coreclr.dll", "hostfxr.dll", "hostpolicy.dll") }

foreach ($file in $required) {
    if (-not (Test-Path (Join-Path $publish $file))) {
        throw "Missing distribution file: $file"
    }
}

if (-not $selfContained -and (Test-Path (Join-Path $publish "coreclr.dll"))) {
    throw "Framework-dependent payload unexpectedly contains the .NET runtime."
}

$assembly = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $publish "WheelPro.dll"))
if ($assembly.Version.Major -ne 1 -or $assembly.Version.Minor -ne 5) {
    throw "Unexpected Wheel Pro assembly version: $($assembly.Version)"
}

Add-Type -AssemblyName System.Drawing
$icon = [Drawing.Icon]::ExtractAssociatedIcon((Join-Path $publish "WheelPro.exe"))
if ($null -eq $icon) { throw "WheelPro.exe does not contain an application icon." }
try {
    if ($icon.Width -lt 16 -or $icon.Height -lt 16) { throw "WheelPro.exe icon is invalid." }
} finally {
    $icon.Dispose()
}

Write-Host "Validated $($assembly.Name) $($assembly.Version) on $([Environment]::OSVersion.VersionString); deployment=$Deployment"
