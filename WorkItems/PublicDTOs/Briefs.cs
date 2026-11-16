namespace WorkItems.PublicDTOs;

/// <summary>A work item as an agent briefs it: what <see cref="WorkItemsApi.CreateFromBrief"/> turns into a change folder (proposal, tasks, delta specs). Empty fields are left out.</summary>
public sealed record NewWorkItem(
    string Title, string Why, string WhatChanges, string? Feature, string? Label, string Status, string? Priority, string? Plan,
    IReadOnlyList<int>? DependsOn, IReadOnlyList<NewSubtask>? Subtasks, IReadOnlyDictionary<string, string>? Specs, string? Folder);

/// <summary>A step of a new work item: its text, the group heading it goes under (none: "Tasks") and whether it is done already.</summary>
public sealed record NewSubtask(string Text, string? Group, bool Done);

/// <summary>
/// What <see cref="WorkItemsApi.CreateFromBrief"/> made. Success false: Message says why; Id is 0 when nothing was created, else the item exists and the step in Message must be repaired.
/// Valid and Validation are the OpenSpec validator's verdict and output.
/// </summary>
public sealed record CreatedWorkItem(bool Success, string Message, int Id, bool Valid, string Validation, IReadOnlyList<string> Warnings);

/// <summary>A delta spec of a change: its capability path and, when asked for, its markdown.</summary>
public sealed record DeltaSpecFile(string Capability, string? Markdown);

/// <summary>How an item ends without being done: the text logged in its worklog and the title of its decision note. Success false: Message says why.</summary>
public sealed record Resolution(bool Success, string Message, string Event, string Title);
