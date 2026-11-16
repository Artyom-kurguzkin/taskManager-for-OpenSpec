using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using Features;
using Features.PublicDTOs;
using GitHistory;
using GitHistory.PublicDTOs;
using LinkedTestRuns;
using LinkedTestRuns.PublicDTOs;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Notes;
using Notes.PublicDTOs;
using OpenSpecStore;
using WorkItems;
using WorkItems.PublicDTOs;

namespace TaskManager.Mcp;

/// <summary>
/// The board as eight tools over the module facades, split by what they do: <c>BoardTools.Read.cs</c> (list, get, features), <c>BoardTools.Write.cs</c> (create, update, note, log) and here validate with the plumbing.
/// Every call resolves its repository, takes the one global lock, works in that repository (a scope of this call, nothing process-wide changes) and, when it wrote, tells the host.
/// </summary>
[McpServerToolType]
public sealed partial class BoardTools(BoardHost? host = null)
{
    private readonly BoardHost host = host ?? new();

    // ponytail: one global lock; per-repo locks if agents ever queue behind each other
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private const string RepoHelp = "Any path inside the repository or a git worktree of it; the folder holding openspec/ is found by walking up. Default: the RTM_REPO environment variable, else the server's working directory.";
    private const string AgentHelp = "Your name, written as the author of this change (default: the RTM_AGENT environment variable, else Claude). Letters, digits, spaces, dots, dashes and underscores only.";
    private const string FormatHelp = "concise (default) or detailed.";
    private const string RevisionHelp = "The revision board_get returned; the write is refused when the item changed since.";


    [McpServerTool(Name = "board_validate", ReadOnly = true, UseStructuredContent = true)]
    [Description("Run the pinned OpenSpec validator (validate --strict) on one work item, or on every open item when `id` is left out (slow: about a second per item), plus the app's own warnings: duplicate ids, items that are Completed but not archived, linked test names that are not in the latest results. Run it before you stop.")]
    public Task<ValidateResult> BoardValidate(
        [Description("The work item number; leave out to validate every open item.")] int? id = null,
        [Description(FormatHelp + " Concise gives the validator's output only for items it rejects.")] string? format = null,
        [Description(RepoHelp)] string? repo = null) =>
        Run(repo, _ =>
        {
            bool detailed = IsDetailed(format);
            IReadOnlyList<WorkItem> items = id is int one ? [Item(one)] : [.. WorkItemsApi.GetAll().Where(i => !i.Archived).OrderBy(i => i.Id)];
            List<ValidationRow> rows = [];
            List<string> warnings = [.. WorkItemsApi.GetWarnings()];
            foreach (WorkItem item in items)
            {
                (bool valid, string output) = WorkItemsApi.Validate(item.Id);
                rows.Add(new ValidationRow(item.Id, item.Folder, valid, detailed || !valid ? output : null));
                warnings.AddRange(LinkedTestRunsApi.MissingLinks((item.Tests ?? []).Select(t => (t.Suite, t.Test))).Select(w => $"#wi{item.Id}: {w}"));
            }
            return new ValidateResult(rows.All(r => r.Valid), rows, warnings);
        });

    // ── Plumbing ──

    private async Task<T> Run<T>(string? repo, Func<string, T> body)
    {
        string root = ResolveRoot(repo, host.DefaultToWorkingDirectory);
        bool wrote = false;
        await Gate.WaitAsync();
        try
        {
            // The call's own repository, repository history and actor: nothing process-wide changes, so the app that hosts this server keeps its active project and signs its edits as the user.
            // The repository is a guest unless the app has it open: it is never added to the open set (the tabs).
            using IDisposable repository = OpenSpecStoreApi.InRepo(root), history = GitHistoryApi.InRepo(root), actor = WorkItemsApi.ActingAs("Board tools");
            // What this call read is its own: the window keeps what it read, so an edit made there on an older read still finds that the file changed (see OpenSpecStoreApi.AsCaller).
            using IDisposable seen = OpenSpecStoreApi.AsCaller();
            OpenSpecStoreApi.Refresh();   // ponytail: every call re-reads the repository (the watcher lags 300 ms and can drop events in a server that runs for hours); cache per call if it ever shows
            try
            {
                NotesApi.RepairDuplicates();   // a merge or a sync may have left two items or notes with one id: settled before the call reads, the way the desktop app settles them when it sees the files change
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or TimeoutException) { }   // files moving under the repair: the call reads what is there, the next one repairs
            long version = OpenSpecStoreApi.Version;   // after the refresh above: only this call's own writes move it from here
            try { return body(root); }
            finally { wrote = OpenSpecStoreApi.Version != version; }
        }
        finally
        {
            Gate.Release();
            // In the app's process the store's watcher ignores the app's own writes, and an agent's write is one: the window is told here, after the call's scopes ended, before the answer goes out.
            if (wrote) host.AfterWrite?.Invoke();
        }
    }

    /// <summary>The repository root for a path inside it: the nearest folder, walking up, that holds openspec/.</summary>
    internal static string ResolveRoot(string? repo, bool defaultToWorkingDirectory = true)
    {
        string? env = Environment.GetEnvironmentVariable("RTM_REPO");
        (string start, string source) = !string.IsNullOrWhiteSpace(repo) ? (repo.Trim(), "the `repo` argument")
            : BoardMcp.Caller.Value.Repo is { Length: > 0 } caller ? (caller, "the client's working directory")
            : !defaultToWorkingDirectory ? throw new McpException("Pass `repo` (any path inside the repository): this server serves every repository, so it has no default one.")
            : !string.IsNullOrWhiteSpace(env) ? (env.Trim(), "the RTM_REPO environment variable")
            : (Directory.GetCurrentDirectory(), "the server's working directory");
        return OpenSpecStoreApi.FindRepoRoot(start)
            ?? throw new McpException($"No repository found: neither {Path.GetFullPath(start)} (from {source}) nor any folder above it has an openspec/ folder. Pass `repo` as a path inside a repository that uses OpenSpec.");
    }

    /// <summary>The events the commands of this call write carry the agent's name. The actor belongs to this call alone (an AsyncLocal scope): <see cref="Run{T}"/>'s own scope, which this one is inside, puts the previous one back when the call ends.</summary>
    private static void ActAs(string who) => WorkItemsApi.ActingAs(who);

    /// <summary>Writes the draft as a note of the item by <paramref name="who"/> through the Notes module (which composes, checks and signs it).</summary>
    private static NoteResult WriteNote(int id, string who, NoteDraft draft) =>
        Written(NotesApi.Write(id, who, draft, hashes => [.. GitHistoryApi.Commits([.. hashes]).Select(c => c.Hash)]));

    private static NoteResult Written(WrittenNote w) =>
        w.Success ? new NoteResult(w.NoteId, w.WorkItemId, w.Path, w.Warnings, w.SubtasksCreated, w.Revision, w.Kind) : throw new McpException(w.Message);

    /// <summary>Test links as given, each with its suite corrected when only one class in the latest results has that display name; warnings for the rest.</summary>
    private static List<TestInput> CheckTests(IEnumerable<TestInput> tests, List<string> warnings)
    {
        TestInput[] given = [.. tests];
        IReadOnlyList<CheckedTest> checks = LinkedTestRunsApi.CheckLinks(given.Select(t => (t.Suite, t.Test)));
        warnings.AddRange(checks.Select(c => c.Warning).OfType<string>());
        return [.. given.Select((t, i) => t with { Suite = checks[i].Suite })];
    }

    /// <summary>The item's notes and events as one feed, newest first (the Notes module's, the one the item page draws); concise keeps the newest 20.</summary>
    private static (IReadOnlyList<WorklogRow> Rows, int Total) Worklog(int id, bool detailed, bool include = true, DateTime? since = null, int? limit = null)
    {
        IReadOnlyList<WorklogEntry> feed = NotesApi.GetWorklog(id);
        if (!include) return ([], feed.Count);
        List<WorklogRow> rows = [.. feed.Where(f => since is null || f.At >= since).Select(f => f.Note is { } n
            ? new WorklogRow(Stamp(n.Created), n.By, "note", n.Title, n.Id, detailed ? n.Body : null)
            : new WorklogRow(Stamp(f.At), f.By, f.Kind, f.Text))];
        int? cap = limit ?? (detailed ? null : 20);
        return (cap is int n2 ? rows.Take(n2).ToList() : rows, feed.Count);
    }

    private const int ConciseDescription = 600;
    private static readonly string[] SectionNames = ["header", "description", "subtasks", "notes", "worklog", "specs", "tests", "links"];

    /// <summary>The sections board_get was asked for (null = all). Header is always there; an unknown name is refused with the valid ones.</summary>
    private static HashSet<string>? Sections(string[]? sections)
    {
        if (sections is not { Length: > 0 }) return null;
        HashSet<string> wanted = new(StringComparer.OrdinalIgnoreCase) { "header" };
        foreach (string s in sections)
        {
            string name = (s ?? "").Trim();
            if (!SectionNames.Contains(name, StringComparer.OrdinalIgnoreCase)) throw new McpException($"Unknown section \"{s}\": use {string.Join(", ", SectionNames)}.");
            wanted.Add(name);
        }
        return wanted;
    }

    private static DateTime? Since(string? since)
    {
        if (string.IsNullOrWhiteSpace(since)) return null;
        return DateTime.TryParse(since.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime at)
            ? at
            : throw new McpException($"`since` must be an ISO date or date-time like 2026-11-09 or 2026-11-09T14:30, not \"{since}\".");
    }

    private static string Stamp(DateTime at) => at.ToString("s", CultureInfo.InvariantCulture);

    private static string AgentName(string? agent)
    {
        string name = (!string.IsNullOrWhiteSpace(agent) ? agent : BoardMcp.Caller.Value.Agent is { Length: > 0 } caller ? caller : Environment.GetEnvironmentVariable("RTM_AGENT") is { Length: > 0 } env ? env : "Claude").Trim();
        Refuse(WorkItemsApi.CheckAgentName(name));
        return name;
    }

    private static void Refuse(string? problem)
    {
        if (problem is not null) throw new McpException(problem);
    }

    private static bool IsDetailed(string? format) => format?.Trim().ToLowerInvariant() switch
    {
        null or "" or "concise" => false,
        "detailed" => true,
        _ => throw new McpException($"Unknown format \"{format}\": use concise or detailed."),
    };

    private static WorkItem Item(int id) =>
        WorkItemsApi.Get(id) ?? throw new McpException($"There is no work item #wi{id}. Use board_list to find the id.");

    private static DateOnly? Day(string? text, string name)
    {
        if (string.IsNullOrEmpty(text)) return null;
        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day) ? day : throw new McpException($"`{name}` must be a day like 2026-11-09 (or an empty string to clear it), not \"{text}\".");
    }

    private static string RevisionOf(WorkItem item) => item.Revision;

    private static ListRow Row(WorkItem i, bool detailed) => detailed
        ? new ListRow(i.Id, i.Title, i.Status, WorkItemsApi.FeatureOf(i), i.Label, RevisionOf(i), i.Priority, i.Contributors, i.DependsOn, i.Plan, i.Folder, i.AgentBy, i.Archived)
        : new ListRow(i.Id, i.Title, i.Status, WorkItemsApi.FeatureOf(i), i.Label, RevisionOf(i));

    private static CommitRow Commit(CommitInfo c, bool mentions) => new(c.Hash, c.Subject, c.Author, c.Date.ToString("s", CultureInfo.InvariantCulture), mentions);

    private static DependencyRow Dependency(int id) =>
        WorkItemsApi.Get(id) is { } d ? new DependencyRow(d.Id, d.Title, d.Status, WorkItemsApi.IsResolved(d)) : new DependencyRow(id, "(no such item)", "", true);

    private static TestRow TestOutcome(TestLink t)
    {
        TestOutcome o = LinkedTestRunsApi.OutcomeOf(t.Suite, t.Test);
        return new TestRow(t.Suite, t.Test, o.Outcome, o.Mark, t.Note);
    }
}
