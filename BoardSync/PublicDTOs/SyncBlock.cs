namespace BoardSync.PublicDTOs;

/// <summary>Why a sync was <see cref="SyncOutcome.Blocked"/>. None: it was not. GitBusy: git is in the middle of a merge, rebase, cherry-pick or revert, or a board file holds conflict markers; it clears by itself. OtherProject: the storage holds the board of another project.</summary>
public enum SyncBlock { None, GitBusy, OtherProject }
