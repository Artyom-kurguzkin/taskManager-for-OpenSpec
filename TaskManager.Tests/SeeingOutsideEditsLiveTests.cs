using OpenSpecStore;

namespace TaskManager.Tests;

[Collection(nameof(ModuleStateCollection))]
public sealed class SeeingOutsideEditsLiveTests
{
    [Fact(DisplayName = "Ticking a checkbox in another editor is noticed within 2 seconds")]
    public async Task OutsideEditRaisesChanged()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        TaskCompletionSource fired = new();
        void Handler() => fired.TrySetResult();
        OpenSpecStoreApi.Changed += Handler;
        try
        {
            repo.Write("openspec/changes/add-health-check/tasks.md", "# Tasks\n\n- [x] 2.1 done elsewhere\n");
            Task done = await Task.WhenAny(fired.Task, Task.Delay(2000));
            Assert.Same(fired.Task, done);
        }
        finally { OpenSpecStoreApi.Changed -= Handler; }
    }

    [Fact(DisplayName = "The app's own save is not reported back as an outside change")]
    public async Task OwnWriteIsQuiet()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        int count = 0;
        void Handler() => Interlocked.Increment(ref count);
        OpenSpecStoreApi.Changed += Handler;
        try
        {
            string path = repo.PathOf("openspec/changes/add-health-check/tasks.md");
            OpenSpecStoreApi.ReadText(path);
            OpenSpecStoreApi.WriteText(path, "# Tasks\n\n- [x] 2.1 saved by the app\n");
            await Task.Delay(1200);
            Assert.Equal(0, count);
        }
        finally { OpenSpecStoreApi.Changed -= Handler; }
    }
}
