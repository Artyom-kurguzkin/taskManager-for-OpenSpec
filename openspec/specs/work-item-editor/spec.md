# work-item-editor Specification

## Purpose
Shows one work item with all of its parts and lets the user edit its header fields, description and subtasks in place, writing straight to the change's files.

## Requirements

### Requirement: Work item page
Opening a work item SHALL show its number as `#wi<id>`, title, linked features, contributors, status, priority and label, then its lists as tiles of equal rank: Description, Subtasks (with the items it waits on), Waiting on this (only when another item waits on it), Linked commits, Linked tests and Change log. Features from the change's specs/ folder SHALL show with a dashed outline and text readable in both themes.

#### Scenario: Open from board
- **WHEN** the user clicks card #wi123
- **THEN** the work item page for #wi123 opens with the Description tile focused and the other tiles compact

#### Scenario: Waiting on this
- **WHEN** #wi107 waits on #wi123
- **THEN** #wi123's page has a "Waiting on this" tile listing #wi107, and an item nothing waits on has no such tile

### Requirement: Edit header fields
Title, features, contributors, status, priority and label SHALL be editable in place. Each edit SHALL be saved when the control loses focus or a value is picked. The title SHALL be stored in `x-rtm.title` and the label in `x-rtm.label` (values: bug, feature, enhancement, or none). Contributors SHALL be stored in `x-rtm.contributors`; while that list is absent, the git commit authors of the change SHALL stand in for it. Every contributor SHALL be removable, and "+ add" SHALL offer everyone who has ever committed to the repository (any branch), people already on other items and the current user, and SHALL accept any typed name. Commit authors whose names differ only in case and `-`, `_` or `.`, or whose emails differ only in case, SHALL be listed once, under the name they committed with most.

#### Scenario: Change status
- **WHEN** the user picks Blocked in the status control
- **THEN** `x-rtm.status` becomes Blocked and returning to the board shows the card under Blocked

#### Scenario: Remove a manual feature
- **WHEN** the user removes Security, which is listed in `x-rtm.features`
- **THEN** it is removed from `x-rtm.features`, while features from delta specs show no remove control

#### Scenario: Commit author stays
- **WHEN** Alice has commits in a change's folder and its `x-rtm.contributors` is absent
- **THEN** Alice shows as a contributor, with a remove control like everyone else

#### Scenario: Remove yourself
- **WHEN** the user removes the only contributor, who has commits on the change
- **THEN** `x-rtm.contributors` becomes `[]` and the item shows no contributors

#### Scenario: Add someone new
- **WHEN** the user opens "+ add", types "Zed" and presses Enter
- **THEN** Zed is added to `x-rtm.contributors`

#### Scenario: Offer the repo's committers
- **WHEN** Alice, Bob and Carol have commits anywhere in the repository and the user opens "+ add" on an item without them
- **THEN** Alice, Bob and Carol are offered

#### Scenario: Same person once
- **WHEN** someone committed as "Ann Lee" and, more often, as "ann-lee" under another email
- **THEN** "+ add" lists only "ann-lee"

### Requirement: Edit description
Clicking the description SHALL switch it to a markdown editor over the body of proposal.md (everything after the title heading). Leaving the editor SHALL save it.

#### Scenario: Save description
- **WHEN** the user edits the description and clicks elsewhere
- **THEN** proposal.md contains the new text and its title heading is unchanged

### Requirement: Manage subtasks
The subtasks panel SHALL list tasks.md checkboxes with their numbers and group headings. The user SHALL be able to tick or untick a subtask, to add one at the end of the last group, numbered after the last task, and to change a subtask's text by clicking it: Enter or leaving the field saves only the text, keeping its box and number, and Esc cancels. Blank text SHALL be refused.

#### Scenario: Add a subtask
- **WHEN** the last task is `- [ ] 2.3 …` and the user adds "Write docs"
- **THEN** tasks.md gains the line `- [ ] 2.4 Write docs` under group 2

#### Scenario: Edit a subtask
- **WHEN** the user clicks the text of `- [x] 1.1 Add /health route`, types "Add /healthz route" and presses Enter
- **THEN** tasks.md has `- [x] 1.1 Add /healthz route` and nothing else changes

#### Scenario: Cancel an edit
- **WHEN** the user changes a subtask's text and presses Esc
- **THEN** the text and tasks.md stay as they were

### Requirement: Move subtasks to a new work item
Selecting one or more subtasks by Shift-clicking (or Ctrl/⌘-clicking) their text SHALL offer to move them into a new work item ("Make it a work item" for one, "Move into a separate work item" for several). This SHALL create a new change holding those tasks (renumbered from 1.1), with the same features and contributors, remove them from the source, and renumber the tasks left in the source. A single moved subtask's text SHALL become the new item's title; several moved together SHALL be titled "Split from #wi<id>".

#### Scenario: Split out
- **WHEN** the user selects subtasks 1.4 and 1.5 of #wi123 and moves them
- **THEN** a new work item "Split from #wi123" with those two subtasks exists, #wi123 keeps 1.1–1.3, and both change logs mention the split

#### Scenario: One subtask
- **WHEN** the user selects only subtask 1.3 "Ping test in suite 543" of #wi123 and picks "Make it a work item" or "Make it a dependency"
- **THEN** a new work item titled "Ping test in suite 543" holds that subtask as 1.1 and #wi123 keeps the rest

### Requirement: Archived item editing
Archived (Completed) work items SHALL show editable controls for all metadata fields except status. The status SHALL display as a read-only "Completed" chip. Title, label, priority, features, contributors, description, subtasks, linked commits and linked tests SHALL all be editable in place, writing changes to the archived change's `x-rtm` block.

#### Scenario: Edit archived label
- **WHEN** the user picks "bug" in the label control of archived #wi124
- **THEN** `x-rtm.label` becomes bug and the change is saved

#### Scenario: Status locked
- **WHEN** the user views archived #wi124
- **THEN** the status field shows "Completed" without a dropdown picker

### Requirement: Navigation and affordances
Esc and a `←` back button on the card SHALL return to the previous screen, unless a text field has focus, in which case Esc only leaves the field. Every editable element SHALL show a dashed outline on hover and on keyboard focus.

#### Scenario: Esc from page
- **WHEN** the user presses Esc on a work item page opened from the board
- **THEN** the board shows again with its previous grouping and scroll position

#### Scenario: Close a page
- **WHEN** the user clicks the `←` button on a work item or note page
- **THEN** the previous screen shows again, the same as pressing Esc

### Requirement: Hide only from the board
The work item page SHALL NOT offer a control to hide an item; hiding is only available from the board's selection bar. A hidden item's page SHALL offer "Restore", which clears `x-rtm.hidden`.

#### Scenario: Hide only from the board
- **WHEN** the user opens a visible item's page
- **THEN** no hide or delete control is shown

#### Scenario: Restore from the page
- **WHEN** the user opens a hidden item's page and clicks "Restore"
- **THEN** `x-rtm.hidden` is cleared and the item shows on the board again

### Requirement: Focus a tile
On the desktop one tile SHALL always be focused: it fills the wide column and offers all its controls. The other tiles SHALL be compact previews stacked beside it, each showing its count and its last three entries. Clicking a compact tile SHALL focus it, and the tiles SHALL slide to their new places. In a narrow window (860 pixels or less) the tiles SHALL stack in one column and the page SHALL open with every tile minimised; tapping a tile expands it, and tapping its header minimises it again.

#### Scenario: Focus subtasks
- **WHEN** the user clicks the compact Subtasks tile
- **THEN** it becomes the large tile listing every subtask with its checkbox, and the description becomes a compact preview

#### Scenario: Compact preview
- **WHEN** a work item has 25 subtasks and its Subtasks tile is compact
- **THEN** the tile shows "/25" in its count and three entries

#### Scenario: Narrow window opens minimised
- **WHEN** the work item page opens 600 pixels wide
- **THEN** every tile is a compact preview; tapping Subtasks expands it, and tapping its header minimises it again

### Requirement: Compact header in small windows
The `#wi` number, the title and the back button SHALL share one row at every window width, with the back button at the right end. In windows 760 pixels wide or less, the header, its fields and the tile titles SHALL use a smaller type size and tighter spacing. The rendered description SHALL not show blank lines between its paragraphs, headings and lists beyond their normal spacing.

#### Scenario: Narrow window
- **WHEN** the work item page is 600 pixels wide
- **THEN** #wi24, its title and the back button sit on one row, and no part of the page scrolls sideways
