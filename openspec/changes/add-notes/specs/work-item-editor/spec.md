## MODIFIED Requirements

### Requirement: Work item page
Opening a work item SHALL show its number as `#wi<id>`, title, linked features, contributors, status, priority, and the panels Description, Change log (its notes), Subtasks, Linked tests and Linked commits.

#### Scenario: Open from board
- **WHEN** the user clicks card #wi123
- **THEN** the work item page for #wi123 opens with all panels, and a panel without data shows a one-line empty state
