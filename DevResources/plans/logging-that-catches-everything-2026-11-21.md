# Logging that shows up and catches everything, 21 Nov 2026

Status: planned 21 Nov 2026 for #wi178 after two investigations (#wi216 n892, #wi178 e30-e33) and the owner's bug note #n887 (`dotnet run` in a terminal shows no log lines). Three chunks in parallel worktrees off `staging`.

## Facts
- The app is a WinExe: started from a terminal (including `dotnet run`), Windows gives it no stdout, `AppLog.HasConsole` finds no handle and the console sink is off. The file sink works and flushes per line, but Explorer shows a stale size for an open file.
- The UI barely logs: five clicks wrote no line; most lines are MCP server chatter.
- The build stamp in every log header comes from stale `TaskManager*.dll` files beside the dist exe (`Paths.cs:23`), so all dist builds say 31 Oct.
- Not caught: exceptions before `Program.cs:56`, `TaskScheduler.UnobservedTaskException`, Photino dispatcher exceptions, Blazor component errors, page JavaScript errors, WebView2 `ProcessFailed`, hangs. The crash handler's `ShowMessage` blocks and can deadlock. Kills, publishes and shutdowns are all logged as "crash". The store's watcher timer (`OpenSpecStoreModule.cs:288`) catches only IO errors. `RepoPage.WindowEnded` can run after dispose.

## Decisions
1. Attach to the parent terminal (`AttachConsole(ATTACH_PARENT_PROCESS)`) when there is one and reopen stdout/stderr on it; the console sink then prints every line. No new console window unless `--console` is passed (then `AllocConsole`).
2. Every user action that changes something logs one Information line (`ui <page> <action> <item>`); the `ModelContextProtocol*` categories log at Warning; a tool refusal is a Warning, not an Error.
3. Every failure leaves a trace: handlers registered right after `AppLog.Init`; `UnobservedTaskException`, Photino dispatcher errors, Blazor errors (a top-level error boundary that logs, plus `#blazor-error-ui`), page `onerror`/`unhandledrejection` bridged to the log, WebView2 `ProcessFailed`; the crash dialog never blocks.
4. Freezes leave a trace: a UI-thread watchdog pings every 5 s; after 30 s without an answer it logs, writes every managed thread's stack and a minidump (`%LOCALAPPDATA%/RepoTaskManager/logs/hang-<pid>-<time>.dmp`), once per freeze.
5. Exits are named, not guessed: a Windows session end (`SystemEvents.SessionEnding` / `WM_QUERYENDSESSION`) is a clean exit; the dist-small publish writes `stopped-by-publish-<pid>` before stopping a process; the next start reports "did not exit cleanly (stopped by a publish / killed / crashed: see the Windows crash report)" and never says "crash" without evidence.
6. The build stamp comes from the assembly's embedded build time; the publish profile clears stale loose DLLs from `dist\`.
7. "Open current log" in the side menu opens the run log in the default editor.

## Chunks
- **console-and-visibility** (decisions 1, 2 MCP levels, 6, 7): `AppLog.cs`, `Paths.cs`, `dist-small.pubxml` (stale-DLL cleanup; the publish-stop marker is chunk 2's), `SideNav.razor` or `SideNavOptions.razor` (Open current log), `LoggingTests`.
- **crash-capture** (decisions 3 host side, 4, 5): `Program.cs`, `RunMarker.cs`, new `UiWatchdog.cs`, `OpenSpecStore/OpenSpecStoreModule.cs` (timer catch-all only), `dist-small.pubxml` (`StopWhatRunsFromDist` writes the marker: one target only), `LeavingATraceOfACrashTests`, a new `CatchingEveryFailureTests`.
- **page-side** (decisions 2 UI actions, 3 page side): `index.html`, `wwwroot/js/app.js` (error bridge), `App.razor`/`MainLayout.razor` (error boundary), new `Helpers/WebViewHealth.cs` (`Attach(PhotinoWindow)`: ProcessFailed; Program.cs calls it, chunk 2 adds the call), `RepoPage.cs` (UI action log line, `WindowEnded` disposed check), `UsingThePagesTests`/new `LoggingUserActionsTests`.

## Verification
Chunk classes; then the merge agent's one full run; then on the hidden desktop: start the Debug exe from a PowerShell console and see lines in it; click a status and see the `ui` line; force a Blazor error and a JS error and see both logged; a publish-stop marker read at the next start. In Review, commit on `staging`.
