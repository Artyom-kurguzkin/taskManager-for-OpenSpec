using Notes;
using Notes.PublicDTOs;
using OpenSpecStore;

namespace TaskManager.Tests;

[Collection(nameof(ModuleStateCollection))]
public sealed class KeepingNotesTests
{
    private const string NoteFile = "openspec/changes/add-health-check/notes/n123.md";

    [Fact(DisplayName = "A note in a change's notes folder is listed on its work item")]
    public void ListedOnWorkItem()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        Note note = Assert.Single(NotesApi.GetForWorkItem(123));

        Assert.Equal((123, 123, "Ping test", "Artyom"), (note.Id, note.WorkItemId, note.Title, note.By));
        Assert.Equal(new DateTime(2026, 10, 2, 9, 14, 0), note.Created);
        Assert.StartsWith("# Ping test", note.Body);
    }

    [Fact(DisplayName = "A note an agent stamped is flagged until reviewed; reviewing adds reviewedAt and leaves the text alone")]
    public void AgentNote()
    {
        using TestRepo repo = new();
        string text = repo.Read(NoteFile);
        repo.Write(NoteFile, new System.Text.RegularExpressions.Regex("\n---\n").Replace(text, "\nagentBy: Claude\nagentAt: 2026-10-30T09:00:00\n---\n", 1));
        OpenSpecStoreApi.Open(repo.Root);
        Assert.Equal(("Claude", true), (NotesApi.Get(123)!.AgentBy, NotesApi.Get(123)!.AgentUnreviewed));

        Assert.True(NotesApi.MarkReviewed(123).Success);

        Assert.False(NotesApi.Get(123)!.AgentUnreviewed);
        string after = repo.Read(NoteFile);
        Assert.Contains("reviewedAt: ", after);
        Assert.Contains("agentBy: Claude", after);
        Assert.Contains("by: Artyom", after);
        Assert.EndsWith("See [[runbook/uptime]] and #wi107.\n", after);
    }

    [Fact(DisplayName = "Adding a note creates notes/n<next>.md with its author and time")]
    public void CreateNote()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        (bool ok, string _, int id) = NotesApi.Create(107, "Tom");

        Assert.True(ok);
        Assert.Equal(124, id);
        string text = repo.Read("openspec/changes/export-remittances/notes/n124.md");
        Assert.StartsWith("---\nid: 124\nby: Tom\ncreated: ", text);
        Assert.Equal(107, NotesApi.Get(124)!.WorkItemId);
    }

    [Fact(DisplayName = "A note's next id counts every note in the repository, archived ones included, by file name and by frontmatter")]
    public void NextIdCountsEveryNote()
    {
        using TestRepo repo = new();
        repo.Write("openspec/changes/archive/2026-09-18-session-timeout/notes/n200.md", "---\nid: 150\nby: Tom\ncreated: 2026-09-01T09:00:00\n---\n# Old\n");   // the file name is the larger number
        OpenSpecStoreApi.Open(repo.Root);

        (bool ok, string _, int first) = NotesApi.Create(107, "Tom");

        Assert.True(ok);
        Assert.Equal(201, first);
        repo.Write("openspec/changes/plain-change/notes/n5.md", "---\nid: 300\nby: Tom\ncreated: 2026-10-01T09:00:00\n---\n# Odd\n");   // the frontmatter is the larger number
        OpenSpecStoreApi.MarkOwnChange(repo.PathOf("openspec/changes/plain-change/notes/n5.md"));

        (ok, _, int second) = NotesApi.Create(107, "Tom");

        Assert.True(ok);
        Assert.Equal(301, second);
        Assert.True(File.Exists(repo.PathOf("openspec/changes/export-remittances/notes/n301.md")));
    }

    [Fact(DisplayName = "Two notes with the same id resolve themselves: the newer one takes the next free id and its file follows; a frontmatter id that disagrees with the file name follows the file name")]
    public void DuplicateAndMismatchedNoteIdsResolve()
    {
        using TestRepo repo = new();
        repo.Write("openspec/changes/archive/2026-09-18-session-timeout/notes/n130.md", "---\nid: 130\nby: Tom\ncreated: 2026-09-01T09:00:00\n---\n# Old\n");
        repo.Write("openspec/changes/export-remittances/notes/n123.md", "---\nid: 123\nby: Tom\ncreated: 2026-10-05T10:00:00\n---\n# Twin note\n\nSee #n123.\n");   // written with a stale number, later than the original
        repo.Write("openspec/changes/plain-change/notes/n7.md", "---\nid: 99\nby: Tom\ncreated: 2026-10-06T10:00:00\n---\n# Misnumbered\n");
        OpenSpecStoreApi.Open(repo.Root);

        IReadOnlyList<Note> all = NotesApi.GetAll();

        Assert.Equal([7, 123, 130, 131], all.Select(n => n.Id).Order());
        Assert.Equal(123, NotesApi.Get(123)!.WorkItemId);   // the older note keeps the number and its file
        Note moved = NotesApi.Get(131)!;
        Assert.Equal((107, "Twin note"), (moved.WorkItemId, moved.Title));
        Assert.Equal(repo.PathOf("openspec/changes/export-remittances/notes/n131.md"), moved.Path);
        Assert.False(File.Exists(repo.PathOf("openspec/changes/export-remittances/notes/n123.md")));
        Assert.Equal("---\nid: 131\nby: Tom\ncreated: 2026-10-05T10:00:00\n---\n# Twin note\n\nSee #n123.\n", repo.Read("openspec/changes/export-remittances/notes/n131.md"));   // links to #n123 are left as written
        Assert.Equal("---\nid: 7\nby: Tom\ncreated: 2026-10-06T10:00:00\n---\n# Misnumbered\n", repo.Read("openspec/changes/plain-change/notes/n7.md"));
        Assert.Contains("id: 123", repo.Read(NoteFile));

        OpenSpecStoreApi.MarkOwnChange(repo.PathOf(NoteFile));   // read again from disk: nothing is renumbered twice
        Assert.Equal(all.Select(n => (n.Id, n.Path)), NotesApi.GetAll().Select(n => (n.Id, n.Path)));
    }

    [Fact(DisplayName = "Saving a note keeps its frontmatter")]
    public void SaveKeepsFrontmatter()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        (bool ok, string _) = NotesApi.SaveBody(123, "# Ping test\n\nNew text.\n");

        Assert.True(ok);
        Assert.Equal("---\nid: 123\nby: Artyom\ncreated: 2026-10-02T09:14:00\n---\n# Ping test\n\nNew text.\n", repo.Read(NoteFile));
        Assert.Equal("# Ping test\n\nNew text.\n", NotesApi.Get(123)!.Body);
    }

    [Fact(DisplayName = "Links resolve to docs, work items and notes, and unknown ones are marked unresolved")]
    public void ResolveLinks()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        IReadOnlyList<NoteLink> links = NotesApi.ResolveLinks("See [[runbook/uptime]], [[missing/page]], #wi107, #wi999 and #n123.");

        Assert.Equal(
        [
            new NoteLink("doc", "runbook/uptime", true),
            new NoteLink("doc", "missing/page", false),
            new NoteLink("item", "107", true),
            new NoteLink("item", "999", false),
            new NoteLink("note", "123", true),
        ], links);
    }

    [Fact(DisplayName = "#wi1234 is not mistaken for #wi123")]
    public void WholeTokens()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        Assert.Equal([new NoteLink("item", "1234", false)], NotesApi.ResolveLinks("Fix #wi1234"));
    }

    [Fact(DisplayName = "Entries added by hand are kept in the note's frontmatter")]
    public void ManualLinks()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        NotesApi.AddManualLink(123, "PR #42 on GitHub");

        Assert.Equal(["PR #42 on GitHub"], NotesApi.Get(123)!.ManualLinks);
        Assert.Contains("# Ping test", repo.Read(NoteFile));
    }

    [Fact(DisplayName = "Commits linked by hand are saved in the note's frontmatter and can be unlinked")]
    public void LinkedCommits()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        Assert.True(NotesApi.SetLinkedCommits(123, ["a1b2c3d"]).Success);
        Assert.Equal(["a1b2c3d"], NotesApi.Get(123)!.Commits);

        Assert.True(NotesApi.SetLinkedCommits(123, []).Success);
        Assert.Empty(NotesApi.Get(123)!.Commits);
    }

    [Fact(DisplayName = "A note takes attachments: a file is copied under local/task-tracking and .gitignore learns local/, a link is kept as a URL, and removing an attachment forgets it (and deletes the copied file)")]
    public void Attachments()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        string src = Path.Combine(Path.GetTempPath(), "rtm-att-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(src);
        string file = Path.Combine(src, "shot.txt");
        File.WriteAllText(file, "evidence");
        const string dir = "local/task-tracking/wi123/n123/";

        Assert.True(NotesApi.AttachFile(123, file).Success);
        Assert.True(NotesApi.AttachFile(123, file).Success);   // same name again: suffixed, not overwritten

        Assert.Equal([dir + "shot.txt", dir + "shot-2.txt"], NotesApi.Get(123)!.Attachments);
        Assert.Equal("evidence", repo.Read(dir + "shot.txt"));
        Assert.True(File.Exists(repo.PathOf(dir + "shot-2.txt")));
        Assert.Single(repo.Read(".gitignore").Replace("\r", "").Split('\n'), l => l.Trim() == "local/");
        Assert.Contains("attachments:", repo.Read(NoteFile));

        Assert.True(NotesApi.AttachLink(123, "https://example.com/x").Success);
        Assert.False(NotesApi.AttachLink(123, "notaurl").Success);
        Assert.Equal(3, NotesApi.Get(123)!.Attachments.Count);
        Assert.Equal("https://example.com/x", NotesApi.Get(123)!.Attachments[2]);

        Assert.True(NotesApi.RemoveAttachment(123, dir + "shot.txt").Success);
        Assert.False(File.Exists(repo.PathOf(dir + "shot.txt")));
        Assert.True(NotesApi.RemoveAttachment(123, "https://example.com/x").Success);
        Assert.Equal([dir + "shot-2.txt"], NotesApi.Get(123)!.Attachments);
        Assert.Contains("by: Artyom", repo.Read(NoteFile));   // the rest of the frontmatter is kept
        Directory.Delete(src, true);
    }

    [Fact(DisplayName = "A note's title is its first heading: SetTitle rewrites it, adds one when missing, refuses blank; AttachBytes stores pasted data like a picked file")]
    public void TitleAndPastedBytes()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        Assert.True(NotesApi.SetTitle(123, "Decided on X").Success);
        Assert.Equal("Decided on X", NotesApi.Get(123)!.Title);
        Assert.StartsWith("# Decided on X", NotesApi.Get(123)!.Body);
        Assert.Contains("by: Artyom", repo.Read(NoteFile));

        Assert.False(NotesApi.SetTitle(123, "   ").Success);
        Assert.Equal("Decided on X", NotesApi.Get(123)!.Title);

        Assert.True(NotesApi.SaveBody(123, "just words\n").Success);
        Assert.True(NotesApi.SetTitle(123, "Now titled").Success);
        Assert.Equal("Now titled", NotesApi.Get(123)!.Title);
        Assert.Equal("# Now titled\n\njust words\n", NotesApi.Get(123)!.Body);

        const string dir = "local/task-tracking/wi123/n123/";
        byte[] png = [0x89, 0x50, 0x4E, 0x47];
        Assert.True(NotesApi.AttachBytes(123, "pasted-1.png", png).Success);
        Assert.True(NotesApi.AttachBytes(123, "pasted-1.png", png).Success);   // same name again: suffixed, not overwritten
        Assert.Equal([dir + "pasted-1.png", dir + "pasted-1-2.png"], NotesApi.Get(123)!.Attachments);
        Assert.Equal(png, File.ReadAllBytes(repo.PathOf(dir + "pasted-1.png")));
        Assert.Single(repo.Read(".gitignore").Replace("\r", "").Split('\n'), l => l.Trim() == "local/");
        Assert.False(NotesApi.AttachBytes(123, "", png).Success);
    }

    [Fact(DisplayName = "A note's attachment is stored under its project's attachments folder, the default being local/task-tracking")]
    public void AttachmentsFolderPerProject()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        byte[] png = [0x89, 0x50, 0x4E, 0x47];
        const string defaultDir = "local/task-tracking/wi123/n123/", custom = "files/shots/wi123/n123/";
        try
        {
            Assert.True(NotesApi.AttachBytes(123, "a.png", png).Success);   // nothing configured: the default

            NotesApi.SetAttachmentsDir(repo.Root, "files\\shots/");   // slashes and a trailing one are tidied
            Assert.True(NotesApi.AttachBytes(123, "b.png", png).Success);
            Assert.True(NotesApi.AttachBytes(123, "b.png", png).Success);   // never overwritten, here too
            Assert.Equal([defaultDir + "a.png", custom + "b.png", custom + "b-2.png"], NotesApi.Get(123)!.Attachments);   // earlier entries stay where they were
            Assert.Equal(png, File.ReadAllBytes(repo.PathOf(custom + "b.png")));
            Assert.Contains(repo.Read(".gitignore").Replace("\r", "").Split('\n'), l => l.Trim() == "files/shots/");   // the folder is git-ignored
            Assert.Contains(repo.Read(".gitignore").Replace("\r", "").Split('\n'), l => l.Trim() == "local/");        // as the default was before

            Assert.True(NotesApi.RemoveAttachment(123, custom + "b.png").Success);   // the copied file goes with its entry
            Assert.False(File.Exists(repo.PathOf(custom + "b.png")));
            Assert.True(NotesApi.RemoveAttachment(123, defaultDir + "a.png").Success);   // also one from before the folder moved
            Assert.False(File.Exists(repo.PathOf(defaultDir + "a.png")));

            NotesApi.SetAttachmentsDir(repo.Root, "../outside");   // a folder outside the repository is refused: the default is used
            Assert.True(NotesApi.AttachBytes(123, "c.png", png).Success);
            Assert.True(File.Exists(repo.PathOf(defaultDir + "c.png")));

            NotesApi.SetAttachmentsDir(repo.Root, "");   // empty = the default again
            Assert.True(NotesApi.AttachBytes(123, "d.png", png).Success);
            Assert.True(File.Exists(repo.PathOf(defaultDir + "d.png")));
        }
        finally { NotesApi.SetAttachmentsDir(repo.Root, null); }
    }

    [Fact(DisplayName = "A pasted image is written into the project its note lives in, even while another thread keeps switching the active project (no entry without its file)")]
    public void AttachmentFollowsItsNoteNotTheActiveProject()
    {
        using TestRepo a = new();
        using TestRepo b = new();
        OpenSpecStoreApi.Open(a.Root, keepOthers: true);
        OpenSpecStoreApi.Open(b.Root, keepOthers: true);   // the fixture's note 123 exists in both
        OpenSpecStoreApi.Use(a.Root);
        using CancellationTokenSource stop = new();
        Task flipper = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested) { OpenSpecStoreApi.Use(b.Root); OpenSpecStoreApi.Use(a.Root); }   // what Workspace.In does for a file event
        });
        byte[] png = [1, 2, 3];
        for (int i = 0; i < 300; i++) NotesApi.AttachBytes(123, $"p{i}.png", png);
        stop.Cancel();
        flipper.Wait();

        foreach (TestRepo repo in new[] { a, b })
        {
            string note = Path.Combine(repo.OpenSpec, "changes", "add-health-check", "notes", "n123.md");
            foreach (string entry in File.ReadAllLines(note).Where(l => l.StartsWith("- local/")).Select(l => l[2..]))
                Assert.True(File.Exists(repo.PathOf(entry)), $"{entry} is listed in {repo.Root} but the file is not there");
        }
    }
}
