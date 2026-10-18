namespace TaskManager.Tests;

/// <summary>
/// A throw-away copy of Fixtures/sample-repo in the temp folder, so tests can write freely.
/// Every module reads files through OpenSpecStore's static state, so all module tests share
/// one non-parallel collection (<see cref="ModuleStateCollection"/>).
/// </summary>
internal sealed class TestRepo : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "rtm-tests", Guid.NewGuid().ToString("N"));
    public string OpenSpec => Path.Combine(Root, "openspec");

    public TestRepo()
    {
        CopyDir(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample-repo"), Root);
    }

    public string PathOf(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
    public string Read(string relative) => File.ReadAllText(PathOf(relative));
    public void Write(string relative, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathOf(relative))!);
        File.WriteAllText(PathOf(relative), text);
    }

    private static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (string d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
    }

    public void Dispose()
    {
        OpenSpecStore.OpenSpecStoreApi.Close();
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { } // ponytail: temp folder, best effort
    }
}

[CollectionDefinition(nameof(ModuleStateCollection), DisableParallelization = true)]
public sealed class ModuleStateCollection;
