# The index is the truth: write-through, no blanket refresh, incremental notes, coalesced scans, a persisted index, 21 Nov 2026

Status: planned 21 Nov 2026, ready to start after #wi212 lands. Four chunks in parallel worktrees from the `staging` HEAD. Research: `local/research/fast-startup-2026-11-20.md` Part C (Obsidian's metadata cache, git's size+mtime stamping and racy rule, VS Code's storage, Foam's sharded parser cache, SQLite as a local cache). Portable by design: plain `System.IO` stamps and `FileSystemWatcher` on Windows, macOS and Linux; no NTFS change journal, no platform API.

## The ask
"Write rebuilds the whole repository cache and every tool call re-reads the repository. How can we improve this?" Then: write-through for the app's own writes; drop the per-call refresh for a cheap staleness check; incremental notes and feed; coalesce the scan; and the persisted index, "I only don't like the NTFS part, I want the thing to run on mac and linux too."

## Today (facts)
- `BoardTools.Run` calls `OpenSpecStoreApi.Refresh()` on every tool call: the config is re-read and every cached item is marked suspect ("the watcher lags 300 ms and can drop events in a server that runs for hours").
- After any write, the next read rebuilds the board: one directory pass over every change folder (a stat per file; the per-folder fingerprint from #wi210 skips unchanged folders' parse), then the notes cache and the feed are rebuilt from all items, and `Workspace.Notify` runs a scan. 25 to 45 ms on a 22-file fixture; one to four seconds on a real board with sync attached, paid after every status change and every agent write.
- `NotesCache` re-reads every item's notes on any change; `Feed` is rebuilt whole.
- The watcher's own events are debounced 300 ms; direct `Notify` calls from writes are not, so a burst of writes (a merge, ticking five subtasks, an agent batch) rebuilds once per write.
- Nothing is persisted between runs: every start lists and parses the whole repository (headers only since #wi210).

## Decisions

1. **Write-through.** Every write the app or a tool makes names its item and files; the write path updates that item's cache entry (and its notes/feed entries) from what it just wrote, stamps the written files, and raises `Changed` for that item only. No rebuild. The watcher's echo of the app's own write is already recognised (`RepoPage` own-echo skip, `Workspace` write queue); the store's echo filter extends to the cache so the echo does not undo the write-through. A write that fails leaves the cache as it was.
2. **No blanket refresh; a cheap staleness check instead.** `Refresh()` per tool call goes. In its place: `Verify()`, one directory listing of `openspec/changes` and the config, compared to the stamps (size + mtime, the racy rule kept) the cache already holds; only entries whose stamp differs are re-read. It runs when the watcher reports a change (after the debounce), when a tool call arrives and the last verification is older than 2 s, and on a timer only while a window is focused (every 5 s, listing only, no file reads). Outside edits are still caught within those bounds; a dropped watcher event is caught by the next verification.
3. **Incremental notes and feed.** The notes cache and the feed are keyed by item; a change to one item re-reads that item's `notes/` folder and `events.log` and patches the feed's entries for it. "Rebuild all" remains only for the start-up load and for an explicit rebuild.
4. **Coalesced rebuilds.** Direct notifications from writes go through the same 300 ms debounce as the watcher's, keyed by repository: a burst becomes one `Changed`/scan after the burst; write-through keeps the UI exact during the burst (the item shown is the item written).
5. **A persisted index.** The parsed headers, task counts, worklog "updated" stamps and the per-file stamps are saved per repository to one file under the app's data folder (`%LOCALAPPDATA%/RepoTaskManager/index/<repo-key>.json`, or the platform's app-data folder via `Environment.SpecialFolder.LocalApplicationData`), written after each rebuild and on exit. On start: paint the board from the saved index at once (before any listing), then run `Verify()` and re-read only what changed; an index for a repository that moved or whose format version differs is dropped and rebuilt. "Rebuild index" is a Settings action and a store API, the safety valve.
6. **Portable.** Stamps are `FileSystemInfo.Length` + `LastWriteTimeUtc`; the watcher is `FileSystemWatcher` (inotify on Linux, FSEvents on macOS, with their known coalescing, which the verification timer covers); paths through `Path.Combine`; no journal, no platform call. The tests run on all three.

## Chunks

### write-through (decisions 1, 4), worktree chunk-ax, port 9433
Files: OpenSpecStore/Commands.cs + Queries.cs (the write path returns what it wrote; cache patch; echo filter), WorkItems/Commands.cs + Queries.cs (item re-parse from the written files), TaskManager/AppState/ItemsCache.cs, CacheState.cs, Workspace.cs (`Notify` goes through the debounce; `DoLater` passes the item), Mcp/BoardTools.cs (`AfterWrite` carries the item). Tests first in a new `WritingThroughTheCacheTests`: after a status change the item is current in the cache with no directory pass (count via the store's `ReadLog` seam); a burst of five ticks raises one `Changed`; the watcher's echo of the write changes nothing; a failed write leaves the cache as it was; an outside edit to the same item still wins at the next verification.

### verify-not-refresh (decision 2), worktree chunk-ay, port 9533
Files: OpenSpecStore/OpenSpecStoreApi.cs + Queries.cs (`Verify()`), Mcp/BoardTools.cs (`Run` calls `Verify` on the 2 s rule instead of `Refresh`), TaskManager/AppState/Workspace.cs (the focused-window timer, listing only), SettingsStore if a toggle is wanted (none: fixed behaviour). Tests first in `SeeingOutsideEditsLiveTests` (recreate the class; the real-watcher tests were deleted) with an injected clock and an injected watcher signal: an outside edit is seen after the watcher signal; a dropped signal is caught by the next tool call after 2 s; a tool call within 2 s of a verification does no listing; the timer lists but never reads an unchanged tree.

### incremental-notes (decision 3), worktree chunk-az, port 9633
Files: TaskManager/AppState/NotesCache.cs, Notes/Feed.cs + NotesApi.cs, WorkItems/Infrastructure/EventLog.cs (per-item read). Tests first in `KeepingNotesTests`/`UsingTheWorklogTests`: a note added to item A re-reads A's notes only (ReadLog); the feed shows it without re-reading B's; the side menu's hidden/bug counts stay right.

### persisted-index (decisions 5, 6), worktree chunk-ba, port 9733
Files: TaskManager/AppState/ItemsCache.cs (save/load, format version, stamps), a new `IndexFile.cs` under AppState, Workspace.cs (`Register` paints from the index, `LoadAsync` verifies), Settings (Rebuild index row), Helpers/Paths.cs (the index folder). Tests first in a new `KeepingAnIndexBetweenRunsTests`: a second start paints the board from the index before any file read (ReadLog empty until Verify); a file changed while closed is re-read and nothing else; a moved repository or an older format drops the index; "Rebuild index" re-reads everything; the index file is portable (forward slashes, UTC stamps). CDP: `tests/startup-time.ps1` before and after on the used demo: `headers-read` from a saved index versus a cold list.

## Verification
Each chunk its classes and its black box; after integration the merge agent's one full run; `tests/startup-time.ps1` once more for the index; then In Review and one commit on `staging`.

## Not in this plan
- SQLite as the index store: a JSON file is enough at this scale (one write per rebuild); revisit if the index passes a few megabytes.
- The NTFS change journal or any platform-specific change feed.
