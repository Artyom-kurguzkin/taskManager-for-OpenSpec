## ADDED Requirements

### Requirement: Move and hide rows
Shift-clicking (or Ctrl/⌘-clicking) a swimlane header SHALL select it. The up and down arrow keys SHALL move the selected rows past the next unselected one, and dragging a header onto another SHALL move it there. A × on the header SHALL hide the row, and the side menu's Hidden list SHALL bring it back. The order and the hidden rows SHALL be remembered per board and row field.

#### Scenario: Move a label row up
- **WHEN** rows are by label and the user Shift-clicks the "feature" header and presses the up arrow
- **THEN** the feature row shows above the row before it, and stays there after a restart

#### Scenario: Hide a row
- **WHEN** the user clicks × on the "bug" row
- **THEN** the row is gone from the board and listed under Hidden in the side menu, whose eye brings it back
