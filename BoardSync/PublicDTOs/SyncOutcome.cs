namespace BoardSync.PublicDTOs;

/// <summary>Synced: something moved. NothingToDo: both sides already agree. Blocked: stopped on purpose, nothing was changed. Failed: could not finish, nothing was changed locally.</summary>
public enum SyncOutcome { Synced, NothingToDo, Blocked, Failed }
