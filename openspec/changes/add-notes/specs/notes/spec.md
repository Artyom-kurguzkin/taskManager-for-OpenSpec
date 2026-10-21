# Spec Delta

## Purpose

Keeps free-form markdown notes next to the work item they belong to, with live links to docs, work items, other notes and commits.

## ADDED Requirements

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
The note page SHALL list every resolved and unresolved link found in the note, the commits linked to its work item, and entries the user added by hand. Manual entries SHALL be stored in the note's frontmatter under `links`. The list SHALL update as the user types.

#### Scenario: Live update
- **WHEN** the user types `[[runbook/uptime]]` and `docs/runbook/uptime.md` exists
- **THEN** the list gains a doc entry for it without saving or reloading

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
