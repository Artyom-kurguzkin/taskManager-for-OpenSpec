# Proposal

## Why

Pasted images and attached files live in a folder kept out of git and are not synced, so a note arrives on the second computer without its pictures. Changing the attachments folder in Settings moves nothing and rewrites nothing, and the folder is set per computer.

## What Changes

- A second sync beside the board's sends the attachments folder to the same connected storage, under `attachments/`: whole files, never merged, the loser of a clash kept in the trash.
- A switch per project, "Sync attachments too", on by default once a storage is connected.
- The attachments folder becomes a project setting, so every computer uses the same one.
- Changing the folder asks, then moves the files and rewrites their paths in notes and descriptions; a change made on another computer is followed automatically.
- A file over the size cap is skipped and reported.
