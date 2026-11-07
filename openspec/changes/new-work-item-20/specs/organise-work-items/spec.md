## ADDED Requirements

### Requirement: List cells stay in their column
In the list view every cell SHALL keep its content inside its own column, however narrow the column is. Text that does not fit SHALL be cut with an ellipsis and SHALL show in full as a tooltip; pills and chips SHALL shrink to fit, and a progress bar SHALL shrink so its count stays visible. The Label column SHALL be wide enough by default for every label name.

#### Scenario: Long title in a narrow column
- **WHEN** an item has a long title and long contributor names and the user narrows the columns
- **THEN** each cell ends in an ellipsis inside its own column, nothing spills into the next column, and hovering shows the full text
