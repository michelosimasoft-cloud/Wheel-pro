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

    public bool IsConnected => connected;
    public VirtualControllerType? ControllerType { get; private set; }

    public void Connect(VirtualControllerType type)
    {
        if (IsConnected && ControllerType == type) return;
        Dispose();
        client = new ViGEmClient();
        if (type == VirtualControllerType.Xbox360)
        {
            xboxController = client.CreateXbox360Controller();
            xboxController.Connect();
        }
        else
        {
            dualShockController = client.CreateDualShock4Controller();
            dualShockController.Connect();
        }
        ControllerType = type;
        connected = true;
    }

    public void Submit(double steering, double accelerator, double brake, uint pov, IReadOnlyDictionary<string, bool> buttons)
    {
        if (!connected) return;
        if (ControllerType == VirtualControllerType.DualShock4 && dualShockController is not null)
        {
            SubmitDualShock4(steering, accelerator, brake, pov, buttons);
            return;
        }
        if (xboxController is null) return;

        xboxController.SetAxisValue(Xbox360Axis.LeftThumbX, (short)Math.Clamp(Math.Round(steering * short.MaxValue), short.MinValue, short.MaxValue));
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
        xboxController.SetButtonState(Xbox360Button.Up, PovMatches(pov, 0)); xboxController.SetButtonState(Xbox360Button.Right, PovMatches(pov, 9000));
        xboxController.SetButtonState(Xbox360Button.Down, PovMatches(pov, 18000)); xboxController.SetButtonState(Xbox360Button.Left, PovMatches(pov, 27000));

        void Set(Xbox360Button button, string control) => xboxController.SetButtonState(button, buttons.TryGetValue(control, out var pressed) && pressed);
    }

    private void SubmitDualShock4(double steering, double accelerator, double brake, uint pov, IReadOnlyDictionary<string, bool> buttons)
    {
        var controller = dualShockController!;
        controller.SetAxisValue(DualShock4Axis.LeftThumbX, (byte)Math.Clamp(Math.Round((steering + 1) * 127.5), 0, 255));
        controller.SetSliderValue(DualShock4Slider.RightTrigger, (byte)Math.Clamp(Math.Round(accelerator * byte.MaxValue), 0, byte.MaxValue));
        controller.SetSliderValue(DualShock4Slider.LeftTrigger, (byte)Math.Clamp(Math.Round(brake * byte.MaxValue), 0, byte.MaxValue));
        Set(DualShock4Button.Cross, "Cross"); Set(DualShock4Button.Circle, "Circle"); Set(DualShock4Button.Square, "Square"); Set(DualShock4Button.Triangle, "Triangle");
        Set(DualShock4Button.ShoulderLeft, "GearDown"); Set(DualShock4Button.ShoulderRight, "GearUp");
        Set(DualShock4Button.ThumbLeft, "L3"); Set(DualShock4Button.ThumbRight, "R3"); Set(DualShock4Button.Share, "Share"); Set(DualShock4Button.Options, "Options");
        controller.SetDPadDirection(pov == 65535 ? DualShock4DPadDirection.None : PovMatches(pov, 0) ? DualShock4DPadDirection.North : PovMatches(pov, 9000) ? DualShock4DPadDirection.East : PovMatches(pov, 18000) ? DualShock4DPadDirection.South : DualShock4DPadDirection.West);
        void Set(DualShock4Button button, string control) => controller.SetButtonState(button, buttons.TryGetValue(control, out var pressed) && pressed);
    }

    private static bool PovMatches(uint pov, uint direction) => pov != 65535 &&
        (pov == direction || (direction == 0 && (pov == 4500 || pov == 31500)) ||
         (direction == 9000 && (pov == 4500 || pov == 13500)) ||
         (direction == 18000 && (pov == 13500 || pov == 22500)) ||
         (direction == 27000 && (pov == 22500 || pov == 31500)));

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
    }
}
