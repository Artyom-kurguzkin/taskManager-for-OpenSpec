using BoardSync;
using BoardSync.PublicDTOs;

namespace TaskManager.Tests;

public sealed class MergingBoardFilesTests
{
    private const string Yaml = "openspec/changes/add-health-check/.openspec.yaml";
    private const string Tasks = "openspec/changes/add-health-check/tasks.md";
    private const string Log = "openspec/changes/add-health-check/events.log";
    private const string Proposal = "openspec/changes/add-health-check/proposal.md";
    private const string Note = "openspec/changes/add-health-check/notes/n12.md";

    private static string Lines(params string[] lines) => string.Join("\n", lines) + "\n";

    private const string BaseYaml =
        "schema: spec-driven\n" +
        "created: 2026-11-01\n" +
        "x-rtm:\n" +
        "  id: 12\n" +
        "  title: Add health check\n" +
        "  status: Planned\n" +
        "  priority: low\n" +
        "  contributors: [Ann, Bob]\n" +
        "  features: [monitoring]\n" +
        "  tests: [{suite: PingTests, test: \"Ping, answers\"}]\n" +
        "  lanes:\n" +
        "    board: test-2\n" +
        "  agentBy: Claude\n";

    [Fact(DisplayName = "Two computers' event logs merge into one: every line once, in time order, and nothing is ever a conflict")]
    public void EventLogs()
    {
        string baseLog = Lines("2026-11-10T10:00:00 | Ann | status | Backlog -> Planned");
        string local = baseLog + Lines("2026-11-10T12:00:00 | Ann | note | local late", "2026-11-10T10:30:00 | Ann | log | local early");
        string remote = baseLog + Lines("2026-11-10T11:00:00 | Bob | log | remote middle", "2026-11-10T10:30:00 | Ann | log | local early");

        MergeResult result = BoardSyncApi.Merge(Log, baseLog, local, remote, remoteIsNewer: true);

        Assert.Equal(Lines(
            "2026-11-10T10:00:00 | Ann | status | Backlog -> Planned",
            "2026-11-10T10:30:00 | Ann | log | local early",
            "2026-11-10T11:00:00 | Bob | log | remote middle",
            "2026-11-10T12:00:00 | Ann | note | local late"), result.Text);
        Assert.Empty(result.Conflicts);
    }

    [Fact(DisplayName = "An event line removed on one computer stays in the merged log, and equal times keep their order")]
    public void EventLogKeepsRemovedLines()
    {
        string baseLog = Lines("2026-11-10T10:00:00 | Ann | log | one", "2026-11-10T10:00:00 | Ann | log | two");
        string local = Lines("2026-11-10T10:00:00 | Ann | log | two");

        MergeResult result = BoardSyncApi.Merge(Log, baseLog, local, baseLog + Lines("2026-11-10T10:00:00 | Bob | log | three"), remoteIsNewer: false);

        Assert.Equal(Lines("2026-11-10T10:00:00 | Ann | log | two", "2026-11-10T10:00:00 | Ann | log | one", "2026-11-10T10:00:00 | Bob | log | three"), result.Text);
        Assert.Empty(result.Conflicts);
    }

    [Fact(DisplayName = "A field changed on one computer and another field changed on the other are both kept in the work item file, and untouched lines keep their formatting")]
    public void YamlDifferentFields()
    {
        string local = BaseYaml.Replace("status: Planned", "status: In Progress");
        string remote = BaseYaml.Replace("title: Add health check", "title: Add a health check endpoint").Replace("priority: low", "priority: high");

        MergeResult result = BoardSyncApi.Merge(Yaml, BaseYaml, local, remote, remoteIsNewer: true);

        string expected = BaseYaml.Replace("status: Planned", "status: In Progress")
            .Replace("title: Add health check", "title: Add a health check endpoint").Replace("priority: low", "priority: high");
        Assert.Equal(expected, result.Text);
        Assert.Empty(result.Conflicts);
    }

    [Fact(DisplayName = "The same field changed on both computers goes to the newer side and the loser is reported")]
    public void YamlSameField()
    {
        string local = BaseYaml.Replace("status: Planned", "status: Blocked");
        string remote = BaseYaml.Replace("status: Planned", "status: In Progress");

        MergeResult remoteWins = BoardSyncApi.Merge(Yaml, BaseYaml, local, remote, remoteIsNewer: true);
        MergeResult localWins = BoardSyncApi.Merge(Yaml, BaseYaml, local, remote, remoteIsNewer: false);

        Assert.Equal(remote, remoteWins.Text);
        MergeConflict conflict = Assert.Single(remoteWins.Conflicts);
        Assert.Equal(("status", "In Progress", "Blocked"), (conflict.What, conflict.Kept, conflict.Dropped));
        Assert.Equal(local, localWins.Text);
        Assert.Equal(("status", "Blocked", "In Progress"), (Assert.Single(localWins.Conflicts).What, Assert.Single(localWins.Conflicts).Kept, Assert.Single(localWins.Conflicts).Dropped));
    }

    [Fact(DisplayName = "Lists merge by what each side added and removed: a removal is not resurrected and both sides' additions are kept")]
    public void YamlLists()
    {
        string local = BaseYaml.Replace("contributors: [Ann, Bob]", "contributors: [Bob, Cy]")                    // removed Ann, added Cy
            .Replace("tests: [{suite: PingTests, test: \"Ping, answers\"}]", "tests: [{suite: PingTests, test: \"Ping, answers\"}, {suite: A, test: \"one, two\"}]");
        string remote = BaseYaml.Replace("contributors: [Ann, Bob]", "contributors: [Ann, Bob, Di]")             // added Di
            .Replace("features: [monitoring]", "features: []");                                                       // removed monitoring

        MergeResult result = BoardSyncApi.Merge(Yaml, BaseYaml, local, remote, remoteIsNewer: true);

        Assert.Contains("  contributors: [Bob, Cy, Di]\n", result.Text);
        Assert.Contains("  features: []\n", result.Text);
        Assert.Contains("  tests: [{suite: PingTests, test: \"Ping, answers\"}, {suite: A, test: \"one, two\"}]\n", result.Text);
        Assert.Empty(result.Conflicts);
    }

    [Fact(DisplayName = "A key added on one computer is added, a key removed on one and untouched on the other is removed, and keys outside x-rtm follow the same rule")]
    public void YamlAddedAndRemovedKeys()
    {
        string local = BaseYaml.Replace("  agentBy: Claude\n", "").Replace("created: 2026-11-01", "created: 2026-11-02");   // removed agentBy, changed created
        string remote = BaseYaml.Replace("  priority: low\n", "  priority: low\n  due: 2026-12-01\n");                                  // added due after priority

        MergeResult result = BoardSyncApi.Merge(Yaml, BaseYaml, local, remote, remoteIsNewer: true);

        Assert.Equal(
            "schema: spec-driven\ncreated: 2026-11-02\nx-rtm:\n  id: 12\n  title: Add health check\n  status: Planned\n  priority: low\n  due: 2026-12-01\n" +
            "  contributors: [Ann, Bob]\n  features: [monitoring]\n  tests: [{suite: PingTests, test: \"Ping, answers\"}]\n  lanes:\n    board: test-2\n", result.Text);
        Assert.Empty(result.Conflicts);
    }

    [Fact(DisplayName = "Subtasks merge one by one: additions from both sides, a removal, and ticks, renumbered within their group")]
    public void TasksMergePerSubtask()
    {
        string baseTasks = Lines("# Tasks", "", "## 1. Tasks", "", "- [ ] 1.1 write the test", "- [ ] 1.2 write the code", "- [ ] 1.3 drop me", "",
            "## 2. Hardening", "", "- [ ] 2.1 retry");
        string local = Lines("# Tasks", "", "## 1. Tasks", "", "- [x] 1.1 write the test", "- [ ] 1.2 write the code", "- [ ] 1.3 add a logo <!-- agent: Claude -->", "",
            "## 2. Hardening", "", "- [ ] 2.1 retry");
        string remote = Lines("# Tasks", "", "## 1. Tasks", "", "- [ ] 1.1 write the test", "- [x] 1.2 write the code", "- [ ] 1.3 write the docs", "",
            "## 2. Hardening", "", "- [ ] 2.1 retry", "- [ ] 2.2 back off");

        MergeResult result = BoardSyncApi.Merge(Tasks, baseTasks, local, remote, remoteIsNewer: true);

        Assert.Equal(Lines("# Tasks", "", "## 1. Tasks", "", "- [x] 1.1 write the test", "- [x] 1.2 write the code", "- [ ] 1.3 add a logo <!-- agent: Claude -->",
            "- [ ] 1.4 write the docs", "", "## 2. Hardening", "", "- [ ] 2.1 retry", "- [ ] 2.2 back off"), result.Text);
        Assert.Empty(result.Conflicts);
    }

    [Fact(DisplayName = "A subtask removed on one computer while another is ticked on the other applies both, whichever side is newer")]
    public void TasksRemovedAndTicked()
    {
        string baseTasks = Lines("## 1. Tasks", "", "- [ ] 1.1 write the test", "- [ ] 1.2 write the code");
        string local = Lines("## 1. Tasks", "", "- [ ] 1.1 write the test");
        string remote = Lines("## 1. Tasks", "", "- [x] 1.1 write the test", "- [ ] 1.2 write the code");

        MergeResult remoteWins = BoardSyncApi.Merge(Tasks, baseTasks, local, remote, remoteIsNewer: true);
        MergeResult localWins = BoardSyncApi.Merge(Tasks, baseTasks, local, remote, remoteIsNewer: false);

        Assert.Equal(Lines("## 1. Tasks", "", "- [x] 1.1 write the test"), remoteWins.Text);
        Assert.Empty(remoteWins.Conflicts);
        Assert.Equal(remoteWins.Text, localWins.Text);
        Assert.Empty(localWins.Conflicts);
    }

    [Fact(DisplayName = "A subtask edited on one computer and removed on the other goes to the newer side and is reported")]
    public void TasksEditedVersusRemoved()
    {
        string baseTasks = Lines("## 1. Tasks", "", "- [ ] 1.1 write the test", "- [ ] 1.2 write the code");
        string local = Lines("## 1. Tasks", "", "- [x] 1.1 write the test", "- [ ] 1.2 write the code");
        string remote = Lines("## 1. Tasks", "", "- [ ] 1.1 write the code");

        MergeResult remoteWins = BoardSyncApi.Merge(Tasks, baseTasks, local, remote, remoteIsNewer: true);
        MergeResult localWins = BoardSyncApi.Merge(Tasks, baseTasks, local, remote, remoteIsNewer: false);

        Assert.Equal(Lines("## 1. Tasks", "", "- [ ] 1.1 write the code"), remoteWins.Text);
        Assert.Equal("subtask: write the test", Assert.Single(remoteWins.Conflicts).What);
        Assert.Equal(Lines("## 1. Tasks", "", "- [x] 1.1 write the test", "- [ ] 1.2 write the code"), localWins.Text);
        Assert.Equal("subtask: write the test", Assert.Single(localWins.Conflicts).What);
    }

    [Fact(DisplayName = "Text edited in different places on both computers keeps both edits")]
    public void TextDifferentPlaces()
    {
        string baseText = Lines("# Title", "", "intro", "", "middle", "", "ending");
        string local = Lines("# Title", "", "intro edited here", "", "middle", "", "ending");
        string remote = Lines("# Title", "", "intro", "", "middle", "", "ending edited there", "and one more");

        MergeResult result = BoardSyncApi.Merge(Proposal, baseText, local, remote, remoteIsNewer: true);

        Assert.Equal(Lines("# Title", "", "intro edited here", "", "middle", "", "ending edited there", "and one more"), result.Text);
        Assert.Empty(result.Conflicts);
    }

    [Fact(DisplayName = "Text edited in the same place on both computers keeps the newer text and reports the other")]
    public void TextSamePlace()
    {
        string baseText = Lines("# Title", "", "intro", "", "ending");
        string local = Lines("# Title", "", "intro from here", "", "ending");
        string remote = Lines("# Title", "", "intro from there", "", "ending");

        MergeResult remoteWins = BoardSyncApi.Merge(Proposal, baseText, local, remote, remoteIsNewer: true);
        MergeResult localWins = BoardSyncApi.Merge(Proposal, baseText, local, remote, remoteIsNewer: false);

        Assert.Equal(remote, remoteWins.Text);
        MergeConflict conflict = Assert.Single(remoteWins.Conflicts);
        Assert.Equal(("intro from there", "intro from here"), (conflict.Kept, conflict.Dropped));
        Assert.Equal(local, localWins.Text);
        Assert.Equal(("intro from here", "intro from there"), (Assert.Single(localWins.Conflicts).Kept, Assert.Single(localWins.Conflicts).Dropped));
    }

    [Fact(DisplayName = "A note's frontmatter merges by field and its body as text")]
    public void NoteFrontmatterAndBody()
    {
        string baseNote = "---\nid: 12\nby: Ann\ncreated: 2026-11-10T10:00:00\n---\n# Title\n\nfirst\n\nsecond\n";
        string local = "---\nid: 12\nby: Ann\ncreated: 2026-11-10T10:00:00\nagentBy: Claude\n---\n# Title\n\nfirst edited\n\nsecond\n";
        string remote = "---\nid: 12\nby: Ann\ncreated: 2026-11-10T10:00:00\nreviewedAt: 2026-11-11T09:00:00\n---\n# Title\n\nfirst\n\nsecond edited\n";

        MergeResult result = BoardSyncApi.Merge(Note, baseNote, local, remote, remoteIsNewer: true);

        Assert.Equal("---\nid: 12\nby: Ann\ncreated: 2026-11-10T10:00:00\nagentBy: Claude\nreviewedAt: 2026-11-11T09:00:00\n---\n# Title\n\nfirst edited\n\nsecond edited\n", result.Text);
        Assert.Empty(result.Conflicts);
    }

    [Fact(DisplayName = "With no last-synced copy both sides' additions are kept and a clash goes to the newer side")]
    public void NoBase()
    {
        string local = Lines("## 1. Tasks", "", "- [ ] 1.1 from here");
        string remote = Lines("## 1. Tasks", "", "- [ ] 1.1 from there");
        Assert.Equal(Lines("## 1. Tasks", "", "- [ ] 1.1 from here", "- [ ] 1.2 from there"), BoardSyncApi.Merge(Tasks, null, local, remote, true).Text);

        string yLocal = "schema: spec-driven\nx-rtm:\n  id: 12\n  status: Blocked\n  contributors: [Ann]\n";
        string yRemote = "schema: spec-driven\nx-rtm:\n  id: 12\n  status: Planned\n  priority: low\n  contributors: [Bob]\n";
        MergeResult yaml = BoardSyncApi.Merge(Yaml, null, yLocal, yRemote, remoteIsNewer: true);
        Assert.Equal("schema: spec-driven\nx-rtm:\n  id: 12\n  status: Planned\n  priority: low\n  contributors: [Ann, Bob]\n", yaml.Text);
        Assert.Equal("status", Assert.Single(yaml.Conflicts).What);

        string tLocal = Lines("intro", "local only");
        string tRemote = Lines("intro", "remote only");
        MergeResult text = BoardSyncApi.Merge(Proposal, null, tLocal, tRemote, remoteIsNewer: false);
        Assert.Equal(tLocal, text.Text);
        Assert.Single(text.Conflicts);

        MergeResult added = BoardSyncApi.Merge(Proposal, null, Lines("intro"), Lines("intro", "more"), remoteIsNewer: false);
        Assert.Equal(Lines("intro", "more"), added.Text);
        Assert.Empty(added.Conflicts);
    }

    [Fact(DisplayName = "A file that changed on one side only, or the same way on both, comes back as that text with no conflict")]
    public void OneSideOnly()
    {
        string baseText = Lines("one", "two");
        string changed = Lines("one", "two", "three");
        foreach (string path in new[] { Yaml, Tasks, Log, Proposal, Note, "docs/image.txt" })
        {
            MergeResult remoteOnly = BoardSyncApi.Merge(path, baseText, baseText, changed, remoteIsNewer: false);
            MergeResult localOnly = BoardSyncApi.Merge(path, baseText, changed, baseText, remoteIsNewer: true);
            MergeResult both = BoardSyncApi.Merge(path, baseText, changed, changed, remoteIsNewer: true);
            Assert.Equal((changed, changed, changed), (remoteOnly.Text, localOnly.Text, both.Text));
            Assert.Empty(remoteOnly.Conflicts.Concat(localOnly.Conflicts).Concat(both.Conflicts));
        }
    }

    [Fact(DisplayName = "Any other file is taken whole from the newer side when both changed, and reported once")]
    public void OtherFiles()
    {
        MergeResult result = BoardSyncApi.Merge("openspec/changes/x/design.json", "{}", "{\"a\":1}", "{\"b\":2}", remoteIsNewer: true);

        Assert.Equal("{\"b\":2}", result.Text);
        Assert.Equal(("openspec/changes/x/design.json", "{\"b\":2}", "{\"a\":1}"), (Assert.Single(result.Conflicts).What, result.Conflicts[0].Kept, result.Conflicts[0].Dropped));
    }

    [Fact(DisplayName = "A work item file with CRLF line endings keeps them, and a list item with a comma inside quotes stays one item")]
    public void YamlCrlf()
    {
        string crlf(string s) => s.Replace("\n", "\r\n");
        string local = crlf(BaseYaml.Replace("status: Planned", "status: Blocked"));
        string remote = BaseYaml.Replace("tests: [{suite: PingTests, test: \"Ping, answers\"}]", "tests: [{suite: PingTests, test: \"Ping, answers\"}, {suite: B, test: \"x\"}]");

        MergeResult result = BoardSyncApi.Merge(Yaml, crlf(BaseYaml), local, remote, remoteIsNewer: true);

        Assert.Equal(crlf(BaseYaml.Replace("status: Planned", "status: Blocked").Replace("\"Ping, answers\"}]", "\"Ping, answers\"}, {suite: B, test: \"x\"}]")), result.Text);
        Assert.Empty(result.Conflicts);
    }

    [Fact(DisplayName = "Line endings are written the way the local file has them, and a line ending alone is not a change")]
    public void LineEndings()
    {
        string baseText = "a\r\nb\r\nc\r\n";
        string local = "a\r\nb edited\r\nc\r\n";
        string remote = "a\nb\nc\nd\n";

        MergeResult result = BoardSyncApi.Merge(Proposal, baseText, local, remote, remoteIsNewer: true);

        Assert.Equal("a\r\nb edited\r\nc\r\nd\r\n", result.Text);
        Assert.Empty(result.Conflicts);
        Assert.Equal(local, BoardSyncApi.Merge(Proposal, baseText, local, local.Replace("\r\n", "\n"), remoteIsNewer: true).Text);
    }
}
