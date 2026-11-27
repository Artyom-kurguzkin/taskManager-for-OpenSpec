using System.Globalization;
using System.Text.RegularExpressions;
using BoardSync;
using BoardSync.PublicDTOs;
using Notes;
using OpenSpecStore;
using WorkItems;
using WorkItems.PublicDTOs;

namespace TaskManager.Tests;

/// <summary>The app writes where a thing went in the item's event log, in the same step as the action, so a sync can follow it instead of guessing a deletion.</summary>
public sealed class LoggingWhereThingsWentTests
{
    private const string HealthLog = "openspec/changes/add-health-check/events.log";

    private static WorkItemEvent[] Of(int id, string kind) => [.. WorkItemsApi.ReadEvents(id).Where(e => e.Kind == kind)];

    [Fact(DisplayName = "Archiving writes where the folder went")]
    public void ArchivingWritesWhereTheFolderWent()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        WorkItemsApi.ConfigureArchiverWithFlags((folder, _) =>
        {
            Directory.Move(repo.PathOf($"openspec/changes/{folder}"), repo.PathOf($"openspec/changes/archive/2026-11-01-{folder}"));
            return (true, "", true);
        });
        try
        {
            Assert.Equal((true, ""), WorkItemsApi.SetStatus(107, "Completed"));

            Assert.True(WorkItemsApi.Get(107)!.Archived);
            Assert.Equal("export-remittances -> archive/2026-11-01-export-remittances", Assert.Single(Of(107, "archived")).Text);
            Assert.Contains(WorkItemsApi.ReadEvents(107), e => e.Kind == "status" && e.Text.EndsWith("-> Completed"));   // beside the status line
        }
        finally { WorkItemsApi.ConfigureArchiver(null); }
    }

    [Fact(DisplayName = "Removing a delta spec is logged")]
    public void RemovingADeltaSpecIsLogged()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        Assert.True(WorkItemsApi.DeleteFeature("non-functional/availability").Success);

        Assert.Equal("specs/non-functional/availability/spec.md", Assert.Single(Of(123, "spec-removed")).Text);
        Assert.Empty(Of(124, "spec-removed"));   // an archived change that specs another feature is not touched
    }

    [Fact(DisplayName = "Renaming a feature logs each spec it moved")]
    public void RenamingAFeatureLogsEachSpecItMoved()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);

        Assert.True(WorkItemsApi.RenameFeature("non-functional/security", "Safety").Success);

        Assert.Equal("specs/non-functional/security -> specs/non-functional/safety", Assert.Single(Of(124, "spec-moved")).Text);   // the archived change's delta moved with it
        Assert.Empty(Of(123, "spec-moved"));   // only a manual link there
    }

    [Fact(DisplayName = "Removing an attachment is logged")]
    public void RemovingAnAttachmentIsLogged()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        string src = Path.Combine(Path.GetTempPath(), "rtm-att-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(src);
        try
        {
            string file = Path.Combine(src, "shot.txt");
            File.WriteAllText(file, "evidence");
            Assert.True(NotesApi.AttachFile(123, file).Success);
            Assert.True(NotesApi.AttachLink(123, "https://example.com/x").Success);
            string copied = Assert.Single(NotesApi.Get(123)!.Attachments, a => a.EndsWith("shot.txt"));

            Assert.True(NotesApi.RemoveAttachment(123, copied).Success);
            Assert.True(NotesApi.RemoveAttachment(123, "https://example.com/x").Success);

            Assert.Equal(["n123: shot.txt", "n123: https://example.com/x"], Of(123, "attachment-removed").Select(e => e.Text));
            Assert.False(NotesApi.RemoveAttachment(123, copied).Success);
            Assert.Equal(2, Of(123, "attachment-removed").Length);   // a removal that did nothing is not logged
        }
        finally { Directory.Delete(src, true); }
    }

    [Fact(DisplayName = "New event lines carry the time zone offset")]
    public void NewEventLinesCarryTheTimeZoneOffset()
    {
        using TestRepo repo = new();
        OpenSpecStoreApi.Open(repo.Root);
        DateTime before = DateTime.Now.AddSeconds(-2);

        Assert.True(WorkItemsApi.LogEvent(123, "found", "an offset line").Success);

        string line = repo.Read(HealthLog).Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Single(l => l.EndsWith("| found | an offset line"));
        Assert.Matches(new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}[+-]\d{2}:\d{2} \| "), line);
        WorkItemEvent read = Assert.Single(Of(123, "found"));
        Assert.InRange(read.At, before, DateTime.Now.AddSeconds(2));   // read back as the same instant in this computer's time
        Assert.True(DateTimeOffset.TryParseExact(line.Split(" | ")[0], "yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset at));
        Assert.Equal(DateTimeOffset.Now.Offset, at.Offset);
    }

    [Fact(DisplayName = "Lines without an offset still read and sort")]
    public void LinesWithoutAnOffsetStillReadAndSort()
    {
        using TestRepo repo = new();
        repo.Write(HealthLog, "2026-11-10T10:00:00 | Ann | created | Old line\n");
        OpenSpecStoreApi.Open(repo.Root);

        Assert.Equal(new DateTime(2026, 11, 10, 10, 0, 0), Assert.Single(Of(123, "created")).At);   // an old line reads as local time, as before

        // two zones: 05:00 at +11:00 is 18:00 the day before in UTC, earlier than 01:00 at -08:00 (09:00 UTC), though 01:00 reads smaller than 05:00
        string local = "2026-11-26T01:00:00-08:00 | Ann | log | later in Los Angeles\n";
        string remote = "2026-11-26T05:00:00+11:00 | Bob | log | earlier in Sydney\n";
        MergeResult merged = BoardSyncApi.Merge(HealthLog, "", local, remote, remoteIsNewer: true);
        Assert.Equal(remote + local, merged.Text);

        // old lines (no offset) keep sorting by their own reading, and a line without any time stays after the one before it
        string first = "2026-11-10T09:00:00 | Ann | log | z\n";
        string old = first + "2026-11-10T12:00:00 | Ann | log | b\n2026-11-10T10:00:00 | Ann | log | a\nplain text line\n";
        string other = first + "2026-11-10T11:00:00 | Bob | log | c\n";
        Assert.Equal(first + "2026-11-10T10:00:00 | Ann | log | a\nplain text line\n2026-11-10T11:00:00 | Bob | log | c\n2026-11-10T12:00:00 | Ann | log | b\n", BoardSyncApi.Merge(HealthLog, first, old, other, remoteIsNewer: false).Text);
    }
}
