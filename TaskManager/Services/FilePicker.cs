namespace TaskManager.Services;

/// <summary>Shows a native "choose file" dialog and returns the chosen path (null when cancelled).</summary>
public delegate string? FilePicker(string title);
