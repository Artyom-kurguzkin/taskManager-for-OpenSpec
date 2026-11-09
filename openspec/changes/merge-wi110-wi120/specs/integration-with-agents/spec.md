## ADDED Requirements

### Requirement: Agents work the board through MCP tools
The app SHALL expose the board over the Model Context Protocol as seven tools (`board_list`, `board_get`, `board_features`, `board_create`, `board_update`, `board_note`, `board_validate`), each write stamped with the calling agent's name and time. A mistake the agent can fix SHALL come back as a tool error that says how to fix the call. Creating an item and changing its files SHALL run the pinned OpenSpec validator and return its output.

#### Scenario: An agent creates a work item
- **WHEN** an agent calls `board_create` with a title, why, what changes and a feature
- **THEN** a valid change folder exists with the next free id, `agentBy`/`agentAt` and the agent in `contributors`, and the result carries the id and the validator's output

#### Scenario: An agent sets Completed
- **WHEN** an agent calls `board_update` with status Completed
- **THEN** the call is refused with a message saying the user archives from the app

#### Scenario: An agent writes a change-log note
- **WHEN** an agent named Codex calls `board_note` on #wi123 with a title and a body
- **THEN** the note exists under that item with the next free note id, `by: Codex` and the agent stamp, and `board_get` lists it

#### Scenario: An agent links a test the results do not have
- **WHEN** an agent calls `board_update` with a test name that is not in the latest results file
- **THEN** the link is written and the result carries a warning naming the test and the results file

#### Scenario: A call outside any repository
- **WHEN** a tool is called with a `repo` path that has no `openspec/` folder at or above it
- **THEN** the call fails with a message naming the path it tried

### Requirement: Ids are allocated once per machine
Work-item and note ids SHALL be allocated under a cross-process lock shared by every worktree of the repository, so two writers never receive the same number. The allocator SHALL take the next number after every item in every worktree and after the last number it handed out. A writer that finds the lock held SHALL wait for it. The desktop app SHALL allocate through the same lock.

#### Scenario: Two agents create at the same moment
- **WHEN** two processes in two worktrees of one repository each create a work item at the same time
- **THEN** the two items have different ids

#### Scenario: A deleted item does not free its number
- **WHEN** the item with the highest id is deleted and a new item is created
- **THEN** the new item gets a higher number than the deleted one had

#### Scenario: The lock is held
- **WHEN** another process holds the id lock for a moment while the app creates a work item
- **THEN** the app waits, then creates the item with the next number

### Requirement: Edits refuse a stale revision
Every work item SHALL carry a revision that changes when any file of its change folder changes. A tool call that carries a revision SHALL be refused when the item changed since that revision was read; a call that carries none SHALL be written unchecked.

#### Scenario: The item moved on
- **WHEN** an agent calls `board_update` with the revision it read and the item was edited in between
- **THEN** nothing is written and the message carries the current revision

#### Scenario: The revision still matches
- **WHEN** an agent calls `board_update` with the revision `board_get` just returned
- **THEN** the change is written and the result carries the new revision

### Requirement: The server runs without the window
The app SHALL run as a headless MCP server on 127.0.0.1 with a bearer token (`--serve`), and as a stdio shim that finds or starts that server and forwards to it (`--mcp`), serving the tools in its own process when no server can be reached. The server SHALL record its pid, port and version for discovery, answer a health check without the token, stop on an authorised shutdown request, and exit by itself after its idle time. The desktop app SHALL not host the endpoint itself.

#### Scenario: No server is running
- **WHEN** an agent's client starts the stdio shim and no server answers
- **THEN** the shim starts the headless server, waits for it, and the agent's tool calls succeed

#### Scenario: An older server is running
- **WHEN** the shim finds a server of an older build
- **THEN** it asks that server to shut down and starts the current build before forwarding

#### Scenario: A foreign origin
- **WHEN** a request reaches the server with an Origin header that is not localhost
- **THEN** the server answers 403

#### Scenario: No token
- **WHEN** a request other than the health check reaches the server without the bearer token, or with a wrong one
- **THEN** the server answers 401

#### Scenario: Nobody calls for the idle time
- **WHEN** the server gets no request for its idle time
- **THEN** it exits and removes its discovery file

#### Scenario: The shim ends
- **WHEN** the agent's client closes the shim that started the server
- **THEN** the shim exits and its output ends, while the server keeps running for the next agent

### Requirement: The agent guide teaches the server
The guide the app writes to `openspec/REPO-TASKS.md` SHALL open with how an agent detects the `repo-tasks` tools, turns them on (the `.mcp.json` entry with the app's path, the `claude mcp add` command, `--serve` and the token file for HTTP-only clients) and uses them, and SHALL present the hand-editing rules as the fallback when no tools can be loaded.

#### Scenario: A repository opened on this machine
- **WHEN** the app opens a repository and writes or refreshes the guide
- **THEN** the guide's `.mcp.json` snippet names the path of the running app

#### Scenario: An agent without the tools
- **WHEN** an agent reads the guide in a session where no `repo-tasks` tools are loaded
- **THEN** the guide tells it the command to add the server before falling back to editing files

### Requirement: One server tells its callers apart
The stdio shim SHALL tell the server which repository it was started in and which agent it speaks for, so a tool call without `repo` acts on the caller's repository under the caller's name. A call that reaches the headless server without a repository SHALL be refused with a message asking for `repo`. Every tool call SHALL read the repository from disk as it is at that moment.

#### Scenario: Two repositories, one server
- **WHEN** two shims started in two repositories call `board_note` without `repo`
- **THEN** each note is written in its own repository with its own agent as the author

#### Scenario: A plain HTTP call names no repository
- **WHEN** a client calls `board_list` on the headless server without `repo`
- **THEN** the call is refused with a message asking for `repo`

#### Scenario: The user edited the item a moment ago
- **WHEN** the desktop app changes an item's status and an agent calls `board_get` right after
- **THEN** the agent reads the new status

### Requirement: Settings shows the server
The Settings page SHALL show the `.mcp.json` entry with the running app's path, whether the headless server is running and on which port, and a button that starts or stops it.

#### Scenario: Start from Settings
- **WHEN** the server is not running and the user clicks Start
- **THEN** the page shows the server running with its port, and the button reads Stop

#### Scenario: Stop from Settings
- **WHEN** the server is running and the user clicks Stop
- **THEN** the page shows the server not running, with the hint that it starts by itself when an agent connects
