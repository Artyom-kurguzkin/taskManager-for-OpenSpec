# Proposal

## Why

A copy of the app does not know when a newer release exists on main. The owner wants it to check on start, show its version in Settings, and update itself with a popup saying so when it is out of date.

## What Changes

- After the window shows, the app asks GitHub for the latest release and compares it with its own version (no delay to start; offline is reported, not fatal).
- Settings › General: a Version row with the version, the state and Check now.
- Out of date: a popup, the release is downloaded and checked (checksum, TaskManager.exe present), then Restart now or Later (installed at exit); the old copy is kept for one rollback and a failed swap puts it back.
- Debug, development (-g<commit>) and repo-folder builds never update themselves.
- RTM_UPDATE_URL points the check at another endpoint, for tests and demos.
