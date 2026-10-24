using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Disctalk.Core.Http;

/// <summary>
/// The browser identity Disctalk presents to Discord: the HTTP headers sent on every API request and the
/// super properties sent both as the base64 <c>X-Super-Properties</c> header and as the gateway IDENTIFY
/// <c>properties</c>. Loaded from headerprofile.json (see README for how to refresh it).
/// </summary>
public sealed class HeaderProfile
{
    public const string FileName = "headerprofile.json";
    public const string PathEnvironmentVariable = "DISCTALK_HEADER_PROFILE";
    const string EmbeddedResourceName = "Disctalk.Core.headerprofile.json";

    internal static readonly JsonSerializerOptions CompactJson = new()
    {
        // Match the browser's JSON.stringify: no HTML-safe escaping of '+', '<', '&' or non-ASCII characters.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    readonly JsonObject _superPropertiesTemplate;

    HeaderProfile(IReadOnlyList<KeyValuePair<string, string>> headers, JsonObject superProperties, int fallbackBuildNumber, int? capabilities, string source)
    {
        Headers = headers;
        _superPropertiesTemplate = superProperties;
        FallbackBuildNumber = fallbackBuildNumber;
        GatewayCapabilities = capabilities;
        Source = source;
    }

    /// <summary>Headers in capture order. Never contains Authorization.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; }

    /// <summary>Build number to use when discord.com can't be reached and nothing is cached.</summary>
    public int FallbackBuildNumber { get; }

    /// <summary>Optional gateway capabilities override (<c>gateway.capabilities</c> in the profile).</summary>
    public int? GatewayCapabilities { get; }

    /// <summary>Where the profile was loaded from, for diagnostics.</summary>
    public string Source { get; }

    public string? GetHeader(string name) =>
        Headers.FirstOrDefault(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    public string UserAgent => GetHeader("User-Agent") ?? "";

    public string Locale => GetHeader("X-Discord-Locale") ?? "en-US";

    /// <summary>
    /// The super properties with <c>client_build_number</c> set and every <c>&lt;runtime…&gt;</c> placeholder replaced
    /// by a UUID generated once per process launch. Key order is preserved from the profile.
    /// </summary>
    public JsonObject BuildSuperProperties(int buildNumber)
    {
        var props = (JsonObject)_superPropertiesTemplate.DeepClone();
        foreach (var key in props.Select(p => p.Key).ToList())
        {
            if (props[key] is JsonValue v && v.TryGetValue(out string? s) && IsRuntimePlaceholder(s))
                props[key] = LaunchValues.Get(key);
        }
        props["client_build_number"] = buildNumber;
        return props;
    }

    public static string EncodeSuperProperties(JsonObject superProperties) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(superProperties.ToJsonString(CompactJson)));

    static bool IsRuntimePlaceholder(string? s) => s != null && s.StartsWith("<runtime", StringComparison.OrdinalIgnoreCase);

    /// <summary>The raw template (placeholders unresolved), for validation.</summary>
    internal JsonObject Template => _superPropertiesTemplate;

    public static HeaderProfile Parse(string json, string source = "(string)")
    {
        var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
            ?? throw new FormatException($"Header profile {source} is not a JSON object.");

        var headers = new List<KeyValuePair<string, string>>();
        if (root["headers"] is JsonObject headerObj)
        {
            foreach (var (name, value) in headerObj)
            {
                if (name.StartsWith('_') || value is null) continue;
                if (string.Equals(name, "Authorization", StringComparison.OrdinalIgnoreCase)) continue; // token never lives in the profile
                headers.Add(new(name, value.GetValue<string>()));
            }
        }

        var superProps = new JsonObject();
        if (root["superProperties"] is JsonObject sp)
        {
            foreach (var (name, value) in sp)
            {
                if (name.StartsWith('_')) continue;
                superProps[name] = value?.DeepClone();
            }
        }

        int fallback = 0;
        if (root["buildNumber"]?["fallback"] is JsonValue fb && fb.TryGetValue(out int f)) fallback = f;
        else if (superProps["client_build_number"] is JsonValue cbn && cbn.TryGetValue(out int c)) fallback = c;

        int? capabilities = root["gateway"]?["capabilities"] is JsonValue cap && cap.TryGetValue(out int capValue) ? capValue : null;

        return new HeaderProfile(headers, superProps, fallback, capabilities, source);
    }

    /// <summary>
    /// Loads the profile from, in order: the path in DISCTALK_HEADER_PROFILE, headerprofile.json next to the
    /// executable, headerprofile.json in the working directory, then the copy embedded in Disctalk.Core at build time.
    /// </summary>
    public static HeaderProfile Load()
    {
        foreach (var path in CandidatePaths())
        {
            if (File.Exists(path)) return Parse(File.ReadAllText(path), path);
        }
        return LoadEmbedded();
    }

    public static HeaderProfile LoadEmbedded()
    {
        using var stream = typeof(HeaderProfile).Assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {EmbeddedResourceName} is missing from Disctalk.Core.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd(), "embedded:" + FileName);
    }

    static IEnumerable<string> CandidatePaths()
    {
        var env = Environment.GetEnvironmentVariable(PathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(env)) yield return env;
        yield return Path.Combine(AppContext.BaseDirectory, FileName);
        yield return Path.Combine(Environment.CurrentDirectory, FileName);
    }

    /// <summary>Per-launch random values (client_launch_id, launch_signature, …). Stable for the life of the process.</summary>
    static class LaunchValues
    {
        static readonly Dictionary<string, string> Values = new();
        public static string Get(string key)
        {
            lock (Values)
            {
                if (!Values.TryGetValue(key, out var v)) Values[key] = v = Guid.NewGuid().ToString();
                return v;
            }
        }
    }
}
