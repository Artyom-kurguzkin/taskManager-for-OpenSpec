# Proposal

Settings screen should let me select folders for plans and attachments for Each opened project separately

## Why

Settings set the plans folder only for the active project, and the attachments folder was fixed at `local/task-tracking`. Pasting an image while several projects were open could write the file into another project: the note's project and the folder were read separately while background refreshes switched the active project, so the note listed a file that was not in its repository (as happened to #wi99's screenshot).

## What Changes

- Settings lists every open project with its own Plans and Attachments folder; empty = the defaults.
- Attachments go under the note's own project's folder, which is git-ignored; the agent guide names both folders (guide v9).
- The note's repository is taken from the note's own path, never from the active project, so a paste cannot land in another project.
