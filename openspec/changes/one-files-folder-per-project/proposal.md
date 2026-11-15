# Proposal

## Why

Plans and attachments live in two unrelated folders with two settings, one per user and one per project. The settings page also carries a default board view nobody needs and a division line that splits one project's settings in two.

## What Changes

- One "Files" folder per project (default `attachments`), saved with the project, holding `images` (attachments) and `plans`.
- Plans stay in git; only `images` is kept out of git and synced through the board storage.
- Choosing the folder asks, then moves existing images and plans and rewrites their links; a project that never chooses keeps its old folders.
- The "Default board view" setting is removed.
- The line between a project's folders and its Board storage is removed.
