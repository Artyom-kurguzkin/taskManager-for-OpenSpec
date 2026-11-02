# workspace Specification

## Purpose
TBD - created by archiving change multi-project-board. Update Purpose after archive.

## Requirements

### Requirement: Several repositories open at once
Every repository open as a tab SHALL be open in the store at the same time, each with its own content version and file watcher, so that reading another tab costs no re-parse and an outside edit in any tab is noticed. The app SHALL be able to read or write another open repository and come back to the active one.

#### Scenario: Two tabs
- **WHEN** two repositories are open as tabs and the second is active
- **THEN** the first can be read without changing the active one, and switching between them parses nothing again

#### Scenario: A single open
- **WHEN** a repository is opened on its own (not as a tab)
- **THEN** the others are closed
