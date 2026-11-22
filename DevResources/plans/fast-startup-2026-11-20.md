# Start as fast as Notepad, 20 Nov 2026

Status: planned 20 Nov 2026 after the research in `local/research/fast-startup-2026-11-20.md` (69 references). Three chunks in parallel worktrees, one measurement contract between them. No diagram: the concept is one sentence, "window first, headers first, everything else behind".

## The ask

"Too slow. Get it to start approximately as fast as Notepad. I don't need the whole thing to load before I start working, neither do you need to load all of it into memory all the time. We are not inventing a wheel here, all this has been done before." Today: close to half a minute from double-click to the window on the owner's machine; 12 s in the hidden-desktop run on a demo copy.

## Where the time goes (facts)

- `Program.Main` loads the whole repository (`workspace.Open`) and starts the Kestrel agent endpoint on the main thread before `main.Run()`, and Photino creates the window inside `Run`. Everything before `Run` delays the window.
- `Workspace.Open` reads every file of every change folder, scans duplicate ids, configures git and the watcher, walks worklogs, attaches sync and runs one blocking scan, per open project.
- Scale on this repository: 1,363 files, 1.49 MB. Cold read of everything 21 s, warm 0.2 s; the 108 yaml headers alone 80 ms warm.
- The exe the owner runs (`dist-small.pubxml`, framework-dependent single file) is not ReadyToRun: everything is JIT-compiled on each start. Native AOT is not available for Blazor Hybrid on .NET 10.

## Decisions (each also a decision note on the item)

1. **Window before work.** The window is created and shown first; the repository load and the agent endpoint start right after, off the main thread. Every page renders at once from a "not loaded" state with a skeleton as its first frame (the board's columns and a few grey cards), never an empty board or a blank page. Pages re-render as data arrives through the existing `Workspace.Changed`.
2. **Headers first, bodies on demand.** The board, list and calendar are built from what the yaml header and tasks.md give (title, status, label, feature, priority, dates, contributors, subtask counts, the agent stamps). Proposal bodies, notes, events and specs are read when an item page, the worklog or the feed needs them, into a size-bounded cache that is dropped when the page closes. Reads are stamped with size and mtime so an unchanged file is never parsed twice (git's own index rule). No snapshot file yet: measure headers-first cold start first; a snapshot of the last board is the next step only if the cold start is still over 2 s.
3. **Git, duplicate scan and worklog tracking after first paint.** Git is configured and read in the background once the window is up; the contributor and commit filters say "loading" until it is in. The duplicate-id scan and worklog tracking run after the first paint, off the main thread; a repair found by the scan shows as a change like any other.
4. **ReadyToRun, uncompressed, native libraries beside the exe** for the published exe; framework-dependent stays unless the measurement says self-contained R2R is faster. No trimming, no Native AOT, tiered compilation and quick JIT at their defaults. WebView2 is left alone.
5. **One measurement contract.** The run log (from #wi178) carries timing marks with fixed names, written by `AppLog.Mark(name)`: `main` (first line of Main), `window-created`, `first-render` (the first page's OnAfterRender), `headers-read <project>`, `repo-loaded <project>`, `git-ready <project>`, `scan-done <project>`. A script `tests/startup-time.ps1` starts the exe N times cold-ish and warm on the hidden desktop and prints each mark's time since process start, median and max. Every chunk and the merge agent use it; numbers are for deciding, not for the owner's summary.

## Chunks

### window-first (decisions 1, 3, 5 marks), worktree chunk-al, port 9433
Files: TaskManager/Program.cs, TaskManager/AppState/Workspace.cs (Open becomes: synchronous cheap part that registers the project and returns; `LoadAsync` does the reads, git, scan, tracking, sync on the thread pool, raising `Changed` at each step and setting `Loading`/`Loaded` state per project), TaskManager/Helpers/AppLog.cs (`Mark`), TaskManager/Components/Layout/MainLayout.razor and SideNav/SideNavOptions (skeleton, filters say "loading"), TaskManager/Components/Pages/Board/BoardPage.razor(.cs) (skeleton first frame; first-render mark), TaskManager/wwwroot/index.html (inline skeleton + theme attribute set from a cookie/localStorage before Blazor boots), the other pages' empty states (list, calendar, analytics, settings, item page: a skeleton or "loading" line instead of "nothing here").
Tests first: `StartingTheAppTests` (new): Open returns before the files are read (a fake slow store), pages render the skeleton while `Workspace.Loading`, the board shows cards after `Changed`, the marks appear in order (`main`, `window-created`, `first-render`, `repo-loaded`); existing `SwitchingProjectsTests`, `UsingTheBoardTests`, `OpeningSettingsWithoutSideEffectsTests`, `UsingThePagesTests` keep passing with any `Open` call sites updated to await the load where a test reads right after opening (a `Workspace.WaitLoaded()` helper for tests and for the agent endpoint).
CDP: the hidden app's time from process start to CDP reachable and to the first board paint, before and after, light and dark screenshots of the skeleton frame and the loaded board `wi-startup-*`.

### headers-first (decision 2), worktree chunk-am, port 9533
Files: OpenSpecStore (Queries: the change list and headers; `ReadText` and the per-path cache: stamp by size+mtime), WorkItems/Queries.cs and Infrastructure (Metadata, TasksFile, EventLog: what `GetAll` reads per item; make the bodies lazy), Notes (notes read per item on demand), the AppState caches (`Caches.Install`, `ItemsCache`, `FeaturesCache`): a bounded body cache (e.g. 64 items) with eviction. Do not touch Workspace.cs or Program.cs.
Tests first in `ReadingOnlyWhatIsNeededTests` (new): a repository of 300 generated change folders: `GetAll` for the board reads only `.openspec.yaml` and `tasks.md` (count opened files through the store's test seam), opening one item reads its proposal, notes and events, a second `GetAll` with nothing changed parses nothing (stamps equal), a changed file is re-read, the body cache never holds more than its bound. Existing `WorkItems`/`Notes`/`OpenSpecStore` test classes and `UsingTheBoardTests` keep passing.
Black box: `tests/startup-time.ps1` on the used demo, headers-read and repo-loaded marks before and after (the marks come from window-first; until integration, log them locally with the same names).

### publish-and-measure (decisions 4, 5 script), worktree chunk-an, port 9633
Files: TaskManager/Properties/PublishProfiles/dist-small.pubxml and dist.pubxml (PublishReadyToRun, no compression, no IncludeNativeLibrariesForSelfExtract), tests/startup-time.ps1 (new; uses tests/hidden-app.ps1's desktop helpers; reads the newest run log for the marks; N runs; prints a table and writes tests/out/startup-<tag>.txt), README or DevResources note on how to publish and measure.
Tests: a script self-check (`-WhatIf` prints the plan), the pubxml parses and publishes into a scratch folder (`dotnet publish -p:PublishProfile=dist-small -o tests/out/pub-small`), the exe starts on the hidden desktop and writes a run log. Measure the current build and the R2R build, warm and cold-ish (drop the OS file cache is not possible without admin: "cold-ish" = after a build and a copy of the repo), 5 runs each, report the medians of `main` and `window-created`.

## Verification
Each chunk its named classes and its black box. After integration: the full fast suite once (baseline 1323 passed, 1 skipped, 1324) and `tests/exploratory.ps1` once (52 of 52) by a merge agent, plus `tests/startup-time.ps1` on the integrated build; then the item goes to In Review with the before/after marks in the hand-off note.

## Not in this plan
- A snapshot of the last board for the cold first paint (decision 2: only if headers-first cold start is still over 2 s).
- Self-contained R2R publish as the shipped exe (measured in chunk 3, decided after).
- A table of "we are now X ms": the owner wants it fast, not measured; the numbers stay in the board notes.
