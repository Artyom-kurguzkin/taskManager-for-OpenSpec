namespace OpenSpecStore.PublicDTOs;

/// <summary>
/// One section of a project's openspec/config.yaml (under <c>x-rtm</c>) as read. <paramref name="Value"/> is null when the section is missing, empty or could not be read
/// (the caller then uses its default). <paramref name="Problem"/> says what is wrong when it could not be read (null otherwise); a section that is simply missing is no problem.
/// <paramref name="Unavailable"/> is true when the file itself could not be opened right now (locked): that is not a statement about the section.
/// </summary>
public sealed record ConfigSection<T>(T? Value, string? Problem = null, bool Unavailable = false);
