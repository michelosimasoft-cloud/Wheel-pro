using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using Nefarius.ViGEm.Client.Targets.DualShock4;

namespace WheelPro;

public enum VirtualControllerType { Xbox360, DualShock4 }

/// <summary>Creates a temporary Xbox 360 or DualShock 4 controller only while Wheel Pro is running.</summary>
public sealed class VirtualControllerBridge : IDisposable
{
    private ViGEmClient? client;
    private IXbox360Controller? xboxController;
    private IDualShock4Controller? dualShockController;
    private bool connected;
    private bool dpadActionMode;
    private bool previousDpadActionToggle;

    public bool IsConnected => connected;
    public bool DpadActionMode => dpadActionMode;
    public VirtualControllerType? ControllerType { get; private set; }

    public void Connect(VirtualControllerType type)
    {
        if (IsConnected && ControllerType == type) return;
        Dispose();
        client = new ViGEmClient();
        if (type == VirtualControllerType.Xbox360)
        {
            xboxController = client.CreateXbox360Controller();
            xboxController.AutoSubmitReport = false;
            xboxController.Connect();
        }
        else
        {
            dualShockController = client.CreateDualShock4Controller();
            dualShockController.AutoSubmitReport = false;
            dualShockController.Connect();
        }
        ControllerType = type;
        connected = true;
    }

    public void Submit(double steering, double accelerator, double brake, uint pov, IReadOnlyDictionary<string, bool> buttons, bool dpadActionToggle, bool returnToNavigation)
    {
        if (!connected) return;
        if (dpadActionToggle && !previousDpadActionToggle) dpadActionMode = !dpadActionMode;
        previousDpadActionToggle = dpadActionToggle;
        if (returnToNavigation) dpadActionMode = false;
        if (ControllerType == VirtualControllerType.DualShock4 && dualShockController is not null)
        {
            SubmitDualShock4(steering, accelerator, brake, pov, buttons);
            return;
        }
        if (xboxController is null) return;

        var (leftX, leftY) = GetNavigationStick(steering, pov, dpadActionMode);
        xboxController.SetAxisValue(Xbox360Axis.LeftThumbX, (short)Math.Clamp(Math.Round(leftX * short.MaxValue), short.MinValue, short.MaxValue));
        xboxController.SetAxisValue(Xbox360Axis.LeftThumbY, (short)Math.Clamp(Math.Round(leftY * short.MaxValue), short.MinValue, short.MaxValue));
        xboxController.SetSliderValue(Xbox360Slider.RightTrigger, (byte)Math.Clamp(Math.Round(accelerator * byte.MaxValue), 0, byte.MaxValue));
        xboxController.SetSliderValue(Xbox360Slider.LeftTrigger, (byte)Math.Clamp(Math.Round(brake * byte.MaxValue), 0, byte.MaxValue));

        Set(Xbox360Button.A, "Cross"); Set(Xbox360Button.B, "Circle");
        Set(Xbox360Button.X, "Square"); Set(Xbox360Button.Y, "Triangle");
        Set(Xbox360Button.LeftShoulder, "GearDown"); Set(Xbox360Button.RightShoulder, "GearUp");
        Set(Xbox360Button.LeftThumb, "L3"); Set(Xbox360Button.RightThumb, "R3");
        Set(Xbox360Button.Back, "Share"); Set(Xbox360Button.Start, "Options");
        // Never forward a physical Home/PS button as Xbox Guide. Windows reserves
        // Guide for Game Bar, so sending it would escape the game session.
        xboxController.SetButtonState(Xbox360Button.Guide, false);
        xboxController.SetButtonState(Xbox360Button.Up, dpadActionMode && PovMatches(pov, 0)); xboxController.SetButtonState(Xbox360Button.Right, dpadActionMode && PovMatches(pov, 9000));
        xboxController.SetButtonState(Xbox360Button.Down, dpadActionMode && PovMatches(pov, 18000)); xboxController.SetButtonState(Xbox360Button.Left, dpadActionMode && PovMatches(pov, 27000));
        xboxController.SubmitReport();

        void Set(Xbox360Button button, string control) => xboxController.SetButtonState(button, buttons.TryGetValue(control, out var pressed) && pressed);
    }

    private void SubmitDualShock4(double steering, double accelerator, double brake, uint pov, IReadOnlyDictionary<string, bool> buttons)
    {
        var controller = dualShockController!;
        var (leftX, leftY) = GetNavigationStick(steering, pov, dpadActionMode);
        controller.SetAxisValue(DualShock4Axis.LeftThumbX, (byte)Math.Clamp(Math.Round((leftX + 1) * 127.5), 0, 255));
        controller.SetAxisValue(DualShock4Axis.LeftThumbY, (byte)Math.Clamp(Math.Round((1 - leftY) * 127.5), 0, 255));
        controller.SetSliderValue(DualShock4Slider.RightTrigger, (byte)Math.Clamp(Math.Round(accelerator * byte.MaxValue), 0, byte.MaxValue));
        controller.SetSliderValue(DualShock4Slider.LeftTrigger, (byte)Math.Clamp(Math.Round(brake * byte.MaxValue), 0, byte.MaxValue));
        Set(DualShock4Button.Cross, "Cross"); Set(DualShock4Button.Circle, "Circle"); Set(DualShock4Button.Square, "Square"); Set(DualShock4Button.Triangle, "Triangle");
        Set(DualShock4Button.ShoulderLeft, "GearDown"); Set(DualShock4Button.ShoulderRight, "GearUp");
        Set(DualShock4Button.ThumbLeft, "L3"); Set(DualShock4Button.ThumbRight, "R3"); Set(DualShock4Button.Share, "Share"); Set(DualShock4Button.Options, "Options");
        controller.SetDPadDirection(!dpadActionMode || pov == 65535 ? DualShock4DPadDirection.None : PovMatches(pov, 0) ? DualShock4DPadDirection.North : PovMatches(pov, 9000) ? DualShock4DPadDirection.East : PovMatches(pov, 18000) ? DualShock4DPadDirection.South : DualShock4DPadDirection.West);
        controller.SubmitReport();
        void Set(DualShock4Button button, string control) => controller.SetButtonState(button, buttons.TryGetValue(control, out var pressed) && pressed);
    }

    private static bool PovMatches(uint pov, uint direction) => pov != 65535 &&
        (pov == direction || (direction == 0 && (pov == 4500 || pov == 31500)) ||
         (direction == 9000 && (pov == 4500 || pov == 13500)) ||
         (direction == 18000 && (pov == 13500 || pov == 22500)) ||
         (direction == 27000 && (pov == 22500 || pov == 31500)));

    private static (double X, double Y) GetNavigationStick(double steering, uint pov, bool dpadActionMode)
    {
        // The D-pad remains a real D-pad in every game. When the wheel is
        // centred, mirror it to the virtual left stick for map/menu screens.
        // Any meaningful wheel turn immediately wins, preventing map movement
        // from interfering with analogue steering while driving.
        if (dpadActionMode || Math.Abs(steering) > .08 || pov == 65535) return (steering, 0);
        var x = PovMatches(pov, 9000) ? 1 : PovMatches(pov, 27000) ? -1 : 0;
        var y = PovMatches(pov, 0) ? 1 : PovMatches(pov, 18000) ? -1 : 0;
        return (x, y);
    }

    public void Dispose()
    {
        if (xboxController is not null)
        {
            if (connected) xboxController.Disconnect();
            xboxController = null;
        }
        if (dualShockController is not null) { if (connected) dualShockController.Disconnect(); dualShockController = null; }
        client?.Dispose();
        client = null;
        connected = false;
        ControllerType = null;
        dpadActionMode = false;
        previousDpadActionToggle = false;
    }
}
