# work-item-editor Specification

## Purpose
Shows one work item with all of its parts and lets the user edit its header fields, description and subtasks in place, writing straight to the change's files.

## Requirements

### Requirement: Work item page
Opening a work item SHALL show its number as `#wi<id>`, title, linked features, contributors, status, priority and label, then two views with a button each, both always shown and the current one marked: Worklog (the default) and Linked tasks. There SHALL be no Description view: the description is the first entry of the worklog. Features from the change's specs/ folder SHALL show with a dashed outline and text readable in both themes.

#### Scenario: Open from board
- **WHEN** the user clicks card #wi123
- **THEN** the work item page for #wi123 opens on the Worklog view, with the Linked tasks button beside it, and the description is the first thing in the feed

#### Scenario: Waiting on this
- **WHEN** #wi107 waits on #wi123
- **THEN** #wi123's Linked tasks view lists #wi107 in its "Waiting on this" column, and an item nothing waits on says so there

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
Selecting one or more subtasks by Shift-clicking (or Ctrl/⌘-clicking) their text SHALL offer to move them into a new work item ("Make it a work item" for one, "Move into a separate work item" for several). This SHALL create a new change holding those tasks (renumbered from 1.1), with the same features and contributors, remove them from the source, and renumber the tasks left in the source. A single moved subtask's text SHALL become the new item's title; several moved together SHALL be titled "Split from #wi<id>". Each moved subtask SHALL be logged in both feeds, with the person who moved it: in the source item as `subtask-moved` ("moved subtask 1.4 to #wi130", the item a link) and in the new item as `made-from-subtask` ("made from subtask 1.4 of #wi123", the item a link). The same SHALL hold when the new item is made a dependency.

#### Scenario: Split out
- **WHEN** the user selects subtasks 1.4 and 1.5 of #wi123 and moves them
- **THEN** a new work item "Split from #wi123" with those two subtasks exists, #wi123 keeps 1.1–1.3, and both change logs mention the split

#### Scenario: One subtask
- **WHEN** the user selects only subtask 1.3 "Ping test in suite 543" of #wi123 and picks "Make it a work item" or "Make it a dependency"
- **THEN** a new work item titled "Ping test in suite 543" holds that subtask as 1.1 and #wi123 keeps the rest

#### Scenario: The move is in both feeds
- **WHEN** Tom moves subtask 1.3 of #wi123 into the new work item #wi130
- **THEN** the feed of #wi123 shows "moved subtask 1.3 to #wi130" by Tom, and the feed of #wi130 shows "made from subtask 1.3 of #wi123" by Tom

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
The Linked tasks view SHALL show four columns: Subtasks, Waiting on (each work item this one waits on, highlighted while unresolved, struck through once In Review or Completed, with a control to remove it and a picker to add one), Waiting on this (each work item waiting on this one) and Related (everything attached to the item, see "Related list"), each work-item row with the item's current status. Each work-item row SHALL open the named item's page. In a narrow window the columns SHALL wrap to two per row, then one, and nothing is minimised.

#### Scenario: Waits on
- **WHEN** #wi123's `x-rtm.dependsOn` lists #wi124
- **THEN** #wi123's Waiting on column shows a highlighted row for #wi124 with its status, and removing it clears it from `x-rtm.dependsOn`

#### Scenario: Blocks
- **WHEN** #wi123's `x-rtm.dependsOn` lists #wi124
- **THEN** #wi124's Waiting on this column shows a row for #wi123 with its status

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

### Requirement: Attachments on notes
A note MAY have attachments, listed in its frontmatter as `attachments:` entries: repository-relative file paths or http(s) URLs. Attaching a file SHALL copy it to `<attachments folder>/wi<item>/n<note>/<name>` in the repository the note lives in (a taken name gets `-2`, `-3`…); the attachments folder is set per project in Settings and defaults to `local/task-tracking`. The app SHALL add the folder (`local/` for the default) to that repository's `.gitignore` when it is not ignored yet, so attachments never enter git. Attaching a link SHALL accept only absolute http(s) URLs. Removing an attachment SHALL forget it and delete a copied file.

#### Scenario: Attach a screenshot
- **WHEN** the user attaches `shot.png` to note #n44 of #wi85 in a repository whose `.gitignore` has no `local/` line
- **THEN** the file is at `local/task-tracking/wi85/n44/shot.png`, `.gitignore` ends with `local/`, and the note's frontmatter lists that path

#### Scenario: Paste while another project is open
- **WHEN** two projects are open, demo2's attachments folder is `files/shots`, and the user pastes an image into a demo2 note
- **THEN** the file is under demo2's `files/shots/wi<item>/n<note>/`, nothing is written to the other project, and the note lists that path

### Requirement: Attachments on the note page
The note page SHALL list a note's attachments: an image inline, another file by its name opening in the operating system, a URL as a link, each with a remove control. "Attach file…" SHALL open the system file picker; a field SHALL add a link.

#### Scenario: Add a link
- **WHEN** the user types `https://example.com/spec` in the link field and adds it
- **THEN** the note lists it as a link and its frontmatter has the URL

### Requirement: The agent is a contributor, not a title badge
The work item page SHALL show no agent chip in its title row, and cards and list rows SHALL show none either. An agent that stamped the item (`agentBy`) and is not already a contributor SHALL appear after the people in the contributors row as a lilac chip without a remove control; while the item is unreviewed that chip SHALL be the review control (click marks the item reviewed), afterwards it SHALL stay as a plain lilac chip. An agent already listed among the contributors SHALL be shown once.

#### Scenario: Review from the contributors row
- **WHEN** #wi123 is stamped `agentBy: Claude` and the user clicks the lilac "Claude" chip among the contributors
- **THEN** the item is marked reviewed, the chip stays lilac but is no longer the review control, and the title row never had a chip

### Requirement: Plan link
A work item MAY link a plan with `x-rtm.plan`, a repository-relative path. While no plan is linked, the Related column of the Linked tasks view SHALL offer "+ link plan", listing the markdown files in the plans folder (newest first, filterable); picking one links it. A linked plan SHALL show as a row of the Related list with its file name that opens the plan rendered inside the app, and a × that unlinks it (the file stays). The header SHALL not show the plan. `x-rtm.plan` SHALL refuse a path outside the repository or with `..`.

#### Scenario: Link and open
- **WHEN** the user picks `health-2026-11-05.md` from "+ link plan" in the Related list and clicks its row
- **THEN** `x-rtm.plan` is `DevResources/plans/health-2026-11-05.md` and the plan opens rendered, headed by its path

### Requirement: Document page
`/doc/<path>` SHALL render the markdown file at that repository-relative path, with the path shown and a way back. A missing file, or a path that climbs out of the repository, SHALL show "not found".

#### Scenario: Outside the repository
- **WHEN** `/doc/../outside.md` is opened
- **THEN** the page says not found and reads nothing

### Requirement: Start and due dates
A work item SHALL have an optional start date (`x-rtm.started`) and due date (`x-rtm.due`), both `yyyy-MM-dd`. The first move to In Progress SHALL stamp the start date with today when it is empty, and a start date already there SHALL be kept. The work item page SHALL edit both with one "dates" range control: the first day clicked is the start, the second the due date (picked backwards they swap); Clear removes both.

#### Scenario: Started on first move
- **WHEN** #wi107 with no start date moves to In Progress, back to Planned and to In Progress again a week later
- **THEN** `started` is the day of the first move

#### Scenario: Pick a range and clear it
- **WHEN** the user opens the dates control, clicks 10 November and then 14 November, and later presses Clear
- **THEN** `.openspec.yaml` first has `started: 2026-11-10` and `due: 2026-11-14` and the control reads "10 Nov – 14 Nov", then neither key

### Requirement: Transition date
A work item SHALL carry `x-rtm.moved` (`yyyy-MM-dd`): the day it last moved to its status. Creation SHALL stamp it with that day, every status change SHALL restamp it with the day of the change, and setting the same status again SHALL leave it. A change without the key SHALL read as moved on its creation date. The work item page SHALL show it as "in <status> since <date>" beside the dates, and the list SHALL have an "In status since" column after Due.

#### Scenario: Moved to In Progress
- **WHEN** #wi107, created on 1 Nov, moves to In Progress on 7 Nov
- **THEN** `moved` is 2026-11-07, the page reads "in In Progress since 7 Nov", and the list column shows 7 Nov

#### Scenario: No key yet
- **WHEN** a change created on 18 Sep has no `moved` key
- **THEN** the page reads "in <status> since 18 Sep" and the key is not written until the status changes

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

### Requirement: Folders per project in Settings
Settings SHALL list every open project under its name with its location (the repository path, with a control that shows it in the file manager), its plans folder and its attachments folder, each relative to that repository; empty means the default (`DevResources/plans`, `local/task-tracking`). A change SHALL apply to that project only and SHALL rewrite that project's `openspec/REPO-TASKS.md`, whose first line names both folders.

#### Scenario: Every location shown
- **WHEN** demo and demo2 are open and the user opens Settings
- **THEN** each project's row is headed by its name and shows its own folder path, and the top row is an empty "Open another repository" box

#### Scenario: Two projects, two attachments folders
- **WHEN** demo and demo2 are open and the user sets demo2's attachments folder to `files/shots`
- **THEN** demo2's agent guide names `files/shots`, demo keeps `local/task-tracking`, and a note attachment in each project goes to its own folder

### Requirement: Images in the description
Pasting an image into the description editor SHALL save it as `pasted-<yyyyMMdd-HHmmss>.<ext>` under the project's attachments folder in `wi<item>/` (a taken name gets `-2`, `-3`…; the folder is git-ignored) and SHALL insert `![](<path>)` with that repository-relative path at the caret. In view mode the description SHALL show the image, scaled to the tile's width.

#### Scenario: Paste a screenshot
- **WHEN** the user pastes a screenshot into #wi119's description and clicks outside
- **THEN** the file is under local/task-tracking/wi119/, the description holds a link to it, and the image shows in the description

### Requirement: New feature from the item page
The work item page's feature list SHALL list every feature and end with "+ New feature…". Picking it SHALL ask for the new feature as the board does, create it and set it on the item; cancelling SHALL leave the item unchanged.

#### Scenario: Create and set
- **WHEN** the user picks "+ New feature…" on #wi115 and names it "Exports"
- **THEN** the feature "Exports" exists and #wi115's feature is "Exports"

### Requirement: Field changes are events
The app SHALL record an event in the work item's `events.log` (`time | who | kind | text`) for a changed title, priority, label, dates or feature (`old -> new`), a contributor added or removed (the name), a plan linked or unlinked (the path) and an edited description. A write that changes nothing SHALL record nothing.

#### Scenario: Rename and change priority
- **WHEN** the user renames #wi123 from "Old" to "New" and sets its priority from low to high
- **THEN** `events.log` gains `title | Old -> New` and `priority | low -> high`, each with the user's name and the time

#### Scenario: Save unchanged
- **WHEN** the user saves the description of #wi123 without changing it
- **THEN** no event is recorded

### Requirement: Attachments, created subtasks and merges are events
The app SHALL record `attachment-added` with `n<note>: <file>` when a file or URL is attached to a note, `subtask-added` for each subtask written when an item is created through the tools, and `merged-from` with `#wi<id> <title>` on the item that survives a merge, one per merged item, after the events it inherited.

#### Scenario: Merge
- **WHEN** #wi92 and #wi94 are merged into #wi137
- **THEN** #wi137's log gains one `merged-from` line for #wi92 and one for #wi94 after the inherited events

### Requirement: Decision notes
A note MAY have the kind `decision` (frontmatter `kind: decision`). The worklog SHALL show a decision emphasised: a DECISION label, an accent mark and accent title, no frame; when its title is the default "Decision" only the label and the body show. The Worklog block and the day menu SHALL offer "Add decision", which creates a decision note and opens it in place with the placeholder "What was decided, and why. Two lines."

#### Scenario: Add a decision
- **WHEN** the user clicks Add decision, types "Use note kinds. Why: one place." and clicks outside
- **THEN** the feed shows the entry with the DECISION label, and the note file has `kind: decision`

### Requirement: Worklog filters by kind
The Worklog block SHALL offer six filters: Notes, Decisions, Events (created, status, merged-from), Links (commit, test, doc and plan links), Subtasks (subtask and dependency events) and Edits (title, priority, label, dates, feature, contributors, description, attachments). Unticking one SHALL hide its entries and the Days counts SHALL follow.

#### Scenario: Hide the housekeeping
- **WHEN** the user unticks Edits
- **THEN** field changes and attachments leave the feed while notes, decisions, status changes and links stay

### Requirement: A document shows the images it links to

The document page SHALL show an image that the document links to with a markdown image link when the link names an image file of the repository: a path relative to the document's folder is tried first, then a path from the repository root. A link to a file that does not exist, or to a path outside the repository, SHALL be left unchanged.

#### Scenario: A drawing beside the plan

- **WHEN** a plan in the plans folder contains `![loop](loop.excalidraw.svg)` and that file is in the same folder
- **THEN** the document page shows the drawing

#### Scenario: A path from the repository root

- **WHEN** a document contains `![shot](docs/shot.svg)` and that file exists under the repository root
- **THEN** the document page shows the image

#### Scenario: A missing file

- **WHEN** a document links to an image file that does not exist
- **THEN** the link is left as written and nothing else on the page changes

### Requirement: Notes edit in place
A note's title is its first `#` heading and SHALL be editable in the note header; a blank title SHALL be refused. The note page SHALL have no Edit or Done button: clicking the rendered note SHALL open the editor, and clicking outside the editor SHALL save and show the rendered note again.

#### Scenario: Retitle and edit
- **WHEN** the user changes the title field to "Decided on X", clicks the note text, types, and clicks outside the editor
- **THEN** the note file's first heading is "# Decided on X", the body holds the typed text, and the rendered note shows both

### Requirement: Paste an image as an attachment
While the note page is open, an image pasted from the clipboard SHALL become an attachment of the note, stored under `local/task-tracking/wi<item>/n<note>/` as `pasted-<timestamp>.<ext>`, with the same `.gitignore` handling as a picked file.

#### Scenario: Paste a screenshot
- **WHEN** the user presses Ctrl+V with a PNG on the clipboard
- **THEN** the note lists a new image attachment and the file exists under `local/task-tracking/`

### Requirement: Description last in the worklog
The description SHALL be the last entry of the Worklog view, below every day and below "Linked earlier", as the item's first post, rendered as markdown, with no hover effect of its own. Shift-clicking it SHALL offer Edit, which turns it into an editor in place that saves when the user leaves it; a pasted image becomes an attachment linked at the caret. An item without a description SHALL say so in that entry and offer the same Edit.

#### Scenario: Edit the description in the feed
- **WHEN** the user Shift-clicks the description entry of #wi123, picks Edit, changes the text and clicks outside
- **THEN** `proposal.md` of #wi123 holds the new text and the entry shows it rendered

### Requirement: Worklog block in the side menu
While a worklog is open the side menu SHALL show a Worklog block first, before the list's organise, sort and display blocks: filters for Notes, Events, Links and Subtasks, a who picker, and the actions Add note, Link document, Link test, Link commit and Add subtask. The block SHALL go when the worklog closes. Link test and Link commit SHALL be switched off, saying why, when there are no test runs or no git.

#### Scenario: Filter the feed
- **WHEN** the user unticks Events in the Worklog block
- **THEN** status changes and link events leave the feed and the notes stay; ticking it brings them back

#### Scenario: Split view
- **WHEN** a work item is open beside the list
- **THEN** the side menu shows the Worklog block above the list's blocks

### Requirement: Days list in the Worklog block
The Worklog block SHALL list the feed's days newest first, each with its entry count, and "Linked earlier" when that group is shown. Clicking a day SHALL unfold it and scroll its heading into view. The list SHALL follow the filters.

#### Scenario: Jump to a day
- **WHEN** the user clicks "9 Nov 2026" in the Days list while that day is folded
- **THEN** the day unfolds and its heading scrolls to the top of the feed

### Requirement: Related list
The Related column of the Linked tasks view SHALL list everything attached to the work item: its plan, its linked documents, the documents and files its description and notes link to or attach, and their images, each once, each opening on a click. The list SHALL be the only place the page shows the plan and the linked documents: the header SHALL not repeat them. A plan or linked document SHALL have a × there that unlinks it (the file stays).

#### Scenario: A plan and a pasted screenshot
- **WHEN** #wi123 has a plan and a note with a pasted screenshot
- **THEN** the Related list has a row for the plan and a row for the image, and an item with nothing attached says "Nothing attached yet."

#### Scenario: Unlink a document
- **WHEN** #wi123 lists `docs/guide.md` in `x-rtm.docs` and the user clicks the × on its row in the Related list
- **THEN** `x-rtm.docs` no longer lists it, and a document the description merely mentions has no ×

### Requirement: Worklog feed
The Worklog view SHALL show the work item's notes and events in one feed grouped by day, the newest day first, each entry with the time it was created and who made it. A day SHALL fold and unfold on a click, and today's group SHALL always be present.

#### Scenario: Notes and events together
- **WHEN** #wi123 has a note written yesterday and its status was changed today
- **THEN** the feed shows today with "status: Planned → In Progress" and its time, then yesterday with the note

#### Scenario: Fold a day
- **WHEN** the user clicks yesterday's date
- **THEN** yesterday's entries are hidden until the date is clicked again

### Requirement: Events are recorded
The app SHALL record, in `events.log` in the change folder, when a work item is created, its status changes, a commit, test or document is linked or unlinked, a subtask is ticked, reopened or added, and a dependency is added or removed, each with the time and who did it. A commit, test or document linked before the log existed SHALL still show in the feed: a commit at its commit time, tests and documents in a last group "Linked earlier".

#### Scenario: Status change
- **WHEN** the user moves #wi123 from Planned to In Progress
- **THEN** `events.log` of #wi123 gains a line with the time, the user's name, `status` and `Planned -> In Progress`

#### Scenario: A test linked last week
- **WHEN** #wi123 lists a test in `x-rtm.tests` and has no event for it
- **THEN** the feed shows the test under "Linked earlier" with its latest result

### Requirement: Add to the worklog from a date
Shift-clicking a date in the feed SHALL open a menu with Add note, Link document, Link test and Link commit. Add note SHALL open an editor in place under that day (title, markdown text, pasted images become attachments) that saves when the user leaves it; a note added under an older day SHALL be dated that day. A link SHALL appear in the feed as an event.

#### Scenario: Add a note
- **WHEN** the user Shift-clicks today's date, picks Add note, types a title and text and clicks outside
- **THEN** the note is saved and shows in today's group with the current time

#### Scenario: Link a commit
- **WHEN** the user Shift-clicks today's date, picks Link commit and chooses a commit
- **THEN** the commit is stored in `x-rtm.commits` and today's group shows "linked a commit" with its hash and subject

### Requirement: Edit and unlink from an entry
Shift-clicking a note in the feed SHALL open a menu with Edit, which opens the note in place. Shift-clicking a commit, test or document entry that is still linked SHALL open a menu with Unlink, which removes the link and is itself recorded as an event. An open menu SHALL stay on the entry it was opened on when the feed changes under it (a new entry above it), so Unlink always removes the link that was clicked.

#### Scenario: Edit a note
- **WHEN** the user Shift-clicks a note and picks Edit
- **THEN** the note becomes an editor where it stands, and clicking outside saves it

#### Scenario: Unlink a test
- **WHEN** the user Shift-clicks "linked a test" and picks Unlink
- **THEN** the test is removed from `x-rtm.tests` and the feed gains "unlinked a test"

### Requirement: Linked documents
A work item SHALL keep its linked documents as repository-relative paths in `x-rtm.docs`; a path outside the repository SHALL be refused.

#### Scenario: Link a document
- **WHEN** the user links `docs/guide.md` to #wi123
- **THEN** `x-rtm.docs` of #wi123 lists `docs/guide.md` and the Related list shows it

### Requirement: Views without frames
The two views of the work item page SHALL have no frames: the description entry, the Related list, the linked-tasks columns and their rows, and the notes in the worklog sit directly on the page, with a soft line between rows and between worklog entries. An editor in place SHALL show as a line beside or under the text, not as a box.

#### Scenario: Open the linked tasks
- **WHEN** the user opens the Linked tasks view of an item with subtasks
- **THEN** the columns have plain headings and the subtasks are rows divided by lines, with no box around a column or a row

### Requirement: A subtask made from a note refers to it
Converting a note, decision or bug to a subtask SHALL keep the note as it is and SHALL end the new subtask line in the note's reference `#nN`. The Linked tasks view SHALL show that reference as a "from #nN" link.

#### Scenario: A note with a body becomes a subtask
- **WHEN** the user converts the note "cr", whose text says "When a subtask is being moved to a separate work item, add a log in the feed", to a subtask
- **THEN** the subtask line reads "cr #n412" and its note is still in the feed with its full text

#### Scenario: Renumbered
- **WHEN** a subtask above a note-made subtask is removed
- **THEN** the note-made subtask still refers to its note and the note still offers no second subtask

### Requirement: Linked tasks and the worklog link to each other
The "from #nN" link SHALL open the Worklog view at that note, marked for a moment. A converted entry that names a subtask still in the item SHALL link to it in the Linked tasks view, marked the same way.

#### Scenario: From the subtask to the note
- **WHEN** the user clicks "from #n412" on a subtask
- **THEN** the Worklog view opens at the note #n412, its day unfolded and the entry marked

#### Scenario: From the feed to the subtask
- **WHEN** the user clicks "subtask 7.11" in "promoted note #n412 to subtask 7.11"
- **THEN** the Linked tasks view opens at subtask 7.11, marked, and its group unfolded when the subtask is done

### Requirement: A conversion keeps the original
Converting a feed entry SHALL never remove it: promoting a note, decision or bug to a subtask keeps the note with its text and attachments and adds the subtask; converting a subtask to a note, decision or bug keeps the subtask. Each conversion SHALL be recorded as one event, and a note already promoted SHALL no longer offer the promotion.

#### Scenario: Promote a note
- **WHEN** the user Shift-clicks a note with text and picks Make it a subtask
- **THEN** the subtask exists, the note is still in the feed with its text, and the feed gains "promoted note #nN to subtask X"
