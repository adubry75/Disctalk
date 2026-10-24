using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace DisctalkClient.UI.Services;

/// <summary>Typed wrapper over wwwroot/disctalk.js.</summary>
public sealed class DisctalkJs(IJSRuntime js) : IAsyncDisposable
{
    const string ModulePath = "./_content/DisctalkClient.UI/disctalk.js";
    Task<IJSObjectReference>? _module;

    Task<IJSObjectReference> Module => _module ??= js.InvokeAsync<IJSObjectReference>("import", ModulePath).AsTask();

    public async ValueTask InstallDelegationAsync<T>(DotNetObjectReference<T> target) where T : class =>
        await (await Module).InvokeVoidAsync("installDelegation", target);

    public async ValueTask<IJSObjectReference> ObserveTopAsync<T>(ElementReference scroller, ElementReference sentinel, DotNetObjectReference<T> target) where T : class =>
        await (await Module).InvokeAsync<IJSObjectReference>("observeTop", scroller, sentinel, target);

    public async ValueTask ScrollToBottomAsync(ElementReference scroller) =>
        await (await Module).InvokeVoidAsync("scrollToBottom", scroller);

    public async ValueTask<IJSObjectReference> AttachComposerAsync<T>(ElementReference textarea, DotNetObjectReference<T> target) where T : class =>
        await (await Module).InvokeAsync<IJSObjectReference>("attachComposer", textarea, target);

    public async ValueTask SetComposerTextAsync(ElementReference textarea, string text) =>
        await (await Module).InvokeVoidAsync("setComposerText", textarea, text);

    public async ValueTask<string> InsertAtCursorAsync(ElementReference textarea, string text) =>
        await (await Module).InvokeAsync<string>("insertAtCursor", textarea, text);

    public async ValueTask FocusAsync(ElementReference element) =>
        await (await Module).InvokeVoidAsync("focus", element);

    public async ValueTask DisposeAsync()
    {
        if (_module is null) return;
        try { await (await _module).DisposeAsync(); }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
    }
}
