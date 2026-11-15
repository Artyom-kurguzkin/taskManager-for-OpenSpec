namespace TaskManager.Services;

/// <summary>Shows a native "choose folder" dialog and returns the chosen path (null when cancelled). The title says what the folder is for; null is the repository dialog's.</summary>
public delegate string? FolderPicker(string? title = null);
