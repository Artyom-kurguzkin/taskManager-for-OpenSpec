# Proposal

## Why

The app takes close to half a minute from double-click to its window. Program.Main reads the whole repository (every file of every change folder, a duplicate scan, git, the worklog walk, a blocking scan) and starts the agent endpoint on the main thread before the window is created; the published exe is not precompiled. The owner wants it to start about as fast as Notepad, loading only what the first screen needs and keeping the rest out of memory until asked.

## What Changes

- The window is created and shown first; the repository load and the agent endpoint start after it, off the main thread; every page shows a skeleton as its first frame and fills in as data arrives.
- The board, list and calendar are built from the yaml headers and tasks.md only; proposals, notes, events and specs load when an item page needs them, into a bounded cache; unchanged files (same size and mtime) are never parsed twice.
- Git, the duplicate-id scan and worklog tracking run after the first paint, in the background.
- The published exe is ReadyToRun, uncompressed, with the native libraries beside it.
- The run log carries fixed timing marks and tests/startup-time.ps1 measures them, so every change is judged by the same numbers.
Research: local/research/fast-startup-2026-11-20.md (69 references).
