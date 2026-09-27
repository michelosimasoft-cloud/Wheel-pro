using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.Win32;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace WheelPro;

public partial class MainWindow : Window
{
    private readonly UpdateService updateService = new();
    private readonly VirtualControllerBridge virtualControllerBridge = new();
    private readonly VirtualDriverDownloadService virtualDriverDownload = new();
    private readonly GameMappingIntelligenceService gameMappingIntelligence = new();
    private readonly GameWheelProfileStore gameWheelProfileStore = new();
    private readonly CalibrationLearningStore calibrationLearningStore = new();
    private CalibrationLearningStore backgroundCalibrationLearningStore = new();
    private readonly DispatcherTimer backgroundMonitorTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private DateTime nextBackgroundWheelScanUtc;
    private int backgroundJoystickDeviceId = -1;
    private WheelInputState? backgroundInputBaseline;
    private WheelProfile? backgroundProfile;
    private string? automaticallyDetectedGame;
    private string? selectedGameExecutable;
    private bool gameSessionWasActive;
    private static readonly HttpClient SearchClient = new();
    private CancellationTokenSource? searchCancellation;
    private ConnectedWheel? scannedWheel;
    private readonly DispatcherTimer inputTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private int joystickDeviceId = -1;
    private WheelInputState? inputBaseline;
    private DateTime inputReadyAtUtc;
    private readonly List<UIElement> controlMapDecorations = new();
    private System.Windows.Shapes.Rectangle? dpadUpGraphic, dpadDownGraphic, dpadLeftGraphic, dpadRightGraphic;
    private System.Windows.Shapes.Shape? l2Graphic, r2Graphic, l3Graphic, r3Graphic, shareGraphic, consoleGraphic, optionsGraphic, modeGraphic, sensitivityGraphic;
    private TextBlock? brakeTravelText, acceleratorTravelText, clutchTravelText;
    private System.Windows.Shapes.Shape? l1Graphic, r1Graphic;
    private readonly Dictionary<string, uint> learnedButtonMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> learnedAxisMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> learnedPedalTravel = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> learnedPedalDirection = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, uint> learnedPedalNeutral = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> learnedSteeringTravel = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> pendingSteeringTravel = new(StringComparer.OrdinalIgnoreCase);
    private uint? learnedSteeringCenter;
    private uint? pendingSteeringCenter;
    private bool loadingGameSessionSettings;
    private readonly Dictionary<string, DateTime> buttonActivity = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<InputMappingStep> mappingSteps = new();
    private Button? mapPedalsButton, mapSteeringButton, mapButtonsButton;
    private Button? steamInputButton;
    private Button? exclusiveInputButton;
    private Button? playStationDriverButton;
    private TextBlock? steamInputStatus;
    private Button? backToLibraryButton;
    private Button? resetInputsButton, presetsButton;
    private WrapPanel? mappingActionsPanel;
    private Border? presetsOverlay;
    private Border? mappingOverlay;
    private TextBlock? mappingStepText, mappingProgressText;
    private WheelInputState? mappingBaseline;
    private uint mappingPreviousButtons;
    private bool mappingAwaitingButtonRelease;
    private int mappingStepIndex = -1;
    private string? mappingAxisCandidate;
    private long mappingAxisMaximum;
    private long mappingAxisPeakValue;
    private PedalMappingStage pedalMappingStage;
    private SteeringMappingStage steeringMappingStage;
    private WheelProfile selectedProfile = WheelCatalog.Find("Thrustmaster T98 Ferrari 296 GTB");
    public ObservableCollection<SavedWheel> SavedWheels { get; } = new();
    public ObservableCollection<SavedWheel> DisplayWheels { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        OutputMode.Items.Add(new ComboBoxItem { Content = "PlayStation controller — Cross selects, Circle goes back" });
        RefreshVirtualDriverStatus();
        InstallScrollableStudioLayout();
        AddSteamInputControls();
        AddPlatformDriverControls();
        AddExclusiveInputControl();
        RefreshVirtualDriverStatus();
        DataContext = this;
        LoadGameSessionSettings();
        AddCalibrationDescriptions();
        SavedWheelList.ItemsSource = DisplayWheels;
        SavedWheelList.SelectionChanged += SavedWheelList_SelectionChanged;
        LoadSavedWheels();
        Loaded += async (_, _) =>
        {
            await CheckForUpdatesAsync();
            StartAutomaticMonitoring();
            FirstRunSetupWindow.ShowIfNeeded(this);
            OpenConnectedWheelAutomatically();
        };
        inputTimer.Tick += (_, _) => PollWheelInput();
        Closed += (_, _) => { inputTimer.Stop(); backgroundMonitorTimer.Stop(); calibrationLearningStore.Save(selectedProfile); if (backgroundProfile is not null) backgroundCalibrationLearningStore.Save(backgroundProfile); virtualControllerBridge.Dispose(); };
    }

    private void InstallScrollableStudioLayout()
    {
        // The settings column can grow as features are added. Keep it inside its
        // own scroll viewer so a smaller display never hides calibration or preset controls.
        var studioContent = StudioView.Children.OfType<Grid>().FirstOrDefault();
        var settings = studioContent?.Children.OfType<StackPanel>().FirstOrDefault(panel => Grid.GetColumn(panel) == 0);
        if (studioContent is null || settings is null) return;
        studioContent.Children.Remove(settings);
        var scroll = new ScrollViewer
        {
            Content = settings,
            Margin = new Thickness(0, 0, 16, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetColumn(scroll, 0);
        studioContent.Children.Add(scroll);

        mappingActionsPanel = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var actions = new Border
        {
            Background = System.Windows.Media.Brushes.White,
            BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E0E6F0")),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 14),
            Child = new StackPanel { Children = { new TextBlock { Text = "MAPPING AND NAVIGATION", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#6D788C")) }, mappingActionsPanel } }
        };
        settings.Children.Insert(0, actions);
    }

    private void StartAutomaticMonitoring()
    {
        backgroundMonitorTimer.Tick += (_, _) => MonitorAutomatically();
        backgroundMonitorTimer.Start();
        SearchStatus.Text = "Automatic wheel calibration is running in the background.";
    }

    private void OpenConnectedWheelAutomatically()
    {
        var detected = WheelDetector.FindConnectedWheel();
        if (detected is null) return;
        var profile = WheelCatalog.Resolve(detected);
        ControllerSearch.Text = $"{profile.Brand} {profile.Model}";
        OutputMode.SelectedIndex = 1;
        OpenStudio_Click(this, new RoutedEventArgs());
    }

    private void MonitorAutomatically()
    {
        if (inputTimer.IsEnabled) return; // the live bridge has the higher-frequency monitor
        if (DateTime.UtcNow >= nextBackgroundWheelScanUtc)
        {
            nextBackgroundWheelScanUtc = DateTime.UtcNow.AddSeconds(3);
            var detected = WheelDetector.FindConnectedWheel();
            if (detected is not null)
            {
                var profile = WheelCatalog.Resolve(detected);
                if (backgroundProfile is null || !string.Equals($"{backgroundProfile.Brand} {backgroundProfile.Model}", $"{profile.Brand} {profile.Model}", StringComparison.OrdinalIgnoreCase))
                {
                    if (backgroundProfile is not null) backgroundCalibrationLearningStore.Save(backgroundProfile);
                    backgroundProfile = profile;
                    backgroundCalibrationLearningStore = new CalibrationLearningStore();
                    backgroundInputBaseline = null;
                    backgroundJoystickDeviceId = -1;
                    SearchStatus.Text = $"Detected {profile.Brand} {profile.Model}. Automatic local calibration is active.";
                }
            }
            DetectGameAutomatically();
        }

        if (backgroundProfile is null) return;
        var state = WindowsJoystickInput.FindState(backgroundProfile, ref backgroundJoystickDeviceId);
        if (state is null) return;
        if (backgroundInputBaseline is null || IsTransientZeroBaseline(backgroundInputBaseline, state))
        {
            backgroundInputBaseline = state;
            return;
        }
        SearchStatus.Text = backgroundCalibrationLearningStore.Observe(backgroundProfile, state, backgroundInputBaseline);
    }

    private void DetectGameAutomatically()
    {
        var game = RunningGameDetector.FindActiveGame();
        if (game is null) return;
        selectedGameExecutable = game.Executable;
        gameSessionWasActive = true;
        if (GamePlatform is not null) GamePlatform.SelectedIndex = game.PlatformIndex;
        ApplyGameProfileAutomatically();
        UpdateGameSessionStatus();
        SaveGameSessionSettings();
        if (string.Equals(automaticallyDetectedGame, game.Executable, StringComparison.OrdinalIgnoreCase)) return;
        automaticallyDetectedGame = game.Executable;
        if (gameMappingIntelligence.IsConfigured) _ = RefreshOnlineMappingAsync();
        else GameIntelligenceStatus.Text = $"{game.DisplayName} detected automatically. Universal wheel output is active without a game lock.";
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var update = await updateService.CheckForUpdateAsync();
            if (update is null) return;
            var choice = MessageBox.Show(
                "A new Wheel Pro update is ready. Click Yes to refresh and install it now.",
                "Update ready", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (choice == MessageBoxResult.Yes)
                await updateService.DownloadAndRestartAsync(update);
        }
        catch
        {
            // Development launches and offline use intentionally continue without update checks.
        }
    }

    private void OpenStudio_Click(object sender, RoutedEventArgs e)
    {
        selectedProfile = WheelCatalog.Find(ControllerSearch.Text.Trim());
        var detectedWheel = WheelDetector.FindMatchingWheel(selectedProfile);
        if (detectedWheel is null)
        {
            SearchStatus.Text = $"{selectedProfile.Brand} {selectedProfile.Model} is selected. Connect this wheel in PC mode to open its live controls.";
            return;
        }
        ControllerTitle.Text = $"{selectedProfile.Brand} {selectedProfile.Model}";
        ControllerDetails.Text = $"{selectedProfile.Rotation} rotation - {selectedProfile.Brand} catalogue profile";
        ForceFeedbackStatus.Text = selectedProfile.HasForceFeedback
            ? "Force feedback supported. Enable it only after the vendor driver and native bridge are installed."
            : "Force feedback is not available on this wheel. Input mappings and calibration remain fully supported in non-FFB games.";
        DriverStatus.Text = selectedProfile.RequiresVendorDriver
            ? "Vendor driver/control software is required for full wheel and force-feedback support."
            : "Uses the Windows HID driver; no vendor download is required.";
        var exclusiveStatus = ExclusiveInputIntegration.EnableFor(detectedWheel);
        DriverStatus.Text = $"{DriverStatus.Text} {exclusiveStatus}";
        UpdateCompatibilityStatus();
        UpdateGameSessionStatus();
        UpdatePhysicalInputGroups();
        SearchStatus.Text = selectedProfile.Brand == "Generic HID"
            ? "Unlisted wheel opened with a generic HID profile. Connect it to map its real inputs."
            : $"Loaded {selectedProfile.Brand} catalogue profile.";
        SearchView.Visibility = Visibility.Collapsed;
        StudioView.Visibility = Visibility.Visible;
        StudioStatus.Text = $"USB wheel detected: {detectedWheel.Name}";
        joystickDeviceId = -1;
        inputBaseline = null;
        inputReadyAtUtc = DateTime.UtcNow.AddMilliseconds(900);
        LoadInputMap();
        // The T98 is not on the native PC-wheel lists for the requested Forza
        // and EA titles. Arm XInput before launch so those games see a standard
        // Xbox controller during their startup device scan.
        if (selectedProfile.Model.Contains("T98", StringComparison.OrdinalIgnoreCase) &&
            BuiltInProfiles.FindGameProfile(selectedGameExecutable ?? string.Empty)?.OutputMode is not 0)
            OutputMode.SelectedIndex = 1;
        ApplyGameProfileAutomatically();
        EnsureMapInputsButton();
        var needsPedalCalibration = !HasConfirmedPedalCalibration();
        if (needsPedalCalibration)
            StudioStatus.Text = "Pedal calibration is required before gameplay: choose Map pedals and confirm released 0% and full 100% travel.";
        inputTimer.Start();
        if (needsPedalCalibration) _ = BeginFirstPedalCalibrationAsync();
    }

    private async Task BeginFirstPedalCalibrationAsync()
    {
        await Task.Delay(1100);
        if (StudioView.Visibility != Visibility.Visible || HasConfirmedPedalCalibration() || mappingStepIndex >= 0) return;
        StartInputMapping(MappingSection.Pedals);
        StudioStatus.Text = "First-time pedal setup started. Follow the accelerator and brake prompts; Wheel Pro will learn their actual axes on this PC.";
    }

    private void UpdatePhysicalInputGroups()
    {
        // The wheel face rotates independently; labels and pedal meters remain steady around it.
        WheelGraphic.Width = 620;
        WheelGraphic.Height = 360;
        WheelGraphic.RenderTransform = System.Windows.Media.Transform.Identity;
        BrakePedalGraphic.Width = 66;
        AcceleratorPedalGraphic.Width = 66;
        Canvas.SetLeft(ClutchPedalGraphic, 202);
        Canvas.SetLeft(BrakePedalGraphic, 277);
        Canvas.SetLeft(AcceleratorPedalGraphic, 352);
        // These are the prominent live pedal-travel indicators below the wheel.
        // Keep them visible independently of the compact percentage meters.
        ClutchPedalGraphic.Visibility = selectedProfile.PedalCount >= 3 ? Visibility.Visible : Visibility.Collapsed;
        BrakePedalGraphic.Visibility = selectedProfile.PedalCount >= 2 ? Visibility.Visible : Visibility.Collapsed;
        AcceleratorPedalGraphic.Visibility = Visibility.Visible;
        BuildControlMapDecorations();
        PedalGroupControls.Text = selectedProfile.PedalCount switch
        {
            3 => "Wheel axis · Accelerator · Brake · Clutch",
            2 => "Wheel axis · Accelerator · Brake",
            1 => "Wheel axis · Accelerator / brake pedal",
            _ => "Wheel axis · Pedal inputs detected during setup"
        };
        HShifterGroup.Visibility = selectedProfile.HasHShifter ? Visibility.Visible : Visibility.Collapsed;
        HShifterGraphic.Visibility = selectedProfile.HasHShifter ? Visibility.Visible : Visibility.Collapsed;
        var repeatedInfoPanel = ((PedalGroupControls.Parent as StackPanel)?.Parent as Border)?.Parent as StackPanel;
        if (repeatedInfoPanel is not null) repeatedInfoPanel.Visibility = Visibility.Collapsed;
        ButtonGroupControls.Text = selectedProfile.ButtonCount > 0
            ? $"Buttons 1–{selectedProfile.ButtonCount} · D-pad · Menu controls"
            : "Pedal-only profile — no wheel buttons supplied.";
    }

    private void BuildControlMapDecorations()
    {
        if (WheelGraphic.Parent is not Canvas mapCanvas) return;
        foreach (var decoration in controlMapDecorations) mapCanvas.Children.Remove(decoration);
        controlMapDecorations.Clear();
        brakeTravelText = acceleratorTravelText = clutchTravelText = null;
        WheelGraphic.Children.Clear();
        var accent = BrandAccent(selectedProfile.Brand);
        var accentBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(accent));

        var body = new Canvas { Width = 330, Height = 290, RenderTransformOrigin = new Point(.5, .5) };
        WheelRotation = new System.Windows.Media.RotateTransform();
        var bodyTransforms = new System.Windows.Media.TransformGroup();
        bodyTransforms.Children.Add(new System.Windows.Media.ScaleTransform(.78, .78));
        bodyTransforms.Children.Add(WheelRotation);
        body.RenderTransform = bodyTransforms;
        Canvas.SetLeft(body, 145);
        Canvas.SetTop(body, 10);
        WheelGraphic.Children.Add(body);

        // T98 silhouette: a compact, dark GT rim with its wheel-face controls in
        // the same left/right and lower-centre positions as the physical wheel.
        var rim = new System.Windows.Shapes.Ellipse
        {
            Width = 310, Height = 270, Stroke = System.Windows.Media.Brushes.Black, StrokeThickness = 22,
            Fill = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#242B36"))
        };
        body.Children.Add(rim); Canvas.SetLeft(rim, 10); Canvas.SetTop(rim, 10);
        var innerRim = new System.Windows.Shapes.Ellipse { Width = 278, Height = 238, Stroke = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#667085")), StrokeThickness = 3 };
        body.Children.Add(innerRim); Canvas.SetLeft(innerRim, 26); Canvas.SetTop(innerRim, 26);
        // Grip texture and carbon-style centre accents make the live replica
        // easier to read than a plain generic steering-wheel outline.
        for (var grip = 0; grip < 7; grip++)
        {
            var leftGrip = new System.Windows.Shapes.Ellipse { Width = 5, Height = 5, Fill = System.Windows.Media.Brushes.DimGray };
            var rightGrip = new System.Windows.Shapes.Ellipse { Width = 5, Height = 5, Fill = System.Windows.Media.Brushes.DimGray };
            body.Children.Add(leftGrip); body.Children.Add(rightGrip);
            Canvas.SetLeft(leftGrip, 29 + (grip * 3)); Canvas.SetTop(leftGrip, 76 + (grip * 20));
            Canvas.SetLeft(rightGrip, 296 - (grip * 3)); Canvas.SetTop(rightGrip, 76 + (grip * 20));
        }
        var topPlate = new System.Windows.Shapes.Rectangle { Width = 120, Height = 22, RadiusX = 8, RadiusY = 8, Fill = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#303B4D")) };
        body.Children.Add(topPlate); Canvas.SetLeft(topPlate, 105); Canvas.SetTop(topPlate, 18);
        for (var led = 0; led < 9; led++)
        {
            var ledDot = new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, Fill = led < 5 ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.IndianRed };
            body.Children.Add(ledDot); Canvas.SetLeft(ledDot, 116 + (led * 12)); Canvas.SetTop(ledDot, 26);
        }
        var hub = new System.Windows.Shapes.Ellipse { Width = 112, Height = 112, Fill = accentBrush };
        body.Children.Add(hub); Canvas.SetLeft(hub, 109); Canvas.SetTop(hub, 90);
        var hubRing = new System.Windows.Shapes.Ellipse { Width = 122, Height = 122, Stroke = System.Windows.Media.Brushes.Black, StrokeThickness = 5 };
        body.Children.Add(hubRing); Canvas.SetLeft(hubRing, 104); Canvas.SetTop(hubRing, 85);
        AddBodyLabel(body, selectedProfile.Brand.ToUpperInvariant(), 121, 132, 10, System.Windows.Media.Brushes.Black);
        AddSpoke(body, 154, 28, 22, 75); AddSpoke(body, 45, 148, 95, 20); AddSpoke(body, 190, 148, 95, 20);
        var lowerPlate = new System.Windows.Shapes.Rectangle { Width = 190, Height = 42, RadiusX = 18, RadiusY = 18, Fill = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#111827")) };
        body.Children.Add(lowerPlate); Canvas.SetLeft(lowerPlate, 70); Canvas.SetTop(lowerPlate, 214);
        var carbonStrip = new System.Windows.Shapes.Rectangle { Width = 42, Height = 55, RadiusX = 6, RadiusY = 6, Fill = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#364152")) };
        body.Children.Add(carbonStrip); Canvas.SetLeft(carbonStrip, 144); Canvas.SetTop(carbonStrip, 225);

        var leftPaddle = new System.Windows.Shapes.Rectangle { Width = 36, Height = 56, RadiusX = 12, RadiusY = 12, Fill = accentBrush };
        body.Children.Add(leftPaddle); Canvas.SetLeft(leftPaddle, 16); Canvas.SetTop(leftPaddle, 78);
        var rightPaddle = new System.Windows.Shapes.Rectangle { Width = 36, Height = 56, RadiusX = 12, RadiusY = 12, Fill = accentBrush };
        body.Children.Add(rightPaddle); Canvas.SetLeft(rightPaddle, 278); Canvas.SetTop(rightPaddle, 78);

        // The physical T98 has four individual D-pad directions, not an outer
        // square button. Keep an invisible state holder for the POV update path.
        DpadGraphic = new System.Windows.Shapes.Rectangle { Width = 0, Height = 0, Opacity = 0 };
        var layout = GetControllerVisualLayout();
        var isPlayStationLayout = layout == ControllerVisualLayout.PlayStation;
        var isXboxLayout = layout == ControllerVisualLayout.Xbox;
        var symbols = isPlayStationLayout ? new[] { "△", "○", "×", "□" } : isXboxLayout ? new[] { "Y", "B", "A", "X" } : new[] { "1", "2", "3", "4" };
        WheelButtonY = AddFaceButton(body, symbols[0], 222, 80); WheelButtonB = AddFaceButton(body, symbols[1], 250, 108); WheelButtonA = AddFaceButton(body, symbols[2], 222, 136); WheelButtonX = AddFaceButton(body, symbols[3], 194, 108);

        dpadUpGraphic = AddDpadSegment(body, 76, 105); dpadLeftGraphic = AddDpadSegment(body, 55, 126); dpadRightGraphic = AddDpadSegment(body, 97, 126); dpadDownGraphic = AddDpadSegment(body, 76, 147);
        l1Graphic = AddShoulderControl(body, "L1", 25, 62); r1Graphic = AddShoulderControl(body, "R1", 277, 62);
        l2Graphic = AddPaddleControl(body, "L2", 16, 78, accent); r2Graphic = AddPaddleControl(body, "R2", 278, 78, accent);
        l3Graphic = AddMiniControl(body, "L3", 68, 194); r3Graphic = AddMiniControl(body, "R3", 236, 194);
        modeGraphic = AddMiniControl(body, "MODE", 92, 224);
        shareGraphic = AddMiniControl(body, "SHARE", 122, 224);
        consoleGraphic = AddMiniControl(body, isPlayStationLayout ? "PS" : isXboxLayout ? "XBOX" : "HOME", 155, 224);
        optionsGraphic = AddMiniControl(body, "OPTIONS", 188, 224);
        sensitivityGraphic = AddMiniControl(body, "SENS", 246, 214);
        if (sensitivityGraphic is System.Windows.Shapes.Ellipse sensitivityDial)
        {
            sensitivityDial.Width = 42;
            sensitivityDial.Height = 42;
        }
        sensitivityGraphic.Fill = accentBrush;
        AddBodyLabel(body, selectedProfile.Brand.ToUpperInvariant(), 112, 47, 9, System.Windows.Media.Brushes.White);
        AddBodyLabel(body, selectedProfile.Brand == "Thrustmaster" ? "MANETTINO" : "ROTARY", 238, 260, 7, new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#506078")));

        if (IsFormulaWheel(selectedProfile)) BuildFormulaWheelFace(body, isPlayStationLayout, isXboxLayout);

        AddControlLabel(mapCanvas, "LIVE PEDAL TRAVEL", 244, 278);
        brakeTravelText = AddControlLabel(mapCanvas, "BRAKE  0%", 280, 306);
        acceleratorTravelText = AddControlLabel(mapCanvas, "ACCELERATOR  0%", 345, 306);
        if (selectedProfile.PedalCount >= 3)
            clutchTravelText = AddControlLabel(mapCanvas, "CLUTCH  0%", 205, 306);
    }

    private static bool IsFormulaWheel(WheelProfile profile)
        => profile.Model.Contains("Formula", StringComparison.OrdinalIgnoreCase) ||
           profile.Model.Contains("SF1000", StringComparison.OrdinalIgnoreCase) ||
           profile.Model.Contains("XF1", StringComparison.OrdinalIgnoreCase) ||
           profile.Brand is "Cube Controls" or "Leoxz" or "P1Sim";

    private ControllerVisualLayout GetControllerVisualLayout()
    {
        if (OutputMode?.SelectedIndex == 2) return ControllerVisualLayout.PlayStation;
        if (OutputMode?.SelectedIndex == 1) return ControllerVisualLayout.Xbox;
        if (selectedProfile.Brand == "Generic HID") return ControllerVisualLayout.GenericHid;
        return selectedProfile.Brand is "Thrustmaster" or "HORI" ? ControllerVisualLayout.PlayStation
            : selectedProfile.Model.Contains("G920", StringComparison.OrdinalIgnoreCase) || selectedProfile.Brand == "Turtle Beach"
                ? ControllerVisualLayout.Xbox
                : ControllerVisualLayout.GenericHid;
    }

    private static string BrandAccent(string brand) => brand switch
    {
        "Thrustmaster" => "#E5B719",
        "Logitech" => "#2F70F5",
        "Fanatec" => "#F2C94C",
        "MOZA" => "#FF6B35",
        "Simagic" => "#F2994A",
        "Asetek" => "#E5484D",
        "Simucube" => "#35B7FF",
        "Cube Controls" => "#3DDC97",
        "Leoxz" => "#FF4D6D",
        "Conspit" => "#6C63FF",
        _ => "#4B7BEC"
    };

    private void BuildFormulaWheelFace(Canvas body, bool isPlayStationLayout, bool isXboxLayout)
    {
        body.Children.Clear();
        var shell = new System.Windows.Shapes.Rectangle { Width = 310, Height = 170, RadiusX = 38, RadiusY = 38, Fill = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#171C26")), Stroke = System.Windows.Media.Brushes.Black, StrokeThickness = 7 };
        body.Children.Add(shell); Canvas.SetLeft(shell, 10); Canvas.SetTop(shell, 58);
        var leftGrip = new System.Windows.Shapes.Rectangle { Width = 72, Height = 126, RadiusX = 28, RadiusY = 28, Fill = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2A3342")) };
        var rightGrip = new System.Windows.Shapes.Rectangle { Width = 72, Height = 126, RadiusX = 28, RadiusY = 28, Fill = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2A3342")) };
        body.Children.Add(leftGrip); body.Children.Add(rightGrip); Canvas.SetLeft(leftGrip, 0); Canvas.SetTop(leftGrip, 90); Canvas.SetLeft(rightGrip, 258); Canvas.SetTop(rightGrip, 90);
        var display = new System.Windows.Shapes.Rectangle { Width = 132, Height = 48, RadiusX = 8, RadiusY = 8, Fill = System.Windows.Media.Brushes.Black, Stroke = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#697589")), StrokeThickness = 2 };
        body.Children.Add(display); Canvas.SetLeft(display, 99); Canvas.SetTop(display, 76);
        AddBodyLabel(body, "FORMULA DASH", 120, 92, 10, System.Windows.Media.Brushes.LimeGreen);
        for (var led = 0; led < 10; led++)
        {
            var light = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = led < 6 ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.OrangeRed };
            body.Children.Add(light); Canvas.SetLeft(light, 119 + led * 10); Canvas.SetTop(light, 63);
        }
        var symbols = isPlayStationLayout ? new[] { "â–³", "â—‹", "Ã—", "â–¡" } : isXboxLayout ? new[] { "Y", "B", "A", "X" } : new[] { "1", "2", "3", "4" };
        WheelButtonY = AddFaceButton(body, symbols[0], 44, 90); WheelButtonX = AddFaceButton(body, symbols[3], 44, 128);
        WheelButtonB = AddFaceButton(body, symbols[1], 258, 90); WheelButtonA = AddFaceButton(body, symbols[2], 258, 128);
        dpadUpGraphic = AddDpadSegment(body, 78, 137); dpadLeftGraphic = AddDpadSegment(body, 57, 158); dpadRightGraphic = AddDpadSegment(body, 99, 158); dpadDownGraphic = AddDpadSegment(body, 78, 179);
        l1Graphic = AddShoulderControl(body, "L1", 27, 50); r1Graphic = AddShoulderControl(body, "R1", 275, 50);
        var accent = BrandAccent(selectedProfile.Brand);
        l2Graphic = AddPaddleControl(body, "L2", 12, 116, accent); r2Graphic = AddPaddleControl(body, "R2", 282, 116, accent);
        l3Graphic = AddMiniControl(body, "L3", 114, 139); r3Graphic = AddMiniControl(body, "R3", 188, 139);
        modeGraphic = AddMiniControl(body, "MODE", 121, 186); shareGraphic = AddMiniControl(body, "SHARE", 151, 186);
        consoleGraphic = AddMiniControl(body, isPlayStationLayout ? "PS" : "HOME", 181, 186); optionsGraphic = AddMiniControl(body, "MENU", 211, 186);
        sensitivityGraphic = AddMiniControl(body, "DIAL", 234, 164);
        sensitivityGraphic.Fill = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(accent));
        AddBodyLabel(body, selectedProfile.Brand.ToUpperInvariant(), 112, 213, 9, System.Windows.Media.Brushes.White);
    }

    private static void AddSpoke(Canvas body, double left, double top, double width, double height)
    {
        var spoke = new System.Windows.Shapes.Rectangle { Width = width, Height = height, Fill = System.Windows.Media.Brushes.Black };
        body.Children.Add(spoke); Canvas.SetLeft(spoke, left); Canvas.SetTop(spoke, top);
    }

    private static void AddBodyLabel(Canvas body, string text, double left, double top, double size, System.Windows.Media.Brush color)
    {
        var label = new TextBlock { Text = text, FontSize = size, FontWeight = FontWeights.Bold, Foreground = color };
        body.Children.Add(label); Canvas.SetLeft(label, left); Canvas.SetTop(label, top);
    }

    private static System.Windows.Shapes.Ellipse AddFaceButton(Canvas body, string symbol, double left, double top)
    {
        var accent = symbol switch { "△" => "#49A86C", "○" => "#D9465F", "×" => "#3B82F6", "□" => "#C084FC", "Y" => "#EAB308", "B" => "#EF4444", "A" => "#22C55E", "X" => "#3B82F6", _ => "#FFFFFF" };
        var button = new System.Windows.Shapes.Ellipse { Width = 28, Height = 28, Fill = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(accent)), Stroke = System.Windows.Media.Brushes.Black, StrokeThickness = 2 };
        body.Children.Add(button); Canvas.SetLeft(button, left); Canvas.SetTop(button, top); AddBodyLabel(body, symbol, left + 8, top + 4, 15, System.Windows.Media.Brushes.Black);
        return button;
    }

    private static System.Windows.Shapes.Rectangle AddDpadSegment(Canvas body, double left, double top)
    {
        var segment = new System.Windows.Shapes.Rectangle { Width = 20, Height = 20, Fill = System.Windows.Media.Brushes.White, Stroke = System.Windows.Media.Brushes.Black, StrokeThickness = 1 };
        body.Children.Add(segment); Canvas.SetLeft(segment, left); Canvas.SetTop(segment, top);
        return segment;
    }

    private static System.Windows.Shapes.Rectangle AddPaddleControl(Canvas body, string label, double left, double top, string color = "#B5232E")
    {
        var paddle = new System.Windows.Shapes.Rectangle { Width = 36, Height = 56, RadiusX = 12, RadiusY = 12, Fill = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color)) };
        body.Children.Add(paddle); Canvas.SetLeft(paddle, left); Canvas.SetTop(paddle, top); AddBodyLabel(body, label, left + 9, top + 20, 10, System.Windows.Media.Brushes.White);
        return paddle;
    }

    private static System.Windows.Shapes.Rectangle AddShoulderControl(Canvas body, string label, double left, double top)
    {
        var shoulder = new System.Windows.Shapes.Rectangle
        {
            Width = 28, Height = 14, RadiusX = 6, RadiusY = 6,
            Fill = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#303B4D")),
            Stroke = System.Windows.Media.Brushes.Black, StrokeThickness = 1
        };
        body.Children.Add(shoulder); Canvas.SetLeft(shoulder, left); Canvas.SetTop(shoulder, top);
        AddBodyLabel(body, label, left + 6, top + 2, 7, System.Windows.Media.Brushes.White);
        return shoulder;
    }

    private static System.Windows.Shapes.Ellipse AddMiniControl(Canvas body, string label, double left, double top)
    {
        var control = new System.Windows.Shapes.Ellipse { Width = 28, Height = 28, Fill = System.Windows.Media.Brushes.White, Stroke = System.Windows.Media.Brushes.Black, StrokeThickness = 1 };
        body.Children.Add(control); Canvas.SetLeft(control, left); Canvas.SetTop(control, top); AddBodyLabel(body, label, left - 2, top + 31, 7, System.Windows.Media.Brushes.Black);
        return control;
    }

    private TextBlock AddControlLabel(Canvas mapCanvas, string label, double left, double top)
    {
        var text = new TextBlock
        {
            Text = label,
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#506078")),
            Background = System.Windows.Media.Brushes.White,
            Padding = new Thickness(4, 2, 4, 2)
        };
        Canvas.SetLeft(text, left);
        Canvas.SetTop(text, top);
        mapCanvas.Children.Add(text);
        controlMapDecorations.Add(text);
        return text;
    }

    private void AddCallout(Canvas mapCanvas, string label, double left, double top)
    {
        var text = new TextBlock
        {
            Text = label,
            FontSize = 8,
            FontWeight = FontWeights.Bold,
            Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3D4B63")),
            Background = System.Windows.Media.Brushes.White,
            Padding = new Thickness(3, 1, 3, 1)
        };
        Canvas.SetLeft(text, left);
        Canvas.SetTop(text, top);
        mapCanvas.Children.Add(text);
        controlMapDecorations.Add(text);
    }

    private void ScanConnectedWheels_Click(object sender, RoutedEventArgs e)
    {
        scannedWheel = WheelDetector.FindConnectedWheel();
        if (scannedWheel is null)
        {
            AddWheelButton.IsEnabled = false;
            SearchStatus.Text = "No supported wheel was found. Check the USB cable and PC mode, then scan again.";
            return;
        }

        var matchedProfile = WheelCatalog.Resolve(scannedWheel);
        var profileSearch = $"{matchedProfile.Brand} {matchedProfile.Model}";
        ControllerSearch.Text = profileSearch;
        AddWheelButton.IsEnabled = true;
        SearchStatus.Text = matchedProfile.Brand == "Generic HID"
            ? $"USB wheel found: {scannedWheel.Name}. A generic HID profile is ready to add."
            : $"USB wheel found: {scannedWheel.Name}. Matched to the {matchedProfile.Brand} profile.";
    }

    private void PollWheelInput()
    {
        if (DateTime.UtcNow < inputReadyAtUtc)
        {
            StudioStatus.Text = "Waiting for the wheel input to settle…";
            return;
        }
        var state = WindowsJoystickInput.FindState(selectedProfile, ref joystickDeviceId);
        if (state is null)
        {
            StudioStatus.Text = selectedProfile.Model.Contains("T98", StringComparison.OrdinalIgnoreCase)
                ? "T98 USB detected, but Windows is not receiving wheel input. Set the wheel's MODE LED to green: hold MODE for 5 seconds, choose green with the D-pad, release MODE, then unplug and reconnect the wheel directly to the PC (no USB hub)."
                : "Waiting for Windows game-controller input. If the wheel is connected, set it to PC mode and scan again.";
            return;
        }

        var baseline = inputBaseline;
        // Some WinMM devices return one all-zero axis report immediately after
        // a controller is opened. Do not let that transient report become the
        // idle reference: the next centred T98 report would look like 100% on
        // both pedals.
        if (baseline is null || IsTransientZeroBaseline(baseline, state))
        {
            inputBaseline = state;
            StudioStatus.Text = "Reading the wheel's neutral position…";
            return;
        }
        var physicalSteering = GetCalibratedSteering(state, baseline);
        var steering = ApplySteeringSensitivity(physicalSteering);
        // The connected T98 exposes valid Y and Z ranges in Windows. Its R range
        // is not a usable pedal axis, so use the two genuine analogue channels.
        var pedalsConfirmed = HasConfirmedPedalCalibration();
        var accelerator = pedalsConfirmed ? GetMappedAxisPressure("Accelerator", "Y", state, baseline) : 0;
        var brake = pedalsConfirmed ? GetMappedAxisPressure("Brake", "Z", state, baseline) : 0;
        var clutch = NormalizePressure(state.R, state.RMin, state.RMax, baseline.R);
        var activity = $"Live input — {state.DeviceName}: steering {steering:+0.00;-0.00;0.00}, accelerator {accelerator:P0}, brake {brake:P0}; buttons: {state.PressedButtons}";

        StudioStatus.Text = pedalsConfirmed ? activity : "Pedals are held at 0% until their first calibration is confirmed. Choose Map pedals to record released and full travel.";
        CalibrationMonitorStatus.Text = calibrationLearningStore.Observe(selectedProfile, state, baseline);
        UpdateLiveInputTiles(state, physicalSteering, accelerator, brake, clutch);
        CaptureMappingInput(state);
        SubmitVirtualController(state, steering, accelerator, brake);
    }

    private void UpdateLiveInputTiles(WheelInputState state, double steering, double accelerator, double brake, double clutch)
    {
        WheelRotation.Angle = GetDisplaySteeringAngle(state, inputBaseline ?? state, steering);
        SetPedalPressure(AcceleratorPedalGraphic, accelerator, "#2F70F5");
        SetPedalPressure(BrakePedalGraphic, brake, "#EF4444");
        SetPedalPressure(ClutchPedalGraphic, clutch, "#7C3AED");
        if (acceleratorTravelText is not null) acceleratorTravelText.Text = $"ACCELERATOR  {accelerator:P0}";
        if (brakeTravelText is not null) brakeTravelText.Text = $"BRAKE  {brake:P0}";
        if (clutchTravelText is not null) clutchTravelText.Text = $"CLUTCH  {clutch:P0}";

        // On the T98 GTB the physical Cross/X switch reports as raw button 6.
        // This was previously displayed as R2, which the recording confirmed.
        var isT98 = selectedProfile.Model.Contains("T98", StringComparison.OrdinalIgnoreCase);
        SetGraphicState(WheelButtonA, IsMappedButtonPressed("Cross", state, isT98 ? 32u : 1u));
        SetGraphicState(WheelButtonB, IsMappedButtonPressed("Circle", state, 2));
        SetGraphicState(WheelButtonX, IsMappedButtonPressed("Square", state, 4));
        SetGraphicState(WheelButtonY, IsMappedButtonPressed("Triangle", state, 8));
        SetGraphicState(l1Graphic, IsMappedButtonPressed("GearDown", state, 16));
        SetGraphicState(r1Graphic, IsMappedButtonPressed("GearUp", state, 1));
        SetGraphicState(l2Graphic, IsMappedButtonPressed("L2", state, 16));
        SetGraphicState(r2Graphic, IsMappedButtonPressed("R2", state, isT98 ? 1u : 32u));
        SetGraphicState(l3Graphic, IsMappedButtonPressed("L3", state, 64));
        SetGraphicState(r3Graphic, IsMappedButtonPressed("R3", state, 128));
        SetGraphicState(shareGraphic, IsMappedButtonPressed("Share", state, 256));
        SetGraphicState(consoleGraphic, IsMappedButtonPressed("PS", state, 512));
        SetGraphicState(optionsGraphic, IsMappedButtonPressed("Options", state, 1024));
        SetGraphicState(modeGraphic, IsMappedButtonPressed("Mode", state, 2048));
        var dialGreen = IsMappedDialActive("SensitivityGreen", state);
        var dialRed = IsMappedDialActive("SensitivityRed", state);
        if (sensitivityGraphic is not null)
            sensitivityGraphic.Fill = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(dialGreen ? "#22C55E" : dialRed ? "#D9465F" : "#B5232E"));
        SetDpadDirections(state.Pov);
    }

    private bool IsMappedButtonPressed(string control, WheelInputState state, uint fallbackMask)
    {
        var pressed = (state.Buttons & (learnedButtonMap.TryGetValue(control, out var mask) ? mask : fallbackMask)) != 0;
        if (pressed) buttonActivity[control] = DateTime.UtcNow;
        return pressed || (buttonActivity.TryGetValue(control, out var lastPress) && DateTime.UtcNow - lastPress < TimeSpan.FromMilliseconds(450));
    }

    private bool IsPhysicalButtonPressed(string control, WheelInputState state, uint fallbackMask) =>
        (state.Buttons & (learnedButtonMap.TryGetValue(control, out var mask) ? mask : fallbackMask)) != 0;

    private void SubmitVirtualController(WheelInputState state, double steering, double accelerator, double brake)
    {
        if (OutputMode?.SelectedIndex is not 1 and not 2) return;
        // Create the controller while the session is armed, before launching the
        // game. Forza and several EA titles enumerate XInput devices at startup.
        var virtualType = OutputMode.SelectedIndex == 2 ? VirtualControllerType.DualShock4 : VirtualControllerType.Xbox360;
        if (!virtualControllerBridge.IsConnected || virtualControllerBridge.ControllerType != virtualType)
        {
            try
            {
                virtualControllerBridge.Connect(virtualType);
                UpdateCompatibilityStatus();
            }
            catch (Exception ex)
            {
                virtualControllerBridge.Dispose();
                DriverStatus.Text = $"Virtual controller could not start: {ex.Message}";
                return;
            }
        }
        // Universal mode has no game-process gate. Native-wheel games can see
        // the physical HID device while controller-only games see this output.
        gameSessionWasActive = IsSelectedGameRunning();
        var isT98 = selectedProfile.Model.Contains("T98", StringComparison.OrdinalIgnoreCase);
        var r2Pressed = IsPhysicalButtonPressed("R2", state, isT98 ? 1u : 32u);
        // A wheel can use the physical L2/R2 buttons as secondary brake/throttle
        // while retaining analogue pedal travel for the primary controls.
        var sharePressed = IsPhysicalButtonPressed("Share", state, 256);
        virtualControllerBridge.Submit(steering, accelerator, brake, state.Pov, new Dictionary<string, bool>
        {
            ["Cross"] = IsPhysicalButtonPressed("Cross", state, isT98 ? 32u : 1u),
            ["Circle"] = IsPhysicalButtonPressed("Circle", state, 2),
            ["Square"] = IsPhysicalButtonPressed("Square", state, 4),
            ["Triangle"] = IsPhysicalButtonPressed("Triangle", state, 8),
            ["GearDown"] = IsPhysicalButtonPressed("GearDown", state, 16),
            ["GearUp"] = IsPhysicalButtonPressed("GearUp", state, 1),
            ["L3"] = IsPhysicalButtonPressed("L3", state, 64),
            ["R3"] = IsPhysicalButtonPressed("R3", state, 128),
            ["Share"] = sharePressed,
            ["PS"] = IsPhysicalButtonPressed("PS", state, 512),
            ["Options"] = IsPhysicalButtonPressed("Options", state, 1024)
        }, r2Pressed, sharePressed);
    }

    private bool IsMappedDialActive(string control, WheelInputState state)
    {
        var fromButton = learnedButtonMap.TryGetValue(control, out var buttonMask) && IsMappedButtonPressed(control, state, buttonMask);
        return fromButton || (learnedAxisMap.TryGetValue(control, out var axis) && GetAxisDelta(axis, state, inputBaseline ?? state) > .04);
    }

    private double GetMappedAxisPressure(string control, string fallbackAxis, WheelInputState state, WheelInputState baseline)
    {
        var hasLearnedAxis = learnedAxisMap.TryGetValue(control, out var mappedAxis);
        var axis = hasLearnedAxis ? mappedAxis! : fallbackAxis;
        // Released pedal positions vary by wheel (some T98 PC modes rest at an
        // endpoint, others at centre). Use the verified live neutral sample,
        // never a catalogue midpoint, so an untouched pedal is always 0%.
        var runtimeNeutral = axis switch { "Y" => baseline.Y, "Z" => baseline.Z, "R" => baseline.R, "U" => baseline.U, "V" => baseline.V, _ => 0u };
        var reference = hasLearnedAxis && learnedPedalNeutral.TryGetValue(control, out var mappedNeutral) ? mappedNeutral : runtimeNeutral;
        var rawPressure = axis switch
        {
            "Y" => NormalizePressure(state.Y, state.YMin, state.YMax, reference),
            "Z" => NormalizePressure(state.Z, state.ZMin, state.ZMax, reference),
            "R" => NormalizePressure(state.R, state.RMin, state.RMax, reference),
            "U" => NormalizePressure(state.U, state.UMin, state.UMax, reference),
            "V" => NormalizePressure(state.V, state.VMin, state.VMax, reference),
            _ => 0
        };
        var travel = learnedPedalTravel.TryGetValue(control, out var calibratedTravel) ? calibratedTravel : 0;
        if (travel <= 0 || !learnedPedalDirection.TryGetValue(control, out var direction)) return rawPressure;
        // The learned peak is the position reached just before the user starts
        // releasing the pedal. That point is deliberately treated as 100%.
        return Math.Clamp(((long)GetAxisRawValue(axis, state) - reference) * direction / (double)travel, 0, 1);
    }

    private bool HasConfirmedPedalCalibration() =>
        learnedAxisMap.ContainsKey("Accelerator") && learnedAxisMap.ContainsKey("Brake") &&
        learnedPedalTravel.TryGetValue("Accelerator", out var acceleratorTravel) && acceleratorTravel > 0 &&
        learnedPedalTravel.TryGetValue("Brake", out var brakeTravel) && brakeTravel > 0 &&
        learnedPedalDirection.ContainsKey("Accelerator") && learnedPedalDirection.ContainsKey("Brake") &&
        learnedPedalNeutral.ContainsKey("Accelerator") && learnedPedalNeutral.ContainsKey("Brake");

    private static uint GetAxisMidpoint(string axis, WheelInputState state) => axis switch
    {
        "Y" => (state.YMin + state.YMax) / 2,
        "Z" => (state.ZMin + state.ZMax) / 2,
        "R" => (state.RMin + state.RMax) / 2,
        _ => 0
    };

    private static bool IsTransientZeroBaseline(WheelInputState baseline, WheelInputState current)
    {
        var baselineAtEndpoint = (baseline.Y < 1000 && baseline.Z < 1000) ||
                                 (baseline.Y > baseline.YMax - 1000 && baseline.Z > baseline.ZMax - 1000);
        var yChanged = Math.Abs((long)current.Y - baseline.Y) > (current.YMax - current.YMin) / 4;
        var zChanged = Math.Abs((long)current.Z - baseline.Z) > (current.ZMax - current.ZMin) / 4;
        // Ignore the initial all-zero report whether the following neutral
        // report is centred or endpoint-based. The next report becomes neutral.
        return baselineAtEndpoint && (yChanged || zChanged);
    }

    private double GetCalibratedSteering(WheelInputState state, WheelInputState baseline)
    {
        var rawDelta = (long)state.X - (learnedSteeringCenter ?? baseline.X);
        var key = rawDelta < 0 ? "SteerLeft" : "SteerRight";
        if (!learnedSteeringTravel.TryGetValue(key, out var travelAtNinety) || travelAtNinety <= 0)
            return NormalizeSteering(state.X, state.XMin, state.XMax, baseline.X);
        return SteeringResponse.DirectLinear(rawDelta, travelAtNinety);
    }

    private double GetDisplaySteeringAngle(WheelInputState state, WheelInputState baseline, double normalized)
    {
        var rawDelta = (long)state.X - (learnedSteeringCenter ?? baseline.X);
        var key = rawDelta < 0 ? "SteerLeft" : "SteerRight";
        return learnedSteeringTravel.TryGetValue(key, out var travelAtNinety) && travelAtNinety > 0
            ? Math.Clamp(rawDelta / (double)travelAtNinety * 90, -GetWheelHalfRotation(), GetWheelHalfRotation())
            : normalized * GetWheelHalfRotation();
    }

    private double ApplySteeringSensitivity(double steering) =>
        SteeringResponse.ApplyLinearGain(steering, SensitivitySlider?.Value ?? 1);

    private double GetWheelHalfRotation()
    {
        var number = new string(selectedProfile.Rotation.SkipWhile(character => !char.IsDigit(character)).TakeWhile(char.IsDigit).ToArray());
        return double.TryParse(number, out var rotation) && rotation > 0 ? rotation / 2d : 180d;
    }

    private void EnsureMapInputsButton()
    {
        if (mapPedalsButton is not null) return;
        var accent = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E55C8"));
        var border = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2F70F5"));
        mapPedalsButton = CreateMappingButton("Map pedals", 0, () => StartInputMapping(MappingSection.Pedals), accent, border);
        mapSteeringButton = CreateMappingButton("Map steering", 0, () => StartInputMapping(MappingSection.Steering), accent, border);
        mapButtonsButton = CreateMappingButton("Map buttons", 0, () => StartInputMapping(MappingSection.Buttons), accent, border);
        var resetPedalsButton = CreateMappingButton("Reset pedals", 0, () => ResetMappingSection(MappingSection.Pedals), System.Windows.Media.Brushes.Firebrick, border);
        var resetSteeringButton = CreateMappingButton("Reset steering", 0, () => ResetMappingSection(MappingSection.Steering), System.Windows.Media.Brushes.Firebrick, border);
        var resetButtonsButton = CreateMappingButton("Reset buttons", 0, () => ResetMappingSection(MappingSection.Buttons), System.Windows.Media.Brushes.Firebrick, border);

        backToLibraryButton = new Button { Content = "Back to library", Padding = new Thickness(10, 7, 10, 7) };
        backToLibraryButton.Click += (_, _) => ReturnToLibrary();

        resetInputsButton = new Button { Content = "Reset inputs", Padding = new Thickness(10, 7, 10, 7) };
        resetInputsButton.Click += (_, _) => ResetInputMapping();

        presetsButton = new Button { Content = "Save / apply presets", Padding = new Thickness(10, 7, 10, 7) };
        presetsButton.Click += (_, _) => ShowPresetsOverlay();
        if (mappingActionsPanel is not null)
        {
            mappingActionsPanel.Children.Add(mapPedalsButton); mappingActionsPanel.Children.Add(mapSteeringButton); mappingActionsPanel.Children.Add(mapButtonsButton);
            mappingActionsPanel.Children.Add(resetPedalsButton); mappingActionsPanel.Children.Add(resetSteeringButton); mappingActionsPanel.Children.Add(resetButtonsButton);
            mappingActionsPanel.Children.Add(presetsButton); mappingActionsPanel.Children.Add(resetInputsButton); mappingActionsPanel.Children.Add(backToLibraryButton);
        }
    }

    private Button CreateMappingButton(string content, double rightMargin, Action action, System.Windows.Media.Brush foreground, System.Windows.Media.Brush border)
    {
        var button = new Button { Content = content, Background = System.Windows.Media.Brushes.White, BorderBrush = border, Foreground = foreground, Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 4, rightMargin, 0) };
        button.Click += (_, _) => action();
        return button;
    }

    private void ResetInputMapping()
    {
        learnedButtonMap.Clear(); learnedAxisMap.Clear(); learnedPedalTravel.Clear(); learnedPedalDirection.Clear(); learnedPedalNeutral.Clear(); learnedSteeringTravel.Clear(); learnedSteeringCenter = null; pendingSteeringCenter = null; buttonActivity.Clear();
        if (System.IO.File.Exists(InputMapPath)) System.IO.File.Delete(InputMapPath);
        inputBaseline = null;
        StudioStatus.Text = "Input map reset. Use Map inputs to learn this wheel again.";
    }

    private void ResetMappingSection(MappingSection section)
    {
        if (mappingStepIndex >= 0) StopInputMapping(false);
        switch (section)
        {
            case MappingSection.Pedals:
                learnedAxisMap.Remove("Accelerator"); learnedAxisMap.Remove("Brake");
                learnedPedalTravel.Remove("Accelerator"); learnedPedalTravel.Remove("Brake");
                learnedPedalDirection.Remove("Accelerator"); learnedPedalDirection.Remove("Brake");
                learnedPedalNeutral.Remove("Accelerator"); learnedPedalNeutral.Remove("Brake");
                break;
            case MappingSection.Steering:
                learnedSteeringTravel.Clear(); pendingSteeringTravel.Clear(); learnedSteeringCenter = null; pendingSteeringCenter = null;
                break;
            case MappingSection.Buttons:
                learnedButtonMap.Clear(); buttonActivity.Clear();
                learnedAxisMap.Remove("Sensitivity"); learnedAxisMap.Remove("SensitivityGreen"); learnedAxisMap.Remove("SensitivityRed");
                break;
        }
        inputBaseline = null;
        inputReadyAtUtc = DateTime.UtcNow.AddMilliseconds(500);
        SaveInputMap();
        StudioStatus.Text = $"{section} mapping reset. Choose Map {section.ToString().ToLowerInvariant()} to learn only that group again.";
    }

    private string PresetDirectory => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WheelPro", "presets", $"{selectedProfile.Brand}-{selectedProfile.Model}".ToLowerInvariant().Replace(' ', '-').Replace('/', '-'));

    private void ShowPresetsOverlay()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "CUSTOM BUTTON MAP PRESETS", FontWeight = FontWeights.Bold, Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2463EB")) });
        var save = new Button { Content = "Save full wheel preset", Margin = new Thickness(0, 14, 0, 6) };
        save.Click += (_, _) =>
        {
            SaveInputMap();
            System.IO.Directory.CreateDirectory(PresetDirectory);
            var name = $"{selectedProfile.Brand}-{selectedProfile.Model}-preset-{DateTime.Now:yyyyMMdd-HHmmss}".Replace('/', '-').Replace(' ', '-') + ".json";
            System.IO.File.Copy(InputMapPath, System.IO.Path.Combine(PresetDirectory, name), true);
            PrepareDriver_Click(save, new RoutedEventArgs());
            StudioStatus.Text = "Full wheel preset saved: wheel, controller type, pedals, buttons, steering, sensitivity and deadzone.";
            ShowPresetsOverlay();
        };
        panel.Children.Add(save);
        if (System.IO.Directory.Exists(PresetDirectory))
            foreach (var file in System.IO.Directory.EnumerateFiles(PresetDirectory, "*.json").OrderByDescending(System.IO.File.GetLastWriteTime))
            {
                var preset = new Button { Content = $"Apply {System.IO.Path.GetFileNameWithoutExtension(file)}", HorizontalAlignment = HorizontalAlignment.Stretch };
                preset.Click += (_, _) => { System.IO.File.Copy(file, InputMapPath, true); LoadInputMap(); inputBaseline = null; if (presetsOverlay is not null) presetsOverlay.Visibility = Visibility.Collapsed; StudioStatus.Text = "Full wheel preset applied."; };
                panel.Children.Add(preset);
            }
        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
        close.Click += (_, _) => { if (presetsOverlay is not null) presetsOverlay.Visibility = Visibility.Collapsed; };
        panel.Children.Add(close);
        presetsOverlay ??= new Border { Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(248, 255, 255, 255)), BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2F70F5")), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(18), Padding = new Thickness(24), Width = 410, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        presetsOverlay.Child = panel; Grid.SetRow(presetsOverlay, 1); Grid.SetZIndex(presetsOverlay, 25);
        if (!StudioView.Children.Contains(presetsOverlay)) StudioView.Children.Add(presetsOverlay);
        presetsOverlay.Visibility = Visibility.Visible;
    }

    private void ReturnToLibrary()
    {
        mappingStepIndex = -1;
        if (mappingOverlay is not null) mappingOverlay.Visibility = Visibility.Collapsed;
        StudioView.Visibility = Visibility.Collapsed;
        SearchView.Visibility = Visibility.Visible;
        ControllerSearch.Focus();
    }

    private void AddCalibrationDescriptions()
    {
        AddDescription(SensitivitySlider, SensitivityText, "Sets how quickly steering reaches full lock. Lower is smoother; higher is more responsive.");
        AddDescription(DeadzoneSlider, DeadzoneText, "Ignores tiny movements around centre to prevent steering drift. Keep this as low as your wheel allows.");
    }

    private static void AddDescription(Slider slider, TextBlock value, string description)
    {
        if (slider.Parent is not StackPanel panel) return;
        var position = panel.Children.IndexOf(value);
        if (position < 0) return;
        panel.Children.Insert(position + 1, new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#6D788C")), FontSize = 11, Margin = new Thickness(0, 4, 0, 8) });
    }

    private void StartInputMapping(MappingSection section)
    {
        mappingSteps.Clear();
        var layout = GetControllerVisualLayout();
        var isXboxLayout = layout == ControllerVisualLayout.Xbox;
        var isPlayStationLayout = layout == ControllerVisualLayout.PlayStation;
        var faceNames = isPlayStationLayout
            ? new[] { "Triangle (△)", "Circle (○)", "Cross (×)", "Square (□)" }
            : isXboxLayout ? new[] { "Y", "B", "A", "X" } : new[] { "upper face button", "right face button", "lower face button", "left face button" };
        var homeName = isPlayStationLayout ? "PlayStation" : isXboxLayout ? "Xbox / Home" : "Home";
        var downshiftName = isPlayStationLayout ? "downshift / L1 paddle" : isXboxLayout ? "downshift / LB paddle" : "downshift paddle";
        var upshiftName = isPlayStationLayout ? "upshift / R1 paddle" : isXboxLayout ? "upshift / RB paddle" : "upshift paddle";
        var leftTriggerName = isPlayStationLayout ? "L2" : isXboxLayout ? "LT" : "left trigger";
        var rightTriggerName = isPlayStationLayout ? "R2" : isXboxLayout ? "RT" : "right trigger";
        var leftStickName = isPlayStationLayout ? "L3" : isXboxLayout ? "left-stick click (LS)" : "left-stick click";
        var rightStickName = isPlayStationLayout ? "R3" : isXboxLayout ? "right-stick click (RS)" : "right-stick click";
        var shareName = isPlayStationLayout ? "SHARE / CREATE" : isXboxLayout ? "VIEW / BACK" : "share / view";
        var optionsName = isPlayStationLayout ? "OPTIONS" : isXboxLayout ? "MENU / START" : "menu / options";
        if (section == MappingSection.Pedals)
            mappingSteps.AddRange(new[] { new InputMappingStep("Accelerator", "Calibrate accelerator.", MappingKind.Axis), new InputMappingStep("Brake", "Calibrate brake.", MappingKind.Axis) });
        if (section == MappingSection.Steering)
            mappingSteps.AddRange(new[] { new InputMappingStep("SteerLeft", "Calibrate 90 degrees left.", MappingKind.SteerLeft), new InputMappingStep("SteerRight", "Calibrate 90 degrees right.", MappingKind.SteerRight) });
        if (section == MappingSection.Buttons)
            mappingSteps.AddRange(new[]
        {
            new InputMappingStep("GearDown", $"Press the {downshiftName}.", MappingKind.Button),
            new InputMappingStep("GearUp", $"Press the {upshiftName}.", MappingKind.Button),
            new InputMappingStep("DpadUp", "Press D-pad Up.", MappingKind.Dpad),
            new InputMappingStep("DpadDown", "Press D-pad Down.", MappingKind.Dpad),
            new InputMappingStep("DpadLeft", "Press D-pad Left.", MappingKind.Dpad),
            new InputMappingStep("DpadRight", "Press D-pad Right.", MappingKind.Dpad),
            new InputMappingStep("Triangle", $"Press {faceNames[0]}.", MappingKind.Button), new InputMappingStep("Circle", $"Press {faceNames[1]}.", MappingKind.Button),
            new InputMappingStep("Cross", $"Press {faceNames[2]}.", MappingKind.Button), new InputMappingStep("Square", $"Press {faceNames[3]}.", MappingKind.Button),
            new InputMappingStep("L2", $"Press {leftTriggerName}.", MappingKind.Button), new InputMappingStep("R2", $"Press {rightTriggerName}.", MappingKind.Button),
            new InputMappingStep("L3", $"Press {leftStickName}.", MappingKind.Button), new InputMappingStep("R3", $"Press {rightStickName}.", MappingKind.Button),
            new InputMappingStep("Share", $"Press {shareName}.", MappingKind.Button),
            new InputMappingStep("PS", $"Press the {homeName} button.", MappingKind.Button), new InputMappingStep("Options", $"Press {optionsName}.", MappingKind.Button)
        });
        if (section == MappingSection.Buttons)
            for (var button = 14; button < selectedProfile.ButtonCount; button++) mappingSteps.Add(new InputMappingStep($"Extra{button + 1}", $"Press additional wheel button {button + 1}.", MappingKind.Button));
        var state = WindowsJoystickInput.FindState(selectedProfile, ref joystickDeviceId);
        if (state is null)
        {
            StudioStatus.Text = selectedProfile.Model.Contains("T98", StringComparison.OrdinalIgnoreCase)
                ? "The T98 is connected but not sending PC input. Change its MODE LED to green, unplug/reconnect it directly to the PC, then map again."
                : "Connect the wheel in PC mode before mapping inputs.";
            return;
        }
        mappingBaseline = state;
        mappingPreviousButtons = state.Buttons;
        mappingAwaitingButtonRelease = state.Buttons != 0;
        mappingAxisCandidate = null; mappingAxisMaximum = 0; mappingAxisPeakValue = 0; pedalMappingStage = PedalMappingStage.None; steeringMappingStage = SteeringMappingStage.None;
        if (section == MappingSection.Pedals) { learnedAxisMap.Remove("Accelerator"); learnedAxisMap.Remove("Brake"); learnedPedalTravel.Remove("Accelerator"); learnedPedalTravel.Remove("Brake"); learnedPedalDirection.Remove("Accelerator"); learnedPedalDirection.Remove("Brake"); learnedPedalNeutral.Remove("Accelerator"); learnedPedalNeutral.Remove("Brake"); }
        if (section == MappingSection.Steering) { pendingSteeringTravel.Clear(); pendingSteeringCenter = null; }
        if (section == MappingSection.Buttons) { learnedButtonMap.Clear(); learnedButtonMap.Remove("Sensitivity"); learnedAxisMap.Remove("Sensitivity"); learnedButtonMap.Remove("SensitivityGreen"); learnedButtonMap.Remove("SensitivityRed"); learnedAxisMap.Remove("SensitivityGreen"); learnedAxisMap.Remove("SensitivityRed"); }
        mappingStepIndex = 0;
        ShowMappingOverlay();
        ShowCurrentMappingStep();
    }

    private void ShowMappingOverlay()
    {
        mappingOverlay ??= new Border { Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(242, 255, 255, 255)), BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2F70F5")), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(18), Padding = new Thickness(28), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Width = 440 };
        var cancel = new Button { Content = "Cancel", HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0) };
        cancel.Click += (_, _) => StopInputMapping(false);
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "MAP WHEEL INPUTS", FontWeight = FontWeights.Bold, Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2463EB")) });
        mappingProgressText = new TextBlock { Margin = new Thickness(0, 12, 0, 4), Foreground = System.Windows.Media.Brushes.Gray };
        mappingStepText = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(mappingProgressText); panel.Children.Add(mappingStepText); panel.Children.Add(cancel); mappingOverlay.Child = panel;
        Grid.SetRow(mappingOverlay, 1); Grid.SetZIndex(mappingOverlay, 20);
        if (!StudioView.Children.Contains(mappingOverlay)) StudioView.Children.Add(mappingOverlay);
        mappingOverlay.Visibility = Visibility.Visible;
    }

    private void ShowCurrentMappingStep()
    {
        if (mappingStepIndex < 0 || mappingStepIndex >= mappingSteps.Count) { StopInputMapping(true); return; }
        var step = mappingSteps[mappingStepIndex];
        if (mappingProgressText is not null) mappingProgressText.Text = $"Step {mappingStepIndex + 1} of {mappingSteps.Count}";
        if (step.Kind == MappingKind.Axis)
        {
            pedalMappingStage = PedalMappingStage.ConfirmNeutral;
            mappingAxisCandidate = null; mappingAxisMaximum = 0; mappingAxisPeakValue = 0;
            if (mappingStepText is not null) mappingStepText.Text = $"Release the {step.Key.ToLowerInvariant()} completely (0%), then press any wheel button to confirm.";
        }
        else if (step.Kind is MappingKind.SteerLeft or MappingKind.SteerRight)
        {
            steeringMappingStage = SteeringMappingStage.ConfirmCentre;
            if (mappingStepText is not null) mappingStepText.Text = "Straighten the wheel upright (0°), then press any wheel button to confirm.";
        }
        else if (mappingStepText is not null) mappingStepText.Text = step.Instruction;
    }

    private void CaptureMappingInput(WheelInputState state)
    {
        if (mappingStepIndex < 0 || mappingBaseline is null) return;
        var step = mappingSteps[mappingStepIndex];
        if ((step.Kind is MappingKind.Button or MappingKind.Dial) && mappingAwaitingButtonRelease)
        {
            mappingPreviousButtons = state.Buttons;
            if (state.Buttons == 0) mappingAwaitingButtonRelease = false;
            return;
        }
        var captured = false;
        if (step.Kind == MappingKind.Axis)
        {
            CaptureConfirmedPedalMapping(step, state, ref captured);
        }
        else if (step.Kind == MappingKind.Button)
        {
            var newPress = state.Buttons & ~mappingPreviousButtons;
            if (newPress != 0)
            {
                ButtonMapping.AssignUnique(learnedButtonMap, step.Key, LowestButtonMask(newPress));
                mappingAwaitingButtonRelease = true;
                buttonActivity.Clear();
                captured = true;
            }
        }
        else if (step.Kind == MappingKind.Dpad)
        {
            var required = step.Key switch { "DpadUp" => 0, "DpadRight" => 9000, "DpadDown" => 18000, _ => 27000 };
            captured = state.Pov != 0xFFFF && Math.Abs((int)state.Pov - required) <= 4500;
        }
        else if (step.Kind is MappingKind.SteerLeft or MappingKind.SteerRight)
        {
            CaptureConfirmedSteeringMapping(step, state, ref captured);
        }
        else if (step.Kind == MappingKind.Dial)
        {
            var newPress = state.Buttons & ~mappingPreviousButtons;
            if (newPress != 0)
            {
                ButtonMapping.AssignUnique(learnedButtonMap, step.Key, LowestButtonMask(newPress));
                mappingAwaitingButtonRelease = true;
                buttonActivity.Clear();
                captured = true;
            }
            else if (FindChangedPedalAxis(state, mappingBaseline) is { } dialAxis)
            {
                learnedAxisMap[step.Key] = dialAxis;
                captured = true;
            }
        }
        mappingPreviousButtons = state.Buttons;
        if (!captured) return;
        mappingStepIndex++;
        ShowCurrentMappingStep();
    }

    private void CaptureConfirmedPedalMapping(InputMappingStep step, WheelInputState state, ref bool captured)
    {
        var newPress = state.Buttons & ~mappingPreviousButtons;
        if (pedalMappingStage == PedalMappingStage.ConfirmNeutral)
        {
            if (newPress == 0) return;
            mappingBaseline = state;
            pedalMappingStage = PedalMappingStage.CaptureFull;
            if (mappingStepText is not null) mappingStepText.Text = $"Now press and hold the {step.Key.ToLowerInvariant()} fully down (100%).";
            return;
        }

        var baseline = mappingBaseline!;
        var axis = mappingAxisCandidate ?? FindChangedPedalAxis(state, baseline);
        if (axis is not null)
        {
            mappingAxisCandidate = axis;
            var delta = GetAxisDeltaRaw(axis, state, baseline);
            if (delta > mappingAxisMaximum) { mappingAxisMaximum = delta; mappingAxisPeakValue = GetAxisRawValue(axis, state); }
            if (pedalMappingStage == PedalMappingStage.CaptureFull && mappingAxisMaximum > 2500)
            {
                pedalMappingStage = PedalMappingStage.ConfirmFull;
                if (mappingStepText is not null) mappingStepText.Text = $"Keep the {step.Key.ToLowerInvariant()} fully pressed (100%), then press any wheel button to confirm.";
            }
        }
        if (pedalMappingStage != PedalMappingStage.ConfirmFull || newPress == 0 || mappingAxisCandidate is null) return;

        var direction = Math.Sign(mappingAxisPeakValue - GetAxisRawValue(mappingAxisCandidate, baseline));
        if (step.Key == "Brake" && learnedAxisMap.TryGetValue("Accelerator", out var acceleratorAxis) &&
            acceleratorAxis == mappingAxisCandidate && learnedPedalDirection.TryGetValue("Accelerator", out var acceleratorDirection) && acceleratorDirection == direction)
        {
            pedalMappingStage = PedalMappingStage.ConfirmNeutral;
            if (mappingStepText is not null) mappingStepText.Text = "That is the accelerator axis. Release the brake fully, then press any wheel button to confirm brake 0%.";
            return;
        }
        learnedAxisMap[step.Key] = mappingAxisCandidate;
        learnedPedalTravel[step.Key] = mappingAxisMaximum;
        learnedPedalDirection[step.Key] = direction;
        learnedPedalNeutral[step.Key] = (uint)GetAxisRawValue(mappingAxisCandidate, baseline);
        pedalMappingStage = PedalMappingStage.None;
        captured = true;
    }

    private void CaptureConfirmedSteeringMapping(InputMappingStep step, WheelInputState state, ref bool captured)
    {
        var newPress = state.Buttons & ~mappingPreviousButtons;
        if (steeringMappingStage == SteeringMappingStage.ConfirmCentre)
        {
            if (newPress == 0) return;
            pendingSteeringCenter = pendingSteeringCenter is uint firstCenter
                ? (uint)(((ulong)firstCenter + state.X) / 2)
                : state.X;
            mappingBaseline = state;
            steeringMappingStage = SteeringMappingStage.ConfirmNinety;
            var direction = step.Kind == MappingKind.SteerLeft ? "left" : "right";
            if (mappingStepText is not null) mappingStepText.Text = $"Turn and hold the wheel 90° to the {direction}, then press any wheel button to confirm.";
            return;
        }

        if (steeringMappingStage != SteeringMappingStage.ConfirmNinety || newPress == 0) return;
        var signedDelta = (long)state.X - mappingBaseline!.X;
        var correctDirection = step.Kind == MappingKind.SteerLeft ? signedDelta < 0 : signedDelta > 0;
        var travel = Math.Abs(signedDelta);
        if (!correctDirection || travel < 800)
        {
            var direction = step.Kind == MappingKind.SteerLeft ? "left" : "right";
            if (mappingStepText is not null) mappingStepText.Text = $"No 90° {direction} turn was detected. Keep the wheel 90° {direction} and press any wheel button again.";
            return;
        }
        pendingSteeringTravel[step.Key] = travel;
        if (step.Key == "SteerRight" && pendingSteeringTravel.ContainsKey("SteerLeft"))
        {
            learnedSteeringTravel.Clear();
            foreach (var calibration in pendingSteeringTravel) learnedSteeringTravel[calibration.Key] = calibration.Value;
            learnedSteeringCenter = pendingSteeringCenter;
            pendingSteeringTravel.Clear();
            pendingSteeringCenter = null;
        }
        steeringMappingStage = SteeringMappingStage.None;
        captured = true;
    }

    private static uint LowestButtonMask(uint buttons) => buttons & unchecked((uint)-(int)buttons);

    private static string? FindChangedPedalAxis(WheelInputState state, WheelInputState baseline)
    {
        var candidates = new[]
        {
            ("Y", NormalizePressure(state.Y, state.YMin, state.YMax, baseline.Y)),
            ("Z", NormalizePressure(state.Z, state.ZMin, state.ZMax, baseline.Z)),
            ("R", NormalizePressure(state.R, state.RMin, state.RMax, baseline.R)),
            ("U", NormalizePressure(state.U, state.UMin, state.UMax, baseline.U)),
            ("V", NormalizePressure(state.V, state.VMin, state.VMax, baseline.V))
        };
        var best = candidates.OrderByDescending(item => item.Item2).First();
        return best.Item2 >= .08 ? best.Item1 : null;
    }

    private static long GetAxisDeltaRaw(string axis, WheelInputState state, WheelInputState baseline) => axis switch
    {
        "Y" => Math.Abs((long)state.Y - baseline.Y),
        "Z" => Math.Abs((long)state.Z - baseline.Z),
        "R" => Math.Abs((long)state.R - baseline.R),
        "U" => Math.Abs((long)state.U - baseline.U),
        "V" => Math.Abs((long)state.V - baseline.V),
        _ => 0
    };

    private static long GetAxisSignedDelta(string axis, WheelInputState state, WheelInputState baseline)
        => GetAxisRawValue(axis, state) - GetAxisRawValue(axis, baseline);

    private static long GetAxisRawValue(string axis, WheelInputState state) => axis switch
    {
        "Y" => state.Y,
        "Z" => state.Z,
        "R" => state.R,
        "U" => state.U,
        "V" => state.V,
        _ => 0
    };

    private static double GetAxisDelta(string axis, WheelInputState state, WheelInputState baseline)
    {
        var range = axis switch { "Y" => (long)state.YMax - state.YMin, "Z" => (long)state.ZMax - state.ZMin, "R" => (long)state.RMax - state.RMin, "U" => (long)state.UMax - state.UMin, "V" => (long)state.VMax - state.VMin, _ => 0 };
        return range == 0 ? 0 : Math.Clamp(GetAxisDeltaRaw(axis, state, baseline) / (double)range, 0, 1);
    }

    private void StopInputMapping(bool completed)
    {
        mappingStepIndex = -1;
        if (mappingOverlay is not null) mappingOverlay.Visibility = Visibility.Collapsed;
        if (completed)
        {
            inputBaseline = null;
            inputReadyAtUtc = DateTime.UtcNow.AddMilliseconds(500);
            SaveInputMap();
            StudioStatus.Text = $"Input mapping saved for {selectedProfile.Brand} {selectedProfile.Model}. The live graphic now follows the physical controls.";
        }
        else
        {
            pendingSteeringTravel.Clear();
            pendingSteeringCenter = null;
        }
    }

    private string InputMapPath => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WheelPro", "device-cache", $"{selectedProfile.Brand}-{selectedProfile.Model}".ToLowerInvariant().Replace(' ', '-').Replace('/', '-') + ".input-map.json");

    private void LoadInputMap()
    {
        learnedButtonMap.Clear(); learnedAxisMap.Clear(); learnedPedalTravel.Clear(); learnedPedalDirection.Clear(); learnedPedalNeutral.Clear(); learnedSteeringTravel.Clear(); learnedSteeringCenter = null;
        try
        {
            if (!System.IO.File.Exists(InputMapPath))
            {
                if (BuiltInProfiles.ApplyWheelDefaults(selectedProfile, learnedButtonMap, learnedAxisMap, learnedPedalTravel, learnedPedalDirection, learnedSteeringTravel))
                    SaveInputMap();
                return;
            }
            var stored = JsonSerializer.Deserialize<StoredInputMap>(System.IO.File.ReadAllText(InputMapPath));
            if (stored is null) return;
            foreach (var binding in stored.Buttons ?? new Dictionary<string, uint>()) learnedButtonMap[binding.Key] = binding.Value;
            ButtonMapping.RemoveDuplicateBindings(learnedButtonMap);
            foreach (var binding in stored.Axes ?? new Dictionary<string, string>()) learnedAxisMap[binding.Key] = binding.Value;
            foreach (var travel in stored.PedalTravel ?? new Dictionary<string, long>()) learnedPedalTravel[travel.Key] = travel.Value;
            foreach (var direction in stored.PedalDirection ?? new Dictionary<string, int>()) learnedPedalDirection[direction.Key] = direction.Value;
            foreach (var neutral in stored.PedalNeutral ?? new Dictionary<string, uint>()) learnedPedalNeutral[neutral.Key] = neutral.Value;
            foreach (var travel in stored.SteeringTravel ?? new Dictionary<string, long>()) learnedSteeringTravel[travel.Key] = travel.Value;
            learnedSteeringCenter = stored.SteeringCenter;
            if (learnedSteeringTravel.Count > 0 && learnedSteeringCenter is null)
                learnedSteeringTravel.Clear();
            if (stored.Sensitivity is double sensitivity && SensitivitySlider is not null) SensitivitySlider.Value = Math.Clamp(sensitivity, SensitivitySlider.Minimum, SensitivitySlider.Maximum);
            if (stored.Deadzone is double deadzone && DeadzoneSlider is not null) DeadzoneSlider.Value = Math.Clamp(deadzone, DeadzoneSlider.Minimum, DeadzoneSlider.Maximum);
            if (stored.OutputMode is int outputMode && OutputMode is not null && outputMode >= 0 && outputMode < OutputMode.Items.Count) OutputMode.SelectedIndex = outputMode;
            if (!HasConfirmedPedalCalibration())
            {
                learnedAxisMap.Remove("Accelerator"); learnedAxisMap.Remove("Brake");
                learnedPedalTravel.Remove("Accelerator"); learnedPedalTravel.Remove("Brake");
                learnedPedalDirection.Remove("Accelerator"); learnedPedalDirection.Remove("Brake");
                learnedPedalNeutral.Remove("Accelerator"); learnedPedalNeutral.Remove("Brake");
                SaveInputMap();
            }
        }
        catch { }
    }

    private void SaveInputMap()
    {
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(InputMapPath)!);
        System.IO.File.WriteAllText(InputMapPath, JsonSerializer.Serialize(new StoredInputMap
        {
            WheelName = $"{selectedProfile.Brand} {selectedProfile.Model}",
            ControllerType = GetControllerVisualLayout().ToString(),
            OutputMode = OutputMode?.SelectedIndex ?? 0,
            Sensitivity = SensitivitySlider?.Value ?? 1,
            Deadzone = DeadzoneSlider?.Value ?? 3,
            Buttons = learnedButtonMap, Axes = learnedAxisMap, PedalTravel = learnedPedalTravel,
            PedalDirection = learnedPedalDirection, PedalNeutral = learnedPedalNeutral, SteeringTravel = learnedSteeringTravel,
            SteeringCenter = learnedSteeringCenter
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void SetGraphicState(System.Windows.Shapes.Shape? graphic, bool active)
    {
        if (graphic is null) return;
        graphic.Fill = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(active ? "#2F70F5" : "#DCE6F8"));
    }

    private void SetDpadDirections(uint pov)
    {
        var hasPov = pov != 0xFFFF;
        var up = hasPov && (pov <= 4500 || pov >= 31500);
        var right = hasPov && pov >= 4500 && pov <= 13500;
        var down = hasPov && pov >= 13500 && pov <= 22500;
        var left = hasPov && pov >= 22500 && pov <= 31500;
        SetGraphicState(dpadUpGraphic, up); SetGraphicState(dpadRightGraphic, right);
        SetGraphicState(dpadDownGraphic, down); SetGraphicState(dpadLeftGraphic, left);
        SetGraphicState(DpadGraphic, hasPov);
    }

    private static void SetPedalPressure(System.Windows.Shapes.Rectangle pedal, double pressure, string activeColor)
    {
        const double baseLine = 348;
        var fillHeight = 14 + (Math.Clamp(pressure, 0, 1) * 68);
        pedal.Height = fillHeight;
        Canvas.SetTop(pedal, baseLine - fillHeight);
        pedal.Fill = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(pressure > .04 ? activeColor : "#DCE6F8"));
    }

    private static double NormalizeSteering(uint value, uint min, uint max, uint baseline)
    {
        if (max <= min) return 0;
        return Math.Clamp(((long)value - baseline) / (double)(max - min) * 2, -1, 1);
    }

    private static double NormalizePressure(uint value, uint min, uint max, uint baseline)
    {
        if (max <= min) return 0;
        // Pedals commonly rest at axis centre and travel toward one end.
        // Measure to that end, not across the full bi-directional axis range.
        var delta = (long)value - baseline;
        var travel = delta < 0 ? (long)baseline - min : (long)max - baseline;
        return travel <= 0 ? 0 : Math.Clamp(Math.Abs(delta) / (double)travel, 0, 1);
    }


    private void AddDetectedWheel_Click(object sender, RoutedEventArgs e)
    {
        if (scannedWheel is null) return;
        OpenStudio_Click(sender, e);
        PrepareDriver_Click(sender, e);
    }

    private void LoadSavedWheels()
    {
        SavedWheels.Clear();
        var cacheDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WheelPro", "device-cache");
        if (System.IO.Directory.Exists(cacheDirectory))
        {
            foreach (var file in System.IO.Directory.EnumerateFiles(cacheDirectory, "*.json"))
            {
                try
                {
                    using var document = JsonDocument.Parse(System.IO.File.ReadAllText(file));
                    var root = document.RootElement;
                    var brand = root.TryGetProperty("Brand", out var brandNode) ? brandNode.GetString() : root.GetProperty("controller").GetString();
                    var model = root.TryGetProperty("Model", out var modelNode) ? modelNode.GetString() : "Saved profile";
                    var favourite = root.TryGetProperty("Favourite", out var favouriteNode) && favouriteNode.GetBoolean();
                    if (!string.IsNullOrWhiteSpace(brand))
                    {
                        var folderName = $"{brand}-{model}".ToLowerInvariant().Replace(' ', '-').Replace('/', '-');
                        var presetDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WheelPro", "presets", folderName);
                        var presetCount = System.IO.Directory.Exists(presetDirectory) ? System.IO.Directory.EnumerateFiles(presetDirectory, "*.json").Count() : 0;
                        SavedWheels.Add(new SavedWheel(brand, model ?? "Saved profile", favourite, file, presetCount));
                    }
                }
                catch (Exception) { }
            }
        }
        ReorderSavedWheels();
        if (string.IsNullOrWhiteSpace(ControllerSearch.Text)) ShowSavedWheelGrid();
        EmptySavedWheels.Visibility = SavedWheels.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowSavedWheelGrid()
    {
        DisplayWheels.Clear();
        foreach (var wheel in SavedWheels) DisplayWheels.Add(wheel);
        EmptySavedWheels.Visibility = SavedWheels.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowSavedPresets_Click(object sender, RoutedEventArgs e)
    {
        ControllerSearch.Text = string.Empty;
        DisplayWheels.Clear();
        foreach (var wheel in SavedWheels.Where(wheel => wheel.PresetCount > 0)) DisplayWheels.Add(wheel);
        EmptySavedWheels.Text = "No full wheel presets have been saved yet.";
        EmptySavedWheels.Visibility = DisplayWheels.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SearchStatus.Text = "Showing saved presets. Select a wheel, connect it, then choose Open to apply or manage its presets.";
    }

    private void ShowCatalogueGrid(string query)
    {
        DisplayWheels.Clear();
        var savedByModel = SavedWheels.ToDictionary(wheel => $"{wheel.Brand} {wheel.Model}", StringComparer.OrdinalIgnoreCase);
        foreach (var profile in WheelCatalog.Search(query))
        {
            var key = $"{profile.Brand} {profile.Model}";
            if (savedByModel.TryGetValue(key, out var saved)) DisplayWheels.Add(saved);
            else DisplayWheels.Add(new SavedWheel(profile.Brand, profile.Model, false, string.Empty));
        }
        EmptySavedWheels.Visibility = DisplayWheels.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SavedWheelList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SavedWheelList.SelectedItem is not SavedWheel wheel) return;
        ControllerSearch.Text = $"{wheel.Brand} {wheel.Model}";
        SearchStatus.Text = $"Selected {wheel.Brand} {wheel.Model}. Connect it, then choose Open.";
    }

    private void ToggleFavourite_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not SavedWheel wheel) return;
        if (string.IsNullOrWhiteSpace(wheel.CachePath) || !System.IO.File.Exists(wheel.CachePath))
        {
            selectedProfile = WheelCatalog.Find($"{wheel.Brand} {wheel.Model}");
            PrepareDriver_Click(sender, e);
            LoadSavedWheels();
            ShowCatalogueGrid(ControllerSearch.Text.Trim());
            wheel = DisplayWheels.FirstOrDefault(item => item.Brand == selectedProfile.Brand && item.Model == selectedProfile.Model) ?? wheel;
        }
        wheel.Favourite = !wheel.Favourite;
        var stored = System.Text.Json.Nodes.JsonNode.Parse(System.IO.File.ReadAllText(wheel.CachePath))?.AsObject() ?? new System.Text.Json.Nodes.JsonObject();
        stored["Favourite"] = wheel.Favourite;
        System.IO.File.WriteAllText(wheel.CachePath, stored.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        ReorderSavedWheels();
        if (string.IsNullOrWhiteSpace(ControllerSearch.Text)) ShowSavedWheelGrid(); else ShowCatalogueGrid(ControllerSearch.Text.Trim());
        SavedWheelList.Items.Refresh();
    }

    private void ReorderSavedWheels()
    {
        var ordered = SavedWheels.OrderByDescending(wheel => wheel.Favourite).ThenBy(wheel => wheel.Brand).ThenBy(wheel => wheel.Model).ToList();
        SavedWheels.Clear();
        foreach (var wheel in ordered) SavedWheels.Add(wheel);
    }

    private void ControllerSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && SuggestionList.Visibility == Visibility.Visible)
        {
            SuggestionList.Focus();
            SuggestionList.SelectedIndex = 0;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            OpenStudio_Click(sender, e);
            e.Handled = true;
        }
    }

    private async void ControllerSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = ControllerSearch.Text.Trim();
        searchCancellation?.Cancel();
        if (query.Length < 1)
        {
            ShowSavedWheelGrid();
            SuggestionList.Visibility = Visibility.Collapsed;
            return;
        }

        ShowCatalogueGrid(query);

        var cancellation = searchCancellation = new CancellationTokenSource();
        try
        {
            var suggestions = WheelCatalog.Suggest(query).ToList();
            SuggestionList.ItemsSource = suggestions;
            SuggestionList.Visibility = suggestions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (query.Length < 2) return;

            await Task.Delay(250, cancellation.Token);
            var url = $"https://en.wikipedia.org/w/api.php?action=opensearch&namespace=0&limit=5&format=json&search={Uri.EscapeDataString(query + " racing wheel")}";
            using var document = JsonDocument.Parse(await SearchClient.GetStringAsync(url, cancellation.Token));
            if (document.RootElement.GetArrayLength() > 1)
            {
                foreach (var item in document.RootElement[1].EnumerateArray())
                {
                    var title = item.GetString();
                    if (!string.IsNullOrWhiteSpace(title) && !suggestions.Contains(title, StringComparer.OrdinalIgnoreCase))
                        suggestions.Add($"Online: {title}");
                }
            }
            if (cancellation.IsCancellationRequested) return;
            SuggestionList.ItemsSource = suggestions.Take(8).ToList();
            SuggestionList.Visibility = suggestions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
        catch (HttpRequestException)
        {
            if (!cancellation.IsCancellationRequested)
            {
                SuggestionList.ItemsSource = WheelCatalog.Suggest(query).ToList();
                SuggestionList.Visibility = SuggestionList.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                SearchStatus.Text = "Online suggestions are unavailable on this PC. Showing the built-in wheel catalogue.";
            }
        }
        catch (Exception)
        {
            if (!cancellation.IsCancellationRequested)
            {
                SuggestionList.ItemsSource = WheelCatalog.Suggest(query).ToList();
                SuggestionList.Visibility = SuggestionList.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                SearchStatus.Text = "Online suggestions could not be read. Check this PC's internet connection, then try again.";
            }
        }
    }

    private void SuggestionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SuggestionList.SelectedItem is not string selected) return;
        ControllerSearch.Text = selected.Replace("Brand: ", "", StringComparison.Ordinal).Replace("Online: ", "", StringComparison.Ordinal);
        SuggestionList.Visibility = Visibility.Collapsed;
        OpenStudio_Click(sender, e);
    }
    private void SettingsChanged(object sender, RoutedEventArgs e)
    {
        if (SensitivityText is null || DeadzoneText is null || SensitivitySlider is null || DeadzoneSlider is null) return;
        SensitivityText.Text = $"{SensitivitySlider.Value:F2}x";
        DeadzoneText.Text = $"{DeadzoneSlider.Value:F0}%";
    }

    private void OutputMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OutputMode?.SelectedIndex is 1 or 2)
        {
            virtualControllerBridge.Dispose();
            StudioStatus.Text = "Universal controller output is active whenever Wheel Pro is running; no game lock is required.";
        }
        else
        {
            virtualControllerBridge.Dispose();
        }
        UpdateCompatibilityStatus();
        UpdateGameSessionStatus();
        SaveGameSessionSettings();
        if (StudioView.Visibility == Visibility.Visible) UpdatePhysicalInputGroups();
    }

    private void GamePlatform_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateGameSessionStatus();
        SaveGameSessionSettings();
        if (GameIntelligenceStatus is null) return;
        GameIntelligenceStatus.Text = GamePlatform?.SelectedIndex switch
        {
            1 => "Steam selected. Universal controller output stays active; use Steam Input only if a game requires a specific Steam policy.",
            2 => "EA app / EA Play selected. Universal Xbox output remains available before and during every game launch.",
            _ => "Universal Windows mode is active. Games may use either the physical HID wheel or Wheel Pro's controller output."
        };
    }

    private void UpdateCompatibilityStatus()
    {
        if (CompatibilityStatus is null || OutputMode is null) return;
        CompatibilityStatus.Text = OutputMode.SelectedIndex == 0
            ? "Games that support DirectInput/HID wheels can use this device directly after its official driver is installed."
            : virtualControllerBridge.IsConnected
                ? OutputMode.SelectedIndex == 2
                    ? "Active: Wheel Pro is exposing a DualShock 4 controller. Cross selects, Circle goes back; steering and pedals are analogue."
                    : "Active: Wheel Pro is exposing an Xbox 360 controller. Steering is left stick; pedals and L2/R2 are triggers; mapped face buttons, paddles and D-pad are forwarded."
                : OutputMode.SelectedIndex == 2
                    ? "Ready: Wheel Pro exposes a PlayStation controller continuously while the app is running."
                    : "Ready: Wheel Pro exposes an Xbox controller continuously while the app is running.";
    }

    private void ChooseGameExecutable_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "Select the game executable", Filter = "Windows executables (*.exe)|*.exe" };
        if (picker.ShowDialog(this) != true) return;
        selectedGameExecutable = picker.FileName;
        gameSessionWasActive = IsSelectedGameRunning();
        ApplyGameProfileAutomatically();
        UpdateGameSessionStatus();
        SaveGameSessionSettings();
        StudioStatus.Text = "Game lock configured. Launch this game normally (including through Steam or EA app); Wheel Pro enables controller input only while its process is open.";
        _ = RefreshOnlineMappingAsync();
    }

    private void ClearGameLock_Click(object sender, RoutedEventArgs e)
    {
        selectedGameExecutable = null;
        gameSessionWasActive = false;
        virtualControllerBridge.Dispose();
        UpdateGameSessionStatus();
        UpdateCompatibilityStatus();
        SaveGameSessionSettings();
        GameIntelligenceStatus.Text = "Online game intelligence waits for a selected game.";
    }

    private void LoadGameSessionSettings()
    {
        loadingGameSessionSettings = true;
        try
        {
            var settings = GameSessionSettingsStore.Load();
            selectedGameExecutable = string.IsNullOrWhiteSpace(settings.GameExecutable) ? null : settings.GameExecutable;
            if (GamePlatform is not null && settings.Platform >= 0 && settings.Platform < GamePlatform.Items.Count)
                GamePlatform.SelectedIndex = settings.Platform;
            if (OutputMode is not null && settings.OutputMode >= 0 && settings.OutputMode < OutputMode.Items.Count)
                OutputMode.SelectedIndex = settings.OutputMode;
            gameSessionWasActive = IsSelectedGameRunning();
            ApplyGameProfileAutomatically();
            UpdateGameSessionStatus();
        }
        finally { loadingGameSessionSettings = false; }
    }

    private void SaveGameSessionSettings()
    {
        if (loadingGameSessionSettings || OutputMode is null || GamePlatform is null) return;
        GameSessionSettingsStore.Save(new GameSessionSettings
        {
            GameExecutable = selectedGameExecutable,
            Platform = GamePlatform.SelectedIndex,
            OutputMode = OutputMode.SelectedIndex
        });
    }

    private async void RefreshOnlineMapping_Click(object sender, RoutedEventArgs e) => await RefreshOnlineMappingAsync();

    private void ApplyGameProfileAutomatically()
    {
        if (string.IsNullOrWhiteSpace(selectedGameExecutable)) return;
        var gameProfile = BuiltInProfiles.FindGameProfile(selectedGameExecutable);
        if (gameProfile is null) return;
        if (OutputMode is not null) OutputMode.SelectedIndex = gameProfile.OutputMode;
        if (GameIntelligenceStatus is not null)
            GameIntelligenceStatus.Text = $"{gameProfile.Name} profile applied automatically. {gameProfile.Description}";
    }

    private async Task RefreshOnlineMappingAsync()
    {
        if (string.IsNullOrWhiteSpace(selectedGameExecutable))
        {
            GameIntelligenceStatus.Text = "Choose a game executable first. A separate learned profile is kept for every wheel and game pair.";
            return;
        }

        GameIntelligenceStatus.Text = "Checking the online game adviser for this wheel and game…";
        try
        {
            var controls = learnedButtonMap.Keys.Concat(learnedAxisMap.Keys).Distinct(StringComparer.OrdinalIgnoreCase);
            var recommendation = await gameMappingIntelligence.GetRecommendationAsync(selectedProfile, selectedGameExecutable, controls);
            gameWheelProfileStore.Save(selectedProfile, selectedGameExecutable, recommendation);
            GameIntelligenceStatus.Text = recommendation;
        }
        catch (Exception ex)
        {
            GameIntelligenceStatus.Text = $"Online mapping is unavailable. Local mapping stays active. ({ex.Message})";
        }
    }

    private bool IsSelectedGameRunning()
    {
        if (string.IsNullOrWhiteSpace(selectedGameExecutable)) return false;
        var processName = System.IO.Path.GetFileNameWithoutExtension(selectedGameExecutable);
        var processes = Process.GetProcessesByName(processName);
        try { return processes.Length > 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private void UpdateGameSessionStatus()
    {
        if (GameSessionStatus is null) return;
        if (string.IsNullOrWhiteSpace(selectedGameExecutable))
        {
            GameSessionStatus.Text = "Automatic universal mode: no game selection is required. Launch any game after Wheel Pro detects the wheel.";
            RefreshSteamInputStatus();
            return;
        }
        var name = System.IO.Path.GetFileName(selectedGameExecutable);
        var platform = GamePlatform?.SelectedIndex switch { 1 => "Steam", 2 => "EA app / EA Play", _ => "Windows" };
        GameSessionStatus.Text = gameSessionWasActive
            ? $"Detected: {name} is running through {platform}. Universal controller input remains active."
            : $"Optional profile saved for {name} through {platform}. Universal input also works without this selection.";
        RefreshSteamInputStatus();
    }

    private void AddSteamInputControls()
    {
        if (GameSessionStatus.Parent is not StackPanel panel) return;
        var heading = panel.Children.OfType<TextBlock>().FirstOrDefault();
        if (heading is not null) heading.Text = "AUTOMATIC GAME COMPATIBILITY";
        steamInputStatus = new TextBlock { Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#697589")), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        steamInputButton = new Button { Content = "Open Steam Input settings", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        steamInputButton.Click += (_, _) =>
        {
            try { SteamInputIntegration.OpenControllerSettings(); StudioStatus.Text = "Opened Steam Controller settings. Configure the selected game's Steam Input policy, then return to Wheel Pro."; }
            catch { StudioStatus.Text = "Steam could not be opened. Start Steam, then use its Settings > Controller page."; }
        };
        panel.Children.Add(steamInputStatus); panel.Children.Add(steamInputButton);
        RefreshSteamInputStatus();
    }

    private void AddExclusiveInputControl()
    {
        if (InstallVirtualDriverButton.Parent is not StackPanel panel) return;
        exclusiveInputButton = new Button
        {
            Content = ExclusiveInputIntegration.IsInstalled ? "Exclusive input support installed" : "Install exclusive input support",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 6, 0, 0),
            IsEnabled = !ExclusiveInputIntegration.IsInstalled
        };
        exclusiveInputButton.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(ExclusiveInputIntegration.OfficialDownloadUrl) { UseShellExecute = true }); }
            catch (Exception ex) { DriverStatus.Text = $"Could not open the official HidHide installer page: {ex.Message}"; }
        };
        panel.Children.Add(exclusiveInputButton);
    }

    private void AddPlatformDriverControls()
    {
        if (InstallVirtualDriverButton.Parent is not StackPanel panel) return;
        playStationDriverButton = new Button
        {
            Content = "Install PlayStation controller driver",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 6, 0, 0)
        };
        playStationDriverButton.Click += InstallVirtualDriver_Click;
        var xboxPosition = panel.Children.IndexOf(InstallVirtualDriverButton);
        panel.Children.Insert(Math.Max(0, xboxPosition + 1), playStationDriverButton);
    }

    private void RefreshSteamInputStatus()
    {
        if (steamInputStatus is not null) steamInputStatus.Text = SteamInputIntegration.GetStatus(OutputMode?.SelectedIndex is 1 or 2);
    }

    private async void InstallVirtualDriver_Click(object sender, RoutedEventArgs e)
    {
        if (SetupDiagnostics.IsVirtualControllerDriverInstalled())
        {
            RefreshVirtualDriverStatus();
            StudioStatus.Text = "The virtual-controller driver is already installed. No download is needed.";
            return;
        }
        try
        {
            InstallVirtualDriverButton.IsEnabled = false;
            DriverStatus.Text = "Checking the official virtual-controller driver release for the newest setup…";
            var installer = await virtualDriverDownload.DownloadLatestAsync();
            DriverStatus.Text = $"Latest virtual-controller driver downloaded: {System.IO.Path.GetFileName(installer)}.";
            var install = MessageBox.Show(this,
                "The latest virtual-controller driver has been downloaded. Install it now? Windows will show its own administrator and signature prompts.",
                "Install virtual-controller driver", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (install == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true });
                StudioStatus.Text = "Driver installer launched. Complete the Windows prompts, then restart Wheel Pro.";
            }
            else StudioStatus.Text = "Driver download is ready locally. Use Download latest virtual-controller driver whenever you want to install it.";
        }
        catch (Exception ex)
        {
            DriverStatus.Text = "Could not download the latest virtual-controller driver. Check the internet connection and try again.";
            StudioStatus.Text = $"Driver download failed: {ex.Message}";
        }
        finally { RefreshVirtualDriverStatus(); }
    }

    private void RefreshVirtualDriverStatus()
    {
        var installed = SetupDiagnostics.IsVirtualControllerDriverInstalled();
        InstallVirtualDriverButton.Content = installed ? "Xbox controller driver installed" : "Install Xbox controller driver";
        InstallVirtualDriverButton.IsEnabled = !installed;
        if (playStationDriverButton is not null)
        {
            playStationDriverButton.Content = installed ? "PlayStation controller driver installed" : "Install PlayStation controller driver";
            playStationDriverButton.IsEnabled = !installed;
        }
        if (installed && DriverStatus is not null)
            DriverStatus.Text = "Xbox and PlayStation virtual-controller support is installed. Both modes use the same verified Windows controller bus.";
    }

    private void OpenOfficialDriverPage_Click(object sender, RoutedEventArgs e)
    {
        var url = WheelCatalog.GetOfficialSupportUrl(selectedProfile.Brand);
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            StudioStatus.Text = $"Opened the official {selectedProfile.Brand} support page. Review and install its signed driver, then reconnect the wheel.";
        }
        catch (Exception ex)
        {
            StudioStatus.Text = $"Could not open the official support page: {ex.Message}";
        }
    }

    private void RefreshControllers_Click(object sender, RoutedEventArgs e)
    {
        StudioStatus.Text = "Connect the wheel in PC mode. HID capture requires the dedicated bridge service.";
    }

    private void PrepareDriver_Click(object sender, RoutedEventArgs e)
    {
        var cacheDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WheelPro", "device-cache");
        System.IO.Directory.CreateDirectory(cacheDirectory);
        var cacheName = $"{selectedProfile.Brand}-{selectedProfile.Model}".ToLowerInvariant().Replace(' ', '-').Replace('/', '-');
        var cachePath = System.IO.Path.Combine(cacheDirectory, $"{cacheName}.json");
        var existingFavourite = SavedWheels.FirstOrDefault(wheel => wheel.CachePath == cachePath)?.Favourite ?? false;
        var deviceCache = new { selectedProfile.Brand, selectedProfile.Model, selectedProfile.HasForceFeedback, selectedProfile.RequiresVendorDriver, selectedProfile.Rotation, Favourite = existingFavourite, cachedAtUtc = DateTime.UtcNow };
        System.IO.File.WriteAllText(cachePath, JsonSerializer.Serialize(deviceCache, new JsonSerializerOptions { WriteIndented = true }));
        DriverStatus.Text = selectedProfile.RequiresVendorDriver
            ? "Profile cached. Install the official vendor driver once to enable full wheel and force-feedback support."
            : "Profile cached locally. It will be ready when this wheel is selected again.";
        StudioStatus.Text = $"Saved verified device metadata to {cachePath}";
        LoadSavedWheels();
    }

    private void ExportProfile_Click(object sender, RoutedEventArgs e)
    {
        var profile = new { controller = $"{selectedProfile.Brand} {selectedProfile.Model}", sensitivity = SensitivitySlider.Value, deadzone = DeadzoneSlider.Value, outputMode = ((ComboBoxItem)OutputMode.SelectedItem).Content, inputTest = "Windows live controller input enabled" };
        var fileName = $"WheelPro-{selectedProfile.Brand}-{selectedProfile.Model}".Replace('/', '-').Replace(' ', '-') + "-profile.json";
        var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), fileName);
        System.IO.File.WriteAllText(path, JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
        StudioStatus.Text = $"Profile exported to {path}";
    }

    private enum MappingKind { Axis, SteerLeft, SteerRight, Button, Dpad, Dial }
    private enum ControllerVisualLayout { PlayStation, Xbox, GenericHid }
    private enum MappingSection { Pedals, Steering, Buttons }
    private enum PedalMappingStage { None, ConfirmNeutral, CaptureFull, ConfirmFull }
    private enum SteeringMappingStage { None, ConfirmCentre, ConfirmNinety }
    private sealed record InputMappingStep(string Key, string Instruction, MappingKind Kind);
    private sealed class StoredInputMap
    {
        public string? WheelName { get; set; }
        public string? ControllerType { get; set; }
        public int? OutputMode { get; set; }
        public double? Sensitivity { get; set; }
        public double? Deadzone { get; set; }
        public Dictionary<string, uint>? Buttons { get; set; }
        public Dictionary<string, string>? Axes { get; set; }
        public Dictionary<string, long>? PedalTravel { get; set; }
        public Dictionary<string, int>? PedalDirection { get; set; }
        public Dictionary<string, uint>? PedalNeutral { get; set; }
        public Dictionary<string, long>? SteeringTravel { get; set; }
        public uint? SteeringCenter { get; set; }
    }
}
