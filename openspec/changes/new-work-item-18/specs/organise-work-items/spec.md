## MODIFIED Requirements

### Requirement: Dates on cards and rows
An expanded card SHALL show its planned dates as one period pill built from the item's start and due days, and SHALL show neither the work item's id nor an agent badge. The period reads "9–16 Nov" inside one month, "28 Oct – 3 Nov" across months, "9 Nov 25 – 3 Jan 26" across years, "16 Nov" for a single day, "from 9 Nov" with only a start and "due 16 Nov" with only a due day. The pill SHALL be in the overdue colour (peach, the colour the board gives an overdue item by due date) while the item has a due day that is past and is not completed; a compact card and an item with neither day show no pill. The card's first line SHALL read the time of the item's last change as "15:34. 16 Nov 26" (time, dot, day, month, two-digit year). The list SHALL have Updated, Started and Due as three separate columns, present by default and in that order, beside "In status since": Updated is when the work item was last changed and reads "15:34. 16 Nov 26" like the card; Started is its planned start day and Due its planned end day, each as "9 Nov". The list row SHALL carry no agent badge.

#### Scenario: One period pill
- **WHEN** an item started on 9 Nov and is due on 16 Nov
- **THEN** its expanded card's pill reads "9–16 Nov"; started 28 Oct and due 3 Nov reads "28 Oct – 3 Nov"; started 9 Nov 2025 and due 3 Jan 2026 reads "9 Nov 25 – 3 Jan 26"; only a start reads "from 9 Nov" and only a due day reads "due 16 Nov"

#### Scenario: Overdue card
- **WHEN** #wi123 was due yesterday
- **THEN** its period pill is peach; an item due today or in two days has a plain pill; an item that is only started has a plain pill; an item without dates has none

#### Scenario: No id and no agent badge
- **WHEN** an agent changed #wi123 three minutes ago and the user has not reviewed it
- **THEN** its card shows no "#wi123" pill and no "Claude, 3 min ago" line, and neither does its list row

#### Scenario: Modified with its time
- **WHEN** the newest change of #wi123 was at 15:34 on 16 Nov 2026
- **THEN** the card's first line and the list's Updated cell read "15:34. 16 Nov 26"

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
