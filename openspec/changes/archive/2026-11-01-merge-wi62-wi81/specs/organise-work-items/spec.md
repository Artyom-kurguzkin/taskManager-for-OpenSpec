## MODIFIED Requirements

### Requirement: Complete by archiving
Dropping a card on Completed SHALL ask for confirmation and then archive the change with the OpenSpec CLI. Before the CLI runs, the app SHALL refresh the change's delta specs: every MODIFIED requirement SHALL get, appended verbatim from the current main spec, the scenarios it lacks, so a requirement that another archive changed since the delta was written never makes the archive fail. Cards SHALL NOT be dragged out of Completed.

#### Scenario: Archive
- **WHEN** the user drops #wi123 on Completed and confirms
- **THEN** the change folder moves to `openspec/changes/archive/<date>-<name>/`, its delta specs merge into `openspec/specs/`, and the card shows under Completed

#### Scenario: Stale modified requirement
- **WHEN** #wi107 modifies "Send money" with one scenario and the main spec has since gained "Fee shown" and "Limit"
- **THEN** both are appended to #wi107's block before the archive, the archive succeeds and the main spec keeps all three with #wi107's text

#### Scenario: Archive fails
- **WHEN** the CLI reports an error, for example unfinished tasks or invalid deltas
- **THEN** the card returns to its column and the CLI's message is shown
