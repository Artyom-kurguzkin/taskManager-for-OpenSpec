# Spec Delta

## Purpose

Every scrolling view comes back where the user left it, kept by the app per window and repository, not by browser history.

## ADDED Requirements

### Requirement: A view comes back where it was left
The board, list, calendar, analytics, settings, work item, note and document views SHALL remember, per window and per repository, how far the user had scrolled when leaving, and SHALL scroll back to that spot after the view's first render when the user returns, by the back button or from the side menu. A view never left before SHALL start at the top.

#### Scenario: Back from an item
- **WHEN** the user scrolls the list down to a row, opens the work item and goes back
- **THEN** the list is at the same scroll position, within a row

#### Scenario: Settings and analytics
- **WHEN** the user scrolls the settings page, opens the board and returns to settings
- **THEN** the settings page is at the spot it was left at, and the same holds for analytics

### Requirement: Restoring waits for the content and adds no draw
The restore SHALL wait until the view is tall enough to reach the spot, SHALL stop if the user scrolls meanwhile, and SHALL NOT cause an extra render.

#### Scenario: A list that fills after its first draw
- **WHEN** the list returns and its rows are drawn a moment after the page
- **THEN** the list ends at the saved spot once the rows are there

### Requirement: Each repository keeps its own spot
The remembered spot SHALL be kept per repository: switching to another repository SHALL restore the spot left there, and switching back SHALL restore the first one.

#### Scenario: Two repositories
- **WHEN** the list is scrolled to 1800 in one repository, the user switches to a second one and scrolls it to 700, then switches back
- **THEN** the first repository's list is at 1800 and the second's at 700 when the user returns to it
