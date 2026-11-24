## ADDED Requirements

### Requirement: Split panes in a window
Alt+\ SHALL split the active pane of the window: a new pane to its right shows the same page and becomes active, up to four panes side by side at equal widths. Each pane SHALL show any page with its own address. Below 1400px only the active pane SHALL show.

#### Scenario: Split
- **WHEN** the board is open and the user presses Alt+\
- **THEN** two panes show the board and the second is active

### Requirement: Closing a pane
Ctrl+W SHALL close the active pane and make its neighbour active; with one pane it SHALL close nothing and never the window. A click or focus in a pane SHALL make it active.

#### Scenario: Close down to one
- **WHEN** the window has two panes and the user presses Ctrl+W twice
- **THEN** the first press leaves one pane and the second closes nothing

### Requirement: Items open beside the list
Opening a work item from a board or list pane while the window is split SHALL open it in the neighbouring pane: the right one, or the left one from the last pane.

#### Scenario: List beside an item
- **WHEN** the window is split and the user clicks the card of #wi107 in the left pane
- **THEN** #wi107 opens in the right pane and the left pane still shows the board

### Requirement: A new full window
Alt+Win+\ SHALL open one more full window of the app in the same process, on the active pane's page, with its own side menu, panes and dialogs. Agents SHALL keep the one agent endpoint. Closing that window SHALL close only it; closing the main window SHALL end the app.

#### Scenario: Open a second window
- **WHEN** #wi93 is open and the user presses Alt+Win+\
- **THEN** a second window shows #wi93 with the side menu, and an agent's call still answers on the same port

## MODIFIED Requirements

### Requirement: Item in a separate window
The work item page SHALL have a "Separate window" button. It SHALL open the item in a second window titled "#wi<N> <title>" and return the main window to the board. That window SHALL show only the work item page, without the side menu, the separate-window button or the back button, and SHALL never write the settings file; edits made in either window SHALL show in the other.

#### Scenario: Move the item out
- **WHEN** #wi93 is open and the user clicks Separate window
- **THEN** a second window shows #wi93 alone, the main window shows the board, and a title edited in the second window appears in the main window

### Requirement: Worklog block in the side menu
While a worklog is open in the active pane the side menu SHALL show a Worklog block first, before the list's organise, sort and display blocks: filters for Notes, Events, Links and Subtasks, a who picker, and the actions Add note, Link document, Link test, Link commit and Add subtask. The block SHALL go when the worklog closes or another pane becomes active, and each pane's worklog SHALL keep its own filters. Link test and Link commit SHALL be switched off, saying why, when there are no test runs or no git.

#### Scenario: Filter the feed
- **WHEN** the user unticks Events in the Worklog block
- **THEN** status changes and link events leave the feed and the notes stay; ticking it brings them back

#### Scenario: Split view
- **WHEN** the board is in Tab 1 and a work item in Tab 2, and the user clicks into Tab 1
- **THEN** the side menu shows no Worklog block; clicking into Tab 2 brings it back above the list's blocks

## REMOVED Requirements

### Requirement: Split view
**Reason**: Replaced by split panes in a window (Alt+\), where any page can sit beside any other.
**Migration**: Press Alt+\ on the board and open the item from the left pane; it opens in the right one.
