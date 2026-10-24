using DisctalkClient.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DisctalkClient.UI.Services;

public static class ServiceCollectionExtensions
{
    /// <summary>Core + UI services. Hosts add their own <c>ISettingsStore</c> and <c>ITokenStore</c>.</summary>
    public static IServiceCollection AddDisctalkClient(this IServiceCollection services)
    {
        services.AddDisctalkClientCore();
        services.TryAddScoped<UiState>();
        services.TryAddScoped<DisctalkJs>();
        return services;
    }
}
