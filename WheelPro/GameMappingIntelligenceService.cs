using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace WheelPro;

/// <summary>
/// Optional, privacy-preserving online adviser for a wheel + game pair. It sends only
/// the wheel model, capability summary, game executable name and existing map labels.
/// Raw input values, Windows account details and driver files never leave the PC.
/// </summary>
public sealed class GameMappingIntelligenceService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(25) };

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WHEELPRO_AI_API_KEY"));

    public async Task<string> GetRecommendationAsync(WheelProfile wheel, string gameExecutable, IEnumerable<string> mappedControls, CancellationToken cancellationToken = default)
    {
        var key = Environment.GetEnvironmentVariable("WHEELPRO_AI_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
            return "Online AI is ready but not connected. Set the WHEELPRO_AI_API_KEY user environment variable, restart Wheel Pro, then select the game again.";

        var endpoint = Environment.GetEnvironmentVariable("WHEELPRO_AI_ENDPOINT")?.TrimEnd('/') ?? "https://api.openai.com/v1";
        var model = Environment.GetEnvironmentVariable("WHEELPRO_AI_MODEL") ?? "gpt-4.1-mini";
        var gameName = Path.GetFileNameWithoutExtension(gameExecutable);
        var capabilities = $"{wheel.Rotation} rotation, {wheel.PedalCount} pedals, {wheel.ButtonCount} buttons, H shifter: {wheel.HasHShifter}, force feedback: {wheel.HasForceFeedback}";
        var controls = string.Join(", ", mappedControls.Take(40));
        var prompt = $"You are a racing-wheel compatibility adviser. Recommend safe Windows controller mapping defaults for this single game and wheel. Do not invent driver downloads, force-feedback APIs, or unsupported controls. State whether the game should prefer native wheel/DirectInput or a virtual Xbox controller, then give concise mappings for steering, accelerator, brake, paddles, face buttons and D-pad. Wheel: {wheel.Brand} {wheel.Model}; capabilities: {capabilities}; game executable: {gameName}; already learned controls: {controls}.";
        var body = JsonSerializer.Serialize(new { model, input = prompt, max_output_tokens = 420 });
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await Client.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            return $"Online AI could not provide a mapping ({(int)response.StatusCode}). Your local wheel map remains unchanged.";

        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.TryGetProperty("output_text", out var outputText) && !string.IsNullOrWhiteSpace(outputText.GetString()))
            return outputText.GetString()!;
        return "Online AI returned no usable mapping. Your local wheel map remains unchanged.";
    }
}

public sealed class GameWheelProfileStore
{
    private static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WheelPro", "game-profiles");

    public void Save(WheelProfile wheel, string gameExecutable, string recommendation)
    {
        Directory.CreateDirectory(DirectoryPath);
        var fileName = $"{Safe(wheel.Brand)}-{Safe(wheel.Model)}--{Safe(Path.GetFileNameWithoutExtension(gameExecutable))}.json";
        var data = new
        {
            wheel = $"{wheel.Brand} {wheel.Model}",
            game = Path.GetFileName(gameExecutable),
            updatedUtc = DateTime.UtcNow,
            recommendation
        };
        File.WriteAllText(Path.Combine(DirectoryPath, fileName), JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string Safe(string value) => string.Concat(value.Select(c => char.IsLetterOrDigit(c) ? c : '-')).Trim('-');
}

/// <summary>Stores compact, local-only calibration observations for one wheel.</summary>
public sealed class CalibrationLearningStore
{
    private static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WheelPro", "calibration-history");
    private WheelCalibrationSnapshot? snapshot;
    private DateTime lastSavedUtc;

    public string Observe(WheelProfile wheel, WheelInputState state, WheelInputState baseline)
    {
        snapshot ??= Load(wheel) ?? new WheelCalibrationSnapshot
        {
            Wheel = $"{wheel.Brand} {wheel.Model}", SteeringMinimum = state.X, SteeringMaximum = state.X,
            AcceleratorMinimum = state.Y, AcceleratorMaximum = state.Y, BrakeMinimum = state.Z, BrakeMaximum = state.Z
        };
        snapshot.Samples++;
        snapshot.SteeringMinimum = Math.Min(snapshot.SteeringMinimum, state.X); snapshot.SteeringMaximum = Math.Max(snapshot.SteeringMaximum, state.X);
        snapshot.AcceleratorMinimum = Math.Min(snapshot.AcceleratorMinimum, state.Y); snapshot.AcceleratorMaximum = Math.Max(snapshot.AcceleratorMaximum, state.Y);
        snapshot.BrakeMinimum = Math.Min(snapshot.BrakeMinimum, state.Z); snapshot.BrakeMaximum = Math.Max(snapshot.BrakeMaximum, state.Z);
        snapshot.LastCentreDrift = Ratio(Math.Abs((long)state.X - baseline.X), (long)state.XMax - state.XMin);
        snapshot.UpdatedUtc = DateTime.UtcNow;
        if (DateTime.UtcNow - lastSavedUtc > TimeSpan.FromSeconds(30)) Save(wheel);
        var steeringTravel = Ratio((long)snapshot.SteeringMaximum - snapshot.SteeringMinimum, (long)state.XMax - state.XMin);
        var acceleratorTravel = Ratio((long)snapshot.AcceleratorMaximum - snapshot.AcceleratorMinimum, (long)state.YMax - state.YMin);
        var brakeTravel = Ratio((long)snapshot.BrakeMaximum - snapshot.BrakeMinimum, (long)state.ZMax - state.ZMin);
        return $"Local calibration learning: centre drift {snapshot.LastCentreDrift:P1}; travel observed — steering {steeringTravel:P0}, accelerator {acceleratorTravel:P0}, brake {brakeTravel:P0}.";
    }

    public void Save(WheelProfile wheel)
    {
        if (snapshot is null) return;
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(Path.Combine(DirectoryPath, Safe($"{wheel.Brand}-{wheel.Model}") + ".json"), JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        lastSavedUtc = DateTime.UtcNow;
    }

    private static WheelCalibrationSnapshot? Load(WheelProfile wheel)
    {
        try { var file = Path.Combine(DirectoryPath, Safe($"{wheel.Brand}-{wheel.Model}") + ".json"); return File.Exists(file) ? JsonSerializer.Deserialize<WheelCalibrationSnapshot>(File.ReadAllText(file)) : null; }
        catch { return null; }
    }
    private static double Ratio(long amount, long range) => range <= 0 ? 0 : Math.Clamp(amount / (double)range, 0, 1);
    private static string Safe(string value) => string.Concat(value.Select(c => char.IsLetterOrDigit(c) ? c : '-')).Trim('-');
}

public sealed class WheelCalibrationSnapshot
{
    public string Wheel { get; set; } = "";
    public long Samples { get; set; }
    public uint SteeringMinimum { get; set; }
    public uint SteeringMaximum { get; set; }
    public uint AcceleratorMinimum { get; set; }
    public uint AcceleratorMaximum { get; set; }
    public uint BrakeMinimum { get; set; }
    public uint BrakeMaximum { get; set; }
    public double LastCentreDrift { get; set; }
    public DateTime UpdatedUtc { get; set; }
}
