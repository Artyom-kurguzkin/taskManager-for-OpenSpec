using System.Diagnostics;
using Features;
using Notes;
using OpenSpecStore;
using WorkItems;

namespace TaskManager.Tests;

/// <summary>
/// Runs the real OpenSpec CLI (via npx) against a repo the app has edited. Slow (~10 s) and needs Node.js;
/// exclude with <c>dotnet test --filter Category!=OpenSpecCli</c> when offline.
/// </summary>
[Collection(nameof(ModuleStateCollection))]
[Trait("Category", "OpenSpecCli")]
public sealed class KeepingTheRepoValidForOpenSpecTests
{
    private static (int Exit, string Output) OpenSpec(string root, string args)
    {
        ProcessStartInfo psi = OperatingSystem.IsWindows()
            ? new("cmd.exe", $"/c npx -y @fission-ai/openspec@1.14.3 {args}")
            : new("/bin/sh", $"-c \"npx -y @fission-ai/openspec@1.14.3 {args}\"");
        psi.WorkingDirectory = root;
        psi.RedirectStandardOutput = psi.RedirectStandardError = true;
        using Process p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output);
    }

    private static void Ok((bool Success, string Message) r) => Assert.Equal((true, ""), (r.Success, r.Message));
    private static void Ok((bool Success, string Message, int Id) r) => Assert.Equal((true, ""), (r.Success, r.Message));

    [Fact(DisplayName = "Everything the app writes keeps the repository valid for openspec validate --strict")]
    public void AppEditsStayValid()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        (bool ok, string msg, int created) = WorkItemsApi.Create("Rate-limit the API", "Planned", ["functional/banking"], ["Artyom"]);
        Assert.True(ok, msg);
        Ok(WorkItemsApi.SetStatus(created, "In Progress"));
        Ok(WorkItemsApi.AddSubtask(created, "Pick a limiter"));
        Ok(WorkItemsApi.SetSubtaskDone(123, "2.1", true));
        Ok(WorkItemsApi.SetTitle(125, "Plain change, now tracked"));
        Ok(WorkItemsApi.SetLane(123, "In Progress", "Lower priority"));
        Ok(WorkItemsApi.AddLane("Blocked", "Needs review"));
        Ok(WorkItemsApi.MoveSubtasksToNew(123, ["1.3"]));
        Ok(WorkItemsApi.Merge([107, 125]));
        Ok(WorkItemsApi.Hide(created));
        Ok(NotesApi.Create(123, "Artyom"));
        (bool sg, string sgMessage, _) = FeaturesApi.CreateFeature("Functional", "Exports");
        Ok((sg, sgMessage));

        (int exit, string output) = OpenSpec(repo.Root, "validate --all --strict");

        if (exit != 0) Assert.Fail("openspec validate failed:" + Environment.NewLine + output);
    }

    [Fact(DisplayName = "Values full of YAML-significant characters, written by the app, validate with the OpenSpec CLI")]
    public void NastyValuesStayValid()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        (bool ok, string msg, int id) = WorkItemsApi.Create("Fix: the thing #1", "Planned", ["yes"], ["null", "Dr. No: x"]);
        Assert.True(ok, msg);
        Ok(WorkItemsApi.SetTitle(123, "[WIP] - \"quoted\" & 'single': 100% #done"));
        Ok(WorkItemsApi.SetPlan(123, "DevResources/plans/a: b #c [v2].md"));
        Ok(WorkItemsApi.SetLinkedDocs(123, ["docs/yes.md", "123"]));
        Ok(WorkItemsApi.SetLinkedTests(123, [new("Suite: one", "Does \"x\" # [y]", null)]));
        Ok(WorkItemsApi.SetLinkedCommits(123, ["1234567", "1e34567"]));
        Ok(WorkItemsApi.SetLane(123, "In Progress", "Needs: review #2"));
        Ok(WorkItemsApi.StampAgent(id, "yes"));
        (bool nk, string nm, int note) = NotesApi.Create(123, "Dr. No: x");
        Assert.True(nk, nm);
        Ok(NotesApi.SetTitle(note, "Ping: pong #1"));

        (int exit, string output) = OpenSpec(repo.Root, "validate --all --strict");

        if (exit != 0) Assert.Fail("openspec validate failed:" + Environment.NewLine + output);
    }

    [Fact(DisplayName = "A folder the app set up from nothing is valid for the OpenSpec CLI, and a work item created in it validates too")]
    public void ScaffoldedRepoIsValid()
    {
        string empty = Directory.CreateTempSubdirectory("rtm-scaffold").FullName;
        try
        {
            Ok(OpenSpecStoreApi.Open(empty) is var (ok, _) ? (ok, "") : (false, "open failed"));
            (bool created, string msg, int _) = WorkItemsApi.Create("First task", "Backlog", [], ["Artyom"]);
            Assert.True(created, msg);

            (int exit, string output) = OpenSpec(empty, "validate --all --strict");

            if (exit != 0) Assert.Fail("openspec validate failed:" + Environment.NewLine + output);
        }
        finally { Directory.Delete(empty, true); }
    }

    [Fact(DisplayName = "An item whose modified requirement went stale (the main spec gained scenarios since) still archives through the real CLI, with every scenario kept")]
    public void StaleDeltaStillArchives()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        repo.Write("openspec/specs/functional/remittances/spec.md", EditingWorkItemsTests.RemittancesMain);
        repo.Write("openspec/changes/export-remittances/specs/functional/remittances/spec.md", EditingWorkItemsTests.RemittancesDelta);

        Ok(WorkItemsApi.SetStatus(107, "Completed"));

        Assert.True(WorkItemsApi.Get(107)!.Archived);
        string main = repo.Read("openspec/specs/functional/remittances/spec.md");
        foreach (string s in new[] { "Works", "Fee shown", "Limit" }) Assert.Contains("#### Scenario: " + s, main);
        Assert.Contains("it arrives and is exported", main);   // the change's text replaced the requirement
        Assert.Contains("### Requirement: Export", main);
        (int exit, string output) = OpenSpec(repo.Root, "validate --all --strict");
        if (exit != 0) Assert.Fail("openspec validate failed:" + Environment.NewLine + output);
    }

    [Fact(DisplayName = "Two changes that both add the same requirement archive one after the other through the real CLI, the second as a modification, and the repository stays valid")]
    public void SameAddedRequirementInTwoChangesBothArchive()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        repo.Write("openspec/specs/functional/remittances/spec.md", EditingWorkItemsTests.RemittancesMain);
        int[] ids = new int[2];
        foreach ((string title, int n) in new[] { ("Export as CSV", 0), ("Export as PDF", 1) })
        {
            (bool ok, string msg, int id) = WorkItemsApi.Create(title, "Planned", [], ["Artyom"]);
            Assert.True(ok, msg);
            ids[n] = id;
            string format = n == 0 ? "CSV" : "PDF";
            repo.Write($"openspec/changes/{WorkItemsApi.Get(id)!.Folder}/specs/functional/remittances/spec.md",
                $"## ADDED Requirements\n\n### Requirement: Export\nThe system SHALL export.\n\n#### Scenario: Export as {format}\n- **WHEN** exported\n- **THEN** a {format} file appears\n");
        }

        Ok(WorkItemsApi.SetStatus(ids[0], "Completed"));
        Ok(WorkItemsApi.SetStatus(ids[1], "Completed"));

        Assert.True(WorkItemsApi.Get(ids[0])!.Archived);
        Assert.True(WorkItemsApi.Get(ids[1])!.Archived);
        string main = repo.Read("openspec/specs/functional/remittances/spec.md");
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(main, "### Requirement: Export"));
        Assert.Contains("#### Scenario: Export as CSV", main);
        Assert.Contains("#### Scenario: Export as PDF", main);
        (int exit, string output) = OpenSpec(repo.Root, "validate --all --strict");
        if (exit != 0) Assert.Fail("openspec validate failed:" + Environment.NewLine + output);
    }

    [Fact(DisplayName = "Archiving twice on one day with the same folder name goes through the real CLI without a clash and the repository stays valid")]
    public void SameDayArchiveClashGoesThrough()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        Ok(WorkItemsApi.SetStatus(107, "Completed"));
        (bool created, string msg, int again) = WorkItemsApi.Create("Export remittances", "Backlog", [], []);
        Assert.True(created, msg);
        Ok(WorkItemsApi.SetStatus(again, "Completed"));

        string[] archived = Directory.GetDirectories(repo.PathOf("openspec/changes/archive")).Select(Path.GetFileName).OfType<string>().Where(n => n.Contains("export-remittances")).ToArray();
        Assert.Equal(2, archived.Length);
        Assert.Equal(2, archived.Distinct().Count());
        Assert.True(WorkItemsApi.Get(107)!.Archived);
        Assert.True(WorkItemsApi.Get(again)!.Archived);
        (int exit, string output) = OpenSpec(repo.Root, "validate --all --strict");
        if (exit != 0) Assert.Fail("openspec validate failed:" + Environment.NewLine + output);
    }}
