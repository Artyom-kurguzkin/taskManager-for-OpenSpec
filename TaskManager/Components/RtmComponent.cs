using System.Collections.Concurrent;
using Microsoft.AspNetCore.Components;
using TaskManager.Helpers;

namespace TaskManager.Components;

/// <summary>
/// The base of every component we write: the one place that decides when a component draws again (see DevResources/plans/universal-ui-structure-2026-11-16.md).
/// By default it draws exactly as <see cref="ComponentBase"/> does (after every event, after every parameter set), so deriving from it changes nothing;
/// a component opts in to less drawing with <see cref="Strict"/> and <see cref="RenderKey"/>.
/// </summary>
public abstract class RtmComponent : ComponentBase, IHandleEvent, IHandleAfterRender
{
    /// <summary>
    /// True: an event does not draw the component by itself. The handler calls <see cref="Invalidate"/> when it changed something the component shows.
    /// False (the default): as Blazor does, a draw after every handler.
    /// </summary>
    protected virtual bool Strict => false;

    /// <summary>
    /// What a parameter-set draw depends on. Blazor sets the parameters again whenever the parent draws; while this key stays equal (Equals) the
    /// component is not drawn for it. Null (the default) draws on every parameter set, as Blazor does.
    /// </summary>
    protected virtual object? RenderKey => null;

    /// <summary>Draws the component after state it shows changed outside a legacy event (a strict handler, a timer, an event from another component). Safe from any thread.</summary>
    protected void Invalidate() => _ = InvokeAsync(StateHasChanged);

    // ---- the window's project ----

    [Inject] private IServiceProvider WindowServices { get; set; } = default!;

    /// <summary>What reads and event handlers run inside: a separate window's own project, null in the main window (<see cref="Ui.WindowScope"/>).</summary>
    protected IDisposable? WindowScope() => Ui.WindowScope(WindowServices);

    // ---- events ----

    Task IHandleEvent.HandleEventAsync(EventCallbackWorkItem callback, object? arg) =>
        Strict ? HandleStrictAsync(callback, arg) : HandleLegacyAsync(callback, arg);

    // ComponentBase's own handling: a draw after the synchronous part, and one more when an async handler completes.
    private Task HandleLegacyAsync(EventCallbackWorkItem callback, object? arg)
    {
        Task task;
        using (WindowScope()) task = callback.InvokeAsync(arg);
        bool awaitTask = task.Status != TaskStatus.RanToCompletion && task.Status != TaskStatus.Canceled;
        StateHasChanged();
        return awaitTask ? DrawAfterAsync(task) : Task.CompletedTask;
    }

    private async Task DrawAfterAsync(Task task)
    {
        try { await task; }
        catch { if (task.IsCanceled) return; throw; }
        StateHasChanged();
    }

    // No draw. IHandleEvent skips the error boundary, so a failing handler is handed to it explicitly (dotnet/aspnetcore#54543).
    private async Task HandleStrictAsync(EventCallbackWorkItem callback, object? arg)
    {
        try { using (WindowScope()) await callback.InvokeAsync(arg); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await DispatchExceptionAsync(ex); }
    }

    // ---- parameters ----

    private bool parametersPass, neverSet = true;
    private object? drawnKey;

    public override Task SetParametersAsync(ParameterView parameters)
    {
        bool first = neverSet;
        neverSet = false;
        parametersPass = !first;
        Task task;
        try { task = base.SetParametersAsync(parameters); }
        finally { parametersPass = false; }
        if (first) drawnKey = RenderKey;   // the first draw does not ask ShouldRender
        return task;
    }

    /// <summary>Components with their own rule override this and call the base first (or skip it, for a rule that needs no key).</summary>
    protected override bool ShouldRender()
    {
        object? key = RenderKey;
        if (parametersPass && key is not null && key.Equals(drawnKey)) return false;
        drawnKey = key;
        return true;
    }

    // ---- counting ----

    private bool afterRenderSeen;

    Task IHandleAfterRender.OnAfterRenderAsync()
    {
        RenderCounts.Add(GetType());
        bool first = !afterRenderSeen;
        afterRenderSeen = true;
        OnAfterRender(first);
        return OnAfterRenderAsync(first);
    }
}

/// <summary>How many times each component type has been drawn. The app has one count; a test runs against its own (<see cref="Isolated"/>), as the modules do.</summary>
public static class RenderCounts
{
    internal static readonly AsyncLocal<ConcurrentDictionary<Type, int>?> Isolated = new();
    private static readonly ConcurrentDictionary<Type, int> Shared = new();
    private static ConcurrentDictionary<Type, int> S => Isolated.Value ?? Shared;

    internal static void Add(Type type) => S.AddOrUpdate(type, 1, (_, n) => n + 1);

    /// <summary>Draws of this component type (exactly this type, not its subclasses) since the count began or was last reset.</summary>
    public static int Of<T>() where T : IComponent => Of(typeof(T));

    public static int Of(Type type) => S.TryGetValue(type, out int n) ? n : 0;

    public static void Reset() => S.Clear();
}

/// <summary>A layout: <see cref="LayoutComponentBase"/>'s <c>Body</c>, on <see cref="RtmComponent"/>.</summary>
public abstract class RtmLayout : RtmComponent
{
    [Parameter] public RenderFragment? Body { get; set; }
}
