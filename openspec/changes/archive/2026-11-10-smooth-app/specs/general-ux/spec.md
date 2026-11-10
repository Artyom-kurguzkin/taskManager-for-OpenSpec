## ADDED Requirements

### Requirement: The app answers at once
A change the user makes (ticking a subtask, changing a status, saving a note) SHALL be visible within 150 ms, switching a view within 150 ms, and opening a work item within 250 ms, on a board of 50 items; each SHALL redraw once. A change SHALL re-read only the folder it touched, and git history SHALL be asked once per commit, not once per question.

#### Scenario: Tick a subtask
- **WHEN** the user ticks a subtask on the item page
- **THEN** the tick shows within 150 ms in one redraw, and only that item's folder is read again

#### Scenario: Open an item
- **WHEN** the user clicks a card on the board
- **THEN** the item page is drawn within 250 ms, once
