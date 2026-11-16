## MODIFIED Requirements

### Requirement: One process serves windows and agents
The desktop app SHALL host the agents' endpoint itself, in its own process, and "Separate window" SHALL open a window of that same process. The `--mcp` relay SHALL stay a separate process that forwards to the app and, with the app closed, tells the agent to open it. A second app instance SHALL leave the first one serving.

#### Scenario: Agent writes while the window is open
- **WHEN** an agent writes a note through the relay
- **THEN** the open window shows it without a manual refresh, and a later edit made in the window on an older read is refused as stale rather than overwriting it

#### Scenario: Separate window
- **WHEN** the user opens an item in a separate window
- **THEN** a second window of the same process opens, with its own toasts and dialogs, and no other process starts
