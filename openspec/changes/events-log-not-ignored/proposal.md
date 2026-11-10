# Proposal

## Why

Each work item's history lives in `events.log` in its change folder. A common `.gitignore` rule, `*.log`, silently keeps every one of them out of git: on 10 Nov this repository had 23 event files on disk and none tracked, so a clone would have shown notes but no status changes, links or subtask events. The user: "the app should automatically check gitignore and add this exception rule if not present".

## What Changes

- This repository's `.gitignore` gets `!openspec/**/events.log` (done by hand, 10 Nov).
- When the app opens a repository it checks whether git would ignore a work item's `events.log`, and when it would, appends the exception to the repository's `.gitignore` under a "Repo Tasks" comment, once, and says so in a toast.
- Nothing is written when the file is not ignored, when the exception is already there, or when the folder is not a git repository.
