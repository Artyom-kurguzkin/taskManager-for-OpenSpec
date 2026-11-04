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
}
