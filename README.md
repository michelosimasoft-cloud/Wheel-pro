# Wheel Pro

Wheel Pro is a self-contained Windows desktop application for detecting racing wheels, checking live inputs, calibrating pedals and steering, saving wheel presets, and exposing a virtual Xbox or PlayStation controller only while a selected game is running.

## Download

Download `WheelPro-win-Setup.exe` from the latest GitHub Release. The setup includes the application and its .NET runtime.

The virtual-controller driver is downloaded from its official release only when requested in the application, then Windows asks for permission before installation. Vendor wheel drivers remain owned and signed by their respective manufacturers.

## Build from source

Requires the .NET 8 SDK on Windows:

```powershell
dotnet build WheelPro\WheelPro.csproj -c Release
dotnet run --project WheelPro\WheelPro.csproj
```

## Supported platform

Wheel Pro is currently a Windows WPF application. It supports known wheel profiles and a Generic HID profile for unlisted Windows game controllers. Game output can be native wheel/HID, virtual Xbox, or virtual PlayStation controller mode.

The distributed `win-x64` build is self-contained, so target PCs do not need to install .NET separately. Physical wheel detection, vendor-driver behaviour, force feedback, and virtual-controller output still require validation on each supported wheel/driver combination; CI covers compilation, profile/mapping regression tests, and the completeness of the self-contained payload.

## Validation

```powershell
dotnet run --project tests\WheelPro.RegressionTests\WheelPro.RegressionTests.csproj -c Release
dotnet publish WheelPro\WheelPro.csproj -c Release -r win-x64 --self-contained -o artifacts\publish
```

GitHub Actions repeats these checks on a clean Windows runner for every pull request and push to `main`.
