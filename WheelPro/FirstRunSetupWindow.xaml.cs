using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace WheelPro;

public partial class FirstRunSetupWindow : Window
{
    private static readonly string MarkerPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WheelPro", "setup-complete.json");
    private readonly VirtualDriverDownloadService driverDownload = new();
    private SetupDiagnosticSnapshot snapshot = SetupDiagnostics.Inspect();

    public FirstRunSetupWindow()
    {
        InitializeComponent();
        RefreshSnapshot();
    }

    public static void ShowIfNeeded(Window owner)
    {
        if (File.Exists(MarkerPath)) return;
        new FirstRunSetupWindow { Owner = owner }.ShowDialog();
    }

    private void RefreshSnapshot()
    {
        snapshot = SetupDiagnostics.Inspect();
        WheelStatus.Text = snapshot.WheelStatus;
        VirtualDriverStatus.Text = snapshot.VirtualDriverStatus;
        Recommendation.Text = snapshot.Recommendation;
        VendorButton.IsEnabled = snapshot.Wheel is not null;
        InstallButton.IsEnabled = !snapshot.VirtualControllerDriverInstalled;
        InstallButton.Content = snapshot.VirtualControllerDriverInstalled ? "Virtual driver installed" : "Install virtual-controller support";
    }

    private void Scan_Click(object sender, RoutedEventArgs e)
    {
        ActionStatus.Text = "Device scan refreshed.";
        RefreshSnapshot();
    }

    private void Vendor_Click(object sender, RoutedEventArgs e)
    {
        if (snapshot.Wheel is null) return;
        var profile = WheelCatalog.Resolve(snapshot.Wheel);
        Process.Start(new ProcessStartInfo(WheelCatalog.GetOfficialSupportUrl(profile.Brand)) { UseShellExecute = true });
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (SetupDiagnostics.IsVirtualControllerDriverInstalled())
        {
            ActionStatus.Text = "The virtual-controller driver is already installed. No download is needed.";
            RefreshSnapshot();
            return;
        }
        try
        {
            InstallButton.IsEnabled = false;
            DownloadProgress.Visibility = Visibility.Visible;
            ActionStatus.Text = "Downloading and verifying the official driver installer...";
            var installer = await driverDownload.DownloadLatestAsync();
            DownloadProgress.Visibility = Visibility.Collapsed;
            var approval = MessageBox.Show(this,
                "The publisher-signed virtual-controller driver is ready. Start its installer now? Windows will request administrator approval.",
                "Install virtual-controller support", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (approval == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true });
                ActionStatus.Text = "Complete the Windows installer, restart if requested, then choose Scan again.";
            }
            else ActionStatus.Text = "The verified installer was downloaded but not started.";
        }
        catch (Exception ex)
        {
            DownloadProgress.Visibility = Visibility.Collapsed;
            ActionStatus.Text = $"Driver setup could not continue: {ex.Message}";
        }
        finally { RefreshSnapshot(); }
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath)!);
        File.WriteAllText(MarkerPath, JsonSerializer.Serialize(new
        {
            completedUtc = DateTime.UtcNow,
            wheelDetected = snapshot.Wheel is not null,
            virtualControllerDriverInstalled = snapshot.VirtualControllerDriverInstalled
        }, new JsonSerializerOptions { WriteIndented = true }));
        Close();
    }
}
