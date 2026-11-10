## ADDED Requirements

### Requirement: Markdown tables and wrapping
The app SHALL render markdown tables as tables and bare web addresses as links wherever it shows markdown, and SHALL keep the text inside the window: a wide table scrolls on its own and a long word or address breaks.

#### Scenario: A plan with a chunk table
- **WHEN** the user opens a plan whose Chunks section is a table
- **THEN** the doc page shows a table with its header row, and no line runs past the right edge
