# Spec Delta

## ADDED Requirements

### Requirement: Open repositories as tabs
Building on "Open a repository", the side nav SHALL let the user keep several repositories open as tabs, switch between them, and close a tab. The last remaining tab SHALL NOT be closable. The set of open tabs SHALL be remembered between sessions. Only the active tab's data SHALL be loaded at a time; switching re-reads that repository's files.

#### Scenario: Open a second tab
- **WHEN** the user picks "Open another repository…" and chooses a folder with an `openspec/` directory
- **THEN** it becomes a new tab and the active one, while the previously open tab stays listed

#### Scenario: Switch tabs
- **WHEN** the user clicks a different open tab
- **THEN** the board shows that repository's work items, with that tab's own remembered board view

#### Scenario: Close a tab
- **WHEN** the user closes a tab that isn't the only one open
- **THEN** it is removed from the list, and if it was active, another open tab becomes active

#### Scenario: Can't close the last tab
- **WHEN** only one tab is open
- **THEN** it has no close control

#### Scenario: Tabs persist
- **WHEN** the app restarts
- **THEN** the same tabs are listed, with the previously active one shown

## ADDED Requirements

### Requirement: Several repositories open at once
Every repository open as a tab SHALL be open in the store at the same time, each with its own content version and file watcher, so that reading another tab costs no re-parse and an outside edit in any tab is noticed. The app SHALL be able to read or write another open repository and come back to the active one.

#### Scenario: Two tabs
- **WHEN** two repositories are open as tabs and the second is active
- **THEN** the first can be read without changing the active one, and switching between them parses nothing again

#### Scenario: A single open
- **WHEN** a repository is opened on its own (not as a tab)
- **THEN** the others are closed
