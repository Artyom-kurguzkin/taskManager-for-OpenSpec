using System.Diagnostics;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Radzen;
using TaskManager.Components;
using TaskManager.Components.Pages;
using TaskManager.Components.Shared;
using TaskManager.AppState;
using TaskManager.BackgroundJobs;
using TaskManager.Helpers;

namespace TaskManager.Tests;

/// <summary>
/// The images attached to a note are shown on the worklog and on the note page through one safe reader: a file reached through a link out of the
/// repository or larger than the limit is not shown, and the note page does not read the files again on every render.
/// </summary>
public sealed class ShowingAttachedImagesSafelyTests : IDisposable
{
    private const string NoteFile = "openspec/changes/add-health-check/notes/n123.md";
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly TestRepo repo = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-img-" + Guid.NewGuid().ToString("N") + ".json");
    private readonly List<string> links = [];
    private readonly List<string> outsideFolders = [];

    public void Dispose()
    {
        foreach (string link in links) try { if (Directory.Exists(link)) Directory.Delete(link); } catch (IOException) { }   // the link only, never what it points to
        foreach (string folder in outsideFolders) try { Directory.Delete(folder, true); } catch (IOException) { }
        repo.Dispose();
        try { File.Delete(settingsFile); } catch (IOException) { }
    }

    private BunitContext NewContext()
    {
        BunitContext ctx = new();
        ctx.Services.AddSingleton<TimeProvider>(new FakeTimeProvider());   // the page's timers (preview, autosave) never run: nothing here waits
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        ctx.Render<Overlays>();
        return ctx;
    }

    /// <summary>Note #n123 of #wi123 with these attachments (repository-relative entries).</summary>
    private void AttachToNote(params string[] attachments) =>
        repo.Write(NoteFile, "---\nid: 123\nby: Artyom\ncreated: 2026-10-02T09:14:00\nattachments:\n" + string.Concat(attachments.Select(a => $"- {a}\n")) + "---\n# Ping test\n\nSome text.\n");

    private void Image(string relative, int bytes = 0)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(repo.PathOf(relative))!);
        byte[] content = bytes == 0 ? Png : new byte[bytes];
        if (bytes != 0) Png.CopyTo(content, 0);
        File.WriteAllBytes(repo.PathOf(relative), content);
    }

    /// <summary>A folder of the repository that is a link to a folder outside it holding a picture of the same name.</summary>
    private void PictureBehindALink(string link, string name)
    {
        string outside = Path.Combine(Path.GetTempPath(), "rtm-out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        File.WriteAllBytes(Path.Combine(outside, name), Png);
        outsideFolders.Add(outside);
        using Process p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{repo.PathOf(link)}\" \"{outside}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!;
        p.StandardOutput.ReadToEnd();
        p.WaitForExit(10000);
        Assert.True(Directory.Exists(repo.PathOf(link)), "the junction could not be made");
        links.Add(repo.PathOf(link));
    }

    [Fact(DisplayName = "An image attached through a junction outside the repository is not shown in the worklog")]
    public async Task JunctionImageNotInTheWorklog()
    {
        Image("files/att/ok.png");
        PictureBehindALink("via", "pic.png");
        AttachToNote("files/att/ok.png", "via/pic.png");
        await using BunitContext ctx = NewContext();

        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        Assert.Equal(["ok.png"], page.FindAll("img.att-img").Select(i => i.GetAttribute("alt")));   // the safe one is shown, the linked one is only a name
        Assert.Contains(page.FindAll(".worklog span.muted"), s => s.TextContent.Trim() == "pic.png");
    }

    [Fact(DisplayName = "An image attached through a junction outside the repository is not shown on the note page")]
    public async Task JunctionImageNotOnTheNotePage()
    {
        Image("files/att/ok.png");
        PictureBehindALink("via", "pic.png");
        AttachToNote("files/att/ok.png", "via/pic.png");
        await using BunitContext ctx = NewContext();

        IRenderedComponent<NotePage> page = ctx.Render<NotePage>(p => p.Add(c => c.Id, 123));

        Assert.Equal(["ok.png"], page.FindAll("img.att-img").Select(i => i.GetAttribute("alt")));
        Assert.Contains(page.FindAll(".att-list span.muted"), s => s.TextContent.Trim() == "pic.png");
    }

    [Fact(DisplayName = "An image larger than the limit is not shown in the worklog")]
    public async Task LargeImageNotInTheWorklog()
    {
        Image("files/att/ok.png");
        Image("files/att/big.png", Ui.MaxImageBytes + 1);
        AttachToNote("files/att/ok.png", "files/att/big.png");
        await using BunitContext ctx = NewContext();

        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));

        Assert.Equal(["ok.png"], page.FindAll("img.att-img").Select(i => i.GetAttribute("alt")));
        Assert.Contains(page.FindAll(".worklog span.muted"), s => s.TextContent.Trim() == "big.png");
    }

    [Fact(DisplayName = "An image larger than the limit is not shown on the note page")]
    public async Task LargeImageNotOnTheNotePage()
    {
        Image("files/att/ok.png");
        Image("files/att/big.png", Ui.MaxImageBytes + 1);
        AttachToNote("files/att/ok.png", "files/att/big.png");
        await using BunitContext ctx = NewContext();

        IRenderedComponent<NotePage> page = ctx.Render<NotePage>(p => p.Add(c => c.Id, 123));

        Assert.Equal(["ok.png"], page.FindAll("img.att-img").Select(i => i.GetAttribute("alt")));
        Assert.Contains(page.FindAll(".att-list span.muted"), s => s.TextContent.Trim() == "big.png");
    }

    // The files are not counted through a seam: once the page has shown the picture, the file is held open without sharing, so a second read of it
    // fails with a sharing violation (File.Exists and the length are still answered). The page showing the same picture after typing proves it read nothing.
    [Fact(DisplayName = "Typing in a note does not read its attached images again")]
    public async Task TypingDoesNotReadImagesAgain()
    {
        Image("files/att/ok.png");
        AttachToNote("files/att/ok.png");
        await using BunitContext ctx = NewContext();
        IRenderedComponent<NotePage> page = ctx.Render<NotePage>(p => p.Add(c => c.Id, 123));
        string shown = page.Find("img.att-img").GetAttribute("src")!;
        Assert.StartsWith("data:image/png;base64,", shown);

        using FileStream held = new(repo.PathOf("files/att/ok.png"), FileMode.Open, FileAccess.Read, FileShare.None);
        page.Find(".note-view").Click();
        page.Find("textarea.desc-in").Input("typed one");
        page.Find("textarea.desc-in").Input("typed two");

        Assert.Equal(shown, page.Find("img.att-img").GetAttribute("src"));
    }
}
