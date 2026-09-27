using Windows.Gaming.Input;

namespace WheelPro;

/// <summary>Uses Windows Gaming Input for HID controllers omitted by the legacy WinMM and DirectInput registries.</summary>
public static class GamingInputWheelInput
{
    public static WheelInputState? FindState(WheelProfile profile)
    {
        try
        {
            var controller = RawGameController.RawGameControllers.FirstOrDefault(candidate => Matches(profile, candidate));
            if (controller is null) return null;
            var buttons = new bool[controller.ButtonCount];
            var switches = new GameControllerSwitchPosition[controller.SwitchCount];
            var axes = new double[controller.AxisCount];
            controller.GetCurrentReading(buttons, switches, axes);
            uint buttonMask = 0;
            for (var index = 0; index < Math.Min(buttons.Length, 32); index++)
                if (buttons[index]) buttonMask |= 1u << index;
            return new WheelInputState(controller.DisplayName, controller.HardwareVendorId, controller.HardwareProductId,
                Axis(axes, 0), Axis(axes, 1), Axis(axes, 2), Axis(axes, 3), Axis(axes, 4), Axis(axes, 5),
                buttonMask, Pov(switches.FirstOrDefault()),
                0, 65535, 0, 65535, 0, 65535, 0, 65535, 0, 65535, 0, 65535);
        }
        catch { return null; }
    }

    private static bool Matches(WheelProfile profile, RawGameController controller)
    {
        var hardwareKey = $"VID_{controller.HardwareVendorId:X4}&PID_{controller.HardwareProductId:X4}";
        return profile.HardwareIds?.Any(id => id.Equals(hardwareKey, StringComparison.OrdinalIgnoreCase)) == true ||
               controller.DisplayName.Contains(profile.Brand, StringComparison.OrdinalIgnoreCase) ||
               profile.Model.Split(new[] { ' ', '/' }, StringSplitOptions.RemoveEmptyEntries)
                   .Any(part => part.Length >= 3 && controller.DisplayName.Contains(part, StringComparison.OrdinalIgnoreCase));
    }

    private static uint Axis(double[] axes, int index) => index < axes.Length
        ? (uint)Math.Clamp(Math.Round(axes[index] * 65535), 0, 65535)
        : 0;

    private static uint Pov(GameControllerSwitchPosition position) => position switch
    {
        GameControllerSwitchPosition.Up => 0,
        GameControllerSwitchPosition.UpRight => 4500,
        GameControllerSwitchPosition.Right => 9000,
        GameControllerSwitchPosition.DownRight => 13500,
        GameControllerSwitchPosition.Down => 18000,
        GameControllerSwitchPosition.DownLeft => 22500,
        GameControllerSwitchPosition.Left => 27000,
        GameControllerSwitchPosition.UpLeft => 31500,
        _ => 65535
    };
}
