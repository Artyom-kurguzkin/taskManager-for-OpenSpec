# Cheap feed events for agents, 21 Nov 2026

Status: planned 21 Nov 2026. One chunk, worktree from the `staging` HEAD (clean), CDP port 9433. No diagram.

## The ask
"It would be good if they can follow the event standard, if they can. I just don't want it to be too expensive on usage. Can we optimise the MCP to make it easy for the agents to use?"

## Where the cost is (facts)
- An event posted with `board_note` costs an agent about 150 to 300 tokens in and out; the events themselves are cheap.
- The eight board tools' definitions (descriptions plus parameter help, `TaskManager/Mcp/BoardTools*.cs`) and the server instructions (`BoardMcp.Instructions`) go into every agent's context at its first message: roughly 5,000 tokens per agent, the dominant cost.
- Friction: `board_note` wants a title, a revision and an agent name without parentheses and answers with a JSON object; `board_log` takes one line of at most 300 characters.

## Decisions
1. **`board_log` is the agent's one event tool.** `text` may span lines, up to 4,000 characters (longer is written as a note instead, same call); optional `kind` from the fixed event standard `started | found | decision | tests-red | tests-green | verified | blocked | done` (default `log`); no `revision`, no `title`; `agent` from `RTM_AGENT` or the parameter (parentheses allowed, trimmed). It answers one short line (`#wiN e<id> kind`), not an object. `board_note` stays for decisions and hand-offs that deserve a note file.
2. **The worklog shows the kind.** The event line keeps the existing `time | by | kind | text` format; the Worklog renders the standard kinds as a small label and keeps `decision` emphasised as notes are.
3. **Definitions halved.** Each tool's description is one sentence; each parameter's help one clause; the shared guidance (rules, workflow, naming) lives once in the server instructions, trimmed to what an agent needs. Measured before and after by characters of `Description(...)` text plus the instructions.
4. **Briefs use it.** The delegate-workflow brief template says: post every status update and the final report with `board_log id=N kind=<kind> text=...`.

## Chunk: feed-events, worktree chunk-aw, port 9433
Files: TaskManager/Mcp/BoardTools.Write.cs (`board_log`), BoardTools.cs/Read.cs/Write.cs (descriptions), BoardMcp.cs (instructions), WorkItems EventLog (if `kind` needs allowing), TaskManager/Components/Pages/Worklog/Worklog.razor (kind label), app.css; tests in UsingTheBoardOverMcpTests (board_log multi-line, kind, answer shape, note fallback, agent from env/parameter with parentheses), UsingTheWorklogTests (kind label rendered), a measuring test "The board tools' definitions are under N characters" (read the Description attributes by reflection, assert the budget).
CDP: a worklog with started/found/tests-green/done events, light and dark `feed-events-*`.

## Verification
The chunk's classes; after integration the merge agent's one full run; then In Review, commit on `staging`, briefs and the skill updated.
