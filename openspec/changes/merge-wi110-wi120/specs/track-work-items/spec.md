## MODIFIED Requirements

### Requirement: Note ids, subtask numbers and subgroups never collide
A new note SHALL take its id from the allocator that every worktree of the repository shares: the next id after every note in every worktree, archived ones included, and after the last id handed out, so the id of a deleted note is not used again. Two notes with one id SHALL resolve on read: the newer takes the next free id and its file follows; a frontmatter id that disagrees with the file name follows the file name. Duplicate subtask numbers SHALL read as the next free number and be renumbered on the next write. Adding a subgroup that exists SHALL be a no-op.

#### Scenario: Agent wrote a stale note number
- **WHEN** an agent adds `notes/n40.md` to a change while `n40.md` already exists in an archived change
- **THEN** the newer note becomes `n<next free>.md` with matching frontmatter and nothing else changes

#### Scenario: A note in another worktree
- **WHEN** the highest note in this worktree is n40, the shared counter says the last id handed out was 500, and the user adds a note
- **THEN** the new note is n501
