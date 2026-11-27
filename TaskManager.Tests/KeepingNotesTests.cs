using Notes;
using Notes.PublicDTOs;
using OpenSpecStore;
using TaskManager.AppState;
using WorkItems;

namespace TaskManager.Tests;

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
        Assert.Equal("---\nid: 131\nby: Tom\ncreated: 2026-10-05T10:00:00\n---\n# Twin note\n\nSee #n131.\n", repo.Read("openspec/changes/export-remittances/notes/n131.md"));   // what the note says about itself follows it; other notes' links to #n123 are left as written
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
        byte[] png = [1, 2, 3];

        // The other thread's switch (what Workspace.In does for a file event) lands at the one moment that matters: after the note is found
        // and its folder chosen, while the bytes are being written, before the entry is added to the note. No thread, no loop: the switch is the write step itself.
        (bool ok, string message) = Notes.Commands.Store(123, "p.png", target => { OpenSpecStoreApi.Use(b.Root); File.WriteAllBytes(target, png); });
        Assert.True(ok, message);
        OpenSpecStoreApi.Use(a.Root);

        string[] inA = Entries(a), inB = Entries(b);
        Assert.Single(inA);                                      // the entry is in the project the note lives in...
        Assert.True(File.Exists(a.PathOf(inA[0])), $"{inA[0]} is listed in {a.Root} but the file is not there");   // ...and so is its file
        Assert.Empty(inB);                                       // nothing leaked into the project that was active at the end
        Assert.Empty(Directory.Exists(b.PathOf("local")) ? Directory.GetFiles(b.PathOf("local"), "*", SearchOption.AllDirectories) : []);
    }

    private static string[] Entries(TestRepo repo) =>
        [.. File.ReadAllLines(Path.Combine(repo.OpenSpec, "changes", "add-health-check", "notes", "n123.md")).Where(l => l.StartsWith("- local/")).Select(l => l[2..])];

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
        // the values do not touch one another: four workers, each with a repository of its own, take every fourth (see IsolatedWorkers)
        IsolatedWorkers.Run(YamlChecks.Nasty, 4, values =>
        {
            using TestRepo repo = new();
            OpenSpecStoreApi.Open(repo.Root);
            foreach (string v in values)
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
        });
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
    // ── note kinds ──

    [Fact(DisplayName = "A note can be a decision: created with kind decision it reads back as one, SetKind turns a note into a decision or back, and an ordinary note has no kind")]
    public void DecisionNotes()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        (bool ok, string message, int id) = NotesApi.Create(123, "Claude", DateTime.Now, "decision");
        Assert.True(ok, message);
        Assert.Equal("decision", NotesApi.Get(id)!.Kind);
        Assert.Contains("kind: decision", repo.Read("openspec/changes/add-health-check/notes/n" + id + ".md"));

        (ok, message, int plain) = NotesApi.Create(123, "Claude");
        Assert.True(ok, message);
        Assert.Null(NotesApi.Get(plain)!.Kind);
        Assert.DoesNotContain("kind:", repo.Read("openspec/changes/add-health-check/notes/n" + plain + ".md"));

        (ok, message) = NotesApi.SetKind(plain, "decision");
        Assert.True(ok, message);
        Assert.Equal("decision", NotesApi.Get(plain)!.Kind);
        (ok, message) = NotesApi.SetKind(plain, null);
        Assert.True(ok, message);
        Assert.Null(NotesApi.Get(plain)!.Kind);
        Assert.False(NotesApi.SetKind(plain, "poem").Success);   // only known kinds
    }

    // ── events for every write ──

    [Fact(DisplayName = "Attaching a file or a URL to a note records an attachment-added event on its work item naming the note and the file")]
    public void AttachmentsAreRecordedOnTheWorkItem()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        WorkItemsApi.ConfigureActor(() => "Tester");
        string src = Path.Combine(Path.GetTempPath(), "rtm-att-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(src);
            string file = Path.Combine(src, "shot.txt");
            File.WriteAllText(file, "evidence");

            Assert.True(NotesApi.AttachFile(123, file).Success);
            Assert.True(NotesApi.AttachFile(123, file).Success);   // stored as shot-2.txt
            Assert.True(NotesApi.AttachBytes(123, "pasted.png", [1, 2, 3]).Success);
            Assert.True(NotesApi.AttachLink(123, "https://example.com/x").Success);
            Assert.False(NotesApi.AttachLink(123, "notaurl").Success);   // refused: nothing recorded
            Assert.True(NotesApi.RemoveAttachment(123, "https://example.com/x").Success);   // removing is not an addition: it is its own event

            var events = WorkItemsApi.GetEvents(NotesApi.Get(123)!.WorkItemId);
            Assert.Equal(
                [("attachment-added", "n123: shot.txt"), ("attachment-added", "n123: shot-2.txt"), ("attachment-added", "n123: pasted.png"), ("attachment-added", "n123: https://example.com/x"), ("attachment-removed", "n123: https://example.com/x")],
                events.Select(e => (e.Kind, e.Text)));
            Assert.All(events, e => Assert.Equal("Tester", e.By));
        }
        finally { WorkItemsApi.ConfigureActor(null); try { Directory.Delete(src, true); } catch (IOException) { } }
    }

    // ── edited promoted subtask ──

    [Fact(DisplayName = "Editing a note line that already made a subtask retexts that subtask instead of creating another, and the line keeps one marker at its end")]
    public void EditingAPromotedLineRetextsItsSubtask()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        Assert.True(NotesApi.SaveBody(123, "- [ ] Still need to rework the Linked tasks\n").Success);
        (bool ok, string message, IReadOnlyList<string> created) = NotesApi.PromoteSubtasks(123);
        Assert.True(ok, message);
        string number = Assert.Single(created);
        int count = WorkItemsApi.Get(123)!.Subtasks.Count;
        Assert.Equal("- [ ] Still need to rework the Linked tasks (created subtask)\n", NotesApi.Get(123)!.Body);

        // the user types after the marker and saves
        Assert.True(NotesApi.SaveBody(123, "- [ ] Still need to rework the Linked tasks (created subtask) - layout should be nicer\n").Success);
        (ok, message, created) = NotesApi.PromoteSubtasks(123);

        Assert.True(ok, message);
        Assert.Empty(created);
        Assert.Equal(count, WorkItemsApi.Get(123)!.Subtasks.Count);
        Assert.Equal("Still need to rework the Linked tasks - layout should be nicer", WorkItemsApi.Get(123)!.Subtasks.Single(s => s.Number == number).Text);
        Assert.Equal("- [ ] Still need to rework the Linked tasks - layout should be nicer (created subtask)\n", NotesApi.Get(123)!.Body);
        Assert.Equal(1, WorkItemsApi.GetEvents(123).Count(e => e.Kind == "subtask-added"));

        // and again: the new text is now the original
        Assert.True(NotesApi.SaveBody(123, "- [ ] Still need to rework the Linked tasks - layout should be nicer (created subtask) too\n").Success);
        (ok, message, created) = NotesApi.PromoteSubtasks(123);
        Assert.True(ok, message);
        Assert.Empty(created);
        Assert.Equal(count, WorkItemsApi.Get(123)!.Subtasks.Count);
        Assert.Equal("Still need to rework the Linked tasks - layout should be nicer too", WorkItemsApi.Get(123)!.Subtasks.Single(s => s.Number == number).Text);
        Assert.Equal("- [ ] Still need to rework the Linked tasks - layout should be nicer too (created subtask)\n", NotesApi.Get(123)!.Body);
    }

    [Fact(DisplayName = "A line with the marker anywhere is never promoted again, and no subtask text ever contains the marker")]
    public void AMarkedLineIsNeverPromotedAgain()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        int before = WorkItemsApi.Get(123)!.Subtasks.Count;
        Assert.True(NotesApi.SaveBody(123, "- [ ] same\n- [ ] same\n").Success);
        Assert.Equal(2, NotesApi.PromoteSubtasks(123).Created.Count);
        int count = WorkItemsApi.Get(123)!.Subtasks.Count;
        Assert.Equal(before + 2, count);

        // no subtask has the text before the marker, or several do: the subtasks are left alone, the line is only tidied
        Assert.True(NotesApi.SaveBody(123, "- [ ] never made (created subtask) b\n- [ ] x (created subtask) y (created subtask)\n- [ ] same (created subtask) edited\n  * [ ]   spaced   out (created subtask)  more\n").Success);
        (bool ok, string message, IReadOnlyList<string> created) = NotesApi.PromoteSubtasks(123);

        Assert.True(ok, message);
        Assert.Empty(created);
        Assert.Equal(count, WorkItemsApi.Get(123)!.Subtasks.Count);
        Assert.All(WorkItemsApi.Get(123)!.Subtasks, s => Assert.DoesNotContain("(created subtask)", s.Text));
        Assert.Equal("- [ ] never made b (created subtask)\n- [ ] x y (created subtask)\n- [ ] same edited (created subtask)\n  * [ ]   spaced out more (created subtask)\n", NotesApi.Get(123)!.Body);
        Assert.Equal(["same", "same"], WorkItemsApi.Get(123)!.Subtasks.TakeLast(2).Select(s => s.Text));

        // a tidy body is not written again
        string after = repo.Read(NoteFile);
        Assert.True(NotesApi.PromoteSubtasks(123).Success);
        Assert.Equal(after, repo.Read(NoteFile));
    }

    // ── convert entries ──

    [Fact(DisplayName = "Deleting a note removes its file and the attachment files copied for it, and refuses a note that is gone")]
    public void DeleteNote()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        Assert.True(NotesApi.AttachBytes(123, "pasted.png", [1, 2, 3]).Success);
        Assert.True(NotesApi.AttachLink(123, "https://example.com/x").Success);
        string copied = NotesApi.Get(123)!.Attachments.Single(a => !a.StartsWith("http"));
        Assert.True(File.Exists(Path.Combine(repo.Root, copied)));

        (bool ok, string message) = NotesApi.Delete(123);

        Assert.True(ok, message);
        Assert.Null(NotesApi.Get(123));
        Assert.False(File.Exists(Path.Combine(repo.Root, NoteFile)));
        Assert.False(File.Exists(Path.Combine(repo.Root, copied)));
        Assert.False(NotesApi.Delete(123).Success);
    }

    [Fact(DisplayName = "Converting a note to a decision and back changes only its kind and records a converted event each time")]
    public void ConvertKind()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        string title = NotesApi.Get(123)!.Title;

        (bool ok, string message) = NotesApi.ConvertKind(123, NoteKinds.Decision);
        Assert.True(ok, message);
        Assert.True(NotesApi.Get(123)!.IsDecision);
        Assert.Equal(title, NotesApi.Get(123)!.Title);
        Assert.True(NotesApi.ConvertKind(123, null).Success);
        Assert.False(NotesApi.Get(123)!.IsDecision);

        Assert.Equal(["note #n123 -> decision", "decision #n123 -> note"], WorkItemsApi.GetEvents(123).Where(e => e.Kind == "converted").Select(e => e.Text));
        Assert.False(NotesApi.ConvertKind(123, "poem").Success);
        Assert.False(NotesApi.ConvertKind(999, null).Success);
    }

    [Fact(DisplayName = "Converting a note to a subtask uses its title, or the first line of its text when the title is the default, and keeps the note")]
    public void ConvertNoteToSubtask()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        // a real title wins
        (bool ok, string message, string number) = NotesApi.ConvertToSubtask(123);
        Assert.True(ok, message);
        Assert.Equal("2.2", number);
        Assert.Equal("Ping test #n123", WorkItemsApi.Get(123)!.Subtasks[^1].Text);
        Assert.NotNull(NotesApi.Get(123));
        Assert.Contains(WorkItemsApi.GetEvents(123), e => e.Kind == "converted" && e.Text == "note #n123 -> subtask 2.2");

        // the default title: the first non-empty line, without its markdown lead, cut to 200 characters
        (bool _, string _, int plain) = NotesApi.Create(123, "Tom", DateTime.Now);
        Assert.True(NotesApi.SaveBody(plain, "\n\n## - [ ] " + new string('x', 300) + "\nsecond line\n").Success);
        (ok, message, number) = NotesApi.ConvertToSubtask(plain);
        Assert.True(ok, message);
        Assert.Equal(new string('x', 200) + $" #n{plain}", WorkItemsApi.Get(123)!.Subtasks[^1].Text);
        Assert.NotNull(NotesApi.Get(plain));

        (bool _, string _, int decision) = NotesApi.Create(123, "Tom", DateTime.Now, NoteKinds.Decision);
        Assert.True(NotesApi.SaveBody(decision, "- Keep it simple\n").Success);
        Assert.True(NotesApi.ConvertToSubtask(decision).Success);
        Assert.Equal($"Keep it simple #n{decision}", WorkItemsApi.Get(123)!.Subtasks[^1].Text);

        // nothing to make a subtask of: refused, and the note stays
        (bool _, string _, int empty) = NotesApi.Create(123, "Tom", DateTime.Now);
        Assert.False(NotesApi.ConvertToSubtask(empty).Success);
        Assert.NotNull(NotesApi.Get(empty));
    }

    [Fact(DisplayName = "Converting a note to a subtask keeps the note and its body, and the subtask line refers to it")]
    public void ConvertingKeepsTheBodyAndRefersToIt()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        (bool _, string _, int note) = NotesApi.Create(123, "Tom", DateTime.Now);
        Assert.True(NotesApi.SetTitle(note, "cr").Success);
        Assert.True(NotesApi.SaveBody(note, "When a subtask is being moved to a separate work item, add a log in the feed\n").Success);
        string noteFile = $"openspec/changes/add-health-check/notes/n{note}.md";
        string file = repo.Read(noteFile);

        (bool ok, string message, string number) = NotesApi.ConvertToSubtask(note);

        Assert.True(ok, message);
        Assert.Equal(file, repo.Read(noteFile));   // the note, with its body, is not touched
        Assert.Contains("add a log in the feed", NotesApi.Get(note)!.Body);
        string line = WorkItemsApi.Get(123)!.Subtasks.Single(s => s.Number == number).Text;
        Assert.Equal($"cr #n{note}", line);
        Assert.Equal(("cr", (int?)note), NotesApi.SubtaskReference(line));
        Assert.Equal(("plain text #n", (int?)null), NotesApi.SubtaskReference("plain text #n"));
        Assert.Equal(("two #n1 refs", (int?)7), NotesApi.SubtaskReference("two #n1 refs #n7"));
        Assert.Contains(note, NotesApi.PromotedNotes(123));
        Assert.Contains(WorkItemsApi.GetEvents(123), e => e.Kind == "converted" && e.Text == $"note #n{note} -> subtask {number}");

        // the reference follows the subtask when the numbers move: the note is still promoted after the subtask above it goes
        Assert.Equal("2.2", number);
        Assert.True(WorkItemsApi.RemoveSubtask(123, "2.1").Success);   // the new subtask is 2.1 now, and the converted event still says 2.2
        Assert.Contains(note, NotesApi.PromotedNotes(123));
    }

    [Fact(DisplayName = "Converting a subtask to a note or a decision creates it from the subtask's text, dated now and by the given person, and keeps the subtask")]
    public void ConvertSubtaskToNote()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        (bool ok, string message, int id) = NotesApi.ConvertSubtask(123, "1.2", NoteKinds.Decision, "Tom");
        Assert.True(ok, message);
        Note made = NotesApi.Get(id)!;
        Assert.True(made.IsDecision);
        Assert.Equal("Tom", made.By);
        Assert.Equal(DateTime.Today, made.Created.Date);
        Assert.Equal(["1.1", "1.2", "1.3", "2.1"], WorkItemsApi.Get(123)!.Subtasks.Select(s => s.Number)); // the subtask stays
        Assert.Contains(WorkItemsApi.GetEvents(123), e => e.Kind == "converted" && e.Text == $"subtask 1.2 -> decision #n{id}");

        (ok, message, int plain) = NotesApi.ConvertSubtask(123, "2.1", null, "Tom");
        Assert.True(ok, message);
        Assert.Equal("Hide internals from anonymous callers", NotesApi.Get(plain)!.Title);
        Assert.False(NotesApi.Get(plain)!.IsDecision);
        Assert.Contains(WorkItemsApi.GetEvents(123), e => e.Kind == "converted" && e.Text == $"subtask 2.1 -> note #n{plain}");

        Assert.False(NotesApi.ConvertSubtask(123, "9.9", null, "Tom").Success);   // gone: no note made
        Assert.Equal(3, NotesApi.GetForWorkItem(123).Count);
    }
    // ── nothing is lost; bugs ──

    [Fact(DisplayName = "Promoting a note to a subtask keeps the note with its text and attachments, adds the subtask and records one converted event")]
    public void PromotingKeepsTheNote()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        Assert.True(NotesApi.SaveBody(123, "# Ping test\n\nLong details the user wrote.\n").Success);
        Assert.True(NotesApi.AttachBytes(123, "pasted.png", [1, 2, 3]).Success);
        Assert.True(NotesApi.AttachLink(123, "https://example.com/x").Success);
        Note before = NotesApi.Get(123)!;
        string file = repo.Read(NoteFile);
        int subtasks = WorkItemsApi.Get(123)!.Subtasks.Count;

        (bool ok, string message, string number) = NotesApi.ConvertToSubtask(123);

        Assert.True(ok, message);
        Assert.Equal(file, repo.Read(NoteFile));   // not a byte of the note changed
        Assert.Equal(before, NotesApi.Get(123));
        Assert.True(File.Exists(Path.Combine(repo.Root, before.Attachments.Single(a => !a.StartsWith("http")))));
        Assert.Equal(subtasks + 1, WorkItemsApi.Get(123)!.Subtasks.Count);
        Assert.Equal("Ping test #n123", WorkItemsApi.Get(123)!.Subtasks.Single(s => s.Number == number).Text);
        Assert.Equal(["note #n123 -> subtask " + number], WorkItemsApi.GetEvents(123).Where(e => e.Kind == "converted").Select(e => e.Text));
        Assert.Equal([123], NotesApi.PromotedNotes(123));
        Assert.False(NotesApi.ConvertToSubtask(123).Success);   // already promoted: no second subtask
        Assert.Equal(subtasks + 1, WorkItemsApi.Get(123)!.Subtasks.Count);
    }

    [Fact(DisplayName = "Converting a subtask to a note, a decision or a bug creates it and keeps the subtask")]
    public void SubtaskToNoteDecisionBug()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        string[] numbers = [.. WorkItemsApi.Get(123)!.Subtasks.Select(s => s.Number)];

        (bool ok, string message, int note) = NotesApi.ConvertSubtask(123, "1.1", null, "Tom");
        Assert.True(ok, message);
        (ok, message, int decision) = NotesApi.ConvertSubtask(123, "1.2", NoteKinds.Decision, "Tom");
        Assert.True(ok, message);
        (ok, message, int bug) = NotesApi.ConvertSubtask(123, "2.1", NoteKinds.Bug, "Tom");
        Assert.True(ok, message);

        Assert.Equal(numbers, WorkItemsApi.Get(123)!.Subtasks.Select(s => s.Number));   // every subtask stays
        Assert.True(NotesApi.Get(bug)!.IsBug);
        Assert.Equal(WorkItemsApi.Get(123)!.Subtasks.Single(s => s.Number == "2.1").Text, NotesApi.Get(bug)!.Title);
        Assert.True(NotesApi.Get(decision)!.IsDecision);
        Assert.False(NotesApi.Get(note)!.IsDecision || NotesApi.Get(note)!.IsBug);
        Assert.Equal([$"subtask 1.1 -> note #n{note}", $"subtask 1.2 -> decision #n{decision}", $"subtask 2.1 -> bug #n{bug}"],
            WorkItemsApi.GetEvents(123).Where(e => e.Kind == "converted").Select(e => e.Text));
        Assert.DoesNotContain(WorkItemsApi.GetEvents(123), e => e.Kind == "subtask-removed");

        // a note, a decision and a bug change into one another in place
        Assert.True(NotesApi.ConvertKind(note, NoteKinds.Bug).Success);
        Assert.True(NotesApi.Get(note)!.IsBug);
        Assert.Contains(WorkItemsApi.GetEvents(123), e => e.Kind == "converted" && e.Text == $"note #n{note} -> bug");
        Assert.True(NotesApi.ConvertKind(note, NoteKinds.Decision).Success);
        Assert.Contains(WorkItemsApi.GetEvents(123), e => e.Kind == "converted" && e.Text == $"bug #n{note} -> decision");
    }

    // ── #wi213: notes and feed per item ──

    /// <summary>Thirty more changes, each with one note: a board of the size where reading or listing everything shows.</summary>
    private static void ManyFolders(TestRepo repo, int count)
    {
        for (int i = 0; i < count; i++)
        {
            string dir = $"openspec/changes/bulk-{i:D3}";
            repo.Write(dir + "/.openspec.yaml", $"schema: spec-driven\ncreated: 2026-09-22\nx-rtm:\n  id: {200 + i}\n  title: Bulk item {i}\n  status: Planned\n  priority: low\n");
            repo.Write(dir + "/proposal.md", $"## Why\n\nBulk item {i} exists so the board has many folders.\n");
            repo.Write(dir + "/tasks.md", "# Tasks\n\n## 1. Work\n\n- [ ] 1.1 First\n");
            repo.Write(dir + "/events.log", $"2026-09-22T08:00:00 | Tom | created | Bulk item {i}\n");
            repo.Write(dir + $"/notes/n{1000 + i}.md", $"---\nid: {1000 + i}\nby: Test\ncreated: 2026-10-02T09:14:00\n---\n# Note {i}\n\nBody {i}.\n");
        }
    }

    /// <summary>Opens the many folders with everything read once and the clock far ahead (no file is "racy"), then starts counting: the files opened, and the notes folders listed.</summary>
    private static (List<string> Opened, List<string> Listed) WarmAndCount(TestRepo repo)
    {
        ManyFolders(repo, 30);
        Caches.S.Items.Clock = () => DateTime.UtcNow.AddMinutes(10);
        OpenSpecStoreApi.Open(repo.Root);
        ItemsCache.All(true);
        NotesCache.All();
        foreach (int id in new[] { 205, 206 }) { NotesApi.GetWorklog(id); NotesCache.ForWorkItem(id); }
        List<string> opened = [], listed = [];
        OpenSpecStoreModule.State.ReadLog = opened;
        NotesCache.ListLog = listed;
        return (opened, listed);
    }

    /// <summary>The notes and events.log files among the paths (the board's own headers are the items cache's business), relative to the change folders, forward slashes; folder listings (no file name) are kept as they are.</summary>
    private static List<string> Within(List<string> paths, string repoRoot)
    {
        string changes = Path.Combine(repoRoot, "openspec", "changes");
        lock (paths) return [.. paths.Select(p => Path.GetRelativePath(changes, p).Replace('\\', '/')).Where(p => p.Contains("/notes") || p.EndsWith("events.log"))];
    }

    [Fact(DisplayName = "A note added to one item re-reads that item's notes folder only, not the other items'")]
    public void ANoteReReadsOnlyItsItemsFolder()
    {
        using TestRepo repo = new();
        (List<string> opened, List<string> listed) = WarmAndCount(repo);

        (bool ok, string message, int id) = NotesApi.Create(205, "Tom");
        Assert.True(ok, message);
        Assert.True(NotesApi.SaveEdit(id, "Added to bulk 5.", "Fresh").Success);
        IReadOnlyList<Note> all = NotesCache.All();

        Assert.Contains(all, n => n.Id == id && n.WorkItemId == 205 && n.Title == "Fresh");
        Assert.Equal(32, all.Count);
        Assert.All(Within(opened, repo.Root), p => Assert.StartsWith("bulk-005/", p));   // the item's own notes and events only: no other item's notes were opened
        Assert.All(Within(listed, repo.Root), p => Assert.Equal("bulk-005/notes", p));   // and no other item's notes folder was even looked at
    }

    [Fact(DisplayName = "A feed read after a note is added to one item opens that item's notes and events only")]
    public void TheFeedShowsTheNoteWithoutReReadingOthers()
    {
        using TestRepo repo = new();
        (List<string> opened, List<string> listed) = WarmAndCount(repo);
        IReadOnlyList<WorklogEntry> before = NotesApi.GetWorklog(205);

        Assert.True(NotesApi.SaveEdit(NotesApi.Create(205, "Tom").Id, "Added to bulk 5.", "Fresh").Success);
        IReadOnlyList<WorklogEntry> feed = NotesApi.GetWorklog(205);

        Assert.Equal(before.Count + 1, feed.Count);
        Assert.Contains(feed, e => e.Kind == "note" && e.Text == "Fresh");
        Assert.DoesNotContain(feed, e => e.Note is not null && e.Note.WorkItemId != 205);
        Assert.All(Within(opened, repo.Root), p => Assert.StartsWith("bulk-005/", p));   // the item's notes and events only
        Assert.All(Within(listed, repo.Root), p => Assert.Equal("bulk-005/notes", p));
        Assert.Equal(1, NotesApi.GetWorklog(206).Count(e => e.Kind == "note"));   // another item's feed is as it was
        Assert.DoesNotContain(Within(opened, repo.Root), p => p.StartsWith("bulk-006/notes"));
    }

    [Fact(DisplayName = "The side menu's bug count and the hidden list stay right after a note is added to one item")]
    public void TheSideMenuCountsStayRight()
    {
        using TestRepo repo = new();
        (List<string> opened, List<string> listed) = WarmAndCount(repo);
        Assert.Empty(NotesCache.All().Where(n => n.IsBug));
        Assert.Empty(TaskManager.Components.Pages.BoardModel.Resolutions(NotesCache.All()));

        Assert.True(NotesApi.Write(205, "Claude", new NoteDraft("# Crash on start\n\nIt fails.\n", Kind: NoteKinds.Bug)).Success);
        Assert.True(NotesApi.Resolve(206, WorkItemsApi.ParseResolution("cancelled", 206), "Not needed.", "Claude").Hidden);
        IReadOnlyList<Note> all = NotesCache.All();

        Assert.Equal(["Crash on start"], all.Where(n => n.IsBug).Select(n => n.Title));
        Assert.Equal(new Dictionary<int, string> { [206] = "Cancelled" }, TaskManager.Components.Pages.BoardModel.Resolutions(all));
        Assert.Equal(33, all.Count);
        Assert.All(Within(opened, repo.Root), p => Assert.Matches("^bulk-00[56]/", p));
        Assert.All(Within(listed, repo.Root), p => Assert.Matches("^bulk-00[56]/notes$", p));   // the two items written to; the other 28 were not listed
    }

    [Fact(DisplayName = "An item's events are re-read for that item only when its events.log changes")]
    public void EventsAreReReadOnlyWhenTheirLogChanges()
    {
        using TestRepo repo = new();
        (List<string> opened, _) = WarmAndCount(repo);

        NotesApi.GetWorklog(205);
        NotesApi.GetWorklog(206);
        Assert.DoesNotContain(Within(opened, repo.Root), p => p.EndsWith("events.log"));   // nothing changed: the kept events answer

        Assert.True(WorkItemsApi.LogEvent(205, "started", "Reading the plan.").Success);
        ItemsCache.All(true);   // the board's own read of the changed folder (its newest event time) is the items cache's; counted from here: the feed's
        opened.Clear();
        IReadOnlyList<WorklogEntry> feed = NotesApi.GetWorklog(205);
        NotesApi.GetWorklog(205);
        NotesApi.GetWorklog(206);

        Assert.Contains(feed, e => e.Kind == "started" && e.Text == "Reading the plan.");
        Assert.Empty(Within(opened, repo.Root).Where(p => p.EndsWith("events.log")));   // the app's own write: the text it wrote answers, no log is opened (write-through), and no other item's either

        repo.Write("openspec/changes/bulk-005/events.log", repo.Read("openspec/changes/bulk-005/events.log") + "2026-11-02T10:00:00 | Ann | found | By hand.\n");   // an edit from outside: nobody told the cache
        feed = NotesApi.GetWorklog(205);
        NotesApi.GetWorklog(205);
        NotesApi.GetWorklog(206);

        Assert.Contains(feed, e => e.Kind == "found" && e.Text == "By hand.");
        Assert.Equal(["bulk-005/events.log"], Within(opened, repo.Root).Where(p => p.EndsWith("events.log")));   // once, for the item whose log changed; the other item's kept events answer
    }

    [Fact(DisplayName = "Refreshing one item's notes after an outside edit lists that item's folder only and the notes are right")]
    public void RefreshingOneItemListsOneFolder()
    {
        using TestRepo repo = new();
        (List<string> opened, List<string> listed) = WarmAndCount(repo);

        repo.Write("openspec/changes/bulk-007/notes/n2000.md", "---\nid: 2000\nby: Ann\ncreated: 2026-11-01T09:00:00\n---\n# Added elsewhere\n\nBy hand.\n");   // no tool wrote it: nobody told the cache
        Assert.DoesNotContain(NotesCache.All(), n => n.Id == 2000);
        listed.Clear();
        NotesCache.Refresh(repo.Root, 207);
        IReadOnlyList<Note> all = NotesCache.All();

        Assert.Contains(all, n => n.Id == 2000 && n.WorkItemId == 207 && n.Title == "Added elsewhere");
        Assert.All(Within(listed, repo.Root), p => Assert.Equal("bulk-007/notes", p));
        Assert.All(Within(opened, repo.Root), p => Assert.StartsWith("bulk-007/notes/", p));

        repo.Write("openspec/changes/bulk-008/notes/n2001.md", "---\nid: 2001\nby: Ann\ncreated: 2026-11-01T09:00:00\n---\n# Also by hand\n");
        NotesCache.Refresh(repo.Root);   // nothing says where: every folder is stamped, and only the changed one is read
        opened.Clear();
        Assert.Contains(NotesCache.All(), n => n.Id == 2001);
        Assert.All(Within(opened, repo.Root), p => Assert.StartsWith("bulk-008/notes/", p));
    }
}
