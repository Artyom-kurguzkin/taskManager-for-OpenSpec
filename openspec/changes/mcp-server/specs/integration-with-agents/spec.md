## ADDED Requirements

### Requirement: Agents work the board through MCP tools
The app SHALL expose the board over the Model Context Protocol as tools for listing, reading, creating, updating, annotating and validating work items, each write stamped with the calling agent's name and time.

#### Scenario: An agent creates a work item
- **WHEN** an agent calls `board_create` with a title, why, what changes and a feature
- **THEN** a valid change folder exists with the next free id, `agentBy`/`agentAt` and the agent in `contributors`, and the result carries the id and the validator's output

#### Scenario: An agent sets Completed
- **WHEN** an agent calls `board_update` with status Completed
- **THEN** the call is refused with a message saying the user archives from the app

### Requirement: Ids are allocated once per machine
Work-item and note ids SHALL be allocated under a cross-process lock shared by every worktree of the repository, so two writers never receive the same number.

#### Scenario: Two agents create at the same moment
- **WHEN** two processes in two worktrees of one repository each create a work item at the same time
- **THEN** the two items have different ids

#### Scenario: A deleted item does not free its number
- **WHEN** the item with the highest id is deleted and a new item is created
- **THEN** the new item gets a higher number than the deleted one had

### Requirement: Edits refuse a stale revision
A tool call that carries a revision SHALL be refused when the item changed since that revision was read.

#### Scenario: The item moved on
- **WHEN** an agent calls `board_update` with the revision it read and the item was edited in between
- **THEN** nothing is written and the message carries the current revision

### Requirement: The server runs without the window
The app SHALL run as a headless MCP server on 127.0.0.1 with a bearer token, and as a stdio shim that finds or starts that server and forwards to it.

#### Scenario: No server is running
- **WHEN** an agent's client starts the stdio shim and no server answers
- **THEN** the shim starts the headless server, waits for it, and the agent's tool calls succeed

#### Scenario: An older server is running
- **WHEN** the shim finds a server of an older build
- **THEN** it asks that server to shut down and starts the current build before forwarding

#### Scenario: A foreign origin
- **WHEN** a request reaches the server with an Origin header that is not localhost
- **THEN** the server answers 403
