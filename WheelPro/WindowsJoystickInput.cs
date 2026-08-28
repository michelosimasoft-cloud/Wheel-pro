using System.Runtime.InteropServices;

namespace WheelPro;

public sealed record WheelInputState(
    string DeviceName, uint X, uint Y, uint Z, uint R, uint U, uint V, uint Buttons, uint Pov,
    uint XMin, uint XMax, uint YMin, uint YMax, uint ZMin, uint ZMax, uint RMin, uint RMax,
    uint UMin, uint UMax, uint VMin, uint VMax)
{
    public string PressedButtons => Buttons == 0
        ? "none"
        : string.Join(", ", Enumerable.Range(0, 32).Where(bit => (Buttons & (1u << bit)) != 0).Select(bit => $"{bit + 1}"));
}

public static class WindowsJoystickInput
{
    private const uint JoyReturnAll = 0x000000FF;

    [DllImport("winmm.dll")]
    private static extern uint joyGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern uint joyGetDevCaps(uint deviceId, ref JoyCaps caps, uint capsSize);

    [DllImport("winmm.dll")]
    private static extern uint joyGetPosEx(uint deviceId, ref JoyInfoEx info);

    public static WheelInputState? FindState(WheelProfile profile, ref int deviceId)
    {
        if (deviceId >= 0 && TryRead(deviceId, out var savedState, out _)) return savedState;

        var deviceCount = Math.Min(joyGetNumDevs(), 16u);
        WheelInputState? fallback = null;
        var fallbackId = -1;
        for (uint id = 0; id < deviceCount; id++)
        {
            if (!TryRead((int)id, out var state, out var name)) continue;
            if (name.Contains(profile.Brand, StringComparison.OrdinalIgnoreCase) ||
                profile.Model.Split(new[] { ' ', '/' }, StringSplitOptions.RemoveEmptyEntries).Any(part => part.Length >= 3 && name.Contains(part, StringComparison.OrdinalIgnoreCase)))
            {
                deviceId = (int)id;
                return state;
            }
            fallback ??= state;
            fallbackId = (int)id;
        }
        deviceId = fallbackId;
        return fallback;
    }

    private static bool TryRead(int deviceId, out WheelInputState state, out string deviceName)
    {
        var caps = new JoyCaps();
        if (joyGetDevCaps((uint)deviceId, ref caps, (uint)Marshal.SizeOf<JoyCaps>()) != 0)
        {
            state = null!; deviceName = string.Empty; return false;
        }
        var info = new JoyInfoEx { Size = (uint)Marshal.SizeOf<JoyInfoEx>(), Flags = JoyReturnAll };
        if (joyGetPosEx((uint)deviceId, ref info) != 0)
        {
            state = null!; deviceName = string.Empty; return false;
        }
        deviceName = caps.Name?.Trim() ?? $"Controller {deviceId + 1}";
        state = new WheelInputState(deviceName, info.X, info.Y, info.Z, info.R, info.U, info.V, info.Buttons, info.Pov,
            caps.XMin, caps.XMax, caps.YMin, caps.YMax, caps.ZMin, caps.ZMax, caps.RMin, caps.RMax,
            caps.UMin, caps.UMax, caps.VMin, caps.VMax);
        return true;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct JoyCaps
    {
        // Exact JOYCAPS ordering from mmsystem.h. The device name comes before
        // the axis ranges; placing it after them corrupts every min/max value.
        public ushort ManufacturerId, ProductId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        public uint XMin, XMax, YMin, YMax, ZMin, ZMax, RMin, RMax, UMin, UMax, VMin, VMax, NumButtons, PeriodMin, PeriodMax;
        public uint Caps, MaxAxes, NumAxes, MaxButtons;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string RegKey;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string OemVxd;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JoyInfoEx
    {
        public uint Size, Flags, X, Y, Z, R, U, V, Buttons, ButtonNumber, Pov, Reserved1, Reserved2;
    }
}
