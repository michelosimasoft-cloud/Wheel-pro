using WheelPro;

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var hardwareId = @"HID\VID_044F&PID_B697\7&18575677&0&0000";
var profile = WheelCatalog.FindByHardwareId(hardwareId);
Assert(profile is not null, "The T98 hardware ID must resolve to a known profile.");
Assert(profile!.Brand == "Thrustmaster" && profile.Model.Contains("T98", StringComparison.OrdinalIgnoreCase),
    "The T98 hardware ID resolved to the wrong profile.");

var genericWindowsName = new ConnectedWheel("HID-compliant game controller", hardwareId);
Assert(WheelCatalog.Resolve(genericWindowsName) == profile,
    "A generic Windows device name must not override a known hardware ID.");
Assert(WheelCatalog.FindByHardwareId(@"HID\VID_044F&PID_B668\1") == profile,
    "The alternate T98 compatibility PID must resolve to the same profile.");
Assert(WheelCatalog.FindByHardwareId(@"HID\VID_044F&PID_B697\DIFFERENT_USB_PORT_INSTANCE") == profile,
    "A changed USB-port instance path must not change the resolved wheel profile.");

var buttons = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
var axes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
var travel = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
var direction = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
var steering = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
Assert(BuiltInProfiles.ApplyWheelDefaults(profile, buttons, axes, travel, direction, steering),
    "T98 safe defaults were not applied.");
Assert(!axes.ContainsKey("Accelerator") && !axes.ContainsKey("Brake") && travel.Count == 0 && direction.Count == 0,
    "Catalogue defaults must not impersonate a live pedal calibration.");

var rawState = new WheelInputState("HID-compliant game controller", 0x044F, 0xB697,
    32767, 32767, 32767, 0, 0, 0, 0, 65535,
    0, 65535, 0, 65535, 0, 65535, 0, 65535, 0, 65535, 0, 65535);
var monitorText = new CalibrationLearningStore().Observe(profile, rawState, rawState);
Assert(monitorText.Contains("raw axis Y") && monitorText.Contains("raw axis Z") &&
       !monitorText.Contains("accelerator", StringComparison.OrdinalIgnoreCase) &&
       !monitorText.Contains("brake", StringComparison.OrdinalIgnoreCase),
    "Unmapped raw axes must not be presented as physical pedal roles.");

var nfsProfile = BuiltInProfiles.FindGameProfile(@"C:\Program Files\EA Games\Need for Speed Unbound\NeedForSpeedUnbound.exe");
Assert(nfsProfile?.OutputMode == 1, "Need for Speed Unbound must select virtual Xbox output.");
var f1Profile = BuiltInProfiles.FindGameProfile(@"C:\Program Files\EA Games\F1 24\F1_24.exe");
Assert(f1Profile?.OutputMode == 1, "F1 compatibility mode must expose the T98 as an Xbox controller.");
Assert(BuiltInProfiles.FindGameProfile("ForzaHorizon5.exe")?.OutputMode == 1,
    "Forza Horizon 5 must select XInput output.");
Assert(BuiltInProfiles.FindGameProfile("NeedForSpeedHeat.exe")?.OutputMode == 1,
    "Need for Speed Heat must select XInput output.");
Assert(BuiltInProfiles.FindGameProfile("speed.exe")?.OutputMode == 1,
    "Classic Need for Speed executables must select XInput output.");

Assert(Math.Abs(SteeringResponse.DirectLinear(5000, 10000) - .5) < .000001,
    "Half calibrated steering travel must produce exactly half Xbox-stick travel.");
Assert(SteeringResponse.DirectLinear(10000, 10000) == 1 && SteeringResponse.DirectLinear(-10000, 10000) == -1,
    "The calibrated left/right points must produce full Xbox-stick travel.");
Assert(Math.Abs(SteeringResponse.DirectLinear(2500, 10000, 1.2) - .3) < .000001,
    "Sensitivity must remain a linear gain and must not introduce a response curve.");
Assert(Math.Abs(SteeringResponse.ApplyLinearGain(.25, 1.2) - .3) < .000001,
    "Normalized Xbox steering gain must remain directly proportional.");

var mappedButtons = new Dictionary<string, uint> { ["Triangle"] = 4, ["Square"] = 8 };
ButtonMapping.AssignUnique(mappedButtons, "Square", 4);
Assert(!mappedButtons.ContainsKey("Triangle") && mappedButtons["Square"] == 4,
    "One raw HID button must never own both Xbox X and Xbox Y mappings.");
mappedButtons["Triangle"] = 4;
ButtonMapping.RemoveDuplicateBindings(mappedButtons);
Assert(mappedButtons.Count == 0, "Previously saved duplicate button mappings must be invalidated.");

Console.WriteLine("Wheel Pro regression tests passed.");
