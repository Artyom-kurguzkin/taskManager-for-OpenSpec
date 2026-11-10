## ADDED Requirements

### Requirement: Start and due dates
A work item SHALL have an optional start date (`x-rtm.started`) and due date (`x-rtm.due`), both `yyyy-MM-dd`. The first move to In Progress SHALL stamp the start date with today when it is empty, and a start date already there SHALL be kept. The work item page SHALL edit both with one "dates" range control: the first day clicked is the start, the second the due date (picked backwards they swap); Clear removes both.

#### Scenario: Started on first move
- **WHEN** #wi107 with no start date moves to In Progress, back to Planned and to In Progress again a week later
- **THEN** `started` is the day of the first move

#### Scenario: Pick a range and clear it
- **WHEN** the user opens the dates control, clicks 10 November and then 14 November, and later presses Clear
- **THEN** `.openspec.yaml` first has `started: 2026-11-10` and `due: 2026-11-14` and the control reads "10 Nov – 14 Nov", then neither key

## ADDED Requirements

### Requirement: Transition date
A work item SHALL carry `x-rtm.moved` (`yyyy-MM-dd`): the day it last moved to its status. Creation SHALL stamp it with that day, every status change SHALL restamp it with the day of the change, and setting the same status again SHALL leave it. A change without the key SHALL read as moved on its creation date. The work item page SHALL show it as "in <status> since <date>" beside the dates, and the list SHALL have an "In status since" column after Due.

#### Scenario: Moved to In Progress
- **WHEN** #wi107, created on 1 Nov, moves to In Progress on 7 Nov
- **THEN** `moved` is 2026-11-07, the page reads "in In Progress since 7 Nov", and the list column shows 7 Nov

#### Scenario: No key yet
- **WHEN** a change created on 18 Sep has no `moved` key
- **THEN** the page reads "in <status> since 18 Sep" and the key is not written until the status changes
