## ADDED Requirements

### Requirement: An action shows its result before it is saved
The board and the item page SHALL apply a status change, a drop and a subtask tick to what is shown and render before the files are written, SHALL write on a background thread, and on a failed write SHALL restore what was shown and tell the user.

#### Scenario: Status pill while a lock is held
- WHEN the user changes a status pill while another process holds the change folder's lock
- THEN the new status is shown at once, the window stays responsive, and the file is written when the lock is released

#### Scenario: Arrow key writes no file
- WHEN the user moves a card with an arrow key
- THEN the list is re-sorted in memory and rendered once, no repository folder is read, and the board settings are saved once after the presses stop

### Requirement: A page does not reload what it just wrote
A page SHALL skip the change notification that carries only the version of its own write, and SHALL still reload on a change from an agent, git or the file watcher.

#### Scenario: Own echo
- WHEN a page's write bumps the store version and the Changed event fires for that version
- THEN the page runs no second Load and bumps no render version
