using Bunit;
using DisctalkClient.Core.Abstractions;
using DisctalkClient.Core.Settings;
using DisctalkClient.Core.State;
using DisctalkClient.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace DisctalkClient.Tests.Rendering;

/// <summary>
/// bUnit context wired like a host: real ChatClient/settings/UI state, a hand-driven fake gateway, mocked REST and
/// webhook executor, a fixed clock (UTC, en-US) and loose JS interop.
/// </summary>
public abstract class UiTestBase : BunitContext
{
    protected readonly FakeConnectionFactory Factory = new();
    protected readonly InMemorySettingsStore Store = new();
    protected readonly InMemoryTokenStore Tokens = new();
    protected readonly IWebhookExecutor Executor = Substitute.For<IWebhookExecutor>();
    protected readonly FakeTimeProvider Time = new(TestData.Now);

    protected UiTestBase()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ISettingsStore>(Store);
        Services.AddSingleton<ITokenStore>(Tokens);
        Services.AddSingleton<IDiscordConnectionFactory>(Factory);
        Services.AddSingleton(Executor);
        Services.AddSingleton<TimeProvider>(Time);
        Services.AddSingleton(sp => new TimestampFormatter(sp.GetRequiredService<SettingsService>(), Time, TimeZoneInfo.Utc, TestData.EnUs));
        Services.AddDisctalkClient();
    }

    protected FakeConnection Conn => Factory.Connection;
    protected ChatClient Client => Services.GetRequiredService<ChatClient>();
    protected SettingsService Settings => Services.GetRequiredService<SettingsService>();
    protected UiState Ui => Services.GetRequiredService<UiState>();

    protected async Task LoginAsync()
    {
        await Client.ConnectAsync(TestData.Token);
        await Conn.LoginAsync();
    }
}
