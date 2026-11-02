## ADDED Requirements

### Requirement: Start and due dates
A work item SHALL have an optional start date (`x-rtm.started`) and due date (`x-rtm.due`), both `yyyy-MM-dd`. The first move to In Progress SHALL stamp the start date with today when it is empty, and a start date already there SHALL be kept. The work item page SHALL show both in native date fields in the header; picking a date saves it at once, clearing the field removes the key.

#### Scenario: Started on first move
- **WHEN** #wi107 with no start date moves to In Progress, back to Planned and to In Progress again a week later
- **THEN** `started` is the day of the first move

#### Scenario: Pick and clear
- **WHEN** the user picks 5 November in the due field, then clears it
- **THEN** `.openspec.yaml` first has `due: 2026-11-05`, then no `due` key
