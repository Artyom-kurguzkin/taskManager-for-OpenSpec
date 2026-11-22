using Bunit;
using AngleSharp.Dom;
using TaskManager.Components.Pages;

namespace TaskManager.Tests;

/// <summary>The Settings page is a row of tabs (General, then one per open project): tests choose the tab whose rows they look at, the way a user clicks it.</summary>
internal static class SettingsTabs
{
    public static IReadOnlyList<IElement> Radios(this IRenderedComponent<SettingsPage> page) => page.FindAll("input[name=settings-tab]");

    public static string[] Labels(this IRenderedComponent<SettingsPage> page) => [.. page.FindAll(".settings-tabs .seg-o").Select(l => l.TextContent.Trim())];

    public static string Chosen(this IRenderedComponent<SettingsPage> page) => page.Find(".settings-tabs .seg-o.is-sel input").GetAttribute("value")!;

    /// <summary>Chooses the tab with this value ("general" or a project's folder).</summary>
    public static IRenderedComponent<SettingsPage> OnTab(this IRenderedComponent<SettingsPage> page, string value)
    {
        page.FireWhenReady("input[name=settings-tab]", e => e.Change(value), all => all.First(r => r.GetAttribute("value") == value));
        page.Eventually(() => Assert.Equal(value, page.Chosen()));
        return page;
    }

    /// <summary>Chooses the n-th project's tab (the first by default).</summary>
    public static IRenderedComponent<SettingsPage> OnProject(this IRenderedComponent<SettingsPage> page, int index = 0)
    {
        page.Ready("input[name=settings-tab]");
        return page.OnTab(page.Radios().Select(r => r.GetAttribute("value")!).Where(v => v != "general").ElementAt(index));
    }
}
