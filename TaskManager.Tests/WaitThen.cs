using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace TaskManager.Tests;

/// <summary>
/// Every UI action in a test goes through here: the element is waited for (rendered and present), then found and fired in one renderer step, so no
/// re-render can slip between the find and the event; every check after an action waits for the render that carries its result. A wait that runs out
/// is a failure of the test, not a timing budget: <see cref="Bound"/> is a hang bound, never waited out by a passing test. Nothing here reads the clock.
/// </summary>
public static class WaitThen
{
    /// <summary>A bound for a hang, not a budget: a correct test never waits this long.</summary>
    public static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    /// <summary>Waits until <paramref name="selector"/> is rendered, then clicks it in the same renderer step as the find. Fails when it never appears.</summary>
    public static void ClickWhenReady<T>(this IRenderedComponent<T> cut, string selector) where T : IComponent =>
        cut.FireWhenReady(selector, e => e.Click());

    public static void DoubleClickWhenReady<T>(this IRenderedComponent<T> cut, string selector) where T : IComponent =>
        cut.FireWhenReady(selector, e => e.DoubleClick());

    public static void ChangeWhenReady<T>(this IRenderedComponent<T> cut, string selector, object value) where T : IComponent =>
        cut.FireWhenReady(selector, e => e.Change(value));

    public static void InputWhenReady<T>(this IRenderedComponent<T> cut, string selector, object value) where T : IComponent =>
        cut.FireWhenReady(selector, e => e.Input(value));

    public static void SubmitWhenReady<T>(this IRenderedComponent<T> cut, string selector) where T : IComponent =>
        cut.FireWhenReady(selector, e => e.Submit());

    public static void BlurWhenReady<T>(this IRenderedComponent<T> cut, string selector) where T : IComponent =>
        cut.FireWhenReady(selector, e => e.Blur());

    public static void KeyDownWhenReady<T>(this IRenderedComponent<T> cut, string selector, string key, bool shift = false, bool ctrl = false) where T : IComponent =>
        cut.FireWhenReady(selector, e => e.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = key, ShiftKey = shift, CtrlKey = ctrl }));

    /// <summary>
    /// The general form: waits for the element, then runs <paramref name="fire"/> on a fresh find of it inside one renderer step. <paramref name="pick"/> chooses among
    /// several matches (default: the first); the chooser runs on the fresh list, in the same step.
    /// </summary>
    public static void FireWhenReady<T>(this IRenderedComponent<T> cut, string selector, Action<IElement> fire, Func<IReadOnlyList<IElement>, IElement>? pick = null) where T : IComponent
    {
        cut.WaitForElement(selector, Bound);   // rendered and present, or the test fails here
        cut.InvokeAsync(() => fire((pick ?? (all => all[0]))(cut.FindAll(selector)))).GetAwaiter().GetResult();
    }

    /// <summary>Fires on the element whose text contains <paramref name="text"/> (waited for, found and fired in one step).</summary>
    public static void ClickWhenReady<T>(this IRenderedComponent<T> cut, string selector, string text) where T : IComponent
    {
        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll(selector), e => e.TextContent.Contains(text)), Bound);
        cut.InvokeAsync(() => cut.FindAll(selector).First(e => e.TextContent.Contains(text)).Click()).GetAwaiter().GetResult();
    }

    /// <summary>A check that waits for the render carrying its result; the assertion stays as strict as written.</summary>
    public static void Eventually<T>(this IRenderedComponent<T> cut, Action assertion) where T : IComponent => cut.WaitForAssertion(assertion, Bound);

    /// <summary>The element, once rendered; the test fails when it never appears.</summary>
    public static IElement Ready<T>(this IRenderedComponent<T> cut, string selector) where T : IComponent => cut.WaitForElement(selector, Bound);
}
