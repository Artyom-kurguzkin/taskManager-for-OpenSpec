using System.Diagnostics;
using System.Text.RegularExpressions;
using OpenSpecStore;
using TaskManager.Services;
using WorkItems;
using WorkItems.PublicDTOs;

namespace TaskManager.Tests;

/// <summary>
/// openspec/REPO-TASKS.md is the only thing an agent reads before it writes a work item, so its example has to be right:
/// these tests write the example's files exactly as the guide shows them and hand them to the app and to the OpenSpec CLI.
/// </summary>
[Collection(nameof(ModuleStateCollection))]
public sealed partial class GuidingAgentsTests
{
    // A file of the example: its path in backticks on a line of its own, then a fenced block.
    [GeneratedRegex(@"^`(?<path>openspec/changes/[^`]+)`:?[ \t]*\n\n```\w*\n(?<text>.*?)\n```[ \t]*$", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex ExampleFile();

    private static string Guide => AgentGuide.Text("DevResources/plans");

    /// <summary>Writes the guide's example work item into the repository, file by file, and returns its change folder name.</summary>
    private static string WriteExample(TestRepo repo)
    {
        MatchCollection files = ExampleFile().Matches(Guide);
        Assert.Equal(4, files.Count);   // .openspec.yaml, proposal.md, tasks.md, one delta spec
        foreach (Match f in files) repo.Write(f.Groups["path"].Value, f.Groups["text"].Value + "\n");
        return files[0].Groups["path"].Value.Split('/')[2];
    }

    [Fact(DisplayName = "The agent guide has a step-by-step recipe for creating a work item, and says when a requirement is ADDED and when MODIFIED")]
    public void GuideHasCreateRecipe()
    {
        string text = Guide;
        Assert.Contains("## 3. Create a work item", text);
        foreach (string s in new[] { "Folder name", "openspec/changes/archive/", "plus 1", "skip_specs: true", "SHALL or MUST", "#### Scenario:", "## Why", "validate" })
            Assert.Contains(s, text);
        // the rule the reported failure broke: MODIFIED only for a requirement the main spec already has
        Assert.Contains("`## ADDED Requirements`", text);
        Assert.Contains("`## MODIFIED Requirements`", text);
        Assert.Contains("no spec file yet", text);
        // what agents got wrong in practice: a whole plan as subtasks of one item, a spec under another feature's name, a flat task list
        foreach (string s in new[] { "Work item or subtask?", "dependsOn: [<id>]", "`x-rtm.features` in `openspec/config.yaml`", "under that same name", "`## <group>. <name>`", "- [ ] 1.1" })
            Assert.Contains(s, text);
        // how to link a plan, other items, tests, commits, dates and notes from the item
        foreach (string s in new[] { "**Links**", "`plan: DevResources/plans/<title-slug>-<yyyy-MM-dd>.md`", "Write the file first", "`dependsOn: [<id>, …]`", "`tests: [{suite:", "`commits: [<7-char hash>, …]`", "`due: yyyy-MM-dd`", "`#n<id>`", "`attachments:`" })
            Assert.Contains(s, text);
        Assert.Matches(@"(?m)^## 4\. Keep OpenSpec valid$", text);
        Assert.Matches(@"(?m)^## 5\. Numbers$", text);
    }

    [Fact(DisplayName = "A work item written exactly as the agent guide's example shows up in the app with its title, status, feature, subtask and agent stamp")]
    public void GuideExampleReadsAsWorkItem()
    {
        using TestRepo repo = new();
        string folder = WriteExample(repo);
        OpenSpecStoreApi.Open(repo.Root);

        WorkItem item = Assert.Single(WorkItemsApi.GetAll(), i => i.Folder == folder);

        Assert.Equal("Rate-limit the API", item.Title);
        Assert.Equal("Planned", item.Status);
        Assert.Equal("DevResources/plans/rate-limit-the-api-2026-11-06.md", item.Plan);
        Assert.Equal("Claude", item.AgentBy);
        Assert.True(item.AgentUnreviewed);
        Assert.Contains(item.Features, f => f.Path == "api-limits");
        Subtask task = Assert.Single(item.Subtasks);
        Assert.Equal(("1.1", "Claude"), (task.Number, task.Agent));
        Assert.Empty(WorkItemsApi.GetWarnings());
    }

    [Fact(DisplayName = "A work item written exactly as the agent guide's example passes openspec validate --strict")]
    [Trait("Category", "OpenSpecCli")]
    public void GuideExampleIsValidForTheCli()
    {
        using TestRepo repo = new();
        string folder = WriteExample(repo);

        ProcessStartInfo psi = OperatingSystem.IsWindows()
            ? new("cmd.exe", $"/c npx -y @fission-ai/openspec@{WorkItemsApi.OpenSpecCliVersion} validate {folder} --strict")
            : new("/bin/sh", $"-c \"npx -y @fission-ai/openspec@{WorkItemsApi.OpenSpecCliVersion} validate {folder} --strict\"");
        psi.WorkingDirectory = repo.Root;
        psi.RedirectStandardOutput = psi.RedirectStandardError = true;
        using Process p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();

        if (p.ExitCode != 0) Assert.Fail("openspec validate failed:" + Environment.NewLine + output);
    }

    [Fact(DisplayName = "The agent guide opens with how to detect the repo-tasks tools, turn them on and use them, and names the hand rules as the fallback")]
    public void GuideOpensWithTheMcpTools()
    {
        string text = AgentGuide.Text("DevResources/plans", appPath: @"C:\apps\TaskManager.exe");

        foreach (string s in new[] { "board_list", "board_get", "board_features", "board_create", "board_update", "board_note", "board_validate",
                     "claude mcp list", "claude mcp add", "--serve", "RepoTaskManager/token", "127.0.0.1:7431/mcp", "C:/apps/TaskManager.exe", "RTM_AGENT", "RTM_APP", "In Review", "fallback" })
            Assert.Contains(s, text);
        Assert.Matches(@"(?i)never.{0,40}`Completed`", text);
        Assert.DoesNotContain(@"C:\apps", text);   // the snippet is JSON, forward slashes

        string[] lines = text.Split('\n');
        string firstHeading = lines.First(l => l.StartsWith("## "));
        Assert.StartsWith("## 0.", firstHeading);
        Assert.Contains("repo-tasks", firstHeading);
        Assert.DoesNotContain("apps", lines[0]);   // the marker line carries no path
    }

    [Fact(DisplayName = "The agent guide tells agents to keep the worklog current, with the tools and by hand in events.log")]
    public void GuideTellsAgentsToKeepTheWorklog()
    {
        string text = AgentGuide.Text("DevResources/plans");

        string tools = text[..text.IndexOf("## 0a.", StringComparison.Ordinal)];
        Assert.Contains("worklog", tools);
        Assert.Contains("logged for you", tools);   // the tools write the events themselves
        Assert.Contains("`board_note`", tools);
        Assert.Contains("`docs`", tools);

        string hand = text[text.IndexOf("Keep the worklog current by hand", StringComparison.Ordinal)..];
        foreach (string s in new[] { "events.log", "yyyy-MM-ddTHH:mm:ss | <who> | <kind> | <text>", "append", "never rewrite", "x-rtm.docs:",
                     "status", "Old -> New", "commit-linked", "commit-unlinked", "test-linked", "test-unlinked", "doc-linked", "doc-unlinked",
                     "subtask-done", "subtask-reopened", "subtask-added", "dependency-added", "dependency-removed", "created" })
            Assert.Contains(s, hand);
    }

    [Fact(DisplayName = "An older guide is refreshed to the current version with the running app's path in its .mcp.json snippet")]
    public void OlderGuideIsRefreshedWithTheAppPath()
    {
        using TestRepo repo = new();
        repo.Write("openspec/REPO-TASKS.md", "<!-- repo-tasks-guide v11 plans=DevResources/plans attachments=" + SettingsStore.DefaultAttachmentsDir + " -->\nold text\n");
        OpenSpecStoreApi.Open(repo.Root);

        GuideChange change = AgentGuide.Ensure(repo.Root, "DevResources/plans", appPath: @"C:\apps\TaskManager.exe");

        Assert.Equal(GuideChange.Updated, change);
        string text = repo.Read("openspec/REPO-TASKS.md");
        Assert.StartsWith("<!-- repo-tasks-guide v17 ", text);
        Assert.Contains("C:/apps/TaskManager.exe", text);
    }

    [Fact(DisplayName = "The agent guide tells agents to quote values with colons, hashes or leading special characters")]
    public void GuideTellsToQuote()
    {
        string line = Assert.Single(Guide.Split('\n'), l => l.Contains("title: \"Fix: the thing\""));
        Assert.Contains("Quote", line);
        foreach (string s in new[] { "`:`", "`#`", "starts with a special character" }) Assert.Contains(s, line);
    }

    // ── v15: tools only, log as you go, decisions ──

    [Fact(DisplayName = "The agent guide says the repo-tasks tools are the only way to touch the board when they are present, and the hand rules are for when they are absent")]
    public void GuideSaysToolsAreTheOnlyWay()
    {
        string text = Guide;
        Assert.StartsWith("<!-- repo-tasks-guide v17 ", text);
        Assert.Contains("`board_log`", text);

        string tools = text[..text.IndexOf("## 0a.", StringComparison.Ordinal)];
        Assert.Contains("the only way to touch the board", tools);
        Assert.Contains("Never edit anything under `openspec/` by hand", tools);

        string sign = text[text.IndexOf("## 1. Sign", StringComparison.Ordinal)..];
        string intro = sign[..sign.IndexOf("- **Work item**", StringComparison.Ordinal)];
        Assert.Contains("only for when the `repo-tasks` tools are absent", intro);
    }

    [Fact(DisplayName = "The agent guide says a review of items reads their worklog, not only the description")]
    public void GuideSaysAReviewReadsTheWorklog()
    {
        string tools = Guide[..Guide.IndexOf("## 0a.", StringComparison.Ordinal)];
        foreach (string s in new[] { "**Looking through items.**", "In Progress", "`board_get` each", "detailed", "worklog", "notes, decisions and events added since you last looked", "say what is new before acting" })
            Assert.Contains(s, tools);
    }

    [Fact(DisplayName = "The agent guide tells supervisor and subagents to log as they go, one short entry per event, and to record decisions as decision notes, clear and brief")]
    public void GuideTellsToLogAsYouGoAndRecordDecisions()
    {
        string text = Guide;
        string tools = text[..text.IndexOf("## 0a.", StringComparison.Ordinal)];

        foreach (string s in new[] { "**Log as you go.**", "supervisor and every subagent", "one short `board_note` per event", "not one summary at the end",
                     "chunk started", "tests red", "a blocker", "chunk finished", "`Passed!`", "integration done", "`repo`" })
            Assert.Contains(s, tools);

        foreach (string s in new[] { "**Decisions.**", "decision note", "`kind: \"decision\"`", "`kind: decision`", "clear and brief", "what was decided", "why", "rules out", "emphasises" })
            Assert.Contains(s, tools);

        string plans = text[text.IndexOf("## 0a.", StringComparison.Ordinal)..text.IndexOf("## 1.", StringComparison.Ordinal)];
        Assert.Contains("decision note", plans);

        string hand = text[text.IndexOf("Keep the worklog current by hand", StringComparison.Ordinal)..];
        foreach (string s in new[] { "`title`", "`priority`", "`label`", "`dates`", "`feature`", "`contributor-added`", "`contributor-removed`", "`plan-linked`", "`plan-unlinked`", "`description-edited`", "`attachment-added`", "`merged-from`", "`#wi<id> <its title>`", "a rename", "n<note>: <file>", "written at creation" })
            Assert.Contains(s, hand);
    }
}
