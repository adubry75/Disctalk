using System.Text;
using System.Text.RegularExpressions;

namespace DisctalkClient.Core.Security;

/// <summary>Scrubs bot tokens and webhook tokens out of any text before it reaches a log.</summary>
public static partial class SecretRedactor
{
    public const string Mask = "[REDACTED]";

    [GeneratedRegex(@"(?<![A-Za-z0-9_-])[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{25,}(?![A-Za-z0-9_-])")]
    private static partial Regex BotTokenPattern();

    [GeneratedRegex(@"(webhooks/\d+/)[A-Za-z0-9_-]{20,}")]
    private static partial Regex WebhookTokenPattern();

    [GeneratedRegex(@"(Bot\s+)\S+", RegexOptions.IgnoreCase)]
    private static partial Regex AuthorizationPattern();

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var result = BotTokenPattern().Replace(text, Mask);
        result = WebhookTokenPattern().Replace(result, "$1" + Mask);
        result = AuthorizationPattern().Replace(result, "$1" + Mask);
        return result;
    }
}

public static class BotTokenValidator
{
    /// <summary>
    /// Shape check only (three base64url segments, the first decoding to a numeric id). Catches pasted junk or a
    /// client secret before we hit the gateway. Never include the token in the returned message.
    /// </summary>
    public static string? Validate(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return "Paste your bot token.";
        token = token.Trim();
        if (token.StartsWith("Bot ", StringComparison.OrdinalIgnoreCase)) token = token[4..];
        var parts = token.Split('.');
        if (parts.Length != 3 || parts.Any(p => p.Length == 0 || !p.All(IsBase64UrlChar)))
            return "That doesn't look like a bot token (expected three dot-separated parts). Copy it from Developer Portal → Bot → Reset Token.";
        if (DecodeId(parts[0]) is null)
            return "That doesn't look like a bot token. Make sure you copied the Bot token, not the Client Secret or Public Key.";
        return null;
    }

    public static string Normalize(string token)
    {
        token = token.Trim();
        return token.StartsWith("Bot ", StringComparison.OrdinalIgnoreCase) ? token[4..].Trim() : token;
    }

    static bool IsBase64UrlChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_';

    static ulong? DecodeId(string segment)
    {
        var s = segment.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        try
        {
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(s));
            return ulong.TryParse(text, out var id) ? id : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
