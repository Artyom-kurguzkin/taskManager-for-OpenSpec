# Choose where logs are saved, 23 Nov 2026

Status: planned 23 Nov 2026. One chunk (worktree chunk-be, CDP port 9433), then one merge run.

## The ask
"Add a setting option for me to choose where logs are saved."

## Today
`Paths.Logs` = `<Paths.Home>/logs` (`%LOCALAPPDATA%/RepoTaskManager/logs`, or `RTM_HOME/logs`). It holds the run logs (`run-*.log`, AppLog), the crash notes (`crash-*.log`, Program.LogCrash), the freeze dumps (`hang-*.dmp`, UiWatchdog), the run markers (`run-<pid>.marker`, RunMarker) and the publish-stop files (`stopped-by-publish-<pid>`, written by dist-small.pubxml's PowerShell, and `ended-by-windows-<pid>`). `AppLog.Init` runs first in `Main`, before the settings are opened.

## Decisions
1. **The files a person reads move; the app's bookkeeping does not.** Run logs, crash notes and freeze dumps go to the chosen folder. Run markers and the stop files stay in `<Paths.Home>/logs`, so a publish (which cannot read the user's settings) and the next start still find them.
2. **A General-tab row in Settings, like the Plans and Images rows:** "Logs folder", a path input (absolute; empty = the default, shown as placeholder), "Choose folder" (the OS folder picker the app already has), "Open folder", "Use default". The setting lives in the user's settings file (`SettingsStore`, app-wide, not per project).
3. **Takes effect at once.** Saving the row closes the current run log and opens a new one in the new folder (the header line names the previous file, so a run split across two folders can be followed); the next crash note and dump go there too. Retention (newest 20) applies per folder. Nothing is moved automatically; the row offers "Move existing logs here" (copies `run-*.log`, `crash-*.log`, `hang-*.dmp` from the old folder, then deletes the copied ones) as a separate button.
4. **At start the setting is read before `AppLog.Init`** (the settings file is read once, cheaply, from the same path `SettingsStore.Open` uses; a missing or unreadable folder falls back to the default with a Warning line saying so). `RTM_HOME` (tests, demos) still wins over the setting.
5. **"Open current log" follows the setting** (`AppLog.CurrentFile`).

## Verification
Tests first in a new `ChoosingTheLogsFolderTests`: the row renders on the General tab; choosing a folder switches the run log there at once and the header names the old file; crash notes and dumps follow; markers stay in the app folder; a missing folder falls back with a Warning; RTM_HOME wins; Move existing logs here moves only the three kinds. Real app on the hidden desktop: set the folder, see the new file, restart, see logs start there. Then the merge agent's one full run, In Review, commit on staging.
