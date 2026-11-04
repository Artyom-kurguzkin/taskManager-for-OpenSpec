## MODIFIED Requirements

### Requirement: One selection look
Anything selected with Shift-click — a card, a list row, a project heading, a subtask — SHALL look the same: it keeps its own surface, a solid line in the text colour hugs it and a yellow halo runs round that line, wide enough to be seen at a glance on the light theme. Selection SHALL never be shown as a fill.

#### Scenario: Card and row
- **WHEN** the user Shift-clicks a card on the board and a row in the list
- **THEN** both show the same solid line with a yellow halo round it, and neither changes its fill
