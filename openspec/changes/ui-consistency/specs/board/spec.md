## ADDED Requirements

### Requirement: One vocabulary on every screen
A feature SHALL be a dashed chip wherever it is shown (a card, a list row, the item page); a status, label or priority SHALL be a pill filled with its colour wherever it can be set (a list row, the item page); and every control that adds something ("+ Add item", "+ feature", "+ link commit", "+ Add entry") SHALL be the same quiet text button.

#### Scenario: Feature chips
- **WHEN** a work item with the feature "Board" is shown on the board and in the list
- **THEN** both show "Board" as a dashed chip

#### Scenario: Pills
- **WHEN** a work item's status is shown in the list and on its page
- **THEN** both show it as a pill filled with the status colour, and clicking either opens the same list of statuses, navigable with the arrow keys
