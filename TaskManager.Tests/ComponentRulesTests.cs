using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using TaskManager.Components;

namespace TaskManager.Tests;

/// <summary>
/// The rules every screen's components follow (DevResources/plans/universal-ui-structure-2026-11-16.md): they all derive from the base component,
/// which decides when a component draws again. Behaviour is checked on small components made for the test.
/// </summary>
public sealed class ComponentRulesTests
{
    // ---- components made for the tests ----

    /// <summary>A button that does nothing, and one that changes what is shown, and one that fails.</summary>
    internal abstract class ClickBox : RtmComponent
    {
        private int shown;

        protected abstract void Changed();

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            Button(b, 0, "nothing", () => { });
            Button(b, 10, "change", () => { shown++; Changed(); });
            Button(b, 20, "fail", () => throw new InvalidOperationException("handler failed"));
            b.OpenElement(30, "span");
            b.AddContent(31, shown);
            b.CloseElement();
        }

        private void Button(RenderTreeBuilder b, int seq, string name, Action onClick)
        {
            b.OpenElement(seq, "button");
            b.AddAttribute(seq + 1, "data-name", name);
            b.AddAttribute(seq + 2, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, onClick));
            b.CloseElement();
        }
    }

    private sealed class StrictBox : ClickBox
    {
        protected override bool Strict => true;
        protected override void Changed() => Invalidate();
    }

    internal sealed class LegacyBox : ClickBox
    {
        protected override void Changed() { }
    }

    /// <summary>Draws again for a parameter set only when <see cref="Key"/> changed; with <see cref="Keyed"/> off it has no key.</summary>
    private sealed class KeyedBox : RtmComponent
    {
        [Parameter] public string Key { get; set; } = "";
        [Parameter] public string Other { get; set; } = "";
        [Parameter] public bool Keyed { get; set; } = true;

        protected override object? RenderKey => Keyed ? Key : null;

        protected override void BuildRenderTree(RenderTreeBuilder b) => b.AddContent(0, Key + "/" + Other);
    }

    // ---- the rules ----

    [Fact(DisplayName = "Every component we write derives from the base component")]
    public void EveryComponentDerivesFromTheBase()
    {
        string[] offenders = [.. typeof(RtmComponent).Assembly.GetTypes()
            .Where(t => typeof(IComponent).IsAssignableFrom(t) && t != typeof(RtmComponent))   // third-party components live in other assemblies
            .Where(t => !typeof(RtmComponent).IsAssignableFrom(t))
            .Select(t => t.FullName!)
            .Order()];

        Assert.Empty(offenders);
    }

    [Fact(DisplayName = "A strict component does not redraw after a click that changed nothing")]
    public async Task StrictSkipsAClickThatChangedNothing()
    {
        await using BunitContext ctx = new();
        IRenderedComponent<StrictBox> box = ctx.Render<StrictBox>();
        Assert.Equal(1, RenderCounts.Of<StrictBox>());

        await box.Find("[data-name=nothing]").ClickAsync();

        Assert.Equal(1, RenderCounts.Of<StrictBox>());

        await box.Find("[data-name=change]").ClickAsync();   // it said it changed something: one draw, showing it

        Assert.Equal(2, RenderCounts.Of<StrictBox>());
        Assert.Equal("1", box.Find("span").TextContent);
    }

    [Fact(DisplayName = "A legacy component still redraws after every click")]
    public async Task LegacyRedrawsAfterEveryClick()
    {
        await using BunitContext ctx = new();
        IRenderedComponent<LegacyBox> box = ctx.Render<LegacyBox>();

        await box.Find("[data-name=nothing]").ClickAsync();
        await box.Find("[data-name=nothing]").ClickAsync();

        Assert.Equal(3, RenderCounts.Of<LegacyBox>());
    }

    [Fact(DisplayName = "A component with an unchanged render key skips a parameter redraw")]
    public void UnchangedKeySkipsAParameterRedraw()
    {
        using BunitContext ctx = new();
        IRenderedComponent<KeyedBox> box = ctx.Render<KeyedBox>(p => p.Add(c => c.Key, "a").Add(c => c.Other, "1"));
        Assert.Equal(1, RenderCounts.Of<KeyedBox>());

        box.Render(p => p.Add(c => c.Key, "a").Add(c => c.Other, "2"));   // the parent drew again, the key did not move
        Assert.Equal(1, RenderCounts.Of<KeyedBox>());

        box.Render(p => p.Add(c => c.Key, "b").Add(c => c.Other, "2"));
        Assert.Equal(2, RenderCounts.Of<KeyedBox>());
        Assert.Equal("b/2", box.Markup);   // and it shows what arrived with the new key

        box.Render(p => p.Add(c => c.Keyed, false).Add(c => c.Other, "3"));   // no key: every parameter set draws, as Blazor does
        box.Render(p => p.Add(c => c.Other, "3"));
        Assert.Equal(4, RenderCounts.Of<KeyedBox>());
    }

    [Fact(DisplayName = "An exception in a strict component's handler reaches the error boundary")]
    public async Task StrictHandlerExceptionReachesErrorBoundary()
    {
        await using BunitContext ctx = new();
        IRenderedComponent<ErrorBoundary> boundary = ctx.Render<ErrorBoundary>(p => p.AddChildContent<StrictBox>());

        await boundary.Find("[data-name=fail]").ClickAsync();

        Assert.NotNull(boundary.Find(".blazor-error-boundary"));
        Assert.Empty(boundary.FindAll("[data-name=fail]"));   // the component is gone, replaced by the boundary's message
    }

    [Fact(DisplayName = "Render counts in one test do not leak into another")]
    public async Task RenderCountsStayInTheirTest()
    {
        await using BunitContext ctx = new();
        Assert.Equal(0, RenderCounts.Of<LegacyBox>());   // every other test in the class draws one, and runs beside this one

        IRenderedComponent<LegacyBox> box = ctx.Render<LegacyBox>();
        await box.Find("[data-name=nothing]").ClickAsync();

        Assert.Equal(2, RenderCounts.Of<LegacyBox>());
    }
}

/// <summary>A second class drawing the same component type at the same time as <see cref="ComponentRulesTests"/>: their counts are their own.</summary>
public sealed class ComponentRulesNeighbourTests
{
    [Fact(DisplayName = "A render count is not changed by components drawn in another test class")]
    public void CountIsNotChangedByANeighbour()
    {
        using BunitContext ctx = new();
        ctx.Render<ComponentRulesTests.LegacyBox>();

        Assert.Equal(1, RenderCounts.Of<ComponentRulesTests.LegacyBox>());
    }
}
