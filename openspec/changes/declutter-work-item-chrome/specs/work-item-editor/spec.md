## MODIFIED Requirements

### Requirement: Navigation and affordances
Esc and a `←` back button on the card SHALL return to the previous screen, unless a text field has focus, in which case Esc only leaves the field. Every editable element SHALL show a dashed outline on hover and on keyboard focus.

#### Scenario: Esc from page
- **WHEN** the user presses Esc on a work item page opened from the board
- **THEN** the board shows again with its previous grouping and scroll position

#### Scenario: Close a page
- **WHEN** the user clicks the `←` button on a work item or note page
- **THEN** the previous screen shows again, the same as pressing Esc

## ADDED Requirements

### Requirement: Hide only from the board
The work item page SHALL NOT offer a control to hide an item; hiding is only available from the board's selection bar. A hidden item's page SHALL offer "Restore", which clears `x-rtm.hidden`.

#### Scenario: Hide only from the board
- **WHEN** the user opens a visible item's page
- **THEN** no hide or delete control is shown

#### Scenario: Restore from the page
- **WHEN** the user opens a hidden item's page and clicks "Restore"
- **THEN** `x-rtm.hidden` is cleared and the item shows on the board again
