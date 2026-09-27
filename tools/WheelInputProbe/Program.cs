using WheelPro;
using Windows.Gaming.Input;
using HidSharp;

var hid = DeviceList.Local.GetHidDevices(0x044F, 0xB697).FirstOrDefault();
if (hid is not null)
    Console.WriteLine($"HID={hid.GetProductName()}|PATH={hid.DevicePath}|REPORT={hid.MaxInputReportLength}");
if (args.Contains("--hid"))
{
    if (hid is null || !hid.TryOpen(out var hidStream)) { Console.WriteLine("HID_OPEN_FAILED"); return; }
    using (hidStream)
    {
        hidStream.ReadTimeout = 120;
        foreach (var phase in new[] { ("RELEASED", 4), ("ACCELERATOR", 7), ("BRAKE", 7) })
        {
            Console.WriteLine($"HID_PHASE={phase.Item1}");
            var min = Enumerable.Repeat(255, hid.MaxInputReportLength).ToArray();
            var max = new int[hid.MaxInputReportLength];
            var end = DateTime.UtcNow.AddSeconds(phase.Item2);
            while (DateTime.UtcNow < end)
            {
                var buffer = new byte[hid.MaxInputReportLength];
                try
                {
                    var count = hidStream.Read(buffer);
                    for (var i = 0; i < count; i++) { min[i] = Math.Min(min[i], buffer[i]); max[i] = Math.Max(max[i], buffer[i]); }
                }
                catch (TimeoutException) { }
            }
            Console.WriteLine("HID_BYTES=" + string.Join("|", min.Select((value, i) => $"{i}:{value}-{max[i]}:d={max[i] - value}")));
        }
    }
    return;
}

foreach (var controller in RawGameController.RawGameControllers)
    Console.WriteLine($"GAMINGINPUT={controller.DisplayName}|VID={controller.HardwareVendorId:X4}|PID={controller.HardwareProductId:X4}|AXES={controller.AxisCount}|BUTTONS={controller.ButtonCount}|SWITCHES={controller.SwitchCount}");

if (args.Contains("--list")) return;

var profile = WheelCatalog.Find("Thrustmaster T98 Ferrari 296 GTB");
var selectedDevice = -1;
var phases = new[] { ("RELEASED", 4), ("ACCELERATOR", 7), ("BRAKE", 7) };

foreach (var (name, seconds) in phases)
{
    Console.WriteLine($"PHASE={name}|SECONDS={seconds}");
    var devices = new Dictionary<int, (WheelInputState First, long[] Minimum, long[] Maximum)>();
    var end = DateTime.UtcNow.AddSeconds(seconds);
    while (DateTime.UtcNow < end)
    {
        var state = WindowsJoystickInput.FindState(profile, ref selectedDevice);
        if (state is not null)
        {
            var deviceId = selectedDevice;
            if (!devices.TryGetValue(deviceId, out var sample))
            {
                sample = (state, Enumerable.Repeat(long.MaxValue, 6).ToArray(), new long[6]);
                devices[deviceId] = sample;
            }
            var values = new long[] { state.X, state.Y, state.Z, state.R, state.U, state.V };
            for (var i = 0; i < values.Length; i++)
            {
                sample.Minimum[i] = Math.Min(sample.Minimum[i], values[i]);
                sample.Maximum[i] = Math.Max(sample.Maximum[i], values[i]);
            }
        }
        Thread.Sleep(10);
    }
    if (devices.Count == 0) { Console.WriteLine("RESULT=NO_DEVICE"); continue; }
    var labels = new[] { "X", "Y", "Z", "R", "U", "V" };
    foreach (var device in devices.OrderBy(item => item.Key))
    {
        var sample = device.Value;
        Console.WriteLine($"DEVICE={sample.First.DeviceName}|MID={sample.First.ManufacturerId:X4}|PID={sample.First.ProductId:X4}|ID={device.Key}");
        Console.WriteLine("RANGES=" + string.Join("|", labels.Select((label, i) => $"{label}:{sample.Minimum[i]}-{sample.Maximum[i]}:delta={sample.Maximum[i] - sample.Minimum[i]}")));
    }
}
