namespace BoardSync.PublicDTOs;

public enum PutKind { Always, OnlyIfAbsent, OnlyIfVersion }

/// <summary>When a write may go through: always, only when the file is not there, or only when it still has the version that was read.</summary>
public sealed record PutCondition(PutKind Kind, string? Version = null)
{
    public static PutCondition Always { get; } = new(PutKind.Always);
    public static PutCondition OnlyIfAbsent { get; } = new(PutKind.OnlyIfAbsent);
    public static PutCondition OnlyIfVersion(string version) => new(PutKind.OnlyIfVersion, version ?? throw new ArgumentNullException(nameof(version)));
}
