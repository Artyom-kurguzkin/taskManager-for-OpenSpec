using Bunit;
using Microsoft.Extensions.DependencyInjection;
using TaskManager.Components.Shared;
using TaskManager.Services;
using WorkItems.Infrastructure;

namespace TaskManager.Tests;

/// <summary>Archiving shells out to the OpenSpec CLI (about 1.3 s of its own work), so it skips npx and shows that it is busy.</summary>
public sealed class ArchivingQuicklyTests : IDisposable
{
    private readonly string cache = Path.Combine(Path.GetTempPath(), "rtm-npm-" + Guid.NewGuid().ToString("N"));

    private string FakePackage(string hash, string version)
    {
        string dir = Path.Combine(cache, "_npx", hash, "node_modules", "@fission-ai", "openspec");
        Directory.CreateDirectory(Path.Combine(dir, "bin"));
        File.WriteAllText(Path.Combine(dir, "package.json"), $$"""{ "name": "@fission-ai/openspec", "version": "{{version}}", "bin": { "openspec": "./bin/openspec.js" } }""");
        File.WriteAllText(Path.Combine(dir, "bin", "openspec.js"), "");
        return Path.Combine(dir, "bin", "openspec.js");
    }

    [Fact(DisplayName = "The CLI copy npx already cached, at the pinned version, is run directly with node")]
    public void FindsCachedCli()
    {
        FakePackage("aaa", "1.13.0");
        string pinned = FakePackage("bbb", OpenSpecCli.Version);

        Assert.Equal(pinned, OpenSpecCli.FindCached(cache));
    }

    [Fact(DisplayName = "Without a cached copy at the pinned version, archiving falls back to npx")]
    public void FallsBackToNpx()
    {
        Assert.Null(OpenSpecCli.FindCached(cache));   // no cache folder at all
        FakePackage("aaa", "1.13.0");
        Assert.Null(OpenSpecCli.FindCached(cache));
    }

    [Fact(DisplayName = "While a slow step runs, a busy panel with its message covers the page, then goes away")]
    public async Task BusyPanel()
    {
        await using BunitContext ctx = new();
        ctx.Services.AddSingleton<UiService>();
        UiService ui = ctx.Services.GetRequiredService<UiService>();
        IRenderedComponent<Overlays> overlays = ctx.Render<Overlays>();
        string? seen = null;

        int result = await ui.Busy("Archiving #wi7…", () => { seen = overlays.Find(".busy").TextContent; return 42; });

        Assert.Equal(42, result);
        Assert.Contains("Archiving #wi7…", seen);
        overlays.WaitForAssertion(() => Assert.Empty(overlays.FindAll(".busy")));
    }

    public void Dispose()
    {
        if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true);
    }
}
