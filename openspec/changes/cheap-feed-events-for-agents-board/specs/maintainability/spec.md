## ADDED Requirements

### Requirement: Agents log events cheaply

`board_log` SHALL accept a text of one or more lines up to 4,000 characters and an optional `kind` from the event standard (started, found, decision, tests-red, tests-green, verified, blocked, done; without one the event is `log`), take no revision, and answer with one line. A kind outside the standard SHALL be refused with the standard listed. Text over 4,000 characters SHALL become a note by the same call.

#### Scenario: An event with a kind

- **WHEN** an agent calls `board_log` with a multi-line text and `kind: tests-green`
- **THEN** the event is written with its line breaks kept and the answer is one short line

#### Scenario: A kind outside the standard

- **WHEN** an agent calls `board_log` with `kind: progress`
- **THEN** the call is refused, nothing is written, and the answer lists the eight kinds of the standard

#### Scenario: Long text

- **WHEN** an agent calls `board_log` with a text over 4,000 characters
- **THEN** the same call writes it as a note on the item instead of an event, and the answer says so in one line

### Requirement: The worklog labels events by kind

The worklog SHALL show each event's kind as a label, and SHALL emphasise a decision event the way it emphasises a decision note.

#### Scenario: Kinds in the worklog

- **WHEN** an item has started, tests-green, decision and done events
- **THEN** each shows its kind as a label, and the decision event is emphasised
