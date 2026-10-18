namespace Features.PublicDTOs;

/// <summary>
/// An OpenSpec capability. <paramref name="Path"/> is relative to openspec/specs (e.g. "functional/remittances").
/// <paramref name="Custom"/> = added from the board (listed in config.yaml, no spec yet), so the app may delete it.
/// </summary>
public sealed record Feature(string Path, string Name, string Group, bool Custom = false);
