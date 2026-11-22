# In Progress items, 20 Nov 2026: three asks, four chunks

Status: done 20 Nov 2026 (#wi19, #wi178, #wi208 In Review; regression 1322/1324 with one bUnit timing race in a determinism chunk; exploratory 52/52). Planned 20 Nov 2026, on top of the uncommitted tree (commit 903a6e9 plus three passes of 18 Nov). Each chunk is in its own worktree on its own CDP port; the window-manager chunk works in that repository. No diagram: a bug, a cross-cutting logging change, and a move of one feature to another app.

## The asks

| Item | Ask | Evidence |
|---|---|---|
| #wi19 Research column position | "I moved Research column to the very left. Then imported another project -> Research column jumped back to the right. When I do custom rearrangements, they should REMAIN until explicitly changed" | description |
| #wi178 logging | "when the app runs from `dotnet run` it does not spit any logs at all. The app should follow best practices (research) and implement complete end-to-end traces. When the published app runs, each run should generate a new log file I could review" | description |
| #wi208 move table editor | "Remove table editor feature out of this project and instead add it to the ../window-manager project. It should recognise and work for: Markdown, txt, Word, googleDoc" | description; the editor is `TaskManager/wwwroot/js/md-table.js` (pure table logic) + `md-editor.js` (the overlay), attached by NotePage and Worklog |
| #wi197 header gap | unchanged, unreproduced; waits for the owner | notes #n695 #n696 |

## What the industry does (sources in the chunks' research notes on the items)

- Logging in .NET: `Microsoft.Extensions.Logging` with structured messages, scopes and providers; `System.Diagnostics.ActivitySource` for traces (the same primitives OpenTelemetry exports); one file per process run with a bounded retention is the usual desktop-app convention (VS Code, Rider and Windows Terminal write per-session logs under the user's local app data).
- A system-wide "table editor" for any text field is a clipboard round trip: copy the selection, edit, paste; rich targets (Word, Google Docs) take an HTML table from the clipboard (CF_HTML on Windows), plain targets take text.

## Decisions (each also a decision note on its item)

1. **Opening another project never swaps the view** (#wi19, #n838). `Workspace.Open` sets `viewRoot` to the project just opened, so the board shows that project's saved options (no arrangement) instead of the one the user arranged. Only the first project and choosing a tab set the view root; adding a project keeps the arrangement the user made, with the new project's statuses placed where its own list puts them (StatusColumns and Arrange inPlace already do that).
2. **One logging seam, one file per run** (#wi178, #n839). `Microsoft.Extensions.Logging` (already in the shared framework the app hosts Kestrel from) with two providers: the console when the app runs from `dotnet run` (a console is attached) and a per-run file `%LOCALAPPDATA%/RepoTaskManager/logs/run-<yyyyMMdd-HHmmss>-<pid>.log`, the newest 20 runs kept. End-to-end traces through `ActivitySource("RepoTasks")`: an activity per UI command (`RepoPage.RunInBackground`/`Write`), per MCP tool call (`BoardTools.Run`), per queued write (`Workspace.DoLater`) and per scan (`Workspace.Notify`), with the trace id and duration in each line, so one id follows a press from the UI through the store to the watcher. Levels: Information for commands, tool calls and writes; Debug for reads, scans and renders (`RTM_LOG=Debug` turns it on); Warning for refusals and retries; Error with the exception. The crash log stays and the same text also goes to the run log. The agent endpoint's own file log becomes the run log.
3. **The table editor leaves this app and becomes a window-manager hotkey** (#wi208, #n840; window-manager #wi15). Here: `md-table.js`, `md-editor.js`, their script tags, the `rtm.markdownEditor` calls, the `.md-*` table CSS, `tests/md-table.test.mjs` and `MarkdownTableHelperTests` go; the markdown textareas stay plain. In window-manager: **Win+Alt+T** opens a table editor for the focused control's selection: the selection is copied (Ctrl+C), parsed as a markdown pipe table, a tab-separated block (what Word and Google Docs put in the text format when a table is copied) or, with nothing selected, a new 3x3 table; a WinForms grid (the hotkey list window already uses WinForms) with Word-like editing (Tab/Enter move, insert/delete row and column, paste); OK writes it back as a markdown pipe table for markdown and plain-text targets, or as an HTML table (CF_HTML plus the text fallback) for Word (`WINWORD`) and Google Docs (a browser whose title ends in "Google Docs"), then pastes (Ctrl+V). The pure table logic is a C# port of md-table.js with its tests ported.

## Chunks

### view-root (#wi19), worktree chunk-ah, port 9433
Files: TaskManager/AppState/Workspace.cs (`Open`, lines ~140-170 only), SwitchingProjectsTests / KeepingMyArrangementsTests. Tests first: arrange Research first on project A, open project B, the board's header still starts with Research and ViewRoot is still A; a new status of B appears where B's list puts it; choosing B's tab still switches the view. CDP: two demo repos, light and dark `wi19-*`.

### logging (#wi178), worktree chunk-ai, port 9533
Files: new TaskManager/Helpers/AppLog.cs (factory, providers, retention, activity listener), Program.cs, Api/AgentEndpoint.cs (`FileLoggerProvider`), Components/RepoPage.cs (`RunInBackground`/`Write`), AppState/Workspace.cs (`DoLater`, `Notify` only), Mcp/BoardTools.cs (`Run`). Tests first in a new `LoggingTests`: a run file is created with a header line and the newest 20 kept; a tool call and a queued write log lines carrying the same trace id when one caused the other; Debug lines are off by default and on with `RTM_LOG=Debug`; a logged exception keeps its stack. Black box: `dotnet run` prints lines; the hidden app writes a run file.

### table-out (#wi208), worktree chunk-aj, port 9633
Files: delete TaskManager/wwwroot/js/md-table.js, md-editor.js, tests/md-table.test.mjs, TaskManager.Tests/MarkdownTableHelperTests.cs; edit index.html (script tags), NotePage.razor and Worklog.razor (the `tableFor`/`rtm.markdownEditor` attach), app.css (`.md-*` table rules), SettingsPage.razor if it mentions the helper, package.json scripts if they run the mjs tests. Tests: UsingThePagesTests/UsingTheWorklogTests/KeepingNotesTests keep passing; a test asserts the note editor renders no table overlay and index.html loads no md-*.js. CDP: a note editor with a pipe table typed, light and dark `wi208-out-*`.

### table-in (#wi208, window-manager #wi15), ../window-manager
Files (new): WindowManager/MdTable.cs (port of md-table.js), WindowManager/TableEditor.cs (the form), WindowManager/ClipboardTable.cs (CF_HTML, TSV and pipe parsing, target detection); edit Logic.cs (the hotkey and the help row), README.md (the hotkey table). Tests: WindowManager.Tests/MdTableTests.cs (ported from tests/md-table.test.mjs), ClipboardTableTests.cs (parse TSV/pipe/HTML, CF_HTML framing, target by process/title), TableEditorTests.cs (the form on an STA thread: type into cells, Tab, OK, read the result).

## Verification
Each chunk: named classes green, CDP light and dark where the app is involved. After integration: the full fast suite once (baseline 1316 passed, 1 skipped) and `tests/exploratory.ps1` once (52 of 52) by a merge agent; then the items go to In Review. Window-manager: its own test project green and `dotnet publish` of the exe succeeds.

## Not in this plan
- #wi197 header gap: unreproduced; waits for the owner.
- Typing `/table` in any app to open the editor (needs the key hook to watch typed text): a follow-up if the hotkey is not enough.
