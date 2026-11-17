## ADDED Requirements

### Requirement: Side menu without checkboxes
Every on/off option in the side menu SHALL be a pill toggle: filled when on, underlined text when off. The top row SHALL be a collapse icon only. Under Organise, "sort by" SHALL come first. "show all hidden" SHALL sit under the Hidden header.

#### Scenario: Toggle an option
- **WHEN** the user clicks the "compact cards" pill
- **THEN** it turns from underlined text to a filled pill and the cards compact

### Requirement: Menu in the work item context
While a work item fills the window the menu SHALL hide the projects, layout and new-item rows, and show them again on the board and in split view. The Worklog block SHALL have one "+ Add entry" button whose menu offers Note, Decision, Bug, Subtask, Link document, Link test and Link commit.

#### Scenario: Add a bug
- **WHEN** the user clicks + Add entry and picks Bug
- **THEN** a bug entry opens in the feed's editor

### Requirement: Collapsible menu sections
The expanded side menu SHALL group the view's options in three sections, Organise (sort, columns or groups, rows or subgroups, colour, legend), Filters (contributor, period, changed) and Hidden. Each header SHALL be a click target with a chevron that folds the section, and every section SHALL be folded until the user opens it. The Worklog block SHALL stay as it is.

#### Scenario: Sections start folded and are remembered
- **WHEN** the user expands the menu for the first time and opens Filters
- **THEN** Organise and Hidden stay folded, and Filters is open again the next time the menu is shown, in any project

### Requirement: Folded sections summarise and are remembered per user
Which sections are open SHALL be remembered per user in the settings, not per project. A folded header SHALL show a short summary of what is set inside, so an active filter is visible without opening the section.

#### Scenario: A folded header carries an active filter
- **WHEN** the contributor filter is set to Claude while Filters is folded
- **THEN** the header reads "Filters · contributor: Claude"

### Requirement: Feed day filter
In the Worklog block, Shift-click on a day in the Days list SHALL toggle it into a selection shown as yellow pills, and the feed SHALL then show only the selected days. A plain click SHALL still jump to the day. A "Show all days" link SHALL appear while days are selected and clear the selection. The Days list SHALL keep every day with its count, and the selection SHALL NOT be saved.

#### Scenario: Show only two days
- **WHEN** the user Shift-clicks today and an older day in the Days list
- **THEN** both turn into yellow pills and the feed shows only those two days

#### Scenario: Show all days
- **WHEN** the user clicks "Show all days"
- **THEN** the pills go back to plain buttons and the feed shows every day

### Requirement: New work item in the rail, and a plain view switcher
The collapsed rail SHALL have a "+" button, labelled "New work item", under the view button that does what the expanded "New work item" does. The view switcher SHALL be a plain button showing the current view's icon and name, not a yellow pill, and the "New work item" button SHALL be filled red, expanded and collapsed, with text that reads on the red in light and dark, and a slightly darker fill when pointed at.

#### Scenario: Add from the rail
- **WHEN** the menu is collapsed and the user clicks the + button
- **THEN** a work item is created and opened, as with the expanded New work item button

### Requirement: Examine a work item from the menu
With the side menu expanded, a plain click on a work item's card or list row SHALL examine it instead of opening it. Only one work item SHALL be examined at a time, and the menu SHALL show its metadata and counts in place of Organise, Filters and Hidden.

#### Scenario: Examine, then open
- **WHEN** the menu is expanded and the user clicks a card once
- **THEN** the card has a yellow shadow, the menu shows its feature, status, priority, label, contributors, dates and counts of notes, bugs, open subtasks and dependencies, and the item is not opened
- **WHEN** the user clicks the same card again
- **THEN** the item's page opens

#### Scenario: Dependents and dependencies are marked
- **WHEN** an item is examined that another item waits on and that itself waits on a third
- **THEN** the waiting item has a red shadow and the third a blue one

#### Scenario: Click elsewhere
- **WHEN** an item is examined and the user clicks anywhere that is not a work item, collapses the menu or navigates away
- **THEN** no item is examined and the menu shows its sections again

#### Scenario: Collapsed menu, selection
- **WHEN** the menu is collapsed and the user clicks a card, or the user Shift-clicks a card with the menu expanded
- **THEN** the card opens, or is selected, as before

### Requirement: Feed filter pills are a quick filter
In the Worklog block, while every kind of entry is on, a click on one kind's pill SHALL show only that kind. While some kinds are off, a click SHALL add that kind or take it out. Taking out the last kind SHALL bring every kind back, so the feed is never empty. A small "all" link SHALL appear while some kinds are off and bring every kind back at once.

#### Scenario: Quick filter, then add
- **WHEN** every kind is on and the user clicks Bugs
- **THEN** only Bugs stays filled and the feed shows bug entries only
- **WHEN** the user then clicks Notes
- **THEN** Notes joins Bugs in the feed

#### Scenario: Never empty
- **WHEN** only Notes is on and the user clicks Notes
- **THEN** every kind is on again

#### Scenario: Restore with all
- **WHEN** some kinds are off and the user clicks "all"
- **THEN** every kind is on and the link goes

### Requirement: Subtasks in the examine panel
The examine panel SHALL list the examined work item's subtasks under their groups: open subtasks as rows with their numbers, done ones folded behind a "N done" link that unfolds them. The Work items / Subtasks unit switch SHALL NOT be in the Filters section; the panel SHALL have an "All subtasks on the board" link that turns the board's subtask view on, and Filters SHALL show the switch only while that view is on, as the way back.

#### Scenario: Look at the subtasks of one item
- **WHEN** a work item with two open and two done subtasks is examined
- **THEN** the panel shows the two open ones with their numbers and "2 done", and the rest of the board stays visible

### Requirement: Split a subtask from the examine panel
Clicking, or starting to drag, an open subtask row in the examine panel SHALL open a dialog asking whether to make it a work item of its own or a work item this one waits on, with Cancel. Cancel SHALL change nothing. "Of its own" SHALL move the subtask into a new work item; "this one waits on" SHALL do the same and make the new item a dependency of the examined one, which becomes Blocked. Both SHALL be logged on both items, and the panel SHALL refresh.

#### Scenario: Make a subtask a dependency
- **WHEN** the user clicks an open subtask in the panel and picks "Make it a work item this one waits on"
- **THEN** the subtask leaves the item, a new work item with its text exists, the examined item waits on it, and the panel no longer lists the subtask

### Requirement: One alignment for the menu's pressable rows
In the expanded side menu the view switcher, New work item, the section headers, Add entry and Settings SHALL share one left edge, one height, one inner padding and one radius, with their icons (the chevron for a header) in one column.

#### Scenario: Aligned rows
- **WHEN** the menu is expanded
- **THEN** every such row starts at the same left edge, is the same height, and its icon is in the same column
