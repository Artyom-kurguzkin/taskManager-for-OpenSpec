## ADDED Requirements

### Requirement: Agent work is flagged on the page
The work item page SHALL show an agent chip in its header while the item is flagged, on each subtask carrying a `<!-- agent: Name -->` comment, and on each change-log note an agent stamped; the note page SHALL show it in its header. Clicking a chip SHALL mark that one thing reviewed: `reviewedAt` is written for the item or note, the comment is removed from the subtask. Ticking a subtask SHALL keep its comment; editing its text SHALL remove it.

#### Scenario: Review a subtask
- **WHEN** the user clicks the chip on subtask 1.9
- **THEN** the comment is removed from its line in tasks.md and the item and other subtasks stay flagged

#### Scenario: Review a note
- **WHEN** the user clicks the chip on a note an agent stamped
- **THEN** the note's frontmatter gains `reviewedAt` and its text is unchanged
