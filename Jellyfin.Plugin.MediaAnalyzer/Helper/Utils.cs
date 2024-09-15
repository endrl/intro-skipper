using System;

namespace Jellyfin.Plugin.MediaAnalyzer;

/// <summary>
/// Convert between Ticks and other time representations.
/// </summary>
public static class Utils
{
    /// <summary>
    /// Convert Seconds to Ticks.
    /// </summary>
    /// <param name="value">seconds.</param>
    /// <returns>Time in ticks.</returns>
    public static long SToTicks(double value)
    {
        return TimeSpan.FromSeconds(value).Ticks;
    }

    /// <summary>
    /// Convert Ticks to Seconds.
    /// </summary>
    /// <param name="value">ticks.</param>
    /// <returns>Time in seconds.</returns>
    public static double TicksToS(long value)
    {
        return TimeSpan.FromTicks(value).Seconds;
    }
}
