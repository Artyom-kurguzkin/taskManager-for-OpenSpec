# Proposal

## Why

Research on 2026-11-16 (plan linked): an agent that writes every two minutes postpones the sync without limit, and one that pauses gets half-done work uploaded, including torn files and the store's own .tmp files, because the scan has no settle window or ignore list. Board-tool calls on one machine are serialised, but the desktop app, the board server and agents editing files directly can still lose each other's writes: check-then-rename with no lock, a shared temp name, and an events.log append that is dropped silently when stale. A direct edit that restores earlier content is reverted as stale by the sync.

## What Changes

- The scan leaves files written less than 10 s ago for the next pass (capped at 5 min of deferral) and never syncs temp and editor files (*.tmp, ~$*, *.swp, .#*, *~, .rtm-*).
- The quiet timer still restarts on every change but never past 15 min after the first unsynced change.
- A per-change-folder OS lock in the store (FileShare.None lock file under the git common dir, 5 s wait, dies with the process) held across check, temp write and rename; unique temp names; events.log appended under the lock instead of refused.
- A PreToolUse hook denies agents' direct edits under openspec/changes while the board tools are loaded.
- The card shows "<agent>, N min ago" from the existing agentBy/agentAt stamp; display only, no lease file.
- Rejected on purpose: holding items back until the agent is done; lease files the sync honours; a storage-side lock between computers (the manifest's compare-and-swap already covers it).
