## ADDED Requirements

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
