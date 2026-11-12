using BoardSync.PublicDTOs;
using BoardSync.Storage;

namespace BoardSync;

public static partial class BoardSyncApi
{
    /// <summary>A storage that is a directory (a network share, or a temp folder in tests); the version of a file is its content hash.</summary>
    public static IBoardStorage FolderStorage(string folder) => new Storage.FolderStorage(folder);

    /// <summary>
    /// An S3-compatible bucket: Cloudflare R2 (region "auto"), Amazon S3 or MinIO. Objects live under {endpoint}/{bucket}/{prefix}/.
    /// <paramref name="handler"/> replaces the network (tests pass a fake one).
    /// </summary>
    public static IBoardStorage S3Storage(string endpoint, string bucket, string? prefix, string accessKey, string secretKey, string region = "auto", HttpMessageHandler? handler = null) =>
        new Storage.S3Storage(endpoint, bucket, prefix, accessKey, secretKey, region, handler);

    /// <summary>
    /// <paramref name="storage"/> seen from inside the folder <paramref name="prefix"/> (for example "attachments"): paths given to the view are checked as usual and then put behind the prefix,
    /// listings come back relative to the view, versions and <see cref="IBoardStorage.ConditionalWrites"/> pass through. Nothing given to the view can reach a file outside the prefix.
    /// The prefix follows the rules of a path (plain names between single slashes; no ".", ".." or empty part, no backslash, not absolute) and an ArgumentException says what is wrong. Views nest.
    /// </summary>
    public static IBoardStorage Under(IBoardStorage storage, string prefix) => new PrefixedStorage(storage, prefix);

    /// <summary>Writes a small probe file, reads it back and removes it. The message says what failed, in plain words.</summary>
    public static async Task<(bool Success, string Message)> TestConnection(IBoardStorage storage, CancellationToken ct = default)
    {
        string path = $".rtm-connection-test/probe-{Guid.NewGuid():N}.txt";
        byte[] probe = System.Text.Encoding.UTF8.GetBytes("Repo Tasks connection test " + DateTime.UtcNow.ToString("o"));
        string step = "write a test file";
        try
        {
            PutResult put = await storage.Put(path, probe, PutCondition.OnlyIfAbsent, ct);
            if (!put.Written) return (false, "Could not write a test file: the storage says it is already there.");

            step = "read the test file back";
            StoredFile? back = await storage.Get(path, ct);
            if (back is null) return (false, "The test file was written but could not be read back: the storage does not show it.");
            if (!back.Bytes.SequenceEqual(probe)) return (false, "The test file was read back with different content than was written.");

            step = "remove the test file";
            await storage.Delete(path, ct);
            if (await storage.Get(path, ct) is not null) return (false, "The test file was written and read back, but could not be removed.");
            return (true, "Connected: wrote, read back and removed a test file.");
        }
        catch (BoardStorageException e)
        {
            return (false, $"Could not {step}. {e.Message}");
        }
    }
}
