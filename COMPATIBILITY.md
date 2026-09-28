# Wheel Pro compatibility

## Supported systems

- Windows 10 version 2004 (build 19041) or newer, x64
- Windows 11, x64
- Self-contained installer or portable build with bundled .NET 10 and Windows Desktop runtimes (no separate .NET installation required)

GitHub Actions validates every change on clean Windows Server 2022 and Windows Server 2025 runners using the production self-contained deployment mode. The matrix restores dependencies, runs the regression suite, publishes the application, checks the bundled .NET 10 and Windows Desktop runtimes, validates assembly metadata, and confirms that the executable contains the application icon.

## Hardware validation boundary

Automated runners cannot certify physical USB wheels, force feedback, manufacturer drivers, firmware, Steam Input behaviour, or ViGEmBus on every PC. Those features require a physical hardware matrix with each wheel model and driver version. Unknown controllers continue to use the Generic HID profile.
