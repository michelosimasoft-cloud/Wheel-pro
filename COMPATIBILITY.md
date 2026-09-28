# Wheel Pro compatibility

## Supported systems

- Windows 10 version 2004 (build 19041) or newer, x64
- Windows 11, x64
- Self-contained installer or portable build (no separate .NET installation required)
- Framework-dependent build with the .NET 8 Desktop Runtime

GitHub Actions validates every change on clean Windows Server 2022 and Windows Server 2025 runners, using both self-contained and framework-dependent deployment modes. The matrix restores dependencies, runs the regression suite, publishes the application, checks all required runtime files, validates assembly metadata, and confirms that the executable contains the application icon.

## Hardware validation boundary

Automated runners cannot certify physical USB wheels, force feedback, manufacturer drivers, firmware, Steam Input behaviour, or ViGEmBus on every PC. Those features require a physical hardware matrix with each wheel model and driver version. Unknown controllers continue to use the Generic HID profile.
