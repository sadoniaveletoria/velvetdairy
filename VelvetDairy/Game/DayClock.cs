using System;

namespace VelvetDairy.Game;

/// <summary>
/// The daily reset: midnight Central time (America/Chicago, following daylight saving), the same as the server.
/// Quotas, daily goals, being tired and other "per day" things roll over then.
/// </summary>
public static class DayClock
{
    private static readonly TimeZoneInfo Zone = FindZone();

    private static TimeZoneInfo FindZone()
    {
        foreach (var id in new[] { "Central Standard Time", "America/Chicago" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (Exception) { /* try the next name */ }
        }

        return TimeZoneInfo.CreateCustomTimeZone("Central", TimeSpan.FromHours(-6), "Central", "Central");
    }

    /// <summary>Today's date in Central time, as "yyyy-MM-dd".</summary>
    public static string Today => Now.ToString("yyyy-MM-dd");

    /// <summary>The time in Central time right now.</summary>
    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);

    /// <summary>A unix time as a Central-time date and time, e.g. "Fri Dec 4, 12:00 AM".</summary>
    public static string Show(long unix) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime, Zone).ToString("ddd MMM d, h:mm tt", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>How long until the next daily reset.</summary>
    public static TimeSpan UntilReset
    {
        get
        {
            var next = DateTime.SpecifyKind(Now.Date.AddDays(1), DateTimeKind.Unspecified);
            try { return TimeZoneInfo.ConvertTimeToUtc(next, Zone) - DateTime.UtcNow; }
            catch (ArgumentException) { return next - Now; }
        }
    }

    /// <summary>How the reset is shown to players.</summary>
    public const string ResetName = "midnight Central time";
}
