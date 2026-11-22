using System.ComponentModel;
using System.Text.Json.Serialization;

namespace TaskManager.Mcp;

// What the tools take and return. Results are structured content; the SDK also serialises them as the text block.

public sealed record SubtaskInput(
    [property: Description("Group heading it goes under.")] string? Group = null,
    [property: Description("Subtask text, one line.")] string? Text = null,
    [property: Description("Existing subtask number, like 1.2 (board_update).")] string? Number = null,
    [property: Description("Tick or untick.")] bool? Done = null,
    [property: Description("true removes it (alone, with `number`).")] bool? Remove = null);

public sealed record TestInput(
    [property: Description("Test class short name.")] string Suite,
    [property: Description("Test display name.")] string Test,
    [property: Description("Optional id of the note (#nN) that describes the test.")] int? Note = null);

public sealed record ListRow(
    int Id, string Title, string Status, string? Feature, string? Label, string Revision,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Priority = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Contributors = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<int>? DependsOn = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Plan = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Folder = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AgentBy = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Archived = null);

public sealed record ListResult(int Total, int Count, int? NextAfter, IReadOnlyList<ListRow> Items);

public sealed record SubtaskRow(string Number, string Text, bool Done, string Group, string? Agent);

public sealed record NoteRow(int Id, string Title, string By, string Created, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Body = null, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Kind = null);

/// <summary>One entry of the worklog. A note has Kind "note", its title as Text, its NoteId and (detailed only) Body; an event has its kind and text.</summary>
public sealed record WorklogRow(
    string At, string By, string Kind, string Text,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? NoteId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Body = null);

public sealed record DeltaSpecRow(string Capability, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Markdown = null);

/// <summary>Outcome is passed, failed, skipped, or missing (not in the latest results file).</summary>
public sealed record TestRow(string Suite, string Test, string Outcome, string Mark, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Note = null);

public sealed record CommitRow(string Hash, string Subject, string Author, string Date, bool Mentions);

public sealed record DependencyRow(int Id, string Title, string Status, bool Resolved);

public sealed record GetResult(
    int Id, string Folder, string Title, string Status, string Priority, string? Label, string? Feature,
    IReadOnlyList<string> Contributors, string? Plan, string? Started, string? Due, string? Moved, string Created,
    string? AgentBy, string? AgentAt, bool AgentUnreviewed, bool Archived, bool Hidden,
    string Revision, string? Description, int DescriptionLength, bool DescriptionCut, int SubtasksDone, int SubtasksTotal,
    IReadOnlyList<SubtaskRow> Subtasks, IReadOnlyList<NoteRow> Notes, IReadOnlyList<DeltaSpecRow> DeltaSpecs,
    string? TestResultsFile, IReadOnlyList<TestRow> Tests, IReadOnlyList<CommitRow> Commits,
    IReadOnlyList<DependencyRow> DependsOn, IReadOnlyList<DependencyRow> Blocking,
    IReadOnlyList<string> Docs, IReadOnlyList<WorklogRow> Worklog, int WorklogTotal);

public sealed record FeatureItem(int Id, string Title, string Status);

public sealed record FeatureRow(
    string Path, IReadOnlyList<FeatureItem> OpenItems,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Group = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Custom = null);

/// <summary>Requirements and RequirementText are present only when board_features was asked to read a capability's main spec (feature, and requirement).</summary>
public sealed record FeaturesResult(
    IReadOnlyList<FeatureRow> Features,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Requirements = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RequirementText = null);

public sealed record CreateResult(int Id, string Folder, bool Valid, string Validation, IReadOnlyList<string> Warnings, string Revision);

public sealed record UpdateResult(int Id, IReadOnlyList<string> Applied, IReadOnlyList<string> Warnings, bool? Valid, string? Validation, string Revision, int? NoteId);

public sealed record NoteResult(int NoteId, int WorkItemId, string Path, IReadOnlyList<string> Warnings, IReadOnlyList<string> SubtasksCreated, string Revision, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Kind = null);

public sealed record ValidationRow(int Id, string Folder, bool Valid, string? Output);

public sealed record ValidateResult(bool AllValid, IReadOnlyList<ValidationRow> Items, IReadOnlyList<string> Warnings);

public sealed record NoteInput(
    [property: Description("The entry in markdown.")] string Body,
    [property: Description("Short title.")] string? Title = null,
    [property: Description("decision, or null.")] string? Kind = null);
