namespace Features.PublicDTOs;

/// <summary>An OpenSpec capability. <paramref name="Path"/> is relative to openspec/specs (e.g. "functional/remittances").</summary>
public sealed record Feature(string Path, string Name, string Group);
