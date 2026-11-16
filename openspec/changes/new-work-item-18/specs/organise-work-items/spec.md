## MODIFIED Requirements

### Requirement: Dates on cards and rows
An expanded card SHALL show "due d MMM" as a tag when the item has a due date, in the overdue colour (peach, the colour the board gives an overdue item by due date) while the item is overdue and not completed; a compact card and an item without a due date show none. The list SHALL have Updated, Started and Due as three separate columns, present by default and in that order, beside "In status since": Updated is when the work item was last changed, Started its planned start date and Due its planned end date.

#### Scenario: Overdue card
- **WHEN** #wi123 was due yesterday
- **THEN** its expanded card's due tag is peach; an item due in two days has a plain due tag; an item without a due date has none

#### Scenario: Three dates in the list
- **WHEN** the user opens the list on a fresh repository
- **THEN** the headers read Updated, Started and Due, in that order, each its own column

## ADDED Requirements

### Requirement: Updated is the last change in the worklog
A work item's updated time (the list's Updated column, the "date modified" sort, the card's date and the changed-period filter) SHALL be the time of the last change in its worklog: the newest line of its events.log or the newest note's created time. The time its files were written SHALL only stand in for an item with no worklog, because a checkout or sync rewrites every file.

#### Scenario: Files rewritten by a checkout
- **WHEN** a work item's events.log ends on 20 Oct and all its files were written on 1 Nov
- **THEN** its Updated is 20 Oct

#### Scenario: A note after the last event
- **WHEN** a note was created on 25 Oct and the last event is from 6 Oct
- **THEN** its Updated is 25 Oct

#### Scenario: No worklog
- **WHEN** a work item has neither events nor notes
- **THEN** its Updated is the time its files were last written
