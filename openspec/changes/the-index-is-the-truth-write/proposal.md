# Proposal

## Why

Every write rebuilds the whole repository cache and every tool call re-reads the repository: the next read after a status change or an agent write pays one to four seconds on a real board, the notes cache re-reads all notes of all items on any change, a burst of writes rebuilds once per write, and every start lists and parses the repository from nothing. The owner wants the app to behave like Obsidian's cache: the index is the truth, kept current by the app's own writes and the watcher, verified cheaply by stamps, persisted between runs, and portable to macOS and Linux (no NTFS change journal).

## What Changes

- Write-through: a write updates its own item in the cache from what it wrote and raises one change for that item; no rebuild; the watcher's echo is ignored.
- No blanket refresh per tool call: a stamp-based verification (one directory listing against the cache's size+mtime stamps) runs after a watcher signal, on a tool call older than 2 s since the last check, and on a focused-window timer; only changed entries are re-read.
- Incremental notes and feed: one item's change re-reads that item's notes and events only.
- Coalesced rebuilds: direct write notifications go through the same 300 ms debounce as the watcher.
- A persisted index per repository under the app's data folder: the board paints from it at start, then verifies by stamps; a "Rebuild index" action; a format version; portable paths and UTC stamps.
Plan: DevResources/plans/index-is-the-truth-2026-11-21.md (four chunks).
