using Microsoft.AspNetCore.Components;

namespace TaskManager.Helpers;

/// <summary>
/// Event handlers that do not render their component after they run (Blazor's own advice for handlers that may change
/// nothing, https://learn.microsoft.com/aspnet/core/blazor/performance/rendering): the handler calls StateHasChanged where it did.
/// </summary>
public static class EventUtil
{
    public static Action<T> AsNonRenderingEventHandler<T>(Action<T> callback) => new SyncReceiver<T>(callback).Invoke;

    public static Action AsNonRenderingEventHandler(Action callback) => new SyncReceiver<object?>(_ => callback()).Invoke0;

    // A delegate whose target is not the component: the framework calls it without the render that follows a component's own handler.
    private sealed class SyncReceiver<T>(Action<T> callback) : ReceiverBase
    {
        public void Invoke(T arg) => callback(arg);
        public void Invoke0() => callback(default!);
    }

    private abstract class ReceiverBase : IHandleEvent
    {
        public Task HandleEventAsync(EventCallbackWorkItem item, object? arg) => item.InvokeAsync(arg);
    }
}
