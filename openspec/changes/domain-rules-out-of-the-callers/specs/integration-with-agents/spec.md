## ADDED Requirements

### Requirement: Agents and the app follow the same rules
A rule about work items, notes, features or linked tests SHALL be applied the same way whether an agent's tool call or the app triggers it: item creation and status rules, the worklog an item shows, duplicate-id repair and an item's status history.

#### Scenario: Same worklog for an agent and the item page
- **WHEN** an agent reads an item's worklog and the user opens the item page
- **THEN** both list the same entries in the same order

#### Scenario: Duplicate found by an agent
- **WHEN** an agent's call is the first to find two items with the same id
- **THEN** the later one is renumbered and given the same renumbered note the app writes
