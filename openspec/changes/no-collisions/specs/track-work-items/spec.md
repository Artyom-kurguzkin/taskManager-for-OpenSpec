## ADDED Requirements

### Requirement: Change folder names never collide
A new change folder (created, merged or split) SHALL take a name that no folder under `openspec/changes/` or `openspec/changes/archive/` (ignoring the date prefix) has, numbering duplicates `-2`, `-3`, …. Completing an item whose name was already archived today SHALL rename its folder to a free name before the CLI archives it, keeping its `x-rtm.id`.

#### Scenario: Archived name reused the same day
- **WHEN** `archive/2026-11-05-new-work-item-13` exists and the user completes another item whose folder is `new-work-item-13`
- **THEN** the folder is renamed `new-work-item-13-2`, the archive succeeds, and the item keeps its number

### Requirement: Delta specs are repaired before archiving
Before archiving, the app SHALL repair the change's delta specs so the CLI never refuses them: an ADDED requirement the main spec already has becomes a MODIFIED block carrying every main-spec scenario; a requirement listed twice in one delta is merged into one block; a REMOVED requirement the main spec no longer has is dropped; a MODIFIED block gets the scenarios it lacks.

#### Scenario: Two changes add the same requirement
- **WHEN** two open changes both add "Export" and the user completes them one after the other
- **THEN** both archive, and the main spec has "Export" once with both changes' scenarios

### Requirement: Note ids, subtask numbers and subgroups never collide
A new note SHALL take the next id after every note in the repository, archived ones included. Two notes with one id SHALL resolve on read: the newer takes the next free id and its file follows; a frontmatter id that disagrees with the file name follows the file name. Duplicate subtask numbers SHALL read as the next free number and be renumbered on the next write. Adding a subgroup that exists SHALL be a no-op.

#### Scenario: Agent wrote a stale note number
- **WHEN** an agent adds `notes/n40.md` to a change while `n40.md` already exists in an archived change
- **THEN** the newer note becomes `n<next free>.md` with matching frontmatter and nothing else changes
