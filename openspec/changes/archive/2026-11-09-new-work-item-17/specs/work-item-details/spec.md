## ADDED Requirements

### Requirement: Split view
On a window at least 1400px wide, an open work item SHALL render beside the list: the list view on the left with the open item's row marked as current, the work item page on the right. Clicking a row in that list SHALL open the item on the right while the list keeps its scroll position and filters. The item page SHALL have a toggle that hides the list; the choice SHALL be remembered. Below that width only the item SHALL show.

#### Scenario: Open the next item
- **WHEN** #wi123 is open beside the list and the user clicks the row of #wi107
- **THEN** #wi107 shows on the right, its row is marked, and the list has not scrolled

#### Scenario: Hide the list
- **WHEN** the user clicks the split toggle and reopens the app
- **THEN** the item fills the window and the list stays hidden until the toggle is clicked again

### Requirement: Item in a separate window
The split menu SHALL offer "Beside the list", "Item only" and "Separate window" (only the last two on a narrow window). "Separate window" SHALL open the item in a second window titled "#wi<N> <title>" and return the main window to the board. That window SHALL show only the work item page, without the side menu, the split menu or the back button, and SHALL never write the settings file; edits made in either window SHALL show in the other.

#### Scenario: Move the item out
- **WHEN** #wi93 is open and the user picks Separate window from the split menu
- **THEN** a second window shows #wi93 alone, the main window shows the board, and a title edited in the second window appears in the main window
