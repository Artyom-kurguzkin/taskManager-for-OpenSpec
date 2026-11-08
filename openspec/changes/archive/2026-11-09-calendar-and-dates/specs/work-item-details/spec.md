## ADDED Requirements

### Requirement: Start and due dates
A work item SHALL have an optional start date (`x-rtm.started`) and due date (`x-rtm.due`), both `yyyy-MM-dd`. The first move to In Progress SHALL stamp the start date with today when it is empty, and a start date already there SHALL be kept. The work item page SHALL edit both with one "dates" range control: the first day clicked is the start, the second the due date (picked backwards they swap); Clear removes both.

#### Scenario: Started on first move
- **WHEN** #wi107 with no start date moves to In Progress, back to Planned and to In Progress again a week later
- **THEN** `started` is the day of the first move

#### Scenario: Pick a range and clear it
- **WHEN** the user opens the dates control, clicks 10 November and then 14 November, and later presses Clear
- **THEN** `.openspec.yaml` first has `started: 2026-11-10` and `due: 2026-11-14` and the control reads "10 Nov – 14 Nov", then neither key
