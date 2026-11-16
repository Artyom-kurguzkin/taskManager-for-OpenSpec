using System.Diagnostics;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Notes;
using Notes.PublicDTOs;
using OpenSpecStore;
using Radzen;
using TaskManager.Components.Pages;
using TaskManager.Components.Shared;
using TaskManager.AppState;
using TaskManager.BackgroundJobs;
using TaskManager.Helpers;
using WorkItems;

namespace TaskManager.Tests;

/// <summary>
/// The attachments folder is a project setting (openspec/config.yaml, x-rtm.attachmentsDir), and changing it moves the files and the references to them.
/// </summary>
[Collection(nameof(RealTimeCollection))]
public sealed class MovingAttachmentsTests : IDisposable
{
    private const string Def = "local/task-tracking";
    private const string NoteFile = "openspec/changes/add-health-check/notes/n123.md";
    private const string Proposal = "openspec/changes/add-health-check/proposal.md";
    private const string Archived = "openspec/changes/archive/2026-09-18-session-timeout";
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    private readonly TestRepo repo = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-att-" + Guid.NewGuid().ToString("N") + ".json");
    private readonly List<string> links = [];

    public void Dispose()
    {
        foreach (string link in links) try { if (Directory.Exists(link)) Directory.Delete(link); } catch (IOException) { }   // the link only, never what it points to
        NotesApi.SetAttachmentsDir(repo.Root, null);
        repo.Dispose();
        try { File.Delete(settingsFile); } catch (IOException) { }
    }

    // ── helpers ──

    private SettingsStore NewSettings(string? file = null) => new(file ?? settingsFile);

    private void Bytes(string rel, params byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(repo.PathOf(rel))!);
        File.WriteAllBytes(repo.PathOf(rel), bytes.Length == 0 ? Png : bytes);
    }

    private void Note(string file, int id, string[] attachments, string body) =>
        repo.Write(file, $"---\nid: {id}\nby: Artyom\ncreated: 2026-10-02T09:14:00\n" + (attachments.Length > 0 ? "attachments:\n" + string.Concat(attachments.Select(a => $"- {a}\n")) : "") + $"---\n{body}");

    private static string Unix(string text) => text.Replace("\r\n", "\n");

    private string Config() => Unix(repo.Read("openspec/config.yaml"));

    private void Open() => Assert.True(OpenSpecStoreApi.Open(repo.Root).Success);

    private void Junction(string link, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        using Process p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!;
        p.StandardOutput.ReadToEnd();
        p.WaitForExit(10000);
        Assert.True(Directory.Exists(link), "the junction could not be made");
        links.Add(link);
    }

    private static AttachmentsMoveResult Move(string root, string from, string to) => NotesApi.MoveAttachments(root, from, to);

    // ── the setting ──

    [Fact(DisplayName = "The attachments folder is saved with the project, and a folder a user had set before is used until the project has one")]
    public void SavedWithTheProject()
    {
        SettingsStore settings = NewSettings();
        string key = Path.GetFullPath(repo.Root).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();   // how the settings file keys a repository
        settings.Update(s => s with { AttachmentsDirs = new() { [key] = "mine/old" } });   // what this computer had before the folder was a project setting
        Assert.Equal("mine/old", settings.AttachmentsDir(repo.Root));                     // so nothing changes for an existing user

        Assert.True(settings.SetAttachmentsDir(repo.Root, "files\\att/").Success);       // tidied like before

        Assert.Equal("files/att", settings.AttachmentsDir(repo.Root));
        Assert.Null(settings.Current.AttachmentsDirs);                                    // the user's own value is cleared for this repository
        string config = Config();
        Assert.Contains("x-rtm:\n  lanes:\n    In Progress: [High priority, Lower priority]\n  attachmentsDir: files/att\n", config);   // one inline line at the end of the project's block
        Assert.Contains("schema: spec-driven\n\n# lanes per column, written by the app\n", config);   // the other lines are as they were
        Assert.Contains("  lanes:\n    In Progress: [High priority, Lower priority]\n", config);
        Assert.Equal("files/att", NewSettings(settingsFile + ".other").AttachmentsDir(repo.Root));   // another computer reading the same repository gets it

        settings.Update(s => s with { AttachmentsDirs = new() { [key] = "mine/old" } });  // a stale per-user value never beats the project's
        Assert.Equal("files/att", settings.AttachmentsDir(repo.Root));
    }

    [Fact(DisplayName = "Setting the attachments folder back to the default removes it from the project's settings")]
    public void BackToTheDefaultRemovesIt()
    {
        SettingsStore settings = NewSettings();
        Assert.True(settings.SetAttachmentsDir(repo.Root, "files/att").Success);
        Assert.Contains("attachmentsDir: files/att", Config());

        Assert.True(settings.SetAttachmentsDir(repo.Root, Def).Success);

        Assert.DoesNotContain("attachmentsDir", Config());
        Assert.Contains("  lanes:\n    In Progress: [High priority, Lower priority]\n", Config());   // the block stays for what else it holds
        Assert.Equal(Def, settings.AttachmentsDir(repo.Root));

        Assert.True(settings.SetAttachmentsDir(repo.Root, "files/att").Success);
        Assert.True(settings.SetAttachmentsDir(repo.Root, "").Success);                  // empty is the default too
        Assert.DoesNotContain("attachmentsDir", Config());
        Assert.Equal(Def, settings.AttachmentsDir(repo.Root));
    }

    [Fact(DisplayName = "An attachments folder inside openspec, outside the repository or equal to the repository is refused")]
    public void RefusedFolders()
    {
        SettingsStore settings = NewSettings();
        string before = Config();
        foreach (string bad in new[] { "openspec", "openspec/files", "OpenSpec/files", "../outside", "a/../../outside", repo.Root, "C:/elsewhere", "/abs", ".", "./", "a/<b>" })
        {
            (bool ok, string message) = settings.SetAttachmentsDir(repo.Root, bad);
            Assert.False(ok, bad);
            Assert.NotEmpty(message);
            Assert.False(NotesApi.CheckAttachmentsDir(bad).Ok, bad);
        }
        Assert.Equal(before, Config());                       // nothing was written
        Assert.Equal(Def, settings.AttachmentsDir(repo.Root));

        Open();
        Bytes($"{Def}/wi123/a.png");
        foreach (string bad in new[] { "openspec/files", "../outside", "." })
        {
            Assert.False(Move(repo.Root, Def, bad).Success, bad);   // a move refuses the same folders, from either side
            Assert.False(Move(repo.Root, bad, "files/att").Success, bad);
        }
        Assert.True(File.Exists(repo.PathOf($"{Def}/wi123/a.png")));

        repo.Write("openspec/config.yaml", before.Replace("x-rtm:\n", "x-rtm:\n  attachmentsDir: ../outside\n"));   // a hand edit that is not allowed is not followed
        Assert.Equal(Def, settings.AttachmentsDir(repo.Root));
    }

    // ── the move ──

    [Fact(DisplayName = "Moving the attachments folder moves every file and the notes list them at the new place")]
    public void MovesEveryFile()
    {
        Bytes($"{Def}/wi123/n123/shot.png", 1);
        Bytes($"{Def}/wi123/pasted-1.png", 2);
        Bytes($"{Def}/wi107/n55/spec.pdf", 3);
        Note(NoteFile, 123, [$"{Def}/wi123/n123/shot.png", "https://example.com/y.png"], $"# Ping test\n\nSee ![]({Def}/wi123/n123/shot.png)\n");
        Open();

        AttachmentsMoveResult r = Move(repo.Root, Def, "files/att");

        Assert.True(r.Success, r.Message);
        Assert.Equal((3, 0, 0, 0, 2), (r.Moved, r.Renamed, r.Duplicates, r.Left, r.ReferencesRewritten));
        Assert.Equal([1], File.ReadAllBytes(repo.PathOf("files/att/wi123/n123/shot.png")));
        Assert.Equal([2], File.ReadAllBytes(repo.PathOf("files/att/wi123/pasted-1.png")));
        Assert.Equal([3], File.ReadAllBytes(repo.PathOf("files/att/wi107/n55/spec.pdf")));
        Assert.False(Directory.Exists(repo.PathOf(Def)));      // nothing is left of the old folder, not even the empty folders
        Assert.Equal(["files/att/wi123/n123/shot.png", "https://example.com/y.png"], NotesApi.Get(123)!.Attachments);
        Assert.Contains("by: Artyom", repo.Read(NoteFile));    // the rest of the frontmatter is kept
        Assert.Contains("![](files/att/wi123/n123/shot.png)", NotesApi.Get(123)!.Body);
        Assert.False(File.Exists(repo.PathOf("openspec/changes/add-health-check/events.log")));   // no worklog noise
    }

    [Fact(DisplayName = "Pasted images in descriptions and note bodies point to the new folder after a move, including names with spaces")]
    public void PastedImagesFollow()
    {
        Bytes($"{Def}/wi123/my shot.png");
        Bytes($"{Def}/wi123/my shot 2.png");
        Bytes($"{Def}/wi123/n123/p.png");
        repo.Write(Proposal, $"# Proposal\n\n![]({Def}/wi123/my%20shot.png)\n\n![](<{Def}/wi123/my shot 2.png>)\n\nSee {Def}/wi123/my shot.png as plain text.\n");
        Note(NoteFile, 123, [], $"# Ping test\n\n![a]({Def}/wi123/n123/p.png) and [again]({Def}/wi123/n123/p.png \"title\")\n\n[ref]: {Def}/wi123/n123/p.png\n");
        Open();

        Assert.True(Move(repo.Root, Def, "files/att").Success);

        Assert.Equal($"# Proposal\n\n![](files/att/wi123/my%20shot.png)\n\n![](<files/att/wi123/my shot 2.png>)\n\nSee {Def}/wi123/my shot.png as plain text.\n", Unix(repo.Read(Proposal)));
        Assert.Equal("# Ping test\n\n![a](files/att/wi123/n123/p.png) and [again](files/att/wi123/n123/p.png \"title\")\n\n[ref]: files/att/wi123/n123/p.png\n", NotesApi.Get(123)!.Body);
        Assert.True(File.Exists(repo.PathOf("files/att/wi123/my shot 2.png")));

        Assert.True(Move(repo.Root, "files/att", "files/att store").Success);   // a folder with a space is written the way each link was: encoded, in angle brackets, or as it was
        Assert.Contains("![](files/att%20store/wi123/my%20shot.png)", repo.Read(Proposal));
        Assert.Contains("![](<files/att store/wi123/my shot 2.png>)", repo.Read(Proposal));
        Assert.Contains("![a](files/att%20store/wi123/n123/p.png)", NotesApi.Get(123)!.Body);
    }

    [Fact(DisplayName = "A move never overwrites: the same file is dropped, a different file with the same name is kept beside it and the note points to the kept copy")]
    public void NeverOverwrites()
    {
        Bytes($"{Def}/wi123/n123/a.png", 1);
        Bytes($"{Def}/wi123/n123/b.png", 2);
        Bytes("files/att/wi123/n123/a.png", 1);          // the same bytes: the source is dropped
        Bytes("files/att/wi123/n123/b.png", 9);          // other bytes: both are kept
        Note(NoteFile, 123, [$"{Def}/wi123/n123/a.png", $"{Def}/wi123/n123/b.png"], $"# Ping test\n\n![]({Def}/wi123/n123/b.png)\n");
        Open();

        AttachmentsMoveResult r = Move(repo.Root, Def, "files/att");

        Assert.True(r.Success, r.Message);
        Assert.Equal((1, 1, 1, 0), (r.Moved, r.Renamed, r.Duplicates, r.Left));
        Assert.Equal([9], File.ReadAllBytes(repo.PathOf("files/att/wi123/n123/b.png")));      // the one that was there is untouched
        Assert.Equal([2], File.ReadAllBytes(repo.PathOf("files/att/wi123/n123/b-2.png")));    // the moved one, named like attachments avoid collisions
        Assert.Equal([1], File.ReadAllBytes(repo.PathOf("files/att/wi123/n123/a.png")));
        Assert.Equal(["files/att/wi123/n123/a.png", "files/att/wi123/n123/b-2.png"], NotesApi.Get(123)!.Attachments);
        Assert.Contains("![](files/att/wi123/n123/b-2.png)", NotesApi.Get(123)!.Body);
        Assert.False(Directory.Exists(repo.PathOf(Def)));

        Bytes($"{Def}/wi123/n123/b.png", 2);              // the moved one again: it is already there as b-2, not a third copy
        AttachmentsMoveResult again = Move(repo.Root, Def, "files/att");
        Assert.True(again.Success);
        Assert.Equal((0, 0, 1), (again.Moved, again.Renamed, again.Duplicates));
        Assert.False(File.Exists(repo.PathOf("files/att/wi123/n123/b-3.png")));
    }

    [Fact(DisplayName = "Archived items' attachments move and are rewritten too")]
    public void ArchivedToo()
    {
        Bytes($"{Def}/wi124/n300/old.png");
        Bytes($"{Def}/wi124/diagram.png");
        Note($"{Archived}/notes/n300.md", 300, [$"{Def}/wi124/n300/old.png"], $"# Old\n\n![]({Def}/wi124/n300/old.png)\n");
        repo.Write($"{Archived}/proposal.md", $"# Proposal\n\n![]({Def}/wi124/diagram.png)\n");
        Open();

        AttachmentsMoveResult r = Move(repo.Root, Def, "files/att");

        Assert.True(r.Success, r.Message);
        Assert.Equal(3, r.ReferencesRewritten);
        string note = Unix(repo.Read($"{Archived}/notes/n300.md"));
        Assert.Contains("- files/att/wi124/n300/old.png\n", note);
        Assert.Contains("![](files/att/wi124/n300/old.png)", note);
        Assert.Contains("![](files/att/wi124/diagram.png)", repo.Read($"{Archived}/proposal.md"));
        Assert.True(File.Exists(repo.PathOf("files/att/wi124/diagram.png")));
    }

    [Fact(DisplayName = "Links to the web and text that only mentions the folder are left alone")]
    public void LeavesOtherTextAlone()
    {
        Bytes($"{Def}/wi123/n123/p.png");
        string body = $"# Ping test\n\nThe folder {Def}/ holds the images; see `{Def}/wi123/x.png`.\n"
            + $"A [web link](https://example.com/{Def}/wi123/n123/p.png) and ![web image](https://example.com/{Def}/a.png).\n"
            + $"Another [folder](docs/{Def}/x.md) and [near miss]({Def}-old/wi123/x.png) and [the folder itself]({Def}).\n\n"
            + $"```\n![]({Def}/wi123/n123/p.png)\n```\n\nReal: ![]({Def}/wi123/n123/p.png)\n";
        Note(NoteFile, 123, [$"https://example.com/{Def}/wi123/n123/p.png", $"{Def}-old/x.png", $"{Def}/wi123/n123/p.png"], body);
        Open();

        AttachmentsMoveResult r = Move(repo.Root, Def, "files/att");

        Assert.True(r.Success, r.Message);
        Assert.Equal(2, r.ReferencesRewritten);
        Assert.Equal([$"https://example.com/{Def}/wi123/n123/p.png", $"{Def}-old/x.png", "files/att/wi123/n123/p.png"], NotesApi.Get(123)!.Attachments);
        Assert.Equal(body[..body.LastIndexOf("Real: ", StringComparison.Ordinal)] + "Real: ![](files/att/wi123/n123/p.png)\n", NotesApi.Get(123)!.Body);   // only the last line changed
    }

    [Fact(DisplayName = "An entry or link written with another case of the folder is found on Windows and written with the new folder exactly")]
    public void CaseInsensitivePrefix()
    {
        Bytes($"{Def}/wi123/n123/p.png");
        Note(NoteFile, 123, ["Local/Task-Tracking/wi123/n123/p.png"], "# Ping test\n\n![](LOCAL/task-tracking/wi123/n123/p.png)\n");
        Open();

        Assert.True(Move(repo.Root, "local/TASK-tracking", "Files/Att").Success);   // the from folder in other case finds the same folder

        Assert.Equal(["Files/Att/wi123/n123/p.png"], NotesApi.Get(123)!.Attachments);
        Assert.Contains("![](Files/Att/wi123/n123/p.png)", NotesApi.Get(123)!.Body);
        Assert.True(File.Exists(repo.PathOf("files/att/wi123/n123/p.png")));
    }

    [Fact(DisplayName = "A move that was interrupted is finished by running it again")]
    public void InterruptedMoveIsFinished()
    {
        Bytes("files/att/wi123/n123/a.png", 1);         // moved before the interruption
        Bytes($"{Def}/wi123/n123/b.png", 2);            // not moved yet
        Bytes($"{Def}/wi123/n123/c.png", 3);            // copied, the source not removed yet
        Bytes("files/att/wi123/n123/c.png", 3);
        Note(NoteFile, 123, ["files/att/wi123/n123/a.png", $"{Def}/wi123/n123/b.png", $"{Def}/wi123/n123/c.png"], $"# Ping test\n\n![](files/att/wi123/n123/a.png) ![]({Def}/wi123/n123/b.png)\n");
        repo.Write("openspec/changes/export-remittances/proposal.md", $"# Proposal\n\n![]({Def}/wi107/x.png)\n");
        Bytes($"{Def}/wi107/x.png", 4);
        Open();

        AttachmentsMoveResult r = Move(repo.Root, Def, "files/att");

        Assert.True(r.Success, r.Message);
        Assert.Equal((2, 0, 1), (r.Moved, r.Renamed, r.Duplicates));
        Assert.Equal(["files/att/wi123/n123/a.png", "files/att/wi123/n123/b.png", "files/att/wi123/n123/c.png"], NotesApi.Get(123)!.Attachments);
        Assert.Contains("![](files/att/wi123/n123/b.png)", NotesApi.Get(123)!.Body);
        Assert.Contains("![](files/att/wi107/x.png)", repo.Read("openspec/changes/export-remittances/proposal.md"));
        Assert.False(Directory.Exists(repo.PathOf(Def)));

        AttachmentsMoveResult again = Move(repo.Root, Def, "files/att");   // nothing is left to do
        Assert.True(again.Success);
        Assert.Equal((0, 0, 0, 0, 0), (again.Moved, again.Renamed, again.Duplicates, again.Left, again.ReferencesRewritten));
    }

    [Fact(DisplayName = "A link inside the attachments folder is not followed and is left where it is")]
    public void LinksAreNotFollowed()
    {
        string outside = Path.Combine(Path.GetTempPath(), "rtm-out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "not ours");
        try
        {
            Bytes($"{Def}/wi123/n123/p.png");
            Junction(repo.PathOf($"{Def}/wi123/linked"), outside);
            Note(NoteFile, 123, [$"{Def}/wi123/n123/p.png"], "# Ping test\n");
            Open();

            AttachmentsMoveResult r = Move(repo.Root, Def, "files/att");

            Assert.True(r.Success, r.Message);
            Assert.Equal((1, 1), (r.Moved, r.Left));
            Assert.True(File.Exists(repo.PathOf("files/att/wi123/n123/p.png")));
            Assert.True(File.Exists(Path.Combine(outside, "secret.txt")));                  // what it points to is untouched
            Assert.True(Directory.Exists(repo.PathOf($"{Def}/wi123/linked")));              // the link stays where it was
            Assert.False(Directory.Exists(repo.PathOf("files/att/wi123/linked")));
            Assert.Contains("linked", r.Message);                                           // and is reported

            // the folders themselves, or a folder above them inside the repository, being links is refused: nothing changes
            string other = Path.Combine(Path.GetTempPath(), "rtm-out-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(other);
            try
            {
                Junction(repo.PathOf("via"), other);
                Bytes($"{Def}/wi123/q.png");
                Assert.False(Move(repo.Root, Def, "via/att").Success);        // into a link
                Assert.False(Move(repo.Root, "via", "files/att").Success);     // out of a link
                Assert.False(Move(repo.Root, "via/att", "files/att").Success); // from below a link
                Assert.Empty(Directory.GetFileSystemEntries(other));
                Assert.True(File.Exists(repo.PathOf($"{Def}/wi123/q.png")));
            }
            finally { Directory.Delete(other, true); }
        }
        finally { foreach (string l in links.ToList()) { if (Directory.Exists(l)) Directory.Delete(l); links.Remove(l); } Directory.Delete(outside, true); }
    }

    [Fact(DisplayName = "Moving to the same folder, into itself, or from a folder that does not exist changes nothing")]
    public void RefusedMoves()
    {
        Bytes($"{Def}/wi123/n123/p.png");
        Note(NoteFile, 123, [$"{Def}/wi123/n123/p.png"], "# Ping test\n");
        Open();
        string note = repo.Read(NoteFile);

        Assert.False(Move(repo.Root, Def, Def).Success);
        Assert.False(Move(repo.Root, Def, "LOCAL/Task-Tracking/").Success);              // the same folder in other spelling
        Assert.False(Move(repo.Root, Def, $"{Def}/inner").Success);                      // into itself
        Assert.False(Move(repo.Root, $"{Def}/wi123", Def).Success);                      // out of a folder it sits in: the target contains the source
        Assert.All(new[] { Move(repo.Root, Def, Def), Move(repo.Root, Def, $"{Def}/inner") }, r => Assert.NotEmpty(r.Message));
        Assert.True(File.Exists(repo.PathOf($"{Def}/wi123/n123/p.png")));
        Assert.Equal(note, repo.Read(NoteFile));

        AttachmentsMoveResult none = Move(repo.Root, "no/such/folder", "files/att");      // nothing to move: a success, with nothing done
        Assert.True(none.Success);
        Assert.Equal((0, 0, 0, 0, 0), (none.Moved, none.Renamed, none.Duplicates, none.Left, none.ReferencesRewritten));
        Assert.False(Directory.Exists(repo.PathOf("files")));
        Assert.False(File.Exists(repo.PathOf(".gitignore")));
        Assert.Equal(note, repo.Read(NoteFile));
    }

    [Fact(DisplayName = "The new folder is git-ignored after a move")]
    public void GitIgnoredAfterAMove()
    {
        repo.Write(".gitignore", "node_modules/\nlocal/\n");
        Bytes($"{Def}/wi123/p.png");
        Open();

        Assert.True(Move(repo.Root, Def, "files/att").Success);

        string[] lines = Unix(repo.Read(".gitignore")).Split('\n');
        Assert.Contains("files/att/", lines);
        Assert.Contains("local/", lines);                       // the old line is left alone
        Assert.Contains("node_modules/", lines);
        Assert.Single(lines, l => l == "files/att/");

        Assert.False(Move(repo.Root, "files/att", "files/att").Success);
        Bytes("files/att/wi1/q.png");
        Assert.True(Move(repo.Root, "files/att", "pics/att").Success);                   // and again for the next one, once
        Assert.Single(Unix(repo.Read(".gitignore")).Split('\n'), l => l == "pics/att/");
    }

    [Fact(DisplayName = "A note whose frontmatter cannot be read is left exactly as it is, and the move says so")]
    public void UnreadableNoteIsLeftAlone()
    {
        Bytes($"{Def}/wi123/n123/p.png");
        string broken = $"---\nid: 123\nby: Artyom\nlinks: [unclosed\ncreated: 2026-10-02T09:14:00\nattachments:\n- {Def}/wi123/n123/p.png\n---\n# Ping test\n\n![]({Def}/wi123/n123/p.png)\n";
        repo.Write(NoteFile, broken);
        Open();

        AttachmentsMoveResult r = Move(repo.Root, Def, "files/att");

        Assert.True(r.Success, r.Message);
        Assert.Equal(broken, repo.Read(NoteFile));
        Assert.Contains("n123", r.Message);
        Assert.True(File.Exists(repo.PathOf("files/att/wi123/n123/p.png")));
    }

    [Fact(DisplayName = "The attachment count answers how many files a folder holds without following links, and the root is the folder in effect")]
    public void CountsAndRoot()
    {
        Bytes($"{Def}/wi123/n123/a.png");
        Bytes($"{Def}/wi123/b.png");
        Open();

        Assert.Equal(2, NotesApi.CountAttachmentFiles(repo.Root, Def));
        Assert.Equal(0, NotesApi.CountAttachmentFiles(repo.Root, "nope"));
        Assert.Equal(Path.GetFullPath(repo.PathOf(Def)), NotesApi.AttachmentsRoot(repo.Root));
        NotesApi.SetAttachmentsDir(repo.Root, "files/att");
        Assert.Equal(Path.GetFullPath(repo.PathOf("files/att")), NotesApi.AttachmentsRoot(repo.Root));
    }

    // ── the app follows the setting ──

    [Fact(DisplayName = "A new attachment goes to the project's folder right after it changes")]
    public void NewAttachmentsGoToTheNewFolder()
    {
        SettingsStore settings = NewSettings();
        using Workspace workspace = new(settings, new UiService());
        Assert.True(workspace.Open(repo.Root).Success);

        Assert.True(settings.SetAttachmentsDir(repo.Root, "files/att").Success);
        Assert.True(NotesApi.AttachBytes(123, "x.png", Png).Success);

        Assert.True(File.Exists(repo.PathOf("files/att/wi123/n123/x.png")));
        Assert.Equal(Path.GetFullPath(repo.PathOf("files/att")), NotesApi.AttachmentsRoot(repo.Root));
        Assert.Contains("attachments=files/att", File.ReadLines(repo.PathOf("openspec/REPO-TASKS.md")).First());   // the agents' guide says so as well

        Assert.True(settings.SetAttachmentsDir(repo.Root, "").Success);
        Assert.True(NotesApi.AttachBytes(123, "y.png", Png).Success);
        Assert.True(File.Exists(repo.PathOf($"{Def}/wi123/n123/y.png")));
    }

    [Fact(DisplayName = "A change of the attachments folder in config.yaml from outside is followed without a restart")]
    public async Task OutsideEditOfTheConfigIsFollowed()
    {
        SettingsStore settings = NewSettings();
        using Workspace workspace = new(settings, new UiService());
        Assert.True(workspace.Open(repo.Root).Success);
        Assert.Equal(Path.GetFullPath(repo.PathOf(Def)), NotesApi.AttachmentsRoot(repo.Root));

        repo.Write("openspec/config.yaml", "schema: spec-driven\n\n# lanes per column, written by the app\nx-rtm:\n  attachmentsDir: pulled/dir\n  lanes:\n    In Progress: [High priority, Lower priority]\n");   // a git pull
        string want = Path.GetFullPath(repo.PathOf("pulled/dir"));
        for (int i = 0; i < 100 && NotesApi.AttachmentsRoot(repo.Root) != want; i++) await Task.Delay(50);

        Assert.Equal(want, NotesApi.AttachmentsRoot(repo.Root));
        Assert.True(NotesApi.AttachBytes(123, "z.png", Png).Success);
        Assert.True(File.Exists(repo.PathOf("pulled/dir/wi123/n123/z.png")));
        Assert.Equal("pulled/dir", settings.AttachmentsDir(repo.Root));
        Assert.Contains("attachments=pulled/dir", File.ReadLines(repo.PathOf("openspec/REPO-TASKS.md")).First());

        repo.Write("openspec/config.yaml", "schema: spec-driven\n");                           // and back, by the same way
        want = Path.GetFullPath(repo.PathOf(Def));
        for (int i = 0; i < 100 && NotesApi.AttachmentsRoot(repo.Root) != want; i++) await Task.Delay(50);
        Assert.Equal(want, NotesApi.AttachmentsRoot(repo.Root));
    }

    // ── Settings ──

    private BunitContext NewContext(out IRenderedComponent<Overlays> overlays)
    {
        BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(NewSettings());
        ctx.Services.AddSingleton<Workspace>();
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        overlays = ctx.Render<Overlays>();
        return ctx;
    }

    private static AngleSharp.Dom.IElement Field(IRenderedComponent<SettingsPage> page) => page.Find("input[aria-label='Files folder']");

    private void WithImages()
    {
        Bytes($"{Def}/wi123/n123/shot.png", 1);
        Bytes($"{Def}/wi123/pasted-1.png", 2);
        Note(NoteFile, 123, [$"{Def}/wi123/n123/shot.png"], "# Ping test\n");
        repo.Write(Proposal, $"# Proposal\n\n![]({Def}/wi123/pasted-1.png)\n");
    }

    [Fact(DisplayName = "Changing the folder in Settings asks before moving, and Keep them where they are only changes where new files go")]
    public async Task SettingsAsksBeforeMoving()
    {
        WithImages();
        await using BunitContext ctx = NewContext(out _);
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();

        Field(page).Change("files/att");

        string ask = page.Find(".att-ask").TextContent;
        Assert.Contains("Move 2 images from", ask);
        Assert.Contains(Def, ask);
        Assert.Contains("files/att/images", ask);
        Assert.Contains("Notes, descriptions and plan links will point to the new places.", ask);
        Assert.Equal(["Move", "Keep them where they are", "Cancel"], page.FindAll(".att-ask button").Select(b => b.TextContent.Trim()));
        Assert.Equal(Def, settings.AttachmentsDir(repo.Root));                          // nothing is saved before the answer
        Assert.Null(settings.FilesDir(repo.Root));
        Assert.True(File.Exists(repo.PathOf($"{Def}/wi123/n123/shot.png")));

        page.Find(".att-ask .att-cancel").Click();                                       // cancelling puts the field back
        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".att-ask")));
        Assert.True(string.IsNullOrEmpty(Field(page).GetAttribute("value")));          // the old layout has no files folder to show
        Assert.Equal(Def, settings.AttachmentsDir(repo.Root));

        await page.InvokeAsync(() => { });
        Field(page).Change("files/att");
        page.Find(".att-ask .att-keep").Click();
        page.WaitForAssertion(() => Assert.Equal("files/att/images", settings.AttachmentsDir(repo.Root)));
        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".att-ask")));
        Assert.True(File.Exists(repo.PathOf($"{Def}/wi123/n123/shot.png")));           // the files stay
        Assert.Contains($"{Def}/wi123/n123/shot.png", Unix(repo.Read(NoteFile)));       // and so do the references
        Assert.Contains("filesDir: files/att", Config());
        Assert.True(NotesApi.AttachBytes(123, "new.png", Png).Success);                  // only new files go to the new folder
        Assert.True(File.Exists(repo.PathOf("files/att/images/wi123/n123/new.png")));
        Assert.Equal("files/att", Field(page).GetAttribute("value"));
    }

    [Fact(DisplayName = "After Move the settings show how many files moved and the notes show their images")]
    public async Task SettingsMoves()
    {
        WithImages();
        await using BunitContext ctx = NewContext(out _);
        IRenderedComponent<WorkItemPage> before = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        Assert.Equal(2, before.FindAll("img").Count(i => (i.GetAttribute("src") ?? "").StartsWith("data:image/png")));

        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();
        Field(page).Change("files/att");
        page.Find(".att-ask .att-move").Click();

        page.WaitForAssertion(() => Assert.Contains("Moved 2 files", page.Find(".att-done").TextContent));
        Assert.Contains("2 references", page.Find(".att-done").TextContent);   // the entry of the note and the link in the description
        Assert.Empty(page.FindAll(".att-ask"));
        Assert.Equal("files/att/images", ctx.Services.GetRequiredService<SettingsStore>().AttachmentsDir(repo.Root));
        Assert.Equal("files/att", Field(page).GetAttribute("value"));
        Assert.True(File.Exists(repo.PathOf("files/att/images/wi123/n123/shot.png")));
        Assert.True(File.Exists(repo.PathOf("files/att/images/wi123/pasted-1.png")));
        Assert.False(Directory.Exists(repo.PathOf(Def)));

        IRenderedComponent<WorkItemPage> after = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        Assert.Equal(2, after.FindAll("img").Count(i => (i.GetAttribute("src") ?? "").StartsWith("data:image/png")));
    }

    [Fact(DisplayName = "Changing the folder in Settings saves at once when the old folder holds no files, and refuses a folder that is not allowed with the reason")]
    public async Task SettingsSavesWithoutFilesAndRefusesBadFolders()
    {
        await using BunitContext ctx = NewContext(out _);
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();

        Field(page).Change("files/att");                                                 // nothing in the old folder: nothing to ask
        Assert.Empty(page.FindAll(".att-ask"));
        Assert.Equal("files/att/images", settings.AttachmentsDir(repo.Root));

        await page.InvokeAsync(() => { });
        Field(page).Change("openspec/pics");
        page.WaitForAssertion(() => Assert.Contains("openspec", page.Find(".att-warn").TextContent));
        Assert.Equal("files/att/images", settings.AttachmentsDir(repo.Root));
        Assert.Empty(page.FindAll(".att-ask"));
        Assert.Contains("saved with the project", page.Find("section[data-project] .muted").TextContent);   // the hint no longer calls the attachments folder the user's alone
    }
}
