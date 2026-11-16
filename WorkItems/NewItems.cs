using OpenSpecStore;
using WorkItems.PublicDTOs;

namespace WorkItems;

/// <summary>How a new work item starts, whoever makes it: the app's "+" and an agent's brief take the same road.</summary>
internal static class NewItems
{
    /// <summary>
    /// A new item titled "New work item" (or <paramref name="title"/>) in a status. One that starts In Progress is made Planned and then moved, so it gets the start day,
    /// the move and the status event every item that starts work gets.
    /// </summary>
    internal static (bool Success, string Message, int Id) CreateNew(string status, IEnumerable<string>? features, IEnumerable<string>? contributors, string? folder = null, string? title = null)
    {
        bool starting = status == "In Progress";
        (bool ok, string message, int id) = Commands.Create(title ?? ItemRules.NewItemTitle, starting ? "Planned" : status, features, contributors, folder);
        if (ok && starting && Commands.SetStatus(id, status) is { Success: false } failed) return (false, $"Work item #wi{id} was created but starting it failed: {failed.Message}", id);
        return (ok, message, id);
    }

    /// <summary>Checks the brief, then writes the change folder: proposal, dependencies, subtasks (signed), delta specs; signs it as <paramref name="who"/> and runs the validator.</summary>
    internal static CreatedWorkItem FromBrief(NewWorkItem b, string who)
    {
        CreatedWorkItem Refuse(string message) => new(false, message, 0, false, "", []);
        if (string.IsNullOrWhiteSpace(b.Title)) return Refuse("Give the work item a title.");
        if (ItemRules.CheckWhy(b.Why) is string why) return Refuse(why);
        if (string.IsNullOrWhiteSpace(b.WhatChanges)) return Refuse("`whatChanges` is empty: list what changes, e.g. \"- Each API key gets 100 requests a minute.\"");
        if (ItemRules.CheckCreationStatus(b.Status) is string status) return Refuse(status);
        if (ItemRules.CheckLabel(b.Label) is string label) return Refuse(label);
        if (ItemRules.CheckPriority(b.Priority) is string priority) return Refuse(priority);
        if (ItemRules.CheckDependencies(null, b.DependsOn) is string dependency) return Refuse(dependency);
        foreach (string cap in (b.Specs ?? new Dictionary<string, string>()).Keys)
            if (Features.FeaturesApi.CheckCapability(cap) is string capability) return Refuse(capability);
        if ((b.Subtasks ?? []).Any(s => string.IsNullOrWhiteSpace(s.Text))) return Refuse("Every subtask needs `text`.");
        if (ItemRules.CheckFolder(b.Folder) is string folder) return Refuse(folder);

        List<string> warnings = [];
        (bool ok, string message, int id) = CreateNew(b.Status, string.IsNullOrWhiteSpace(b.Feature) ? null : [b.Feature.Trim()], [who],
            string.IsNullOrWhiteSpace(b.Folder) ? null : b.Folder.Trim(), b.Title.Trim());
        if (!ok) return id == 0 ? Refuse($"Could not create the work item: {message}") : new(false, $"{message}. Fix it with board_update (id {id}).", id, false, "", warnings);
        string? failure = null;
        void Step(string name, (bool Success, string Message) r)
        {
            if (failure is null && !r.Success) failure = $"Work item #wi{id} was created but {name} failed: {r.Message}. Fix it with board_update (id {id}).";
        }
        CreatedWorkItem Done() => new(failure is null, failure ?? "", id, false, "", warnings);

        Step("writing the description", Commands.SetDescription(id, $"## Why\n\n{b.Why.Trim()}\n\n## What Changes\n\n{b.WhatChanges.Trim()}\n"));
        if (failure is not null) return Done();
        if (b.Priority is not null) Step("setting the priority", Commands.SetPriority(id, b.Priority));
        if (failure is not null) return Done();
        if (!string.IsNullOrEmpty(b.Label)) Step("setting the label", Commands.SetLabel(id, b.Label));
        if (failure is not null) return Done();
        if (!string.IsNullOrWhiteSpace(b.Plan))
        {
            Step("linking the plan", WorkItemsApi.SetPlan(id, b.Plan));
            if (failure is not null) return Done();
            if (!File.Exists(Path.Combine(OpenSpecStoreApi.RepoRoot!, b.Plan.Trim().Replace('/', Path.DirectorySeparatorChar)))) warnings.Add($"The plan file {b.Plan.Trim()} does not exist yet; write it.");
        }
        if (b.DependsOn is { Count: > 0 })
        {
            Step("setting the dependencies", Commands.SetDependencies(id, b.DependsOn));
            if (failure is not null) return Done();
            foreach (int dep in b.DependsOn.Where(d => !WorkItemsApi.IsResolved(Queries.Get(d)!))) warnings.Add($"It waits on #wi{dep}, which is not resolved yet: consider status Blocked.");
        }
        if (b.Subtasks is { Count: > 0 })
        {
            string path = Path.Combine(Queries.Get(id)!.Path, "tasks.md");
            OpenSpecStoreApi.ReadText(path);
            Step("writing the subtasks", OpenSpecStoreApi.WriteText(path, ItemRules.TasksText(b.Subtasks, who)));
            if (failure is not null) return Done();
            foreach (Subtask s in Queries.Get(id)!.Subtasks) Commands.LogEvent(id, "subtask-added", $"{s.Number} {s.Text}");   // written straight to tasks.md, so logged here like AddSubtask does
        }
        foreach ((string cap, string markdown) in b.Specs ?? new Dictionary<string, string>())
        {
            Step($"writing the delta spec for {cap}", Commands.SetDeltaSpec(id, cap, markdown));
            if (failure is not null) return Done();
            if (!string.IsNullOrWhiteSpace(b.Feature) && !string.Equals(cap.Trim('/'), b.Feature.Trim('/'), StringComparison.OrdinalIgnoreCase)) warnings.Add($"The delta spec is for {cap} but the feature is {b.Feature}: use one name for both.");
        }
        Step("signing it", Commands.StampAgent(id, who));
        if (failure is not null) return Done();
        (bool valid, string output) = Commands.Validate(id);
        if (!valid) warnings.Add("The OpenSpec validator rejected the item: read `validation`, fix it with board_update, then board_validate.");
        return new(true, "", id, valid, output, warnings);
    }
}
