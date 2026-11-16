## ADDED Requirements

### Requirement: A conversion keeps the original
Converting a feed entry SHALL never remove it: promoting a note, decision or bug to a subtask keeps the note with its text and attachments and adds the subtask; converting a subtask to a note, decision or bug keeps the subtask. Each conversion SHALL be recorded as one event, and a note already promoted SHALL no longer offer the promotion.

#### Scenario: Promote a note
- **WHEN** the user Shift-clicks a note with text and picks Make it a subtask
- **THEN** the subtask exists, the note is still in the feed with its text, and the feed gains "promoted note #nN to subtask X"
