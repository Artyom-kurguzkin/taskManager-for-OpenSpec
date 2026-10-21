# Spec Delta

## ADDED Requirements

### Requirement: Hide selected work items
The selection action bar SHALL offer "Delete (hide)", enabled with one or more cards selected, which hides every selected item the same way the per-item "Delete (hide)" control does. Hiding SHALL clear the selection.

#### Scenario: Hide several
- **WHEN** the user selects three cards and picks "Delete (hide)"
- **THEN** all three items gain `x-rtm.hidden: true`, disappear from the board unless "show hidden" is on, and the selection clears
