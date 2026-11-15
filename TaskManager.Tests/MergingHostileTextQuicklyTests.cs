using System.Diagnostics;
using BoardSync;

namespace TaskManager.Tests;

/// <summary>
/// Merge on hostile text (moved out of <see cref="SecurityOfBoardSyncTests"/>, unchanged): a merge of huge or pathological text must finish within seconds.
/// These tests measure elapsed time, so they run in <see cref="RealTimeCollection"/>, alone, where tests running beside them cannot stretch the time they measure.
/// </summary>
[Collection(nameof(RealTimeCollection))]
public sealed class MergingHostileTextQuicklyTests
{
    private static async Task<T> Within<T>(Task<T> work, int seconds)
    {
        if (await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(seconds))) != work) throw new TimeoutException($"Still running after {seconds} s.");
        return await work;
    }

    private static async Task<TimeSpan> Timed(Action work, int seconds)
    {
        Stopwatch watch = Stopwatch.StartNew();
        await Within(Task.Run(() => { work(); return 0; }), seconds);
        return watch.Elapsed;
    }

    [Fact(DisplayName = "merging a tasks.md that has a subtask line of 100000 spaces finishes within seconds", Timeout = 60000)]
    public async Task Tasks_merge_regex_is_linear()
    {
        string baseText = "## 1. A\n\n- [ ] 1.1 one\n";
        string local = baseText + "- [ ] 1.2 " + new string(' ', 100_000) + "x\n";
        string remote = "## 1. A\n\n- [x] 1.1 one\n";
        TimeSpan took = await Timed(() => BoardSyncApi.Merge("w/tasks.md", baseText, local, remote, false), 30);
        Assert.True(took < TimeSpan.FromSeconds(5), $"took {took}");
    }

    [Fact(DisplayName = "merging a tasks.md with a subtask line of 6000 repeated agent-comment openers finishes within seconds", Timeout = 60000)]
    public async Task Tasks_merge_agent_mark_is_linear()
    {
        string baseText = "## 1. A\n\n- [ ] 1.1 one\n";
        string local = baseText + "- [ ] 1.2 " + string.Concat(Enumerable.Repeat("<!-- agent: ", 6_000)) + "\n";
        string remote = "## 1. A\n\n- [x] 1.1 one\n";
        TimeSpan took = await Timed(() => BoardSyncApi.Merge("w/tasks.md", baseText, local, remote, false), 15);
        Assert.True(took < TimeSpan.FromSeconds(5), $"took {took}");
    }

    [Fact(DisplayName = "merging two markdown files of 100000 lines each, edited all over, finishes within seconds", Timeout = 60000)]
    public async Task Diff3_with_many_lines_is_bounded()
    {
        List<string> baseLines = Enumerable.Range(0, 100_000).Select(i => "line " + i).ToList();
        string baseText = string.Join('\n', baseLines) + "\n";
        string local = string.Join('\n', baseLines.Select((l, i) => i % 3 == 0 ? l + " L" : l)) + "\n";
        string remote = string.Join('\n', baseLines.Select((l, i) => i % 3 == 1 ? l + " R" : l)) + "\n";
        TimeSpan took = await Timed(() => BoardSyncApi.Merge("w/proposal.md", baseText, local, remote, false), 30);
        Assert.True(took < TimeSpan.FromSeconds(10), $"took {took}");
    }

    [Fact(DisplayName = "merging a tasks.md of 50000 subtasks edited on both sides finishes within seconds", Timeout = 120000)]
    public async Task Tasks_merge_with_many_entries_is_bounded()
    {
        string Tasks(Func<int, string> box) => "## 1. A\n\n" + string.Concat(Enumerable.Range(0, 50_000).Select(i => $"- [{box(i)}] 1.{i + 1} task {i}\n"));
        string baseText = Tasks(_ => " "), local = Tasks(i => i % 2 == 0 ? "x" : " "), remote = Tasks(i => i % 2 == 1 ? "x" : " ");
        TimeSpan took = await Timed(() => BoardSyncApi.Merge("w/tasks.md", baseText, local, remote, false), 100);
        Assert.True(took < TimeSpan.FromSeconds(10), $"took {took}");
    }

    [Fact(DisplayName = "merging an .openspec.yaml of 50000 keys edited on both sides finishes within seconds", Timeout = 120000)]
    public async Task Yaml_merge_with_many_keys_is_bounded()
    {
        string Yaml(Func<int, string> value) => string.Concat(Enumerable.Range(0, 50_000).Select(i => $"key{i}: {value(i)}\n"));
        string baseText = Yaml(_ => "a"), local = Yaml(i => i % 2 == 0 ? "l" : "a"), remote = Yaml(i => i % 2 == 1 ? "r" : "a");
        TimeSpan took = await Timed(() => BoardSyncApi.Merge("w/.openspec.yaml", baseText, local, remote, false), 100);
        Assert.True(took < TimeSpan.FromSeconds(10), $"took {took}");
    }
}
