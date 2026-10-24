using System.Globalization;

namespace DisctalkClient.Core.Settings;

/// <summary>
/// All user-visible times go through here so the timestamp format setting (default <c>h:mm:ss tt</c>) applies everywhere:
/// message headers, compact rows, edited markers and &lt;t:...&gt; markdown timestamps.
/// </summary>
public sealed class TimestampFormatter(SettingsService settings, TimeProvider time, TimeZoneInfo? timeZone = null, CultureInfo? culture = null)
{
    TimeZoneInfo Zone => timeZone ?? time.LocalTimeZone;
    CultureInfo Culture => culture ?? CultureInfo.CurrentCulture;
    string TimeFormat => settings.Current.TimestampFormat;

    DateTimeOffset Local(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, Zone);

    /// <summary>Just the time, e.g. <c>3:04:05 PM</c>.</summary>
    public string FormatTime(DateTimeOffset value) => Local(value).ToString(TimeFormat, Culture);

    /// <summary>Message header style: <c>Today at 3:04:05 PM</c>, <c>Yesterday at ...</c>, or <c>10/22/2026 3:04:05 PM</c>.</summary>
    public string FormatMessageTimestamp(DateTimeOffset value)
    {
        var local = Local(value);
        var today = Local(time.GetUtcNow()).Date;
        var t = local.ToString(TimeFormat, Culture);
        if (local.Date == today) return $"Today at {t}";
        if (local.Date == today.AddDays(-1)) return $"Yesterday at {t}";
        return $"{local.ToString("d", Culture)} {t}";
    }

    /// <summary>Full tooltip text, e.g. <c>Thursday, October 22, 2026 3:04:05 PM</c>.</summary>
    public string FormatFull(DateTimeOffset value)
    {
        var local = Local(value);
        return $"{local.ToString("D", Culture)} {local.ToString(TimeFormat, Culture)}";
    }

    public string FormatDateDivider(DateTimeOffset value) => Local(value).ToString("D", Culture);

    /// <summary>Renders a Discord <c>&lt;t:unix:style&gt;</c> timestamp. Time styles use the user's format.</summary>
    public string FormatDiscordTimestamp(long unixSeconds, char style)
    {
        var value = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        var local = Local(value);
        return style switch
        {
            't' or 'T' => local.ToString(TimeFormat, Culture),
            'd' => local.ToString("d", Culture),
            'D' => local.ToString("D", Culture),
            'F' => $"{local.ToString("dddd, ", Culture)}{local.ToString("D", Culture)} {local.ToString(TimeFormat, Culture)}",
            'R' => FormatRelative(value),
            _ => $"{local.ToString("D", Culture)} {local.ToString(TimeFormat, Culture)}", // 'f' and default
        };
    }

    public string FormatRelative(DateTimeOffset value)
    {
        var delta = value - time.GetUtcNow();
        var future = delta > TimeSpan.Zero;
        var abs = delta.Duration();
        string amount = abs.TotalSeconds < 60 ? Plural((int)abs.TotalSeconds, "second")
            : abs.TotalMinutes < 60 ? Plural((int)abs.TotalMinutes, "minute")
            : abs.TotalHours < 24 ? Plural((int)abs.TotalHours, "hour")
            : abs.TotalDays < 30 ? Plural((int)abs.TotalDays, "day")
            : abs.TotalDays < 365 ? Plural((int)(abs.TotalDays / 30), "month")
            : Plural((int)(abs.TotalDays / 365), "year");
        return future ? $"in {amount}" : $"{amount} ago";

        static string Plural(int n, string unit) => n == 1 ? $"1 {unit}" : $"{n} {unit}s";
    }
}
