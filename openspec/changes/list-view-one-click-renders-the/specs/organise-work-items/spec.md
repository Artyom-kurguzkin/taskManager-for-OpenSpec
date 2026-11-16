## ADDED Requirements

### Requirement: Cheap clicks in the list
The list view SHALL render the page at most once per click, and SHALL NOT render it for a click that changes nothing. The status, label and priority cells SHALL be plain buttons, and only the clicked cell SHALL hold a picker.

#### Scenario: Examine click
- **WHEN** the user clicks a row's title in the list
- **THEN** the item is examined and the page renders once

#### Scenario: Background click with nothing selected
- **WHEN** the user clicks the list background with nothing selected or examined
- **THEN** the page does not render

#### Scenario: Pill picker on demand
- **WHEN** the list is shown
- **THEN** no cell holds a picker until the user clicks a pill or presses Enter or Space on it, and then exactly one picker is open

#### Scenario: Completed still asks
- **WHEN** the user picks Completed from a list pill
- **THEN** the app asks before setting it
