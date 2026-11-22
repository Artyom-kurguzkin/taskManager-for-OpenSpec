# notes Specification

## Purpose
TBD - created by archiving change note-editor-preview-first. Update Purpose after archive.

## Requirements

### Requirement: Note page edits like a description
The note page SHALL show the note rendered (wiki links, #wi and #n references resolved). Clicking the rendered note SHALL replace it with the same plain text box the work item description uses, focused. The note SHALL autosave shortly after typing stops, and leaving the text box SHALL save at once and show the rendering again. Pasting an image while editing SHALL attach it to the note.

#### Scenario: Read, edit, leave
- **WHEN** the user opens a note, clicks its text, types, and clicks outside
- **THEN** the note was rendered first, a plain text box took its place, the text was saved while typing and on leaving, and the rendering shows the new text

### Requirement: Editors fill their tile
While editing, the description's and the note's text box SHALL fill the height their tile has below its header and SHALL have no border, outline or background of their own; the tile is the frame.

#### Scenario: Long description
- **WHEN** the user clicks a long description to edit it
- **THEN** the text box reaches the bottom of the tile and shows no box of its own

### Requirement: Note title in the frontmatter
A note's title SHALL be stored in its frontmatter as `title:`. Changing the title SHALL write only that key and leave the body, including its first heading, unchanged. A note without `title:` SHALL read its title from its first heading.

#### Scenario: Retitle a note
- **WHEN** the user renames note #n18, whose body starts with "# 28 Oct — editor replaced", to "Separate title"
- **THEN** its frontmatter has `title: Separate title` and its body still starts with "# 28 Oct — editor replaced"

### Requirement: One scrollbar
Every scrollable area of the app SHALL use one thin, rounded scrollbar drawn from the colour tokens, in both themes; areas that hide their scrollbar keep doing so.

#### Scenario: A long description
- **WHEN** the description in the editor is longer than the window
- **THEN** the editor shows the app's thin scrollbar, not the system one

### Requirement: The description editor fills the window
While the description is edited, the editor SHALL run from just under the page header to the bottom of the window and follow the window's size.

#### Scenario: Open the editor
- **WHEN** the user Shift-clicks the description and picks Edit
- **THEN** the editor's bottom is at the bottom of the window

### Requirement: Only the user's text
The app SHALL NOT show or ask for empty "Why" and "What Changes" sections: the editor, card, item page and feed show the user's text only, saving writes only that text, and a new work item starts with an empty description. Non-empty sections stay.

#### Scenario: Edit an old item
- **WHEN** an item's proposal has the user's text followed by empty Why and What Changes sections and the user edits it
- **THEN** the editor shows only their text and the saved file has no empty sections, and the item still passes openspec validate --strict

### Requirement: Note storage
Each note SHALL be a file `notes/n<id>.md` inside its work item's change folder, starting with frontmatter holding `id`, `by` and `created`. Note ids SHALL be unique across the repository, active and archived changes included.

#### Scenario: Create a note
- **WHEN** the user adds a note to #wi123 and the highest note id in the repo is 124
- **THEN** `openspec/changes/<wi123 folder>/notes/n125.md` is created with `id: 125`, the current git user as `by`, and the current time as `created`

### Requirement: Note page
The note page SHALL show `#n<id>`, the linked work item, created time, last modified time (from the file) and author, next to a markdown editor and a live preview. Edits SHALL be saved automatically within one second of the user stopping typing.

#### Scenario: Autosave
- **WHEN** the user types in the editor and pauses
- **THEN** the file is written within one second and "last modified" updates

### Requirement: Link syntax
In note text, `[[path]]` SHALL link to a file in the repository (the `.md` extension is optional), `#wi<id>` to a work item, and `#n<id>` to a note. In the preview, resolved links SHALL open their target, and unresolved ones SHALL be marked as unresolved.

#### Scenario: Unresolved reference
- **WHEN** the note contains `#wi999` and no work item 999 exists
- **THEN** the preview marks `#wi999` as unresolved and it is not clickable

#### Scenario: Follow a link
- **WHEN** the user clicks `#wi106` in the preview
- **THEN** the work item page for #wi106 opens and Esc returns to the note

### Requirement: Linked docs and commits
The note page SHALL list every resolved and unresolved link found in the note, commits that mention its work item, commits linked by hand, and free-text entries the user added by hand. Manual text entries SHALL be stored in the note's frontmatter under `links`. The list SHALL update as the user types.

#### Scenario: Live update
- **WHEN** the user types `[[runbook/uptime]]` and `docs/runbook/uptime.md` exists
- **THEN** the list gains a doc entry for it without saving or reloading

### Requirement: Link a commit by hand
The note page SHALL offer "+ link commit", browsing and filtering recent commits the same way the work item page does, and linking the picked one. Commits linked this way SHALL be stored in the note's frontmatter under `commits` and SHALL show an unlink control; commits only found by mentioning the work item SHALL not.

#### Scenario: Link a commit by hand
- **WHEN** the user picks a commit from "+ link commit" on a note
- **THEN** it appears in the linked commits list with an unlink control, and the note's frontmatter gains its hash under `commits`

### Requirement: Notes are the change log
A work item's notes SHALL be its change log entries: the Change log panel on the work item page SHALL list them newest first, each showing its id, title (the first heading), created date and author, and SHALL offer "+ Add entry", which creates a note and opens it. There SHALL be no separate notes list.

#### Scenario: Open an entry
- **WHEN** the user clicks entry #n123 in the change log of #wi123
- **THEN** the note page opens, and Esc returns to #wi123

#### Scenario: Add an entry
- **WHEN** the user clicks "+ Add entry" on #wi123
- **THEN** a new note file is created in the change's notes/ folder, its page opens, and back on #wi123 it is the newest change log entry

### Requirement: Edit in an external editor
The note page SHALL offer a button to open the note's file in an external editor, labelled "Edit in &lt;editor&gt;" once one is configured. If none is configured, clicking it SHALL ask which command to use (e.g. `code`, `notepad++`, or a full path) and remember the answer for next time. The editor command SHALL be changeable in Settings.

#### Scenario: First use
- **WHEN** no editor is configured and the user clicks the button
- **THEN** a prompt asks which editor to use; once answered, that note's file opens in it and the command is saved

#### Scenario: Already configured
- **WHEN** an editor is already set and the user clicks "Edit in &lt;editor&gt;"
- **THEN** the note's file opens in that editor directly, without asking again

#### Scenario: Change later
- **WHEN** the user edits the external editor field in Settings
- **THEN** later clicks of "Edit in …" use the new command
