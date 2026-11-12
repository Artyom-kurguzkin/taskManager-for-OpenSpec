namespace BoardSync.Sync;

/// <summary>
/// A hybrid logical clock as a fixed-width string, so the greater string is the later change: utc milliseconds (15 digits), counter (4 digits), device id.
/// </summary>
internal static class SyncClock
{
    private static string Make(long ms, int counter, string device) => $"{ms:D15}-{counter:D4}-{device}";

    private static long Ms(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    /// <summary>The stamp of a change made at <paramref name="changedUtc"/> (a file's last-write time), never later than <paramref name="now"/>.</summary>
    internal static string Stamp(DateTime changedUtc, DateTime now, string device) => Make(Ms(changedUtc < now ? changedUtc : now), 0, device);

    /// <summary>The stamp of a change noticed now (a deleted file has no last-write time).</summary>
    internal static string Stamp(DateTime now, string device) => Make(Ms(now), 0, device);

    /// <summary><paramref name="candidate"/> when it is already greater than <paramref name="floor"/>, otherwise the floor's successor: every clock issued for a path beats the one it replaces, even at the end of the range.</summary>
    internal static string After(string candidate, string? floor, string device)
    {
        if (floor is null || string.CompareOrdinal(candidate, floor) > 0) return candidate;
        if (floor.Length < 21 || !long.TryParse(floor.AsSpan(0, 15), out long ms) || !int.TryParse(floor.AsSpan(16, 4), out int counter)) return floor + "-" + device;
        if (counter < 9999) return Make(ms, counter + 1, device);
        if (ms < MaxMs) return Make(ms + 1, 0, device);
        return floor + "-" + device;      // the fixed-width fields are full: a longer clock still sorts after the floor
    }

    internal static bool IsLater(string clock, string than) => string.CompareOrdinal(clock, than) > 0;

    private const long MaxMs = 999_999_999_999_999;

    /// <summary>How far ahead of this computer's clock a manifest clock is believed.</summary>
    internal static readonly TimeSpan MaxSkew = TimeSpan.FromHours(24);

    /// <summary>
    /// 15 digits, a dash, 4 digits, a dash and a device id of 1 to 64 letters, digits, dot, dash or underscore: the only clocks this engine writes.
    /// </summary>
    internal static bool IsValid(string? clock)
    {
        if (clock is null || clock.Length < 22 || clock.Length > 21 + 64 || clock[15] != '-' || clock[20] != '-') return false;
        for (int i = 0; i < 15; i++) if (!char.IsAsciiDigit(clock[i])) return false;
        for (int i = 16; i < 20; i++) if (!char.IsAsciiDigit(clock[i])) return false;
        for (int i = 21; i < clock.Length; i++) if (!(char.IsAsciiLetterOrDigit(clock[i]) || clock[i] is '.' or '-' or '_')) return false;
        return true;
    }

    /// <summary>
    /// A clock more than <see cref="MaxSkew"/> ahead of <paramref name="now"/> counts as "now" (with no device id, so a tie with this computer's own change goes to this computer's):
    /// a far-future clock neither wins every clash for good nor locks the path.
    /// </summary>
    internal static string Clamp(string clock, DateTime now)
    {
        long nowMs = Ms(now);
        return long.Parse(clock.AsSpan(0, 15)) > nowMs + (long)MaxSkew.TotalMilliseconds ? Make(nowMs, 0, "") : clock;
    }
}
