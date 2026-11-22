## ADDED Requirements

### Requirement: Markdown tables and wrapping
The app SHALL render markdown tables as tables and bare web addresses as links wherever it shows markdown, and SHALL keep the text inside the window: a wide table scrolls on its own and a long word or address breaks.

#### Scenario: A plan with a chunk table
- **WHEN** the user opens a plan whose Chunks section is a table
- **THEN** the doc page shows a table with its header row, and no line runs past the right edge

### Requirement: Table grid ribbon without move buttons
The ribbon over the markdown table grid SHALL offer insert, delete, align, sort and the text/grid switch, and SHALL NOT offer buttons that move rows or columns; the grid SHALL move the selected rows or columns on Alt+arrow instead.

#### Scenario: The ribbon has no move buttons
- **WHEN** the caret is in a table and the grid ribbon is shown
- **THEN** it shows no Row up, Row down, Column left-move or Column right-move button

#### Scenario: Alt+arrow moves the selection
- **WHEN** a column is selected in the grid and the user presses Alt+Left
- **THEN** the column swaps places with the one on its left and stays selected

### Requirement: New table columns get a fresh header
A column added to a table by any path SHALL get the header "Column N", where N is one more than the highest "Column <number>" in the header row, or the column count plus one when there is none, and SHALL NOT copy a neighbour's header.

#### Scenario: A column added to the right of the last
- **WHEN** a table with the headers Column 1 to Column 4 gets a column to the right of Column 4
- **THEN** the new header reads Column 5, and a second added column reads Column 6
