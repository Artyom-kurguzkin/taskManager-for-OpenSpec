## ADDED Requirements

### Requirement: Show all hidden is one action
The side menu's "show all hidden" button SHALL be an action, not a view switch: one press restores everything the user hid and resets the filters, and nothing SHALL be drawn dashed or marked "(hidden)" afterwards. The button SHALL be disabled when there is nothing it can restore.

#### Scenario: One press restores everything hidden
- **WHEN** the user has hidden a work item, a column and a project, and presses "show all hidden" once
- **THEN** the item, the column and the project are shown again, the Hidden list is empty, the repository's hidden lists in the settings are cleared, and one change is raised

#### Scenario: Legacy hidden flags are cleared
- **WHEN** a work item carries an old x-rtm.hidden flag and has no resolution note, and the user presses "show all hidden"
- **THEN** the flag is removed from the item's file and the item is on the board

#### Scenario: Filters are reset so everything shows
- **WHEN** a contributor, due period, changed period or commit filter is set and the user presses "show all hidden"
- **THEN** the filters return to everyone, any time, any commit and no changed period

#### Scenario: A resolution keeps its item hidden
- **WHEN** a work item was hidden by an agent's resolution (cancelled, duplicate or superseded)
- **THEN** "show all hidden" leaves it hidden, it stays listed under Hidden labelled by the resolution, and it is restored only from its own page
