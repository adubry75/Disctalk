using System.Net;
using System.Text;

namespace Disctalk.Tests;

/// <summary>An HttpMessageHandler that answers from a script instead of the network. Records every request.</summary>
sealed class FakeHttpHandler : HttpMessageHandler
{
    readonly Func<HttpRequestMessage, int, Task<HttpResponseMessage>> _respond;
    public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = new();

    public FakeHttpHandler(Func<HttpRequestMessage, int, Task<HttpResponseMessage>> respond) => _respond = respond;

    public FakeHttpHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
        : this((r, i) => Task.FromResult(respond(r, i))) { }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        string? body = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
        int index;
        lock (Requests) { index = Requests.Count; Requests.Add((request, body)); }
        var response = await _respond(request, index);
        response.RequestMessage ??= request;
        return response;
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Text(string text, string mediaType = "text/html") =>
        new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, mediaType) };
}

static class Fixtures
{
    public static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
    public static string Read(string name) => File.ReadAllText(Path(name));
}
