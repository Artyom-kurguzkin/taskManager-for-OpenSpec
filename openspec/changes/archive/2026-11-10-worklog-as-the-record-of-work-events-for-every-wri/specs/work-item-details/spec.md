## ADDED Requirements

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
