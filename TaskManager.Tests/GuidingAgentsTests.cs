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

    [Fact(DisplayName = "An older guide is refreshed to version 12 with the running app's path in its .mcp.json snippet")]
    public void OlderGuideIsRefreshedWithTheAppPath()
    {
        using TestRepo repo = new();
        repo.Write("openspec/REPO-TASKS.md", "<!-- repo-tasks-guide v11 plans=DevResources/plans attachments=" + SettingsStore.DefaultAttachmentsDir + " -->\nold text\n");
        OpenSpecStoreApi.Open(repo.Root);

        GuideChange change = AgentGuide.Ensure(repo.Root, "DevResources/plans", appPath: @"C:\apps\TaskManager.exe");

        Assert.Equal(GuideChange.Updated, change);
        string text = repo.Read("openspec/REPO-TASKS.md");
        Assert.StartsWith("<!-- repo-tasks-guide v12 ", text);
        Assert.Contains("C:/apps/TaskManager.exe", text);
    }
}
