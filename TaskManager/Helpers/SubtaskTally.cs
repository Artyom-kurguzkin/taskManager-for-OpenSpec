using System.Runtime.CompilerServices;
using Notes.PublicDTOs;
using TaskManager.AppState;
using WorkItems.PublicDTOs;

namespace TaskManager.Helpers;

/// <summary>
/// The one place that counts an item's subtasks: its subtasks file's, and its unresolved bugs (notes of kind bug without <c>resolvedAt</c>), which are open subtasks too
/// but live only in the notes. A resolved bug drops out of the count: nothing is left to do for it. Every "done of total", bar and percentage reads it.
/// </summary>
public static class SubtaskTally
{
    private static readonly ConditionalWeakTable<NotesRead, IReadOnlyDictionary<int, int>> ByNotes = new();
    private static readonly IReadOnlyDictionary<int, int> None = new Dictionary<int, int>();

    /// <summary>The unresolved bugs of each work item (by number) of the repository in scope; the same instance while the notes are the same.</summary>
    public static IReadOnlyDictionary<int, int> OpenBugs() =>
        ByNotes.GetValue(NotesCache.Get(), r => r.Notes.Any(n => n.IsOpenBug)
            ? r.Notes.Where(n => n.IsOpenBug).GroupBy(n => n.WorkItemId).ToDictionary(g => g.Key, g => g.Count())
            : None);

    /// <summary>Done and total: the subtasks file's, plus <paramref name="openBugs"/> open ones.</summary>
    public static (int Done, int Total) Of(WorkItem item, int openBugs) => (item.Subtasks.Count(s => s.Done), item.Subtasks.Count + openBugs);

    /// <summary>The share done, 0 to 100 (0 for nothing to do).</summary>
    public static int Percent(WorkItem item, int openBugs) => Of(item, openBugs) is (var done, > 0 and var total) ? done * 100 / total : 0;

    /// <summary>Whether two readings of <see cref="OpenBugs"/> say the same.</summary>
    public static bool Same(IReadOnlyDictionary<int, int> a, IReadOnlyDictionary<int, int> b) =>
        ReferenceEquals(a, b) || a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out int n) && n == p.Value);
}
