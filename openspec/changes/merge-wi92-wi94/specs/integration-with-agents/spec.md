## ADDED Requirements

### Requirement: Agents keep the worklog current
What an agent changes through the MCP tools SHALL be recorded in the item's worklog under the agent's name. `board_get` SHALL return the worklog (notes and events, newest first), `board_update` SHALL link documents, and `board_note` SHALL write a worklog entry with an optional title. The server's instructions and the generated guide SHALL tell agents to write an entry for what the tools cannot see, and the guide SHALL give the `events.log` line format for agents that edit files by hand.

#### Scenario: An agent moves an item
- **WHEN** an agent named Codex calls `board_update` with status In Review
- **THEN** the item's worklog has a status event by Codex

#### Scenario: An agent logs a command
- **WHEN** an agent calls `board_note` with only a body that starts "Ran the migration against dev"
- **THEN** a note titled "Ran the migration against dev" is written and `board_get` lists it in the worklog

#### Scenario: An agent without the tools
- **WHEN** an agent reads the guide in a session where the tools are not loaded
- **THEN** the guide tells it to append `yyyy-MM-ddTHH:mm:ss | <who> | <kind> | <text>` lines to `events.log`
