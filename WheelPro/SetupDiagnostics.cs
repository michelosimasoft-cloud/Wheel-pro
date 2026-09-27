using Microsoft.Win32;
using System.Management;

namespace WheelPro;

public sealed record SetupDiagnosticSnapshot(
    ConnectedWheel? Wheel,
    bool VirtualControllerDriverInstalled,
    string WheelStatus,
    string VirtualDriverStatus,
    string Recommendation);

public static class SetupDiagnostics
{
    private const string ViGEmServicePath = @"SYSTEM\CurrentControlSet\Services\ViGEmBus";

    public static SetupDiagnosticSnapshot Inspect()
    {
        var wheel = WheelDetector.FindConnectedWheel();
        var virtualDriverInstalled = IsVirtualControllerDriverInstalled();
        var wheelStatus = wheel is null
            ? "No racing wheel is visible to Windows. Connect it directly by USB, switch it to PC mode, then scan again."
            : $"Windows detected: {wheel.Name}";
        var virtualStatus = virtualDriverInstalled
            ? "The ViGEm virtual-controller bus is installed."
            : "The ViGEm virtual-controller bus is not installed. Native wheel mode still works, but Xbox and PlayStation output will remain unavailable.";
        var recommendation = wheel is null
            ? "Install the wheel manufacturer's official Windows driver if it is not listed in joy.cpl, then reconnect it."
            : virtualDriverInstalled
                ? "Core device prerequisites are ready. Continue to Wheel Pro and calibrate steering and pedals."
                : "Install virtual-controller support if the target game does not support the wheel natively.";
        return new SetupDiagnosticSnapshot(wheel, virtualDriverInstalled, wheelStatus, virtualStatus, recommendation);
    }

    public static bool IsVirtualControllerDriverInstalled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(ViGEmServicePath, writable: false);
            if (key is not null) return true;
        }
        catch { }

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DisplayName FROM Win32_SystemDriver WHERE Name LIKE '%ViGEm%' OR DisplayName LIKE '%ViGEm%'");
            return searcher.Get().Count > 0;
        }
        catch { return false; }
    }
}
