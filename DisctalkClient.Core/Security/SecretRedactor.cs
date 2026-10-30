using System.Text;
using System.Text.RegularExpressions;
using DisctalkClient.Core.Abstractions;

namespace DisctalkClient.Core.Security;

/// <summary>Scrubs bot/user tokens and webhook tokens out of any text before it reaches a log.</summary>
public static partial class SecretRedactor
{
    public const string Mask = "[REDACTED]";

    [GeneratedRegex(@"(?<![A-Za-z0-9_-])[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{25,}(?![A-Za-z0-9_-])")]
    private static partial Regex BotTokenPattern();

    [GeneratedRegex(@"(webhooks/\d+/)[A-Za-z0-9_-]{20,}")]
    private static partial Regex WebhookTokenPattern();

    [GeneratedRegex(@"(Bot\s+)\S+", RegexOptions.IgnoreCase)]
    private static partial Regex AuthorizationPattern();

    // Legacy user tokens: "mfa." plus one long segment.
    [GeneratedRegex(@"(?<![A-Za-z0-9_-])mfa\.[A-Za-z0-9_-]{20,}")]
    private static partial Regex MfaTokenPattern();

    // A raw user-mode header ("Authorization: <token>") or a gateway IDENTIFY/RESUME body ("token":"...").
    [GeneratedRegex(@"((?:Authorization[""']?\s*[:=]\s*|[""']token[""']\s*:\s*)[""']?)(?!Bot\s)[^\s""',}]{8,}", RegexOptions.IgnoreCase)]
    private static partial Regex HeaderOrJsonTokenPattern();

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var result = BotTokenPattern().Replace(text, Mask);
        result = WebhookTokenPattern().Replace(result, "$1" + Mask);
        result = AuthorizationPattern().Replace(result, "$1" + Mask);
        result = MfaTokenPattern().Replace(result, Mask);
        result = HeaderOrJsonTokenPattern().Replace(result, "$1" + Mask);
        return result;
    }
}

public static class TokenValidator
{
    /// <summary>
    /// Shape check only. Bot and user tokens are both three base64url segments whose first part decodes to the account's
    /// numeric id; very old user tokens are <c>mfa.</c> plus one long segment. Catches pasted junk or a client secret
    /// before we hit the gateway. Never include the token in the returned message.
    /// </summary>
    public static string? Validate(AccountKind kind, string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return kind == AccountKind.Bot ? "Paste your bot token." : "Paste your user token.";
        token = Normalize(kind, token);
        if (kind == AccountKind.User && token.StartsWith("mfa.", StringComparison.Ordinal))
            return token.Length > 50 && token[4..].All(IsBase64UrlChar) ? null : "That doesn't look like a user token.";

        var parts = token.Split('.');
        if (parts.Length != 3 || parts.Any(p => p.Length == 0 || !p.All(IsBase64UrlChar)))
            return kind == AccountKind.Bot
                ? "That doesn't look like a bot token (expected three dot-separated parts). Copy it from Developer Portal → Bot → Reset Token."
                : "That doesn't look like a user token (expected three dot-separated parts). See the README for where to find it.";
        if (DecodeId(parts[0]) is null)
            return kind == AccountKind.Bot
                ? "That doesn't look like a bot token. Make sure you copied the Bot token, not the Client Secret or Public Key."
                : "That doesn't look like a user token: the first part should decode to your user id.";
        return null;
    }

    /// <summary>Trims whitespace and the quotes devtools adds when copying a string; strips <c>Bot </c> for bot tokens.</summary>
    public static string Normalize(AccountKind kind, string token)
    {
        token = token.Trim().Trim('"', '\'').Trim();
        if (token.StartsWith("Bot ", StringComparison.OrdinalIgnoreCase)) token = token[4..].Trim();
        return token;
    }

    /// <summary>The account id encoded in a token's first segment, if any. Lets the UI warn about mixing up the two kinds.</summary>
    public static ulong? AccountId(string token)
    {
        var first = token.Split('.')[0];
        return first.All(IsBase64UrlChar) ? DecodeId(first) : null;
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
