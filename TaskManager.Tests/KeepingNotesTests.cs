using Notes;
using Notes.PublicDTOs;
using OpenSpecStore;
using WorkItems;

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

    [Fact(DisplayName = "AttachBytes stores pasted data like a picked file: same folder, never overwriting, listed on the note and git-ignored")]
    public void PastedBytes()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

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

    [Fact(DisplayName = "A note's title lives in its frontmatter: setting it leaves the body and its first heading alone, and a note without a title reads it from its first heading")]
    public void TitleLivesInFrontmatter()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        string bodyBefore = NotesApi.Get(123)!.Body;
        Assert.Equal("Ping test", NotesApi.Get(123)!.Title);   // no title: key, so the first heading

        Assert.True(NotesApi.SetTitle(123, "Decided on X").Success);

        Assert.Equal("Decided on X", NotesApi.Get(123)!.Title);
        Assert.Equal(bodyBefore, NotesApi.Get(123)!.Body);   // the heading in the body still says Ping test
        string text = repo.Read(NoteFile);
        Assert.Contains("title: Decided on X", text);
        Assert.Contains("by: Artyom", text);
        Assert.EndsWith("See [[runbook/uptime]] and #wi107.\n", text);

        Assert.True(NotesApi.SaveBody(123, "# Another heading\n\nwords\n").Success);   // editing the text does not retitle the note
        Assert.Equal("Decided on X", NotesApi.Get(123)!.Title);
        Assert.StartsWith("# Another heading", NotesApi.Get(123)!.Body);

        Assert.False(NotesApi.SetTitle(123, "   ").Success);   // a blank is refused
        Assert.Equal("Decided on X", NotesApi.Get(123)!.Title);
        Assert.True(NotesApi.SetTitle(123, "Fix: the # thing\r\nacross lines").Success);   // YAML-special characters survive, lines are joined
        Assert.Equal("Fix: the # thing across lines", NotesApi.Get(123)!.Title);

        Assert.True(NotesApi.SaveBody(123, "no heading at all\n").Success);
        Assert.Equal("Fix: the # thing across lines", NotesApi.Get(123)!.Title);
        Assert.Equal("no heading at all\n", NotesApi.Get(123)!.Body);

        (bool ok, string _, int id) = NotesApi.Create(107, "Tom");   // a note the app creates carries its title in the frontmatter
        Assert.True(ok);
        Assert.Contains("title: New note", repo.Read("openspec/changes/export-remittances/notes/n" + id + ".md"));
        Assert.Equal("New note", NotesApi.Get(id)!.Title);
    }

    [Fact(DisplayName = "An image pasted into a description is saved under the project's attachments folder for that work item, never overwriting, and the folder is git-ignored")]
    public void DescriptionImage()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        byte[] png = [0x89, 0x50, 0x4E, 0x47];
        const string dir = "local/task-tracking/wi123/";
        try
        {
            (bool ok, string path) = NotesApi.AttachItemImage(repo.Root, 123, "pasted-20261109-101500.png", png);
            Assert.True(ok);
            Assert.Equal(dir + "pasted-20261109-101500.png", path);   // the repository-relative path, forward slashes: what the description links to
            Assert.Equal(png, File.ReadAllBytes(repo.PathOf(path)));
            Assert.Equal(dir + "pasted-20261109-101500-2.png", NotesApi.AttachItemImage(repo.Root, 123, "pasted-20261109-101500.png", [1]).Message);   // never overwritten
            Assert.Equal(png, File.ReadAllBytes(repo.PathOf(path)));
            Assert.Single(repo.Read(".gitignore").Replace("\r", "").Split('\n'), l => l.Trim() == "local/");
            Assert.Empty(NotesApi.Get(123)!.Attachments);   // no note was involved
            Assert.False(NotesApi.AttachItemImage(repo.Root, 123, "", png).Success);

            NotesApi.SetAttachmentsDir(repo.Root, "files/shots");   // a project's own folder is honoured and git-ignored too
            Assert.Equal("files/shots/wi123/pasted-a.png", NotesApi.AttachItemImage(repo.Root, 123, "pasted-a.png", png).Message);
            Assert.Contains(repo.Read(".gitignore").Replace("\r", "").Split('\n'), l => l.Trim() == "files/shots/");
        }
        finally { NotesApi.SetAttachmentsDir(repo.Root, null); }
    }

    [Fact(DisplayName = "Unticked checkboxes in a note become subtasks once: each line gets (created subtask) appended and stays in the note; ticked lines and code blocks are left alone")]
    public void PromoteSubtasks()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        const string body = "# Plan\n\n- [ ] restrict permissions\n  * [ ] write the docs\n- [x] already done\n- [ ]   \n```\n- [ ] inside a code block\n```\n- [ ] kept as is (created subtask)\nplain text\n";
        Assert.True(NotesApi.SaveBody(123, body).Success);

        (bool ok, string message, IReadOnlyList<string> created) = NotesApi.PromoteSubtasks(123);

        Assert.True(ok, message);
        Assert.Equal(["2.2", "2.3"], created);
        Assert.Equal(["Hide internals from anonymous callers", "restrict permissions", "write the docs"], WorkItemsApi.Get(123)!.Subtasks.TakeLast(3).Select(s => s.Text));
        Assert.Contains("- [ ] restrict permissions (created subtask)\n", NotesApi.Get(123)!.Body);
        Assert.Contains("  * [ ] write the docs (created subtask)\n", NotesApi.Get(123)!.Body);
        Assert.Contains("- [x] already done\n", NotesApi.Get(123)!.Body);
        Assert.Contains("```\n- [ ] inside a code block\n```\n", NotesApi.Get(123)!.Body);
        Assert.Contains("- [ ] kept as is (created subtask)\nplain text\n", NotesApi.Get(123)!.Body);
        Assert.Equal(2, WorkItemsApi.GetEvents(123).Count(e => e.Kind == "subtask-added"));
        string tasks = repo.Read("openspec/changes/add-health-check/tasks.md");
        Assert.Contains("restrict permissions", tasks);
        Assert.DoesNotContain("inside a code block", tasks);

        // a second pass finds nothing new: nothing is written and nothing is made twice
        string after = repo.Read(NoteFile);
        (ok, message, created) = NotesApi.PromoteSubtasks(123);
        Assert.True(ok, message);
        Assert.Empty(created);
        Assert.Equal(after, repo.Read(NoteFile));
        Assert.Equal(2, WorkItemsApi.GetEvents(123).Count(e => e.Kind == "subtask-added"));
        Assert.False(NotesApi.PromoteSubtasks(9999).Success);
    }

    [Fact(DisplayName = "A note can be created with a date of its own, which is its created time")]
    public void NoteWithOwnDate()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        DateTime when = new(2026, 11, 1, 10, 30, 0);

        (bool ok, string message, int id) = NotesApi.Create(123, "Claude", when);

        Assert.True(ok, message);
        Assert.Equal(when, NotesApi.Get(id)!.Created);
        Assert.Contains("created: 2026-11-01T10:30:00", repo.Read("openspec/changes/add-health-check/notes/n" + id + ".md"));
        (ok, message, int now) = NotesApi.Create(123, "Claude");   // the two-argument form still dates it now
        Assert.True(ok, message);
        Assert.True(NotesApi.Get(now)!.Created > DateTime.Now.AddMinutes(-1));
    }

    [Fact(DisplayName = "A note whose frontmatter does not parse still shows with its body and a warning")]
    public void NoteWithBrokenFrontmatter()
    {
        using TestRepo repo = new();
        const string text = "---\nid: 7\nby: Artyom\nlinks: [unclosed\ntitle: Fine\n---\n# Heading\n\nThe body survives.\n";
        repo.Write("openspec/changes/add-health-check/notes/n7.md", text);
        OpenSpecStoreApi.Open(repo.Root);

        Note note = NotesApi.GetForWorkItem(123).Single(n => n.Id == 7);

        Assert.Contains("could not be read", note.Title);
        Assert.Contains("line 4", note.Title);
        Assert.Contains("The body survives.", note.Body);
        Assert.False(NotesApi.SetTitle(7, "New title").Success);   // never overwrites the frontmatter it could not read
        Assert.Equal(text, repo.Read("openspec/changes/add-health-check/notes/n7.md"));
        Assert.Equal("Ping test", NotesApi.Get(123)!.Title);   // the other notes are unaffected
    }

    [Fact(DisplayName = "A note's title, author, links and attachments read back identical whatever characters they contain")]
    public void FrontmatterValuesReadBackIdentical()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        foreach (string v in YamlChecks.Nasty)
        {
            string because = $"(value: {v})";
            (bool ok, string message, int id) = NotesApi.Create(123, v);
            Assert.True(ok, message);
            string title = string.Join(' ', v.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
            Assert.True(NotesApi.SetTitle(id, v).Success, because);
            Assert.True(NotesApi.AddManualLink(id, v).Success, because);
            Assert.True(NotesApi.SetLinkedCommits(id, [v]).Success, because);
            Assert.True(NotesApi.StampAgent(id, v).Success, because);
            string url = "https://example.com/" + v.Replace('\n', ' ');
            bool linked = Uri.TryCreate(url.Trim(), UriKind.Absolute, out _);
            if (linked) Assert.True(NotesApi.AttachLink(id, url).Success, because);
            string name = string.Concat(v.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim().TrimEnd('.');
            if (name.Length > 0) Assert.True(NotesApi.AttachBytes(id, name, [1, 2, 3]).Success, because);

            string file = $"openspec/changes/add-health-check/notes/n{id}.md";
            (string front, string _) = Notes.BusinessRules.NoteFile.Split(repo.Read(file));
            YamlChecks.Valid(front[4..^5], YamlChecks.Nasty, because);
            OpenSpecStoreApi.Open(repo.Root);   // from the files, not from the cache
            Note note = NotesApi.Get(id)!;
            Assert.Equal(title, note.Title);
            Assert.Equal(v, note.By);
            Assert.Equal(v, note.AgentBy);
            Assert.Equal([v], note.Commits);
            Assert.Equal(v.Trim(), note.ManualLinks[0]);
            Assert.Equal(Convert.ToInt32(linked) + Convert.ToInt32(name.Length > 0), note.Attachments.Count);
            if (linked) Assert.Equal(url.Trim(), note.Attachments[0]);
            if (name.Length > 0) Assert.EndsWith("/" + name, note.Attachments[^1]);
        }
    }

    [Fact(DisplayName = "A note frontmatter title with an unquoted colon is repaired on read")]
    public void NoteWithUnquotedColonIsRepaired()
    {
        using TestRepo repo = new();
        const string file = "openspec/changes/add-health-check/notes/n7.md";
        repo.Write(file, "---\nid: 7\nby: Dr. No: the villain\ntitle: Ping: pong #1\ncreated: 2026-10-02T09:14:00\n---\n# Heading\n\nThe body survives.\n");
        OpenSpecStoreApi.Open(repo.Root);

        Note note = NotesApi.GetForWorkItem(123).Single(n => n.Id == 7);

        Assert.Equal("Ping: pong #1", note.Title);
        Assert.Equal("Dr. No: the villain", note.By);
        Assert.Equal(new DateTime(2026, 10, 2, 9, 14, 0), note.Created);
        Assert.Contains("The body survives.", note.Body);
        Assert.True(NotesApi.AddManualLink(7, "#wi123").Success);   // a write is allowed, and writes the repaired form
        string after = repo.Read(file).Replace("\r\n", "\n");
        Assert.Matches("(?m)^title: [\"']Ping: pong #1[\"']$", after);   // quoted, whichever quote the writer picks
        Assert.Matches("(?m)^by: [\"']Dr. No: the villain[\"']$", after);
        YamlChecks.Valid(Notes.BusinessRules.NoteFile.Split(after).Frontmatter[4..^5], []);
        Assert.EndsWith("The body survives.\n", after);
        Assert.Equal("Ping: pong #1", NotesApi.Get(7)!.Title);
    }
}
