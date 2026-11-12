namespace BoardSync.PublicDTOs;

/// <summary>A file read from the storage. <paramref name="Version"/> is an opaque token (an ETag on S3, a content hash in a folder): compare it, never look inside.</summary>
public sealed record StoredFile(byte[] Bytes, string Version)
{
    /// <summary>The answer of <see cref="IBoardStorage.GetIfChanged"/> when the version asked about is still current. Compare with <see cref="IsNotModified"/>.</summary>
    public static StoredFile NotModified { get; } = new([], "");

    public bool IsNotModified => ReferenceEquals(this, NotModified);
}
