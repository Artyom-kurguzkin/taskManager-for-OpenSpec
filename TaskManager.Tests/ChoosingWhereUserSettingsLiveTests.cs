using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using TaskManager.Board;
using TaskManager.Components.Pages;
using TaskManager.Components.Shared;
using TaskManager.Services;

namespace TaskManager.Tests;

/// <summary>The user's own settings (what they arranged per project, their name, the theme) live in one file on this computer; the user chooses its folder, and a small pointer at the fixed default place tells a new build where it went.</summary>
public sealed class ChoosingWhereUserSettingsLiveTests : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(), "rtm-where-" + Guid.NewGuid().ToString("N"));
    private readonly TestRepo repo = new();
    private string DefaultFile => Path.Combine(home, "default", "settings.json");
    private string Pointer => Path.Combine(home, "default", "settings-location.txt");
    private string Chosen => Path.Combine(home, "chosen");

    public ChoosingWhereUserSettingsLiveTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DefaultFile)!);
        Directory.CreateDirectory(Chosen);
    }

    private static readonly string[] Hidden = ["status:Blocked", "col:Progress"];

    [Fact(DisplayName = "The settings file can be moved to a chosen folder and is read from there after a restart")]
    public void MovedFileIsFoundAfterARestart()
    {
        SettingsStore store = SettingsStore.Open(DefaultFile, Pointer);
        store.Update(s => s with { Theme = "dark" });
        store.SaveBoard(repo.Root, new BoardOptions(Hidden: Hidden, View: "list"));

        (bool ok, string message) = store.MoveTo(Chosen);

        Assert.True(ok, message);
        Assert.Equal(Path.Combine(Chosen, "settings.json"), store.FilePath);
        Assert.True(File.Exists(Path.Combine(Chosen, "settings.json")));
        Assert.False(File.Exists(DefaultFile));   // moved, not copied
        Assert.Equal(Chosen, File.ReadAllText(Pointer).Trim());
        store.Update(s => s with { Name = "Ann" });   // written to the new place from now on
        Assert.Contains("Ann", File.ReadAllText(Path.Combine(Chosen, "settings.json")));

        SettingsStore restarted = SettingsStore.Open(DefaultFile, Pointer);   // a new build asks for the default place and finds the pointer
        Assert.Null(restarted.Notice);
        Assert.Equal(Path.Combine(Chosen, "settings.json"), restarted.FilePath);
        Assert.Equal(("dark", "Ann", "list"), (restarted.Current.Theme, restarted.Current.Name, restarted.Board(repo.Root).View));
        Assert.Equal(Hidden, restarted.Board(repo.Root).Hidden);
    }

    [Fact(DisplayName = "Moving the settings back to the default folder removes the pointer, and a folder that cannot take them leaves everything where it was")]
    public void MovingBackAndRefusals()
    {
        SettingsStore store = SettingsStore.Open(DefaultFile, Pointer);
        store.Update(s => s with { Theme = "dark" });

        Assert.False(store.MoveTo(Path.Combine(home, "no-such-folder")).Success);
        File.WriteAllText(Path.Combine(Chosen, "settings.json"), "{ \"Theme\": \"system\" }");   // someone else's file is never overwritten
        Assert.False(store.MoveTo(Chosen).Success);
        Assert.Equal((DefaultFile, false), (store.FilePath, File.Exists(Pointer)));
        Assert.Equal("{ \"Theme\": \"system\" }", File.ReadAllText(Path.Combine(Chosen, "settings.json")));
        Assert.True(File.Exists(DefaultFile));

        File.Delete(Path.Combine(Chosen, "settings.json"));
        Assert.True(store.MoveTo(Chosen).Success);
        Assert.True(store.MoveTo(Path.GetDirectoryName(DefaultFile)!).Success);

        Assert.Equal(DefaultFile, store.FilePath);
        Assert.False(File.Exists(Pointer));
        Assert.Equal("dark", SettingsStore.Open(DefaultFile, Pointer).Current.Theme);
    }

    [Fact(DisplayName = "A missing or unreadable location pointer falls back to the default file and the Settings page says so")]
    public async Task UnusablePointerFallsBackAndSaysSo()
    {
        SettingsStore plain = SettingsStore.Open(DefaultFile, Pointer);   // never moved: no pointer, nothing to say
        Assert.Null(plain.Notice);
        plain.Update(s => s with { Theme = "dark" });

        string missingFolder = Path.Combine(home, "gone");
        File.WriteAllText(Pointer, missingFolder);   // the folder it names is not there
        SettingsStore a = SettingsStore.Open(DefaultFile, Pointer);
        Assert.Equal((DefaultFile, "dark"), (a.FilePath, a.Current.Theme));
        Assert.Contains(missingFolder, a.Notice);

        File.WriteAllText(Pointer, Chosen);   // the folder is there, the settings are not
        SettingsStore b = SettingsStore.Open(DefaultFile, Pointer);
        Assert.Equal(DefaultFile, b.FilePath);
        Assert.Contains(Chosen, b.Notice);

        File.WriteAllText(Pointer, "bad|<name>\0");   // not a path at all
        Assert.NotNull(SettingsStore.Open(DefaultFile, Pointer).Notice);

        File.WriteAllText(Pointer, Chosen);
        using (new FileStream(Pointer, FileMode.Open, FileAccess.ReadWrite, FileShare.None))   // held by something else: unreadable
        {
            SettingsStore c = SettingsStore.Open(DefaultFile, Pointer);
            Assert.Equal(DefaultFile, c.FilePath);
            Assert.NotNull(c.Notice);
        }

        await using BunitContext ctx = NewContext(a, picker: null);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();
        Assert.Contains(missingFolder, page.Find(".settings-notice").TextContent);
        Assert.Contains(Path.GetDirectoryName(DefaultFile)!, page.Find("[data-setting='user-settings'] .set-path").TextContent);
    }

    [Fact(DisplayName = "The Settings page shows where the user settings are, and Browse then Move puts them in the chosen folder")]
    public async Task SettingsPageBrowseAndMove()
    {
        SettingsStore store = SettingsStore.Open(DefaultFile, Pointer);
        store.Update(s => s with { Theme = "dark" });
        await using BunitContext ctx = NewContext(store, picker: _ => Chosen);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();
        Assert.Contains(Path.GetDirectoryName(DefaultFile)!, page.Find("[data-setting='user-settings'] .set-path").TextContent);
        Assert.Empty(page.FindAll(".settings-notice"));

        page.Find("[data-setting='user-settings'] button.pick-settings-folder").Click();   // Browse…
        Assert.Equal(Chosen, page.Find("input[aria-label='User settings folder']").GetAttribute("value"));
        page.Find("[data-setting='user-settings'] button.move-settings").Click();

        Assert.Equal(Path.Combine(Chosen, "settings.json"), store.FilePath);
        Assert.Contains(Chosen, page.Find("[data-setting='user-settings'] .set-path").TextContent);
        Assert.Contains("Moved", page.Find(".settings-moved").TextContent);
        Assert.Equal("dark", SettingsStore.Open(DefaultFile, Pointer).Current.Theme);
    }

    private BunitContext NewContext(SettingsStore store, FolderPicker? picker)
    {
        BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(store);
        ctx.Services.AddSingleton<Workspace>();
        if (picker is not null) ctx.Services.AddSingleton(picker);
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        return ctx;
    }

    public void Dispose()
    {
        repo.Dispose();
        try { Directory.Delete(home, recursive: true); } catch (IOException) { }
    }
}
