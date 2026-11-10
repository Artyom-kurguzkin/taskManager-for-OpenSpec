# integration-with-agents Specification

## Purpose
TBD - created by archiving change worklog-as-the-record-of-work-events-for-every-wri. Update Purpose after archive.

## Requirements

### Requirement: Tools only, log as you go
The agent guide (v15) and the server instructions SHALL say that when the repo-tasks tools are present they are the only way to touch the board and the hand rules are for when they are absent, and that the supervisor and every subagent write one short `board_note` per event while the work is in progress (chunk started, tests red, a blocker, a command and its result, chunk finished, integrated), subagents passing `repo` for the main repository.

#### Scenario: Guide refresh
- **WHEN** the app opens a repository whose guide is older than v15
- **THEN** it rewrites `openspec/REPO-TASKS.md` with the tools-only and log-as-you-go rules and the new event kinds

### Requirement: Decisions and reviews in the guide
The guide and the server instructions SHALL say that decisions are recorded as decision notes (`board_note` with `kind: "decision"`), clear and brief: what was decided and why; and that a review of items reads each item's worklog, not only its description.

#### Scenario: Looking through items
- **WHEN** an agent is asked to look through the In Progress items
- **THEN** the guide tells it to read each item's worklog in detailed format and say what is new before acting

### Requirement: board_note kind and complete results
`board_note` SHALL accept `kind` (`decision` or none); a decision without a title SHALL be titled "Decision" and `board_get` SHALL return the note's kind. Every tool result SHALL match the tool's output schema: nullable fields such as `nextAfter` and `due` SHALL be present as null rather than omitted.

#### Scenario: Last page
- **WHEN** `board_list` returns the last page
- **THEN** the result has `nextAfter: null` and a client that validates the output schema accepts it

### Requirement: Partial reads
`board_get` SHALL accept `sections` (header, description, subtasks, notes, worklog, specs, tests, links), `since` and `limit`, returning only what was asked for, always with the header and revision; `worklogTotal` SHALL still count every entry. The concise format SHALL cut a long description to its first 600 characters and say its full length, and count the subtasks done.

#### Scenario: Only the worklog since this morning
- **WHEN** an agent calls `board_get` with `sections: ["worklog"]` and `since` set to today
- **THEN** the result has the header, the revision and today's worklog entries, and no description, notes or subtasks

### Requirement: Read a main spec
`board_features` in detailed format SHALL return a capability's requirement names when given `feature`, and one requirement's full text with its scenarios when also given `requirement`.

#### Scenario: Before a MODIFIED requirement
- **WHEN** an agent asks for requirement "Work item page" of `work-item-details`
- **THEN** it receives that requirement's text and every scenario under it

### Requirement: Hand-off in one call
`board_note` SHALL return the item's new revision. `board_update` SHALL accept an inline `note`, and `testsAdd`, `testsRemove`, `docsAdd`, `docsRemove` and `commitsAdd` that change a list without re-sending it. A test link whose suite is wrong SHALL be corrected when its display name is unique in the latest results, with a warning saying so.

#### Scenario: Finish a chunk
- **WHEN** an agent calls `board_update` with a ticked subtask, `testsAdd`, status In Review and a `note`
- **THEN** all four are written in that call and the result carries the note id and the new revision

### Requirement: Resolution, folder and log
`board_update` SHALL accept `resolution` (cancelled, duplicate of #wiN, superseded by #wiN), which hides the item, writes a decision note and logs the resolution. `board_create` SHALL accept `folder` and otherwise cut a long title's slug to a few words. `board_log` SHALL append one one-line event to the worklog without a note file.

#### Scenario: Cancel an item
- **WHEN** an agent calls `board_update` on #wi140 with `resolution: "superseded by #wi137"`
- **THEN** #wi140 is hidden, has a decision note titled "Superseded by #wi137" and a `resolution` event

### Requirement: The server lives while the app runs
The desktop app SHALL start the board server when it opens, passing its own process id (`--serve --parent <pid> --idle 0`), reusing a running server of its version. A server started with `--parent` SHALL exit when that process ends, closed or crashed, and remove `server.json`. `--serve` without `--parent` SHALL keep its idle exit.

#### Scenario: Close the app
- **WHEN** the user closes the app that started the server
- **THEN** the server stops answering within a few seconds and no app process holds the exe

### Requirement: The stdio entry only connects
The stdio entry (`--mcp`) SHALL never start a server. While none answers it SHALL still answer `initialize` and `tools/list`, and answer every other request with the error "The Repo Tasks app is not running. Ask the user to open it; the board tools start with it." It SHALL relay to the server as soon as one answers, without a restart.

#### Scenario: App opened later
- **WHEN** an agent calls a board tool with the app closed, then the user opens the app and the agent calls again
- **THEN** the first call returns the error asking to open the app and the second succeeds
