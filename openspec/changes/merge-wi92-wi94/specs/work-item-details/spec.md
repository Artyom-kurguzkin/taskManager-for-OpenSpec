## ADDED Requirements

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

## MODIFIED Requirements

### Requirement: Work item page
Opening a work item SHALL show its number as `#wi<id>`, title, linked features, contributors, status, priority and label, then three views with a button each, all three always shown and the current one marked: Description (the default), Worklog and Linked tasks. Features from the change's specs/ folder SHALL show with a dashed outline and text readable in both themes.

#### Scenario: Open from board
- **WHEN** the user clicks card #wi123
- **THEN** the work item page for #wi123 opens on the Description view, with the Worklog and Linked tasks buttons beside it

#### Scenario: Waiting on this
- **WHEN** #wi107 waits on #wi123
- **THEN** #wi123's Linked tasks view lists #wi107 in its "Waiting on this" column, and an item nothing waits on says so there

### Requirement: Related work items under Subtasks
The Linked tasks view SHALL show three columns: Subtasks, Waiting on (each work item this one waits on, highlighted while unresolved, struck through once In Review or Completed, with a control to remove it and a picker to add one) and Waiting on this (each work item waiting on this one), each row with the item's current status. Each row SHALL open the named item's page. In a narrow window the columns SHALL stack.

#### Scenario: Waits on
- **WHEN** #wi123's `x-rtm.dependsOn` lists #wi124
- **THEN** #wi123's Waiting on column shows a highlighted row for #wi124 with its status, and removing it clears it from `x-rtm.dependsOn`

#### Scenario: Blocks
- **WHEN** #wi123's `x-rtm.dependsOn` lists #wi124
- **THEN** #wi124's Waiting on this column shows a row for #wi123 with its status

### Requirement: Plan link
A work item MAY link a plan with `x-rtm.plan`, a repository-relative path. While no plan is linked, the Related list of the Description view SHALL offer "+ link plan", listing the markdown files in the plans folder (newest first, filterable); picking one links it. A linked plan SHALL show as a row of the Related list with its file name that opens the plan rendered inside the app, and a × that unlinks it (the file stays). The header SHALL not show the plan. `x-rtm.plan` SHALL refuse a path outside the repository or with `..`.

#### Scenario: Link and open
- **WHEN** the user picks `health-2026-11-05.md` from "+ link plan" in the Related list and clicks its row
- **THEN** `x-rtm.plan` is `DevResources/plans/health-2026-11-05.md` and the plan opens rendered, headed by its path

## REMOVED Requirements

### Requirement: Focus a tile
**Reason**: The page no longer has tiles of which one is focused; it has three full views.
**Migration**: Use the Description, Worklog and Linked tasks buttons under the header; nothing is minimised in a narrow window.

## ADDED Requirements

### Requirement: Related list beside the description
The Description view SHALL show, beside the description, a list of everything attached to the work item: its plan, its linked documents, the documents and files its description and notes link to or attach, and their images, each once, each opening on a click. The list SHALL be the only place the page shows the plan and the linked documents: the header SHALL not repeat them. A plan or linked document SHALL have a × there that unlinks it (the file stays).

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
Shift-clicking a note in the feed SHALL open a menu with Edit, which opens the note in place. Shift-clicking a commit, test or document entry that is still linked SHALL open a menu with Unlink, which removes the link and is itself recorded as an event.

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
The three views of the work item page SHALL have no frames: the description, the Related list, the linked-tasks columns and their rows, and the notes in the worklog sit directly on the page, with a soft line between rows and between worklog entries. An editor in place SHALL show as a line beside or under the text, not as a box.

#### Scenario: Open the linked tasks
- **WHEN** the user opens the Linked tasks view of an item with subtasks
- **THEN** the columns have plain headings and the subtasks are rows divided by lines, with no box around a column or a row
