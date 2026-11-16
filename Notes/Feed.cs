using Notes.PublicDTOs;
using OpenSpecStore;
using WorkItems;
using WorkItems.PublicDTOs;

namespace Notes;

/// <summary>What a work item's history is made of (its events and its notes) and what repairs and records touch both: the feed, the changed period, duplicate numbers, sync conflicts.</summary>
internal static class Feed
{
    /// <summary>The item's events and notes as one feed, newest first; at one moment the later entry comes first, and a note after the events that were written with it.</summary>
    internal static IReadOnlyList<WorklogEntry> Worklog(int id)
    {
        IReadOnlyList<WorkItemEvent> events = WorkItemsApi.GetEvents(id);
        List<WorklogEntry> feed = [.. events.Select((e, i) => new WorklogEntry(e.At, e.By, e.Kind, e.Text, null, i, 0))];
        feed.AddRange(Queries.GetAll().Where(n => n.WorkItemId == id).OrderBy(n => n.Id).Select(n => new WorklogEntry(n.Created, n.By, "note", n.Title, n, -1, 0)));
        return [.. feed.Select((f, i) => f with { Order = i }).OrderByDescending(f => f.At).ThenByDescending(f => f.Order)];
    }

    /// <summary>The items that changed in the period: by their own files, their creation, or an event or note of their worklog.</summary>
    internal static IReadOnlySet<int> ChangedIn(IReadOnlyList<WorkItem> items, DateOnly from, DateOnly to, TimeOnly? fromTime, TimeOnly? toTime)
    {
        ILookup<int, DateTime> notes = Queries.GetAll().ToLookup(n => n.WorkItemId, n => n.Created);
        return WorkItemsApi.ChangedIn(items, i => WorkItemsApi.GetEvents(i.Id).Select(e => e.At).Concat(notes[i.Id]), from, to, fromTime, toTime,
            i => WorkItemsApi.GetEvents(i.Id).Any(e => e.Kind == "created"));
    }

    /// <summary>
    /// Settles numbers used twice, the way every caller does it: the later change is renumbered and gets a note saying so; then notes numbered twice (after the work items, whose numbers they belong to).
    /// <paramref name="items"/> false skips the work items (a caller that knows no change folder moved).
    /// </summary>
    internal static DuplicateRepairs RepairDuplicates(bool items, string by)
    {
        List<RenumberedItem> repaired = [];
        if (items)
            foreach ((int oldId, int newId, string folder) in WorkItemsApi.ResolveDuplicateIds())
            {
                (bool ok, string _, int noteId) = Commands.Create(newId, by, DateTime.Now);
                if (ok && Commands.SetTitle(noteId, $"Renumbered from #wi{oldId}").Success)
                    Commands.SaveBody(noteId, $"Two changes carried the number #wi{oldId}. This one ({folder}) was created later, so it is now #wi{newId}. Anything that refers to #wi{oldId} means the other change.\n");
                repaired.Add(new(oldId, newId, folder));
            }
        return new(repaired, [.. Queries.RepairDuplicateIds().Select(r => new RenumberedNote(r.OldId, r.NewId, r.Path))]);
    }

    /// <summary>A project with a files folder keeps its plans in &lt;folder&gt;/plans: a plan linked from anywhere else is linked, with a word about where plans go.</summary>
    internal static string? PlanFolderWarning(string plan)
    {
        if (OpenSpecStoreApi.RepoRoot is not string root || AttachmentsFolder.ProjectFilesDir(root) is not string files) return null;
        string folder = files + "/plans/";
        return plan.Trim().Replace('\\', '/').StartsWith(folder, StringComparison.OrdinalIgnoreCase)
            ? null
            : $"This project keeps its plans in {folder} (see openspec/REPO-TASKS.md): the plan was linked where you wrote it; write new plans there.";
    }

    // ── sync conflicts: the side that lost is never gone ──

    private static string Whose(bool droppedHere) => droppedHere ? "this computer's change" : "another computer's change";

    private static string Clip(string text)
    {
        string one = string.Join(" / ", text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return one.Length <= 120 ? one : one[..117] + "...";
    }

    private static int LongestRun(string text, char c)
    {
        int best = 0, run = 0;
        foreach (char x in text) { run = x == c ? run + 1 : 0; best = Math.Max(best, run); }
        return best;
    }

    /// <summary>
    /// Puts a conflict the sync settled on its item: an event for a changed field (.openspec.yaml, tasks.md), a note for lost text (any other file), carrying the dropped text in full.
    /// <paramref name="file"/> is the path inside the change folder, <paramref name="path"/> the board-relative path the conflict was found at, <paramref name="what"/> what changed.
    /// </summary>
    internal static bool RecordSyncConflict(int itemId, string file, string path, string what, string kept, string dropped, bool droppedHere)
    {
        bool field = file is ".openspec.yaml" or "tasks.md" && kept != "deleted";
        if (field)
            return WorkItemsApi.LogEvent(itemId, "sync-conflict", $"{(what == path ? file : what)}: kept \"{Clip(kept)}\", dropped \"{Clip(dropped)}\" ({Whose(droppedHere)})").Success;

        (bool ok, _, int noteId) = Commands.Create(itemId, "Sync", DateTime.Now);
        if (!ok) return false;
        string where = what == path ? "" : ", " + what;
        Commands.SetTitle(noteId, $"Sync conflict: {file}{where}");
        string fence = new('`', Math.Max(3, LongestRun(dropped, '`') + 1));
        string keptText = kept == "deleted" ? "the deletion" : $"the text now in `{file}`";
        bool saved = Commands.SaveBody(noteId, $"Two computers changed the same place in `{file}` ({what}). The sync kept {keptText} and dropped {Whose(droppedHere)}; the text it dropped is here in full.\n\n{fence}\n{dropped.Replace("\r\n", "\n").TrimEnd('\n')}\n{fence}\n").Success;
        Commands.StampAgent(noteId, "Sync");   // marked like an agent's note: it stands out until the user has looked at it
        return saved;
    }

    /// <summary>A conflict a git merge left as markers and the board's own merge settled: one event on the item saying what was kept and dropped, and how the winner was chosen.</summary>
    internal static void RecordRepairedConflict(int itemId, string file, string path, string what, string kept, string dropped, bool fromGit)
    {
        string how = fromGit ? "the commit that is newer won" : "git's sides were not available, so the first side won";
        WorkItemsApi.LogEvent(itemId, "sync-conflict", $"{(what == path ? file : what)}: kept \"{Clip(kept)}\", dropped \"{Clip(dropped)}\" (conflict markers from a git merge were repaired; {how})");
    }

    /// <summary>An attachment changed on both computers: one event on the item that owns it (the sync's path is wi&lt;id&gt;/name); false when the path names no work item that exists.</summary>
    internal static bool RecordAttachmentConflict(string attachmentPath)
    {
        string[] parts = attachmentPath.Split('/', 2);
        if (parts.Length != 2 || !parts[0].StartsWith("wi", StringComparison.Ordinal) || !int.TryParse(parts[0].AsSpan(2), out int id)) return false;
        if (WorkItemsApi.GetAll(includeHidden: true).All(w => w.Id != id)) return false;
        return WorkItemsApi.LogEvent(id, "sync-conflict", $"attachment {parts[1]}: the newer copy was kept, the other is in the trash").Success;
    }
}
