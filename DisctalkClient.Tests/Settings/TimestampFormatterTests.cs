using DisctalkClient.Core.Abstractions;
using DisctalkClient.Core.Settings;
using Microsoft.Extensions.Time.Testing;

namespace DisctalkClient.Tests.Settings;

public class TimestampFormatterTests
{
    readonly SettingsService _settings = new(new InMemorySettingsStore());
    readonly FakeTimeProvider _time = new(TestData.Now);
    readonly TimestampFormatter _formatter;

    public TimestampFormatterTests() => _formatter = new TimestampFormatter(_settings, _time, TimeZoneInfo.Utc, TestData.EnUs);

    [Fact]
    public void Default_format_includes_seconds() =>
        Assert.Equal("3:04:05 PM", _formatter.FormatTime(TestData.Now));

    [Fact]
    public void Message_header_today_yesterday_and_older()
    {
        Assert.Equal("Today at 9:08:07 AM", _formatter.FormatMessageTimestamp(new DateTimeOffset(2026, 10, 24, 9, 8, 7, TimeSpan.Zero)));
        Assert.Equal("Yesterday at 11:59:59 PM", _formatter.FormatMessageTimestamp(new DateTimeOffset(2026, 10, 23, 23, 59, 59, TimeSpan.Zero)));
        Assert.Equal("10/1/2026 12:00:01 AM", _formatter.FormatMessageTimestamp(new DateTimeOffset(2026, 10, 1, 0, 0, 1, TimeSpan.Zero)));
    }

    [Fact]
    public async Task Format_setting_applies_everywhere()
    {
        await _settings.UpdateAsync(s => s with { TimestampFormat = "HH:mm:ss" });
        Assert.Equal("15:04:05", _formatter.FormatTime(TestData.Now));
        Assert.Equal("Today at 15:04:05", _formatter.FormatMessageTimestamp(TestData.Now));
        Assert.Equal("15:04:05", _formatter.FormatDiscordTimestamp(TestData.Now.ToUnixTimeSeconds(), 't'));
        Assert.EndsWith("15:04:05", _formatter.FormatFull(TestData.Now));
    }

    [Fact]
    public void Local_time_zone_is_respected()
    {
        var tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        var formatter = new TimestampFormatter(_settings, _time, tokyo, TestData.EnUs);
        // 15:04:05 UTC is 00:04:05 the next day in Tokyo, which is "today" there.
        Assert.Equal("Today at 12:04:05 AM", formatter.FormatMessageTimestamp(TestData.Now));
    }

    [Theory]
    [InlineData('t', "3:04:05 PM")]
    [InlineData('T', "3:04:05 PM")]
    [InlineData('d', "10/24/2026")]
    [InlineData('D', "Saturday, October 24, 2026")]
    [InlineData('f', "October 24, 2026 3:04:05 PM")]
    [InlineData('F', "Saturday, October 24, 2026 3:04:05 PM")]
    public void Discord_timestamp_styles(char style, string expected) =>
        Assert.Equal(expected, _formatter.FormatDiscordTimestamp(TestData.Now.ToUnixTimeSeconds(), style));

    [Theory]
    [InlineData(-30, "30 seconds ago")]
    [InlineData(-60, "1 minute ago")]
    [InlineData(-7200, "2 hours ago")]
    [InlineData(86400 * 3, "in 3 days")]
    [InlineData(-86400 * 400, "1 year ago")]
    public void Relative_times(int seconds, string expected) =>
        Assert.Equal(expected, _formatter.FormatRelative(TestData.Now.AddSeconds(seconds)));
}
