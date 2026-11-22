# general-ux Specification

## Purpose
TBD - created by archiving change smooth-app. Update Purpose after archive.

## Requirements

### Requirement: The app answers at once
A change the user makes (ticking a subtask, changing a status, saving a note) SHALL be visible within 150 ms, switching a view within 150 ms, and opening a work item within 250 ms, on a board of 50 items; each SHALL redraw once. A change SHALL re-read only the folder it touched, and git history SHALL be asked once per commit, not once per question.

#### Scenario: Tick a subtask
- **WHEN** the user ticks a subtask on the item page
- **THEN** the tick shows within 150 ms in one redraw, and only that item's folder is read again

#### Scenario: Open an item
- **WHEN** the user clicks a card on the board
- **THEN** the item page is drawn within 250 ms, once

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

### Requirement: Hiding is the app's, never the repository's
The eye in the side menu, a column's "Hide column", a project's "Hide project" and the selection bar's "Delete (hide)" SHALL hide a column, a project or a work item in this app's settings, per repository, and SHALL write nothing under `openspec/`. A work item SHALL be hidden by its number within its repository, so the same number in another repository stays shown. The settings SHALL list the hidden work items (`HiddenItems`) beside the hidden projects and columns.

#### Scenario: Hide a work item from the selection bar
- **WHEN** the user selects a work item and clicks "Delete (hide)"
- **THEN** the item leaves the board and the list, the Hidden section of the side menu lists it with an eye, and no file under `openspec/` changes (the item's `.openspec.yaml` is byte for byte what it was)

#### Scenario: Restore a hidden work item
- **WHEN** the user clicks the eye beside the work item in the Hidden section
- **THEN** the item is back on the board and in the list, and the repository is still untouched

#### Scenario: Show all hidden
- **WHEN** "show all hidden" is on
- **THEN** a work item hidden in the app is drawn dashed like any hidden item, and a column or project hidden in the app stays in view with "(hidden)" after its name, so a hide is visible whatever the switch says; switching it off removes them

### Requirement: A resolution still hides, and says so
A work item an agent ended without doing it (`resolution`: cancelled, duplicate of #wiN, superseded by #wiN) SHALL stay hidden through `x-rtm.hidden`, which only a resolution (or an agent's `hidden`) writes. The Hidden section SHALL list such an item under the same heading, labelled by its resolution, and its page SHALL say it is hidden by that resolution.

#### Scenario: A cancelled item
- **WHEN** an agent resolves a work item as cancelled
- **THEN** it leaves the board, the Hidden section lists it as "#wiN title" labelled "Cancelled", and "show all hidden" draws it dashed

### Requirement: An old hidden flag counts as hidden in the app
A work item with `x-rtm.hidden` and no resolution note (hidden by an earlier version of the app) SHALL be treated as hidden in the app: the Hidden section SHALL list it as "#wiN title" with an eye like any item hidden in the app, and its page SHALL show the app-hidden banner and a Restore button. Restoring it SHALL clear the flag in its `.openspec.yaml` (the only write, because the flag is already there) and SHALL add nothing to the settings.

#### Scenario: Restore an item hidden by an old flag
- **WHEN** the user clicks the eye beside such an item in the Hidden section, or Restore on its page
- **THEN** `x-rtm.hidden` is gone from its `.openspec.yaml`, the settings list no hidden item, and the item is back on the board

#### Scenario: A resolution is not an old flag
- **WHEN** an item has `x-rtm.hidden` and a resolution note
- **THEN** it keeps the resolution behaviour: labelled by the resolution, no eye, its page names the resolution

### Requirement: The item page says where a hidden item comes back
The page of a work item hidden in the app (or by an old flag with no resolution) SHALL show "Hidden in this app. Restore it from the side menu's Hidden section." and a Restore button that shows it again. The page of an item hidden by a resolution SHALL say which resolution hid it. No hidden-item message SHALL mention openspec.

#### Scenario: Open an item hidden in the app
- **WHEN** the user opens a work item they hid
- **THEN** the banner reads "Hidden in this app. Restore it from the side menu's Hidden section."

### Requirement: A view comes back where it was left
The board, list, calendar, analytics, settings, work item, note and document views SHALL remember, per window and per repository, how far the user had scrolled when leaving, and SHALL scroll back to that spot after the view's first render when the user returns, by the back button or from the side menu. A view never left before SHALL start at the top.

#### Scenario: Back from an item
- **WHEN** the user scrolls the list down to a row, opens the work item and goes back
- **THEN** the list is at the same scroll position, within a row

#### Scenario: Settings and analytics
- **WHEN** the user scrolls the settings page, opens the board and returns to settings
- **THEN** the settings page is at the spot it was left at, and the same holds for analytics

### Requirement: Restoring waits for the content and adds no draw
The restore SHALL wait until the view is tall enough to reach the spot, SHALL stop if the user scrolls meanwhile, and SHALL NOT cause an extra render.

#### Scenario: A list that fills after its first draw
- **WHEN** the list returns and its rows are drawn a moment after the page
- **THEN** the list ends at the saved spot once the rows are there

### Requirement: Each repository keeps its own spot
The remembered spot SHALL be kept per repository: switching to another repository SHALL restore the spot left there, and switching back SHALL restore the first one.

#### Scenario: Two repositories
- **WHEN** the list is scrolled to 1800 in one repository, the user switches to a second one and scrolls it to 700, then switches back
- **THEN** the first repository's list is at 1800 and the second's at 700 when the user returns to it

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
