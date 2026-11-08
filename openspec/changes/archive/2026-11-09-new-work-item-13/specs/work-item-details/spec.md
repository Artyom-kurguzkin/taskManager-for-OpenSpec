## ADDED Requirements

### Requirement: Transition date
A work item SHALL carry `x-rtm.moved` (`yyyy-MM-dd`): the day it last moved to its status. Creation SHALL stamp it with that day, every status change SHALL restamp it with the day of the change, and setting the same status again SHALL leave it. A change without the key SHALL read as moved on its creation date. The work item page SHALL show it as "in <status> since <date>" beside the dates, and the list SHALL have an "In status since" column after Due.

#### Scenario: Moved to In Progress
- **WHEN** #wi107, created on 1 Nov, moves to In Progress on 7 Nov
- **THEN** `moved` is 2026-11-07, the page reads "in In Progress since 7 Nov", and the list column shows 7 Nov

#### Scenario: No key yet
- **WHEN** a change created on 18 Sep has no `moved` key
- **THEN** the page reads "in <status> since 18 Sep" and the key is not written until the status changes
