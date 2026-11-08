using System.Diagnostics;

namespace TaskManager.Services;

/// <summary>Shows a folder in the OS file manager.</summary>
public delegate (bool Success, string Message) FolderOpener(string path);

public static class FolderOpeners
{
    public static (bool Success, string Message) Default(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return (true, "");
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return (false, e.Message); }
    }
}
