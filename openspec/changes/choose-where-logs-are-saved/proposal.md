# Proposal

## Why

The owner wants to choose where the app's logs are saved instead of the fixed %LOCALAPPDATA%\RepoTaskManager\logs folder, so the run logs, crash notes and freeze dumps land somewhere they look and keep.

## What Changes

- Settings, General tab: a "Logs folder" row with a path, Choose folder, Open folder, Use default, and Move existing logs here.
- Run logs, crash notes and freeze dumps go to the chosen folder at once; the new log's header names the previous file.
- Run markers and publish-stop files stay in the app's own folder, so a publish and the next start still find them.
- The setting is read before logging starts; a missing folder falls back to the default with a warning; RTM_HOME still wins.
Plan: DevResources/plans/choose-the-logs-folder-2026-11-23.md
