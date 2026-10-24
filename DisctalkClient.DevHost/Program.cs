using DisctalkClient.Core.Abstractions;
using DisctalkClient.DevHost;
using DisctalkClient.DevHost.Components;
using DisctalkClient.DevHost.Demo;
using DisctalkClient.UI.Services;

var builder = WebApplication.CreateBuilder(args);
bool demo = args.Contains("--demo") || Environment.GetEnvironmentVariable("DISCTALK_DEMO") == "1";

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

if (demo)
{
    // Fake Discord: sample guild, scrollback, simulated chatter. Registered first so AddDisctalkClient keeps them.
    builder.Services.AddSingleton<DemoHub>();
    builder.Services.AddSingleton<IDiscordConnectionFactory>(sp => sp.GetRequiredService<DemoHub>());
    builder.Services.AddSingleton<IWebhookExecutor>(sp => sp.GetRequiredService<DemoHub>());
    builder.Services.AddSingleton<ITokenStore>(new InMemoryTokenStore(DemoHub.FakeToken));
    builder.Services.AddSingleton<ISettingsStore, InMemorySettingsStore>();
}
else
{
    // Token: DISCTALK_BOT_TOKEN env var or `dotnet user-secrets set Discord:BotToken <token>`; otherwise the UI asks and
    // keeps it in memory for this run only. Never written to the repo.
    var token = Environment.GetEnvironmentVariable("DISCTALK_BOT_TOKEN") ?? builder.Configuration["Discord:BotToken"];
    builder.Services.AddSingleton<ITokenStore>(new InMemoryTokenStore(token));
    builder.Services.AddSingleton<ISettingsStore>(new JsonFileSettingsStore(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisctalkClient", "devhost-settings.json")));
}

builder.Services.AddDisctalkClient();

var app = builder.Build();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Logger.LogInformation("DisctalkClient dev host running in {Mode} mode", demo ? "demo" : "live");
app.Run();
