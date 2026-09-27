namespace WheelPro;

public static class SteeringResponse
{
    /// <summary>
    /// Converts calibrated wheel travel directly to controller-stick travel.
    /// This is a straight line: half the recorded travel is half stick and the
    /// recorded 90-degree point is full stick. No easing or acceleration curve.
    /// </summary>
    public static double DirectLinear(long rawDelta, long calibratedTravel, double gain = 1)
    {
        if (calibratedTravel <= 0) return 0;
        return Math.Clamp(rawDelta / (double)calibratedTravel * gain, -1, 1);
    }

    public static double ApplyLinearGain(double normalizedSteering, double gain) =>
        Math.Clamp(normalizedSteering * gain, -1, 1);
}
