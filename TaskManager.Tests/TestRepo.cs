using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace TaskManager.Tests;

/// <summary>
/// A throw-away copy of Fixtures/sample-repo in the temp folder, so tests can write freely.
/// Every module reads files through OpenSpecStore's static state, so all module tests share
/// one non-parallel collection (<see cref="ModuleStateCollection"/>).
/// The copy is made the first time the repository is used (a test class that builds a second repository in its constructor pays for it only in the tests that use it),
/// is taken from a few copies that a background thread keeps ready (creating ~40 small files costs ~80 ms on this file system), and is deleted in the background.
/// </summary>
internal sealed class TestRepo : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "rtm-tests", Guid.NewGuid().ToString("N"));
    private readonly object gate = new();
    private bool made;

    public string Root { get { Make(); return root; } }
    public string OpenSpec => Path.Combine(Root, "openspec");

    public string PathOf(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
    public string Read(string relative) => File.ReadAllText(PathOf(relative));
    public void Write(string relative, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathOf(relative))!);
        File.WriteAllText(PathOf(relative), text);
    }

    private void Make()
    {
        lock (gate)
        {
            if (made) return;
            RepoPool.Take(root);
            made = true;
        }
    }

    public void Dispose()
    {
        OpenSpecStore.OpenSpecStoreApi.Close();
        bool wasMade;
        lock (gate) wasMade = made;
        if (!wasMade) return;
        // A repository without git keeps its id counter in %TEMP%/rtm/<hash of its path> (OpenSpecStore's IdAllocator): take that along, or every run leaves a folder per test.
        string counters = Path.Combine(Path.GetTempPath(), "rtm", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(root)))).ToLowerInvariant()[..16]);
        RepoPool.DeleteLater(root, counters);
    }
}

/// <summary>Copies of the fixture made ahead on a background thread, moved to a test's own folder when it asks; the deletes of finished tests run there too.</summary>
internal static class RepoPool
{
    private const int Ahead = 6;
    private static readonly string Source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample-repo");
    private static readonly string PoolRoot = Path.Combine(Path.GetTempPath(), "rtm-tests", "pool-" + Environment.ProcessId);
    private static readonly ConcurrentQueue<string> Ready = new();
    private static readonly ConcurrentDictionary<Task, bool> Pending = new();
    private static int filling;

    static RepoPool()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Task.WaitAll([.. Pending.Keys], TimeSpan.FromSeconds(20)); } catch (AggregateException) { }
            Delete(PoolRoot);
        };
    }

    internal static void Take(string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (Ready.TryDequeue(out string? copy))
        {
            try { Directory.Move(copy, target); Refill(); return; }
            catch (IOException) { Delete(copy); }   // something still has the folder open: make a fresh one
        }
        CopyDir(Source, target);
        Refill();
    }

    private static void Refill()
    {
        if (Ready.Count >= Ahead || Interlocked.CompareExchange(ref filling, 1, 0) != 0) return;
        Task.Run(() =>
        {
            try { while (Ready.Count < Ahead) { string dir = Path.Combine(PoolRoot, Guid.NewGuid().ToString("N")); CopyDir(Source, dir); Ready.Enqueue(dir); } }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            finally { Volatile.Write(ref filling, 0); }
        });
    }

    internal static void DeleteLater(params string[] folders)
    {
        Task? task = null;
        task = Task.Run(() => { try { foreach (string f in folders) Delete(f); } finally { Pending.TryRemove(task!, out _); } });
        Pending[task] = true;
    }

    private static void Delete(string folder)
    {
        try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // ponytail: temp folder (git objects are read-only), best effort
    }

    private static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (string d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
    }
}

/// <summary>Switches on the test seams of the product modules that only matter for speed.</summary>
internal static class TestAssemblySetup
{
    [ModuleInitializer]
    internal static void Init() => GitHistory.Infrastructure.GitCli.AnswerWithoutSpawnWhenNoRepository = true;
}

// One collection = one test at a time, which is all the modules need; it may overlap with the other collections, which touch no module state.
[CollectionDefinition(nameof(ModuleStateCollection), DisableParallelization = true)]
public sealed class ModuleStateCollection;
