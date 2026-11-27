using System.Text.RegularExpressions;

namespace BoardSync.Sync;

/// <summary>
/// One change folder with at least one live file in a view. <paramref name="Path"/> is relative to openspec/changes, forward slashes:
/// "add-health-check" or "archive/2026-11-20-add-health-check". The rest is read from its files by the caller.
/// </summary>
/// <param name="Id">x-rtm.id from .openspec.yaml, or null when the folder has none (it cannot be followed by id).</param>
/// <param name="MergedFrom">x-rtm.mergedFrom: the ids of the items merged into this one.</param>
/// <param name="MergedFolders">x-rtm.mergedFolders: the folder names of the items merged away; null when the field does not exist (an older merge).</param>
/// <param name="MergedFromLines">The ids named by `merged-from #wi&lt;id&gt;` lines of its events.log.</param>
internal sealed record ChangeFolder(string Path, int? Id, IReadOnlyList<int> MergedFrom, IReadOnlyList<string>? MergedFolders, IReadOnlyList<int> MergedFromLines);

/// <summary>The change folders that have a live file in one view: this computer (L), the storage's manifest (R) or the saved base (B).</summary>
internal sealed record FolderView(IReadOnlyList<ChangeFolder> Folders);

internal enum FolderSide { Local, Remote }

internal enum FolderFate
{
    /// <summary>The folder moved to a folder under archive/ with the same id.</summary>
    Archived,
    /// <summary>The folder's content lives in an item whose mergedFrom (or merged-from line) names it.</summary>
    Merged,
    /// <summary>The folder has the same id under another name outside the archive.</summary>
    Renamed,
    /// <summary>Nothing explains it: it is put back.</summary>
    Missing,
}

/// <summary>What happened to base folder <paramref name="Folder"/> on the side that lacks it. <paramref name="Destination"/> is the folder it went to (null for Missing).</summary>
internal sealed record FolderResolution(string Folder, int? Id, FolderSide Lacks, FolderFate Fate, string? Destination);

internal enum FolderClashKind
{
    /// <summary>Both computers archived the item on different days: keep the earlier folder, merge the other into it.</summary>
    EarlierArchiveKept,
    /// <summary>Archived and merged, or merged into two different items: both destinations stay and each is noted.</summary>
    NotedOnEach,
}

/// <summary>Both computers moved the same folder to different places. <paramref name="Local"/> and <paramref name="Remote"/> are where each put it.</summary>
/// <param name="Keep">EarlierArchiveKept only: the earlier-dated archive folder.</param>
/// <param name="MergeAway">EarlierArchiveKept only: the other archive folder, to be merged into <paramref name="Keep"/> and then trashed.</param>
internal sealed record FolderClash(string Folder, int? Id, FolderClashKind Kind, string Local, string Remote, string? Keep, string? MergeAway);

internal sealed record FolderPassResult(IReadOnlyList<FolderResolution> Resolutions, IReadOnlyList<FolderClash> Clashes);

/// <summary>
/// The folder pass: before files are compared, a change folder that the base has and one side lacks is looked up by id on that side.
/// OpenSpec never deletes a change; it archives it, and this app also merges and renames. Pure: it reads nothing and writes nothing.
/// </summary>
internal static partial class FolderPass
{
    private const string ArchivePrefix = "archive/";

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}-")]
    private static partial Regex DatePrefix();

    internal static FolderPassResult Resolve(FolderView local, FolderView remote, FolderView baseView)
    {
        List<FolderResolution> resolutions = [];
        foreach (ChangeFolder f in baseView.Folders.OrderBy(f => f.Path, StringComparer.Ordinal))
        {
            if (!Has(local, f.Path)) resolutions.Add(ResolveOn(f, FolderSide.Local, local));
            if (!Has(remote, f.Path)) resolutions.Add(ResolveOn(f, FolderSide.Remote, remote));
        }
        return new FolderPassResult(resolutions, Clashes(resolutions));
    }

    private static bool Has(FolderView view, string path) => view.Folders.Any(c => string.Equals(c.Path, path, StringComparison.Ordinal));

    private static bool IsArchived(string path) => path.StartsWith(ArchivePrefix, StringComparison.Ordinal);

    /// <summary>The folder's own name: no archive/ part, no yyyy-MM-dd- prefix (the same name mergedFolders records).</summary>
    private static string NameOf(string path)
    {
        string name = IsArchived(path) ? path[ArchivePrefix.Length..] : path;
        return IsArchived(path) ? DatePrefix().Replace(name, "") : name;
    }

    private static FolderResolution ResolveOn(ChangeFolder f, FolderSide side, FolderView view)
    {
        FolderResolution Result(FolderFate fate, string? destination) => new(f.Path, f.Id, side, fate, destination);
        if (f.Id is not { } id) return Result(FolderFate.Missing, null);

        // The view has no folder at f.Path (the caller checked), so every candidate below is another folder.
        IEnumerable<ChangeFolder> sorted = view.Folders.OrderBy(c => c.Path, StringComparer.Ordinal);

        // 1. The archive: a folder under archive/ with the same id.
        if (sorted.FirstOrDefault(c => IsArchived(c.Path) && c.Id == id) is { } archived) return Result(FolderFate.Archived, archived.Path);

        // 2. A merge: mergedFrom names the id, and mergedFolders the name when it exists; an older merge says so in its merged-from lines.
        string name = NameOf(f.Path);
        bool Merges(ChangeFolder c) =>
            (c.MergedFrom.Contains(id) && (c.MergedFolders is not { Count: > 0 } folders || folders.Any(m => string.Equals(NameOf(m), name, StringComparison.Ordinal))))
            || c.MergedFromLines.Contains(id);
        if (sorted.FirstOrDefault(Merges) is { } merged) return Result(FolderFate.Merged, merged.Path);

        // 3. A rename: the same id outside the archive under another name.
        if (sorted.FirstOrDefault(c => !IsArchived(c.Path) && c.Id == id) is { } renamed) return Result(FolderFate.Renamed, renamed.Path);

        // 4. Nothing says it moved.
        return Result(FolderFate.Missing, null);
    }

    /// <summary>Folders that both sides lack have a resolution on each side; where the two went to different places, say what to do about it.</summary>
    private static List<FolderClash> Clashes(List<FolderResolution> resolutions)
    {
        List<FolderClash> clashes = [];
        foreach (IGrouping<string, FolderResolution> group in resolutions.GroupBy(r => r.Folder, StringComparer.Ordinal))
        {
            FolderResolution? l = group.FirstOrDefault(r => r.Lacks == FolderSide.Local), r2 = group.FirstOrDefault(r => r.Lacks == FolderSide.Remote);
            if (l is null || r2 is null) continue;
            // Each side's destination is the folder on that side. Missing and Renamed are not clashes (a rename is the same item under another name).
            if (l.Fate is not (FolderFate.Archived or FolderFate.Merged) || r2.Fate is not (FolderFate.Archived or FolderFate.Merged)) continue;
            if (string.Equals(l.Destination, r2.Destination, StringComparison.Ordinal)) continue;

            if (l.Fate == FolderFate.Archived && r2.Fate == FolderFate.Archived)
            {
                // The earlier date wins; the same date (different names) falls back to the path, so the choice is the same on both computers.
                bool localFirst = string.CompareOrdinal(DateOf(l.Destination!) + l.Destination, DateOf(r2.Destination!) + r2.Destination) <= 0;
                (string keep, string away) = localFirst ? (l.Destination!, r2.Destination!) : (r2.Destination!, l.Destination!);
                clashes.Add(new FolderClash(l.Folder, l.Id, FolderClashKind.EarlierArchiveKept, l.Destination!, r2.Destination!, keep, away));
            }
            else
                clashes.Add(new FolderClash(l.Folder, l.Id, FolderClashKind.NotedOnEach, l.Destination!, r2.Destination!, null, null));
        }
        return clashes;
    }

    /// <summary>The yyyy-MM-dd an archive folder starts with (empty when it has none, which sorts first).</summary>
    private static string DateOf(string archivePath)
    {
        string name = archivePath.StartsWith(ArchivePrefix, StringComparison.Ordinal) ? archivePath[ArchivePrefix.Length..] : archivePath;
        return DatePrefix().IsMatch(name) ? name[..10] : "";
    }
}
