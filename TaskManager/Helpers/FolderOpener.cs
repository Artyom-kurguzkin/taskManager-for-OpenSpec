using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TaskManager.Helpers;

/// <summary>Shows a folder in the OS file manager, or opens a file in its default program.</summary>
public delegate (bool Success, string Message) FolderOpener(string path);

public static class FolderOpeners
{
    public static (bool Success, string Message) Default(string path) =>
        Open(path, OperatingSystem.IsWindows(), HasDefaultProgram, s => Process.Start(s)?.Dispose());

    /// <summary>
    /// Hands the path to the shell, except a file on Windows whose extension has no default program:
    /// the shell would show its "Open with" picker, so it opens in Notepad instead (a .log on a fresh PC).
    /// </summary>
    public static (bool Success, string Message) Open(string path, bool windows, Func<string, bool> hasDefaultProgram, Action<ProcessStartInfo> start)
    {
        try
        {
            if (windows && !Directory.Exists(path) && !hasDefaultProgram(Path.GetExtension(path)))
                start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = false });
            else
                start(new ProcessStartInfo(path) { UseShellExecute = true });
            return (true, "");
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return (false, e.Message); }
    }

    /// <summary>Whether the shell has a program for files with this extension (not the "Open with" fallback).</summary>
    public static bool HasDefaultProgram(string extension)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(extension)) return true;
        uint size = 0;
        int hr = AssocQueryString(AssocfInitIgnoreUnknown, AssocstrExecutable, extension, "open", null, ref size);
        return hr is 0 or 1;   // S_OK or S_FALSE (size only); HRESULT_FROM_WIN32(ERROR_NO_ASSOCIATION) when nothing opens it
    }

    private const uint AssocfInitIgnoreUnknown = 0x400;   // do not answer with the "Open with" program for an unknown type
    private const uint AssocstrExecutable = 2;

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int AssocQueryString(uint flags, uint str, string assoc, string? extra, char[]? output, ref uint size);
}
