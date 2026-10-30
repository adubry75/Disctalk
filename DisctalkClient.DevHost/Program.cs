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
    builder.Services.AddSingleton<ITokenStore>(new InMemoryTokenStore(AccountKind.User, DemoHub.FakeToken));
    builder.Services.AddSingleton<ISettingsStore, InMemorySettingsStore>();
}
else
{
    // Tokens: DISCTALK_USER_TOKEN / DISCTALK_BOT_TOKEN env vars or `dotnet user-secrets set Discord:UserToken <token>`
    // (Discord:BotToken for the bot); otherwise the UI asks and keeps it in memory for this run only. Never written to disk.
    var tokens = new InMemoryTokenStore();
    if ((Environment.GetEnvironmentVariable("DISCTALK_USER_TOKEN") ?? builder.Configuration["Discord:UserToken"]) is { Length: > 0 } userToken)
        await tokens.SaveTokenAsync(AccountKind.User, userToken);
    if ((Environment.GetEnvironmentVariable("DISCTALK_BOT_TOKEN") ?? builder.Configuration["Discord:BotToken"]) is { Length: > 0 } botToken)
        await tokens.SaveTokenAsync(AccountKind.Bot, botToken);
    builder.Services.AddSingleton<ITokenStore>(tokens);
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
