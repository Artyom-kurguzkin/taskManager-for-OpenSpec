using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using TaskManager.AppState;
using TaskManager.Components.Pages;
using TaskManager.Components.Shared;
using WorkItems;

namespace TaskManager.Tests;

/// <summary>The description editor shows a preview of the images the description references, as the note editor does for a note's attachments.</summary>
public sealed class PreviewingImagesInTheDescriptionTests : IDisposable
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly TestRepo repo = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-descimg-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        repo.Dispose();
        try { File.Delete(settingsFile); } catch (IOException) { }
    }

    private BunitContext NewContext()
    {
        BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        ctx.Render<Overlays>();
        return ctx;
    }

    private void Image(string relative)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(repo.PathOf(relative))!);
        File.WriteAllBytes(repo.PathOf(relative), Png);
    }

    private IRenderedComponent<Worklog> Log(BunitContext ctx) =>
        ctx.Render<Worklog>(p => p.Add(c => c.Item, WorkItemsApi.Get(123)!).Add(c => c.Root, repo.Root));

    private static void EditDescription(IRenderedComponent<Worklog> log)
    {
        log.FireWhenReady(".wk-first .wk-e", e => e.Click(new MouseEventArgs { ShiftKey = true }));
        log.FireWhenReady(".menu-pop .menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == "Edit"));
        log.WaitForAssertion(() => Assert.NotEmpty(log.FindAll(".wk-first textarea.desc-in")));
    }

    [Fact(DisplayName = "Editing the description shows a preview of each image it references, like editing a note")]
    public async Task EditingShowsThePreviews()
    {
        await using BunitContext ctx = NewContext();
        Image("local/task-tracking/wi123/one.png");
        Image("local/task-tracking/wi123/two.png");
        Assert.True(WorkItemsApi.SetDescription(123, "Words\n\n![](local/task-tracking/wi123/one.png)\n\n![shot](local/task-tracking/wi123/two.png)\n\n![](https://example.com/far.png)").Success);
        IRenderedComponent<Worklog> log = Log(ctx);

        EditDescription(log);

        IReadOnlyList<IElement> shown = log.FindAll(".wk-first .wk-edit .wk-atts img.att-img");
        Assert.Equal(["one.png", "two.png"], shown.Select(i => i.GetAttribute("alt")));   // the pictures of the repository; a web address is not read
        Assert.All(shown, i => Assert.StartsWith("data:image/png;base64,", i.GetAttribute("src")));
    }

    [Fact(DisplayName = "Editing a description without images shows no preview area")]
    public async Task NoImagesNoPreview()
    {
        await using BunitContext ctx = NewContext();
        Assert.True(WorkItemsApi.SetDescription(123, "Only words").Success);
        IRenderedComponent<Worklog> log = Log(ctx);

        EditDescription(log);

        Assert.Empty(log.FindAll(".wk-first .wk-edit .wk-atts"));
    }

    [Fact(DisplayName = "An image pasted while editing the description shows its preview at once")]
    public async Task PastedImageShowsAtOnce()
    {
        await using BunitContext ctx = NewContext();
        Assert.True(WorkItemsApi.SetDescription(123, "Words").Success);
        IRenderedComponent<Worklog> log = Log(ctx);
        EditDescription(log);
        Assert.Empty(log.FindAll(".wk-first .wk-edit img.att-img"));

        await log.InvokeAsync(() => log.Instance.PastedDescriptionImage("pasted-x.png", Convert.ToBase64String(Png), "Words", 5));

        log.WaitForAssertion(() => Assert.Equal(["pasted-x.png"], log.FindAll(".wk-first .wk-edit .wk-atts img.att-img").Select(i => i.GetAttribute("alt"))));
    }

    [Fact(DisplayName = "The preview of the description editor goes away with the editor, and the saved description still shows its images")]
    public async Task PreviewEndsWithTheEditor()
    {
        await using BunitContext ctx = NewContext();
        Image("local/task-tracking/wi123/one.png");
        Assert.True(WorkItemsApi.SetDescription(123, "![](local/task-tracking/wi123/one.png)").Success);
        IRenderedComponent<Worklog> log = Log(ctx);
        EditDescription(log);

        log.Find(".wk-first .wk-edit textarea").FocusOut();

        log.WaitForAssertion(() => Assert.Empty(log.FindAll(".wk-first .wk-edit")));
        Assert.Empty(log.FindAll(".wk-first .wk-atts"));
        Assert.NotEmpty(log.FindAll(".wk-first .desc img"));
    }
}
