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
Title, features, contributors, status, priority and label SHALL be editable in place. Every linked feature SHALL have a remove control; removing one that comes from the change's specs/ folder SHALL record it in `x-rtm.unlinkedFeatures` and leave the spec files alone, and "+ add" SHALL offer it again. Each edit SHALL be saved when the control loses focus or a value is picked. The title SHALL be stored in `x-rtm.title` and the label in `x-rtm.label` (values: bug, feature, enhancement, or none). Contributors SHALL be stored in `x-rtm.contributors`; while that list is absent, the git commit authors of the change SHALL stand in for it. Every contributor SHALL be removable, and "+ add" SHALL offer everyone who has ever committed to the repository (any branch), people already on other items and the current user, and SHALL accept any typed name. Commit authors whose names differ only in case and `-`, `_` or `.`, or whose emails differ only in case, SHALL be listed once, under the name they committed with most.

#### Scenario: Change status
- **WHEN** the user picks Blocked in the status control
- **THEN** `x-rtm.status` becomes Blocked and returning to the board shows the card under Blocked

#### Scenario: Remove a manual feature
- **WHEN** the user removes Security, which is listed in `x-rtm.features`
- **THEN** it is removed from `x-rtm.features`

#### Scenario: Unlink a spec feature
- **WHEN** the user removes Availability, which comes from the change's specs/ folder
- **THEN** it is added to `x-rtm.unlinkedFeatures`, disappears from the item and its board column, and the spec file is unchanged; choosing it in "+ add" removes it from that list

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

### Requirement: Note page tiles
The note page SHALL present its markdown, preview and links as tiles with coloured heading pills, like the work item page's tiles.

#### Scenario: Note page
- **WHEN** a note is opened
- **THEN** its markdown, preview and links each sit in a tile with a coloured heading pill, and the back button is the same as on the work item page

### Requirement: Split a subtask into a dependency
Selecting one or more subtasks SHALL also offer "Make it a dependency" (one) / "Move into a dependency" (several), alongside "Make it a work item". It SHALL create the new work item the same way as "Make it a work item", then add its id to the source's `x-rtm.dependsOn` and set the source's status to Blocked.

#### Scenario: Create a dependency
- **WHEN** the user selects subtask 1.3 of #wi123 and picks "Make it a dependency"
- **THEN** a new work item holds that subtask, #wi123's status becomes Blocked, and `x-rtm.dependsOn` in #wi123's `.openspec.yaml` lists the new item's id

### Requirement: Related work items under Subtasks
The Subtasks panel SHALL list, after the subtasks, each work item this one waits on ("waits on", highlighted while unresolved, struck through once In Review or Completed, with a control to remove it) and each work item waiting on this one ("blocks"), with its current status. Each SHALL open the named item's page.

#### Scenario: Waits on
- **WHEN** #wi123's `x-rtm.dependsOn` lists #wi124
- **THEN** #wi123's Subtasks panel shows a highlighted "waits on" row for #wi124 with its status, and removing it clears it from `x-rtm.dependsOn`

#### Scenario: Blocks
- **WHEN** #wi123's `x-rtm.dependsOn` lists #wi124
- **THEN** #wi124's Subtasks panel shows a "blocks" row for #wi123 with its status

### Requirement: Status locked while a dependency is unresolved
A dependency SHALL count as resolved once it is In Review or Completed (archived). A work item SHALL refuse to change status away from Blocked while any item in its `x-rtm.dependsOn` is unresolved; the refusal SHALL name the unresolved dependency. Picking Blocked again SHALL always be allowed. Once every dependency is resolved, status changes SHALL be allowed again.

#### Scenario: Refused
- **WHEN** #wi123 depends on #wi124, which is In Progress, and the user picks Planned
- **THEN** #wi123's status stays Blocked and the message names #wi124

#### Scenario: Unblocked
- **WHEN** #wi124, the only item in #wi123's `x-rtm.dependsOn`, moves to In Review or is archived
- **THEN** #wi123 can change to any status again

### Requirement: Depend on an existing item
Typing another work item's id (`#wi12`, `wi12` or `12`) in the subtask box SHALL make that item a dependency instead of adding a subtask. This item SHALL move to Blocked only when that dependency is unresolved; a dependency that is already In Review or Completed SHALL be linked without blocking. It SHALL be refused, with a message, when that item doesn't exist, is this item, is already a dependency, or already waits on this item.

#### Scenario: Wait on an existing item
- **WHEN** the user types `#wi123` in the subtask box of #wi107 and adds it
- **THEN** #wi107 gains `x-rtm.dependsOn: [123]`, becomes Blocked, lists #wi123 as "waits on" under Subtasks, and no subtask is added

#### Scenario: Already in review
- **WHEN** the user types the id of an item that is In Review
- **THEN** it is added to `x-rtm.dependsOn`, this item's status is unchanged, and a message says it doesn't block

#### Scenario: Only done dependencies
- **WHEN** every item in #wi107's `x-rtm.dependsOn` is In Review or Completed
- **THEN** their "waits on" rows under Subtasks are struck through, like done subtasks

### Requirement: Pick an item to wait on
The Subtasks tile of an active work item SHALL offer "+ wait on another work item", a list of the other active work items (number, title, status) with a filter box that matches number, title and status. Choosing one SHALL link it as a dependency exactly as typing its number does. The list SHALL leave out the item itself, items it already waits on, and items that wait on it.

#### Scenario: Choose from the list
- **WHEN** the user opens the picker on #wi107, types "health" and picks #wi123
- **THEN** #wi107 waits on #wi123, shows it under Subtasks, and is Blocked until #wi123 is in review or done

#### Scenario: No loops
- **WHEN** #wi107 waits on #wi123 and the user opens the picker on #wi123
- **THEN** neither #wi123 nor #wi107 is listed
