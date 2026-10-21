using System.Diagnostics;

namespace TaskManager.Services;

/// <summary>Opens a file in an external editor (a command like "code" or "notepad++", or a full path).</summary>
public delegate (bool Success, string Message) EditorLauncher(string editor, string filePath);

public static class EditorLaunchers
{
    public static (bool Success, string Message) Default(string editor, string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = editor, Arguments = $"\"{filePath}\"", UseShellExecute = true });
            return (true, "");
        }
        catch (Exception e) { return (false, $"Couldn't launch \"{editor}\": {e.Message}"); }
    }
}
