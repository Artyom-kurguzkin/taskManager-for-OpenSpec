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
}
