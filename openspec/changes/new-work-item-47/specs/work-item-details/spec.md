## ADDED Requirements

### Requirement: The collapsed menu adds worklog entries from an icon
While a work item's worklog is open and the side menu is collapsed, the rail SHALL show an Add entry icon, the same size and centring as the rail's other icons, that opens the same list of entries as the expanded menu's Add entry button.

#### Scenario: Adding a note from the collapsed rail
- **WHEN** the side menu is collapsed on a work item and the user clicks the Add entry icon
- **THEN** the menu lists Note, Decision, Bug, Subtask, Link document, Link test and Link commit, and picking Note opens the note editor

### Requirement: The collapsed menu stays on screen
On a desktop-wide window the collapsed side menu SHALL stay in the layout as a narrow column beside the page, never hidden off the window's edge.

#### Scenario: Collapsing the menu
- **WHEN** the user collapses the side menu
- **THEN** the rail stays at the left edge and the page starts at its right edge
