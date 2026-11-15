# Proposal

## Why

Research on 2026-11-16 (plan linked) showed the R2 bill is $0 and stays $0, but a pass reads and hashes every board file three times, opens a new HTTPS connection each time, sends objects one after another and holds downloads in memory, and its own downloads wake the watcher into a second pass. An idle computer never learns about the other computer's edits. Wall time per pass and that gap are what to fix; the storage layout stays.

## What Changes

- One pooled HTTPS connection across passes; S3Storage disposable and cached per address.
- One directory walk with a stat cache (size, mtime, hash; git's racy rule), shared by the scan, the git guard, the marker repair and the Settings status line; no file bytes kept in memory.
- The engine reports what it wrote so the service does not schedule a second pass for the sync's own downloads.
- A light idle check: conditional GET of the manifest every 60 s focused, 5 min unfocused, with jitter; a full pass only on a 200.
- Later chunk: objects 4 to 8 at a time, downloads streamed to temp files, base reuse before a GET; compact manifest JSON and history 3.
