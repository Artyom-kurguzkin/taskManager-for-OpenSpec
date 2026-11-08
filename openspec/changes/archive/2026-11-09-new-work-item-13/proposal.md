# Proposal

in addition to start and due dates, a work item should have date - when it was moved to another status column
* first populated on work item's creation date
* then, each time it is moved (e.g, from planning to inProgress, etc)

## Why

Start and due dates say when work is planned; nothing says how long an item has sat in its current column.

## What Changes

- `x-rtm.moved` (yyyy-MM-dd) is stamped when a work item is created and again each time its status changes; an unchanged status leaves it. A change without the key reads as moved on its creation date.
- The work item page shows "in <status> since <date>" next to the dates; the list has an "In status since" column after Due.
