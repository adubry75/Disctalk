using DisctalkClient.Core.Settings;
using DisctalkClient.Core.State;
using DisctalkClient.UI.Services;
using Microsoft.AspNetCore.Components;

namespace DisctalkClient.UI.Components;

/// <summary>
/// Base for components that render live client state. Re-renders (on the renderer's thread) when a relevant
/// <see cref="ChatClient"/> change arrives, and on any UI-state or settings change.
/// </summary>
public abstract class ClientComponentBase : ComponentBase, IDisposable
{
    [Inject] protected ChatClient Client { get; set; } = default!;
    [Inject] protected UiState Ui { get; set; } = default!;
    [Inject] protected SettingsService Settings { get; set; } = default!;
    [Inject] protected TimestampFormatter Time { get; set; } = default!;

    protected ClientSettings S => Settings.Current;

    /// <summary>Which client changes should re-render this component.</summary>
    protected virtual bool Handles(ClientChange change) => true;

    protected override void OnInitialized()
    {
        Client.Changed += OnClientChanged;
        Ui.Changed += OnUiChanged;
        Settings.Changed += OnSettingsChanged;
    }

    void OnClientChanged(ClientChange change)
    {
        if (Handles(change)) _ = InvokeAsync(StateHasChanged);
    }

    void OnUiChanged() => _ = InvokeAsync(StateHasChanged);

    void OnSettingsChanged(ClientSettings settings) => _ = InvokeAsync(StateHasChanged);

    /// <summary>Runs a Discord action and turns failures into a toast instead of an unhandled exception.</summary>
    protected async Task Try(Func<Task> action, string failurePrefix)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Ui.ShowToast($"{failurePrefix}: {Core.Security.SecretRedactor.Redact(ex.Message)}");
        }
    }

    public virtual void Dispose()
    {
        Client.Changed -= OnClientChanged;
        Ui.Changed -= OnUiChanged;
        Settings.Changed -= OnSettingsChanged;
        GC.SuppressFinalize(this);
    }
}
