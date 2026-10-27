## ADDED Requirements

### Requirement: Numbers used twice resolve themselves
When two changes carry the same work item number, the app SHALL give every change but one the next free number as soon as it sees them, SHALL say so in a toast, and SHALL add a change-log note to each renumbered change. The change that keeps the number SHALL be an archived one first, then the earliest created, then the first folder name, so every copy of the repository resolves the same way. No warning about a number used twice SHALL be shown.

#### Scenario: A copied folder
- **WHEN** a folder carrying #wi123 (created 12 Sep) is copied beside it with created 20 Oct, and the app sees the repository
- **THEN** the copy becomes #wi126 (the next free number, archived and unnumbered changes included), a toast says "#wi123 was used twice: second-check is now #wi126", and #wi126 has a note "Renumbered from #wi123"

#### Scenario: An archived change keeps its number
- **WHEN** an archived change and an active one both carry #wi123
- **THEN** the active one is renumbered, whatever was created first
