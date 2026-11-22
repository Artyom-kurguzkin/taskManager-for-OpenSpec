namespace TaskManager.Tests;

/// <summary>
/// Splits a long loop over independent cases across a few workers, each in module state of its own (<see cref="IsolatedModuleState"/>), as a test case runs.
/// A worker makes its own <see cref="TestRepo"/> inside <c>body</c>. Every command writes a handful of files and a file system with a virus scanner
/// answers each in a millisecond or two, so a loop of hundreds of commands is time the test spends waiting, which workers overlap.
/// </summary>
internal static class IsolatedWorkers
{
    public static void Run<T>(IReadOnlyList<T> cases, int workers, Action<IReadOnlyList<T>> body)
    {
        Task[] running = [.. Enumerable.Range(0, workers)
            .Select(w => cases.Where((_, i) => i % workers == w).ToList())
            .Where(slice => slice.Count > 0)
            .Select(slice => Task.Run(() => { IsolatedModuleState.Begin(); body(slice); }))];
        Task.WaitAll(running);   // the first failure comes out as an AggregateException naming the value
    }
}
