namespace TaskManager.Services;

/// <summary>Shows a native "choose folder" dialog and returns the chosen path (null when cancelled).</summary>
public delegate string? FolderPicker();
