using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using TaskManager.Helpers;

namespace TaskManager.Components.Shared;

/// <summary>
/// The app's one error boundary, around everything the layout draws (#wi178): a component that throws is logged at Error (the exception type, its message, the page, the whole exception after the line)
/// and replaced by a small inline message with a Reload link instead of a blank window. It lets go of the error when the address changes, so the next page draws.
/// </summary>
public sealed class AppErrorBoundary : ErrorBoundaryBase, IDisposable
{
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private bool watching;
    private string failedAt = "";   // the address the failure happened on: the navigation that brought it must not undo it (it is raised after the draw that failed)

    protected override Task OnErrorAsync(Exception exception)
    {
        failedAt = Nav.Uri;
        string page = Uri.TryCreate(Nav.Uri, UriKind.Absolute, out Uri? uri) ? uri.AbsolutePath : Nav.Uri;
        AppLog.Logger("Blazor").LogError(exception, "A component failed on page {Page}: {Type}: {Message}", page, exception.GetType().Name, exception.Message);
        return Task.CompletedTask;
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (!watching) { watching = true; Nav.LocationChanged += OnLocation; }
        if (CurrentException is null) { builder.AddContent(0, ChildContent); return; }
        builder.OpenElement(1, "div");
        builder.AddAttribute(2, "class", "app-error");
        builder.AddAttribute(3, "role", "alert");
        builder.OpenElement(4, "span");
        builder.AddContent(5, "Something went wrong on this page. It is in the log.");
        builder.CloseElement();
        builder.OpenElement(6, "a");
        builder.AddAttribute(7, "class", "error-reload");
        builder.AddAttribute(8, "href", Nav.Uri);
        builder.AddAttribute(9, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, () => Nav.NavigateTo(Nav.Uri, forceLoad: true)));
        builder.AddEventPreventDefaultAttribute(10, "onclick", true);
        builder.AddContent(11, "Reload");
        builder.CloseElement();
        builder.CloseElement();
    }

    private void OnLocation(object? sender, Microsoft.AspNetCore.Components.Routing.LocationChangedEventArgs e) { if (CurrentException is not null && Nav.Uri != failedAt) Recover(); }   // raised on the renderer's dispatcher

    public void Dispose()
    {
        if (watching) Nav.LocationChanged -= OnLocation;
    }
}
