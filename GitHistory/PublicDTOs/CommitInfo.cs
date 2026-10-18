namespace GitHistory.PublicDTOs;

/// <summary>One commit as shown in a change log or a linked-commits list. <paramref name="Hash"/> is the short hash.</summary>
public sealed record CommitInfo(string Hash, string Subject, string Author, DateTime Date);
