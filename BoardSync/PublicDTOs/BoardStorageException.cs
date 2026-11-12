namespace BoardSync.PublicDTOs;

/// <summary>The storage could not be reached or refused the request. The message says what to check, in words a person can act on.</summary>
public sealed class BoardStorageException : Exception
{
    /// <summary>True when the file is larger than one stored file may be: the sync skips that one path instead of failing.</summary>
    internal bool IsTooLarge { get; init; }

    public BoardStorageException(string message) : base(message) { }
    public BoardStorageException(string message, Exception inner) : base(message, inner) { }
}
