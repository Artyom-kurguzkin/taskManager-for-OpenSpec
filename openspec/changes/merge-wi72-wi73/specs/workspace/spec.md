## ADDED Requirements

### Requirement: Agent conventions file
When a project is opened, and for every project tab at start, the app SHALL ensure `openspec/REPO-TASKS.md` exists. A missing file SHALL be created and the user told with a notice suggesting a pointer in CLAUDE.md / AGENTS.md; a file carrying the app's version marker SHALL be refreshed when the app's text is newer and the user told which project was updated; a file without the marker SHALL NOT be changed.

#### Scenario: New project
- **WHEN** a project without `openspec/REPO-TASKS.md` is opened
- **THEN** the file exists afterwards and a notice names it

#### Scenario: Outdated copy in a tab
- **WHEN** the app starts with two projects and the second's `REPO-TASKS.md` carries an older version marker
- **THEN** that file is replaced with the current text and the notice says it was updated in that project

#### Scenario: The user's own file
- **WHEN** the file exists without the app's marker
- **THEN** it is left exactly as it is

### Requirement: What the conventions file says
The file SHALL tell agents to sign what they add or change with their own agent name (`agentBy`/`agentAt`, `by` on notes, the subtask comment; never the user's name), to keep the board true after every piece of work (a change-log note, linked tests matching the results file, linked commits, subtasks, spec and tasks text, status and dates), to keep OpenSpec valid (change folder layout, MODIFIED blocks carrying every scenario, `validate --strict`, archive with the CLI), and how ids are allocated.

#### Scenario: An agent reads it
- **WHEN** an agent opens `openspec/REPO-TASKS.md`
- **THEN** it finds the signing keys, the change-log note format, `tests:` and `commits:` link formats, the `validate --strict` command and the id rule
