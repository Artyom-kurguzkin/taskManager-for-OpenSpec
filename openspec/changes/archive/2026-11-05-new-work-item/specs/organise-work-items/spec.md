## MODIFIED Requirements

### Requirement: Card contents
An expanded card SHALL show its title band, its feature as a chip (its status instead when grouped by feature, since the column is the feature), a plain meta row `<date> · <contributors> · <done>/<total>` without icons (the count only when the item has subtasks), and its tags: `#wi`, the label filled with the label's colour, the due date, and warnings. It SHALL NOT show a priority tag: priority is a subgroup of the column.

#### Scenario: Features on cards
- **WHEN** the board is sorted by feature and a card is linked to Board
- **THEN** its expanded form shows a "Board" chip

#### Scenario: Meta row and label
- **WHEN** a card's work item has 2 of 4 subtasks done, was updated on 5 Nov by Artyom and is labelled feature
- **THEN** the meta row reads `5 Nov · Artyom · 2/4` with no icons, and the label tag is filled sky blue

#### Scenario: No subtask count
- **WHEN** a card's work item has no subtasks
- **THEN** the meta row ends with the contributors and shows no count

## ADDED Requirements

### Requirement: Priority subgroups
A column without custom subgroups whose cards differ in priority SHALL show them under "High priority" and "Lower priority" headings, highest first. Dropping a card on a priority subgroup SHALL set its priority. A column whose cards share one priority SHALL show no heading; custom subgroups take precedence.

#### Scenario: Drop on High priority
- **WHEN** Backlog holds a high and a low card and the user drags the low card onto the "High priority" subgroup
- **THEN** its priority becomes high and it shows under that heading
