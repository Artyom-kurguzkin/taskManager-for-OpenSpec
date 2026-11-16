using System.Text.RegularExpressions;
using Notes.BusinessRules;
using Notes.PublicDTOs;
using OpenSpecStore;
using WorkItems;

namespace Notes;

/// <summary>
/// The attachments folder as a whole: which folders are allowed, how many files one holds, and moving it (files first, then every reference to them).
/// Nothing here follows a link (junction, symlink), and nothing overwrites a file.
/// </summary>
internal static partial class AttachmentsFolder
{
    private static StringComparison Cmp => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static StringComparer Comparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    // ── which folders are allowed ──

    /// <summary>
    /// A repository-relative folder, tidied (forward slashes, no trailing slash, no "." parts): empty means the default. Refused: a folder outside the repository
    /// (rooted, a drive, a ".." part), the repository itself, anything inside openspec/, and names the file system cannot have.
    /// </summary>
    internal static (bool Ok, string Dir, string Message) Check(string? dir)
    {
        string raw = (dir ?? "").Trim();
        if (raw.Length == 0) return (true, Commands.DefaultAttachmentsDir, "");
        if (Path.IsPathRooted(raw) || raw.Contains(':')) return (false, "", $"\"{raw}\" is outside the repository: write the folder relative to it");
        List<string> parts = [.. raw.Replace('\\', '/').Split('/').Where(p => p.Length > 0 && p != ".")];
        if (parts.Contains("..")) return (false, "", $"\"{raw}\" is outside the repository: a folder name cannot contain \"..\"");
        if (parts.Count == 0) return (false, "", "That is the repository itself: pick a folder inside it");
        if (parts[0].Equals("openspec", StringComparison.OrdinalIgnoreCase)) return (false, "", "A folder inside openspec/ is the board's own: pick another one");
        if (parts.Any(p => p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || p.EndsWith('.') || p.EndsWith(' '))) return (false, "", $"\"{raw}\" has a name a folder cannot have");
        string clean = string.Join('/', parts);
        return (true, clean.Equals(Commands.DefaultAttachmentsDir, Cmp) ? Commands.DefaultAttachmentsDir : clean, "");
    }

    /// <summary>The default files folder of a project: <c>images</c> and <c>plans</c> go inside it.</summary>
    internal const string DefaultFilesDir = OpenSpecStoreApi.DefaultFilesDir;

    /// <summary>A project's files folder: the same rules as <see cref="Check"/>, but empty means <see cref="DefaultFilesDir"/>.</summary>
    internal static (bool Ok, string Dir, string Message) CheckFiles(string? dir) =>
        string.IsNullOrWhiteSpace(dir) ? (true, DefaultFilesDir, "") : Check(dir);

    /// <summary>The project's files folder: <c>x-rtm.filesDir</c> in its openspec/config.yaml, tidied; null when the key is missing, empty, not allowed or the file cannot be read (the old layout then).</summary>
    internal static string? ProjectFilesDir(string repoRoot)
    {
        string value = OpenSpecStoreApi.ReadConfigSection<string>("filesDir", repoRoot).Value ?? "";   // unreadable or locked reads as none: the old layout
        return value.Trim().Length > 0 && Check(value) is { Ok: true } c ? c.Dir : null;
    }

    private static string Abs(string root, string dir) => Path.Combine(root, dir.Replace('/', Path.DirectorySeparatorChar));

    private static string Root(string repoRoot) => Path.GetFullPath(repoRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsLink(string path) =>
        (Directory.Exists(path) ? new DirectoryInfo(path) : (FileSystemInfo)new FileInfo(path)) is { Exists: true } info && info.Attributes.HasFlag(FileAttributes.ReparsePoint);

    /// <summary>The first folder on the way from the repository root to <paramref name="dir"/> (itself included) that is a link, or null.</summary>
    private static string? LinkOnTheWay(string root, string dir)
    {
        string current = root, rel = "";
        foreach (string part in dir.Split('/'))
        {
            current = Path.Combine(current, part);
            rel = rel.Length == 0 ? part : rel + "/" + part;
            if (!Directory.Exists(current) && !File.Exists(current)) return null;   // what does not exist yet cannot be a link
            if (IsLink(current)) return rel;
        }
        return null;
    }

    // ── looking at a folder ──

    /// <summary>Every file below the folder as (full path, path relative to it), never going into a link: those are listed in <paramref name="left"/> instead.</summary>
    private static void Walk(string dir, string rel, List<(string Full, string Rel)> files, List<string> left)
    {
        foreach (FileSystemInfo entry in new DirectoryInfo(dir).EnumerateFileSystemInfos())
        {
            string r = rel.Length == 0 ? entry.Name : rel + "/" + entry.Name;
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) left.Add(r);
            else if (entry is DirectoryInfo d) Walk(d.FullName, r, files, left);
            else files.Add((entry.FullName, r));
        }
    }

    internal static int Count(string repoRoot, string dir)
    {
        (bool ok, string clean, string _) = Check(dir);
        string root = Root(repoRoot), path = Abs(root, clean);
        if (!ok || !Directory.Exists(path) || LinkOnTheWay(root, clean) is not null) return 0;
        List<(string, string)> files = [];
        try { Walk(path, "", files, []); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return files.Count;
    }

    /// <summary>The plans (*.md) and all the files (drawings beside them included) below a plans folder; (0, 0) when it is missing, not allowed or a link.</summary>
    internal static (int Plans, int Files) CountPlans(string repoRoot, string dir)
    {
        (bool ok, string clean, string _) = Check(dir);
        string root = Root(repoRoot), path = Abs(root, clean);
        if (!ok || !Directory.Exists(path) || LinkOnTheWay(root, clean) is not null) return (0, 0);
        List<(string Full, string Rel)> files = [];
        try { Walk(path, "", files, []); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return (files.Count(f => f.Rel.EndsWith(".md", StringComparison.OrdinalIgnoreCase)), files.Count);
    }

    // ── moving ──

    private static AttachmentsMoveResult Refused(string why) => new(false, why);

    private static bool Inside(string inner, string outer) => inner.StartsWith(outer + "/", Cmp);

    /// <param name="plans">A folder of plans, not of images: the plans stay tracked by git (nothing is ignored), and the work items' plan links follow the files.</param>
    internal static AttachmentsMoveResult Move(string repoRoot, string fromDir, string toDir, bool plans = false)
    {
        (bool okFrom, string from, string whyFrom) = Check(fromDir);
        (bool okTo, string to, string whyTo) = Check(toDir);
        if (!okFrom) return Refused($"The folder to move from is not allowed: {whyFrom}");
        if (!okTo) return Refused($"The folder to move to is not allowed: {whyTo}");
        string root = Root(repoRoot);
        if (!Directory.Exists(root)) return Refused("The repository folder does not exist");
        if (from.Equals(to, Cmp)) return Refused($"{to} is already the {(plans ? "plans" : "attachments")} folder: nothing to move");
        if (Inside(to, from) || Inside(from, to)) return Refused($"{from} and {to} lie one inside the other: pick a folder beside it");
        foreach (string dir in new[] { from, to })
            if (LinkOnTheWay(root, dir) is string link) return Refused($"{link} is a link (a junction or symlink): it is not followed, nothing was moved");
        string src = Abs(root, from), dst = Abs(root, to);
        if (!Directory.Exists(src)) return new(true, $"Nothing to move: there is no {from} folder");

        List<(string Full, string Rel)> files = [];
        List<string> left = [];
        try { Walk(src, "", files, left); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Refused($"{from} could not be read ({e.Message}): nothing was moved"); }
        left = [.. left.Select(l => from + "/" + l)];   // repository-relative, the way references name them
        Dictionary<string, string> mapped = new(Comparer);   // old repository-relative path -> new one, for every file that arrived
        int moved = 0, renamed = 0, duplicates = 0;
        foreach ((string full, string rel) in files.OrderBy(f => f.Rel, StringComparer.Ordinal))
        {
            string oldRel = from + "/" + rel;
            try
            {
                string target = Path.Combine(dst, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                (string final, bool dropped) = Place(full, target);
                mapped[oldRel] = to + "/" + Path.GetRelativePath(dst, final).Replace(Path.DirectorySeparatorChar, '/');
                if (dropped) duplicates++;
                else { moved++; if (!final.Equals(target, Cmp)) renamed++; }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { left.Add(oldRel); }
        }

        (int rewritten, List<string> unreadable) = Rewrite(root, from, to, mapped, left, plans);
        Prune(src);
        if (moved + duplicates > 0 && !plans) Commands.IgnoreFolder(root, to);
        return new(true, Describe(from, to, moved, renamed, duplicates, left, rewritten, unreadable), moved, renamed, duplicates, left.Count, rewritten);
    }

    private static string Describe(string from, string to, int moved, int renamed, int duplicates, List<string> left, int rewritten, List<string> unreadable)
    {
        List<string> parts = [];
        if (moved + duplicates == 0 && left.Count == 0 && rewritten == 0 && unreadable.Count == 0) return $"Nothing to move: {from} holds no files";
        parts.Add($"Moved {moved} file{(moved == 1 ? "" : "s")} from {from} to {to}");
        if (renamed > 0) parts.Add($"{renamed} kept beside a different file of the same name (named -2, -3…)");
        if (duplicates > 0) parts.Add($"{duplicates} dropped because the same file was already there");
        if (rewritten > 0) parts.Add($"{rewritten} reference{(rewritten == 1 ? "" : "s")} updated");
        if (left.Count > 0) parts.Add($"{left.Count} left where {(left.Count == 1 ? "it was" : "they were")} ({string.Join(", ", left.Take(3).Select(l => l[(from.Length + 1)..]))}{(left.Count > 3 ? ", …" : "")}): links are not followed, and a file that could not be moved stays");
        if (unreadable.Count > 0) parts.Add($"{string.Join(", ", unreadable)} could not be read and {(unreadable.Count == 1 ? "was" : "were")} not changed");
        return string.Join("; ", parts) + ".";
    }

    /// <summary>
    /// Moves the file to <paramref name="target"/>, or beside it as name-2, name-3… when another file is there; a file with the same bytes is not
    /// copied again (the source is dropped), so running the move twice never makes a copy. Returns where the file is and whether it was dropped.
    /// </summary>
    private static (string Final, bool Dropped) Place(string source, string target)
    {
        string dir = Path.GetDirectoryName(target)!, stem = Path.GetFileNameWithoutExtension(target), ext = Path.GetExtension(target);
        string candidate = target;
        for (int n = 2; ; n++)
        {
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) { File.Move(source, candidate); return (candidate, false); }
            if (File.Exists(candidate) && !IsLink(candidate) && SameBytes(source, candidate)) { File.Delete(source); return (candidate, true); }
            candidate = Path.Combine(dir, $"{stem}-{n}{ext}");
        }
    }

    private static bool SameBytes(string a, string b)
    {
        if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
        using FileStream x = File.OpenRead(a), y = File.OpenRead(b);
        byte[] bx = new byte[81920], by = new byte[81920];
        while (true)
        {
            int nx = x.ReadAtLeast(bx, bx.Length, throwOnEndOfStream: false), ny = y.ReadAtLeast(by, by.Length, throwOnEndOfStream: false);
            if (nx != ny || !bx.AsSpan(0, nx).SequenceEqual(by.AsSpan(0, ny))) return false;
            if (nx == 0) return true;
        }
    }

    /// <summary>Deletes the folders below <paramref name="dir"/> that hold nothing, then <paramref name="dir"/> itself when it is empty too. A link is never entered or deleted.</summary>
    private static void Prune(string dir)
    {
        try
        {
            foreach (string sub in Directory.GetDirectories(dir)) if (!IsLink(sub)) Prune(sub);
            if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // ── the references ──

    /// <summary>Every change folder, the archived ones included.</summary>
    private static IEnumerable<string> ChangeFolders(string root)
    {
        string changes = Path.Combine(root, "openspec", "changes");
        if (!Directory.Exists(changes)) yield break;
        foreach (string d in Directory.GetDirectories(changes))
        {
            if (!Path.GetFileName(d).Equals("archive", StringComparison.OrdinalIgnoreCase)) { yield return d; continue; }
            foreach (string a in Directory.GetDirectories(d)) yield return a;
        }
    }

    /// <summary>
    /// Points the notes' attachments entries and the link targets in note bodies and in every description (proposal.md) that name the old folder to the new one.
    /// Silent: no event is recorded. Returns the number of references changed and the notes that could not be read (and so were not touched).
    /// </summary>
    private static (int Count, List<string> Unreadable) Rewrite(string root, string from, string to, Dictionary<string, string> mapped, List<string> kept, bool plans)
    {
        int count = 0;
        List<string> unreadable = [];
        string? NewPath(string decoded)
        {
            if (!decoded.StartsWith(from + "/", Cmp) || kept.Any(k => decoded.Equals(k, Cmp) || decoded.StartsWith(k + "/", Cmp))) return null;
            if (mapped.TryGetValue(decoded, out string? m)) return m;
            if (mapped.TryGetValue(decoded + ".md", out string? md)) return md[..^3];   // [[folder/plan]] names a plan without its extension
            return to + decoded[from.Length..];
        }

        if (plans) count += WorkItemsApi.RewritePlanLinks(NewPath);   // x-rtm.plan and x-rtm.docs of every item, archived ones too

        foreach (string change in ChangeFolders(root))
        {
            string proposal = Path.Combine(change, "proposal.md");
            if (File.Exists(proposal))
            {
                string text = OpenSpecStoreApi.ReadText(proposal);
                string changed = Links(text, NewPath, ref count);
                if (changed != text) OpenSpecStoreApi.WriteText(proposal, changed);
            }
            string notes = Path.Combine(change, "notes");
            if (!Directory.Exists(notes)) continue;
            foreach (string file in Directory.GetFiles(notes, "n*.md").OrderBy(f => f, StringComparer.Ordinal))
            {
                string text = OpenSpecStoreApi.ReadText(file);
                (string front, string body) = NoteFile.Split(text);
                NoteMeta meta = NoteFile.Meta(front);
                if (meta.Problem is not null)
                {
                    if (text.Contains(from + "/", Cmp)) unreadable.Add(Path.GetFileNameWithoutExtension(file));
                    continue;
                }
                int before = count;
                string newBody = Links(body, NewPath, ref count);
                bool entries = false;
                if (meta.Attachments is { Count: > 0 })
                    for (int i = 0; i < meta.Attachments.Count; i++)
                        if (NewPath(meta.Attachments[i]) is string np) { meta.Attachments[i] = np; entries = true; count++; }
                if (count == before) continue;
                if (entries && int.TryParse(Path.GetFileNameWithoutExtension(file)[1..], out int number)) meta.Id = number;
                OpenSpecStoreApi.WriteText(file, entries ? NoteFile.Compose(meta, newBody) : front + newBody);   // the frontmatter stays exactly as it was unless an entry changed
            }
        }
        return (count, unreadable);
    }

    [GeneratedRegex(@"(?<=\]\(\s*)(?<t><[^>\r\n]*>|[^\s)]+)")]
    private static partial Regex InlineTarget();

    [GeneratedRegex(@"(?<=\[\[)(?<t>[^\]\r\n]+)(?=\]\])")]
    private static partial Regex WikiTarget();

    [GeneratedRegex(@"^(?<pre>\s{0,3}\[[^\]\r\n]+\]:\s*)(?<t><[^>\r\n]*>|\S+)")]
    private static partial Regex DefinitionTarget();

    /// <summary>
    /// Markdown link and image targets (<c>](target)</c>, <c>](&lt;target&gt;)</c> and <c>[ref]: target</c>) outside code fences, rewritten by <paramref name="newPath"/>
    /// (given the target as a repository-relative path, URL-encoding undone; null = leave it). A target comes back in the form it had: raw, in angle brackets, or encoded.
    /// </summary>
    private static string Links(string text, Func<string, string?> newPath, ref int count)
    {
        int n = 0;
        string Count(string written) { n++; return written; }
        string Replace(string target)
        {
            bool angle = target.Length >= 2 && target[0] == '<' && target[^1] == '>';
            string inner = angle ? target[1..^1] : target;
            bool encoded = inner.Contains('%');
            string decoded = encoded ? Unescape(inner) : inner;
            if (newPath(decoded) is not string np) return target;
            n++;
            string written = encoded ? string.Join('/', np.Split('/').Select(Uri.EscapeDataString)) : angle ? np : np.Replace(" ", "%20");
            return angle ? $"<{written}>" : written;
        }

        string[] lines = text.Split('\n');
        bool fenced = false;
        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~")) { fenced = !fenced; continue; }
            if (fenced || !lines[i].Contains('(') && !lines[i].Contains("]:") && !lines[i].Contains("[[")) continue;
            lines[i] = InlineTarget().Replace(lines[i], m => Replace(m.Groups["t"].Value));
            lines[i] = DefinitionTarget().Replace(lines[i], m => m.Groups["pre"].Value + Replace(m.Groups["t"].Value));
            lines[i] = WikiTarget().Replace(lines[i], m => m.Groups["t"].Value.Trim() is { Length: > 0 } t && newPath(t) is string np ? Count(np) : m.Value);   // [[doc]] links
        }
        count += n;
        return n == 0 ? text : string.Join('\n', lines);
    }

    private static string Unescape(string s)
    {
        try { return Uri.UnescapeDataString(s); } catch (UriFormatException) { return s; }
    }
}
