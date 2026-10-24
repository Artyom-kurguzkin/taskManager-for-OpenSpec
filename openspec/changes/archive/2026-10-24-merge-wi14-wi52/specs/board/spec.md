## MODIFIED Requirements

### Requirement: Hide selected work items
The selection action bar SHALL offer "Delete (hide)", enabled with one or more cards selected, which hides every selected item. This is the only way to hide an item; the work item page offers no hide control of its own. Hiding SHALL clear the selection.

#### Scenario: Hide several
- **WHEN** the user selects three cards and picks "Delete (hide)"
- **THEN** all three items gain `x-rtm.hidden: true`, disappear from the board unless "show hidden" is on, and the selection clears

### Requirement: Board and list views
The side menu's layout dropdown SHALL switch between Board and List; it is one control, not two buttons. The view options (group by, sort by, contributor filter, work items or subtasks, compact cards, show hidden) SHALL be in the expanded side menu. "Compact cards" SHALL show only the title band. The collapsed side menu SHALL also have a compact-cards button, highlighted while compact cards are on and disabled outside the board view.

#### Scenario: Switch to list
- **WHEN** the user opens the layout dropdown in the side menu and picks List
- **THEN** the list table replaces the board and the dropdown reads "List"

#### Scenario: Compact cards
- **WHEN** the user ticks "compact cards"
- **THEN** each card shows only its coloured title band

#### Scenario: Compact from the collapsed menu
- **WHEN** the side menu is collapsed and the user clicks the compact-cards button on the board
- **THEN** the cards show only their title bands, and the button stays highlighted until clicked again
