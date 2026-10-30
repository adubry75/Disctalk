using DisctalkClient.Core.Abstractions;
using DisctalkClient.Services;
using DisctalkClient.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DisctalkClient;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddSingleton<ISettingsStore, PreferencesSettingsStore>();
        builder.Services.AddSingleton<SecureSecretStore>();
        builder.Services.AddSingleton<ISecretStore>(sp => sp.GetRequiredService<SecureSecretStore>());
        builder.Services.AddSingleton<ITokenStore, SecureTokenStore>();
        builder.Services.AddDisctalkClient();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        // Debug output only. Gateway/REST logs pass through SecretRedactor; the token itself is never logged.
        builder.Logging.AddDebug().SetMinimumLevel(LogLevel.Information);
#endif

        return builder.Build();
    }
}
