namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// EP0011 (US0067): Calculates the adaptive frame-skip count for camera decode throttling.
/// A higher skip count means fewer frames are forwarded to the barcode decoder, reducing CPU load
/// as more cameras are added.
/// </summary>
public class AdaptiveDecodeThrottle
{
    private const int MinThrottle = 3;

    /// <summary>
    /// Returns the frame-skip count for a given number of active cameras.
    /// The platform handler only forwards a barcode event every N-th frame where N = Calculate(count).
    /// </summary>
    /// <param name="activeCameraCount">Number of cameras currently active (scanning or starting).</param>
    /// <returns>Frame skip count (minimum 3).</returns>
    public static int Calculate(int activeCameraCount)
    {
        var value = activeCameraCount switch
        {
            <= 0 => 5,
            1 => 5,
            2 => 5,
            3 => 8,
            4 => 8,
            5 => 10,
            6 => 12,
            7 => 13,
            _ => 15   // 8+ cameras
        };

        return Math.Max(value, MinThrottle);
    }
}
