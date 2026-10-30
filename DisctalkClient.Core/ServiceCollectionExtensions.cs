using DisctalkClient.Core.Abstractions;
using DisctalkClient.Core.Archive;
using DisctalkClient.Core.Discord;
using DisctalkClient.Core.Sending;
using DisctalkClient.Core.Settings;
using DisctalkClient.Core.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DisctalkClient.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the client core. The host must also register <see cref="ISettingsStore"/> and <see cref="ITokenStore"/>.
    /// Anything registered before this call (e.g. a fake <see cref="IDiscordConnectionFactory"/>) wins.
    /// </summary>
    public static IServiceCollection AddDisctalkClientCore(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<SettingsService>();
        services.TryAddSingleton(sp => new TimestampFormatter(sp.GetRequiredService<SettingsService>(), sp.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton<IDiscordConnectionFactory>(sp => new DiscordConnectionFactory(sp.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()));
        services.TryAddSingleton<IWebhookExecutor>(_ => HttpWebhookExecutor.CreateDefault());
        services.TryAddSingleton<ChatClient>();
        services.TryAddSingleton<ISecretStore, InMemorySecretStore>();
        services.TryAddSingleton<IArchiveDatabase, MySqlArchiveDatabase>();
        services.TryAddSingleton<ArchiveSearchService>();
        return services;
    }
}
