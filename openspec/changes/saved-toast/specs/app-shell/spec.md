## ADDED Requirements

### Requirement: Saved toast
Every write to the repository made from a page SHALL show a "Saved" toast, unless a more specific toast was shown for it; it SHALL never show twice at once. A note's autosave SHALL show it once the text is on disk.

#### Scenario: Title
- **WHEN** the user changes a work item's title
- **THEN** a "Saved" toast appears

#### Scenario: Specific message wins
- **WHEN** the user links a dependency
- **THEN** the toast says what was linked, not "Saved"
