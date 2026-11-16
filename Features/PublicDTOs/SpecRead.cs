namespace Features.PublicDTOs;

/// <summary>A capability's main spec read for an agent: Success false says why (Message); else its requirement names and, when one was asked for, its full text.</summary>
public sealed record SpecRead(bool Success, string Message, Feature? Feature, IReadOnlyList<string> Requirements, string? RequirementText);
