# Tasks

## 1. local MCP

- [x] 1.1 The tools were not loaded in the Claude Code session: .mcp.json points at dist/TaskManager.exe, which is the 31 Oct build without --mcp. The guide and README should say to set RTM_APP to the Debug exe (or publish) and restart the session; the Settings snippet could offer the Debug path when the app runs from bin/.
- [x] 1.2 board_get has no way to ask for part of an item: detailed on a merged item returned 40 KB (full description, every note body, every event). Wanted: a `fields` or `sections` argument (worklog | notes | description | subtasks | specs), a `since` for the worklog, and a `limit` for notes.
- [x] 1.3 board_get concise still returns the whole description and every subtask; concise should cut the description to its first lines and count the rest.
- [x] 1.4 The first call after a quiet spell takes ~2.6 s (the shim starts the server); later calls 150-700 ms. Acceptable, but the shim could say so on stderr ("starting the board server") so a client does not think it hung.
- [x] 1.5 There is no board_log or lightweight way to attach a short event (e.g. "ran the suite: 495 pass") without making a note; board_note with a title-less body is the only option and makes a note file per line.
- [x] 1.6 board_create writes the subtasks straight into tasks.md, so the worklog shows only created and no added-subtask events for them, unlike subtasks added later.
- [x] 1.7 board_create has no way to choose the folder name; a long title gives a long slug (mcp-in-daily-use-what-was-missing-or-did-not-work). Wanted: an optional folder argument, or a slug cut at a few words.
- [x] 1.8 Cancelling an item has no tool path: there is no Cancelled status or resolution, so #wi140 was set to Backlog, hidden, and given a note saying why. A `board_update` resolution (cancelled | duplicate of #wiN | superseded by #wiN) that hides the item and writes the note would be one call.
- [x] 1.9 A server started from a development build runs from TaskManager/bin/Debug and holds its DLLs for up to three idle hours, so `dotnet build` fails (MSB3021, file in use) until it is shut down. The shim/serve should run from a copy of the build (shadow copy), or the build should stop a server that runs from its own output.
- [x] 1.10 board_update `specs` and `tests` worked for a spec delta and test links in one call. But a MODIFIED requirement needs the main spec's existing scenarios, and nothing in the tools reads the main spec (openspec/specs/<cap>/spec.md); board_features could list requirements, or a board_spec tool could read one.
- [x] 1.11 Client side, not the server: arguments with apostrophes are awkward to pass from a shell; a `--args-file` on the shim, or an example in the guide of piping JSON from a file, would save agents the quoting fight.
- [x] 1.12 When the shim has to start the server, the server inherits the shim's stdout handle: a script that pipes the shim's output (pwsh ... | grep) never sees end-of-file and hangs until the server exits (3 h). Start the server with its own handles (no inheritance, or redirected to the log).
- [x] 1.13 board_note changes the item's revision but does not return it, so a board_update right after a note is refused ('changed since you read it') and needs another board_get. board_note should return the new revision like board_update does.
- [x] 1.14 board_update accepted a test link whose suite was wrong (UsingThePagesTests instead of UsingTheWorklogTests) with only a warning; the warning text says 'not in the latest results' but the display name was there, under another class. Say which class the file has it under, or correct the suite when the display name is unique.
- [x] 1.15 There is no way to send arguments through the shim other than one JSON-RPC line; a 2 KB tests array is fine, but there is no --args-file and no board_update that adds to a list (tests, docs) rather than replacing it, so every link edit re-sends the whole list.
- [x] 1.16 board_update specs replaces the whole delta file of a capability: to reword one requirement an agent sends the entire spec.md back (and must first read it from disk, since board_get returns deltaSpecs as a list). Wanted: a per-requirement edit (name + new text), or at least deltaSpecs in board_get as the full text keyed by capability so the round trip stays inside the tools.
- [x] 1.17 Tool results omitted null fields that the generated output schema marks required (nextAfter, due, plan, label, started, agentBy, …), so Claude Code rejected whole board_list and board_get results. Fixed in #wi142 chunk D by serialising results with DefaultIgnoreCondition.Never; a test now walks every tool's output schema.
- [x] 1.18 The agent name on writes allows only letters, digits, spaces, dots, dashes and underscores: "Claude (chunk A)" was refused. Fine, but the error should say so up front in the tool description.

## 2. #wi143 MCP server lives while the app runs · merged 2026-11-10

- [x] 2.1 A: app starts the server with --parent; server exits with its parent; shim never starts a server and says to open the app
- [x] 2.2 Integrate, full suite, real check: open app -> tools work; close app -> server gone, dist publish succeeds
