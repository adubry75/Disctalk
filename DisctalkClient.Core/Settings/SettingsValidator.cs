using System.Globalization;

namespace DisctalkClient.Core.Settings;

public static class SettingsValidator
{
    public const int MaxTimestampFormatLength = 64;
    public const int MaxWebhookNameLength = 80;

    /// <summary>Discord rejects webhook usernames containing these.</summary>
    static readonly string[] ForbiddenWebhookNameParts = ["discord", "clyde"];

    public static IReadOnlyList<string> Validate(ClientSettings settings)
    {
        var errors = new List<string>();
        if (ValidateTimestampFormat(settings.TimestampFormat) is { } tsError) errors.Add(tsError);
        if (ValidateWebhookName(settings.WebhookDisplayName) is { } nameError) errors.Add(nameError);
        if (ValidateAvatarUrl(settings.WebhookAvatarUrl) is { } urlError) errors.Add(urlError);
        return errors;
    }

    public static string? ValidateTimestampFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format)) return "Timestamp format can't be empty.";
        if (format.Length > MaxTimestampFormatLength) return $"Timestamp format must be at most {MaxTimestampFormatLength} characters.";
        try
        {
            _ = new DateTime(2026, 1, 2, 15, 4, 5).ToString(format, CultureInfo.InvariantCulture);
            return null;
        }
        catch (FormatException)
        {
            return $"\"{format}\" isn't a valid .NET date/time format.";
        }
    }

    public static string? ValidateWebhookName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return "Webhook display name can't be only spaces.";
        if (trimmed.Length > MaxWebhookNameLength) return $"Webhook display name must be at most {MaxWebhookNameLength} characters.";
        foreach (var part in ForbiddenWebhookNameParts)
            if (trimmed.Contains(part, StringComparison.OrdinalIgnoreCase))
                return $"Discord doesn't allow \"{part}\" in webhook names.";
        return null;
    }

    public static string? ValidateAvatarUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return "Avatar URL must be an absolute http(s) link to an image.";
        return null;
    }
}
