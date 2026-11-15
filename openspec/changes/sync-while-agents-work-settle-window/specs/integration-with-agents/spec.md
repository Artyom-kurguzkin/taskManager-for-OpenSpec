## ADDED Requirements

### Requirement: No direct edits of board files
The repository's PreToolUse hook SHALL deny Edit, Write, MultiEdit and NotebookEdit on a path under `openspec/changes/` of this repository, and a Bash command that writes there, with the message "Board files are written through the board_* tools (repo-tasks MCP), not edited directly."; it SHALL allow everything when the environment variable `RTM_ALLOW_DIRECT_EDIT` is `1`.

#### Scenario: An agent edits a board file by hand
- **WHEN** an agent calls Edit on `openspec/changes/some-item/proposal.md`
- **THEN** the call is denied with the message pointing to the board_* tools

#### Scenario: The user allows it
- **WHEN** `RTM_ALLOW_DIRECT_EDIT=1` is set and the same call is made
- **THEN** the hook lets it through

### Requirement: Agent badge on the card and the list row
A board card and a list row of a work item that has `agentBy` and `agentAt` in its x-rtm and that the user has not reviewed since SHALL show a muted badge "<agentBy>, <N> min ago" under one hour of the stamp, "<agentBy>, <N> h ago" under 24 hours, computed against the page's clock; the badge SHALL be hidden when the stamp is 24 hours old or older. The badge is display only and SHALL NOT write a file or hold a lease.

#### Scenario: An agent wrote three minutes ago
- **WHEN** an item's agentAt is three minutes before now
- **THEN** its card and its list row show "Claude, 3 min ago"

#### Scenario: A day later or reviewed
- **WHEN** the stamp is a day old, or the user has reviewed the item since the stamp
- **THEN** the badge is not shown
