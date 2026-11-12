namespace BoardSync.Storage;

/// <summary>The one rule for paths in a storage: forward-slash relative paths that stay inside it.</summary>
internal static class StoragePath
{
    /// <summary>A file path: no empty, "." or ".." segment, not absolute, no backslash. Returns it unchanged, throws ArgumentException otherwise.</summary>
    public static string Check(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Length == 0) throw new ArgumentException("A storage path cannot be empty.", nameof(path));
        Reject(path, allowPartialLast: false);
        return path;
    }

    /// <summary>A listing prefix: "", or a path that may end in "/" or in part of a name.</summary>
    public static string CheckPrefix(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (prefix.Length > 0) Reject(prefix, allowPartialLast: true);
        return prefix;
    }

    private static void Reject(string path, bool allowPartialLast)
    {
        if (path.Contains('\\')) throw new ArgumentException($"The storage path \"{path}\" contains a backslash; paths use forward slashes.", nameof(path));
        if (path[0] == '/' || (path.Length > 1 && path[1] == ':')) throw new ArgumentException($"The storage path \"{path}\" is absolute; paths are relative to the storage.", nameof(path));
        string[] segments = path.Split('/');
        for (int i = 0; i < segments.Length; i++)
        {
            bool last = i == segments.Length - 1;
            if (segments[i] is "." or ".." || (segments[i].Length == 0 && !(allowPartialLast && last)))
                throw new ArgumentException($"The storage path \"{path}\" climbs out of the storage or has an empty part.", nameof(path));
        }
    }
}
