using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Disctalk.Core.Http;

/// <summary>
/// Checks that every value in a <see cref="HeaderProfile"/> describes the same browser, version, platform and
/// locale. A mismatch (say, a Chrome 155 User-Agent with Sec-Ch-Ua for 120) is exactly what makes a client stand out.
/// </summary>
public static partial class HeaderProfileValidator
{
    public static IReadOnlyList<string> Validate(HeaderProfile profile)
    {
        var problems = new List<string>();
        var props = profile.Template;
        string ua = profile.UserAgent;

        foreach (var (name, _) in profile.Headers)
        {
            if (!HeaderNameRegex().IsMatch(name)) problems.Add($"Header name '{name}' is malformed.");
        }

        if (string.IsNullOrEmpty(ua)) problems.Add("User-Agent header is missing.");

        string? Prop(string key) => props[key] is JsonValue v && v.TryGetValue(out string? s) ? s : null;

        var browserUa = Prop("browser_user_agent");
        if (browserUa != null && browserUa != ua)
            problems.Add("superProperties.browser_user_agent does not match the User-Agent header.");

        var browser = Prop("browser");
        var browserVersion = Prop("browser_version");
        var os = Prop("os");

        // Browser family and version.
        var chrome = ChromeRegex().Match(ua);
        var electron = ElectronRegex().Match(ua);
        var firefox = FirefoxRegex().Match(ua);
        var edge = EdgeRegex().Match(ua);
        string? secChUa = profile.GetHeader("Sec-Ch-Ua");

        if (electron.Success)
        {
            if (browser != "Discord Client") problems.Add($"Electron User-Agent but superProperties.browser is '{browser}' (expected 'Discord Client').");
            if (browserVersion != null && browserVersion != electron.Groups[1].Value)
                problems.Add($"superProperties.browser_version '{browserVersion}' does not match Electron/{electron.Groups[1].Value} in the User-Agent.");
            var discord = DiscordDesktopRegex().Match(ua);
            var clientVersion = Prop("client_version");
            if (discord.Success && clientVersion != null && clientVersion != discord.Groups[1].Value)
                problems.Add($"superProperties.client_version '{clientVersion}' does not match discord/{discord.Groups[1].Value} in the User-Agent.");
        }
        else if (firefox.Success)
        {
            if (browser != "Firefox") problems.Add($"Firefox User-Agent but superProperties.browser is '{browser}'.");
            if (browserVersion != null && browserVersion != firefox.Groups[1].Value)
                problems.Add($"superProperties.browser_version '{browserVersion}' does not match Firefox/{firefox.Groups[1].Value}.");
            if (secChUa != null) problems.Add("Firefox does not send Sec-Ch-Ua client hints, but the profile has them.");
        }
        else if (chrome.Success)
        {
            string major = chrome.Groups["major"].Value;
            string expectedBrowser = edge.Success ? "Edge" : "Chrome";
            if (browser != expectedBrowser) problems.Add($"{expectedBrowser} User-Agent but superProperties.browser is '{browser}'.");
            string uaVersion = edge.Success ? edge.Groups[1].Value : chrome.Groups["full"].Value;
            if (browserVersion != null && browserVersion != uaVersion)
                problems.Add($"superProperties.browser_version '{browserVersion}' does not match the User-Agent version '{uaVersion}'.");

            if (secChUa == null) problems.Add("Chromium User-Agent but Sec-Ch-Ua is missing.");
            else
            {
                var brands = new Dictionary<string, string>();
                foreach (Match m in BrandRegex().Matches(secChUa)) brands.TryAdd(m.Groups[1].Value, m.Groups[2].Value);
                if (!brands.TryGetValue("Chromium", out var chromiumV) || chromiumV != major)
                    problems.Add($"Sec-Ch-Ua Chromium version '{chromiumV}' does not match Chrome/{major} in the User-Agent.");
                string brand = edge.Success ? "Microsoft Edge" : "Google Chrome";
                if (brands.TryGetValue(brand, out var brandV) && brandV != (edge.Success ? edge.Groups[1].Value.Split('.')[0] : major))
                    problems.Add($"Sec-Ch-Ua '{brand}' version '{brandV}' does not match the User-Agent.");
            }
        }

        // Platform.
        string? uaOs = ua.Contains("Windows NT") ? "Windows" : ua.Contains("Mac OS X") ? "Mac OS X" : ua.Contains("Android") ? "Android" : ua.Contains("Linux") ? "Linux" : null;
        if (uaOs != null && os != null && os != uaOs)
            problems.Add($"superProperties.os '{os}' does not match the User-Agent platform '{uaOs}'.");
        var platformHeader = profile.GetHeader("Sec-Ch-Ua-Platform")?.Trim('"');
        if (platformHeader != null && uaOs != null)
        {
            string expected = uaOs switch { "Mac OS X" => "macOS", _ => uaOs };
            if (platformHeader != expected) problems.Add($"Sec-Ch-Ua-Platform '{platformHeader}' does not match the User-Agent platform '{expected}'.");
        }
        var mobileHeader = profile.GetHeader("Sec-Ch-Ua-Mobile");
        if (mobileHeader != null && mobileHeader != (ua.Contains("Mobile") ? "?1" : "?0"))
            problems.Add($"Sec-Ch-Ua-Mobile '{mobileHeader}' does not match the User-Agent.");
        if (ua.Contains("WOW64") && Prop("app_arch") is { } appArch && appArch != "ia32")
            problems.Add("User-Agent says WOW64 (32-bit app) but superProperties.app_arch is not 'ia32'.");

        // Locale and timezone.
        var locale = profile.GetHeader("X-Discord-Locale");
        var systemLocale = Prop("system_locale");
        if (locale != null && systemLocale != null && locale != systemLocale)
            problems.Add($"X-Discord-Locale '{locale}' does not match superProperties.system_locale '{systemLocale}'.");
        var acceptLanguage = profile.GetHeader("Accept-Language");
        if (acceptLanguage != null && locale != null && acceptLanguage.Split(',')[0].Split(';')[0].Trim() != locale)
            problems.Add($"Accept-Language '{acceptLanguage}' does not start with the locale '{locale}'.");
        var tz = profile.GetHeader("X-Discord-Timezone");
        if (tz != null && !TimeZoneInfo.TryFindSystemTimeZoneById(tz, out _))
            problems.Add($"X-Discord-Timezone '{tz}' is not a known time zone.");

        if (profile.FallbackBuildNumber <= 0) problems.Add("No fallback build number (buildNumber.fallback).");

        return problems;
    }

    [GeneratedRegex(@"^[A-Za-z0-9!#$%&'*+.^_`|~-]+$")] private static partial Regex HeaderNameRegex();
    [GeneratedRegex(@"Chrome/(?<full>(?<major>\d+)[\d.]*)")] private static partial Regex ChromeRegex();
    [GeneratedRegex(@"Electron/([\d.]+)")] private static partial Regex ElectronRegex();
    [GeneratedRegex(@"discord/([\d.]+)")] private static partial Regex DiscordDesktopRegex();
    [GeneratedRegex(@"Firefox/([\d.]+)")] private static partial Regex FirefoxRegex();
    [GeneratedRegex(@"Edg/([\d.]+)")] private static partial Regex EdgeRegex();
    [GeneratedRegex(@"""([^""]+)"";v=""([^""]+)""")] private static partial Regex BrandRegex();
}
