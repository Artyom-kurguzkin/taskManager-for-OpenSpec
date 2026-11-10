namespace WorkItems.PublicDTOs;

/// <summary>One line of a change's events.log: something that happened to the work item, who did it and when (see WorkItemsApi.GetEvents for the kinds).</summary>
public sealed record WorkItemEvent(DateTime At, string By, string Kind, string Text);
