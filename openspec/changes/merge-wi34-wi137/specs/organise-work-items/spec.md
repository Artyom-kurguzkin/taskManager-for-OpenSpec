## ADDED Requirements

### Requirement: Show only what changed in a period
The side menu SHALL offer a changed-in period (two days). While it is set, only work items created or changed in the period (an event in its log, a note, its creation day or a file write) SHALL be shown on the board, in the list, on the calendar and in Analytics. The choice SHALL be remembered per board.

#### Scenario: A week of changes
- **WHEN** the user picks 1 Nov to 5 Nov under changed
- **THEN** only the items touched in those days remain in every view

### Requirement: Period legend and Show all
While a changed period is set, the legend SHALL name it and say how many items are shown of those the other filters leave, and the picker SHALL offer "Show all", which clears the period.

#### Scenario: Show all
- **WHEN** the legend reads "changed 1 Nov – 5 Nov" and "11 of 34 shown" and the user clicks Show all in the picker
- **THEN** every item is back and the picker reads "any time"

### Requirement: The list survives saved settings from another session
The list SHALL open with the board's current grouping even when its saved list settings were made with a different number of projects open; saved settings SHALL never stop the page from responding.

#### Scenario: A second project opened later
- **WHEN** the list's settings were saved with one project open and the user opens the list with two
- **THEN** the rows are grouped by project and the side menu keeps working
