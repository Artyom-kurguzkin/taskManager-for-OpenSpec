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
        (bool sg, string sgMessage, _) = FeaturesApi.CreateSubgroup("Functional", "Exports");
        Ok((sg, sgMessage));

        (int exit, string output) = OpenSpec(repo.Root, "validate --all --strict");

        if (exit != 0) Assert.Fail("openspec validate failed:" + Environment.NewLine + output);
    }
}
