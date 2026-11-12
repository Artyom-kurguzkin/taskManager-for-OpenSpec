## ADDED Requirements

### Requirement: A renumbered duplicate id is logged

When two work items, or two notes, hold the same id after a merge and the app renumbers one of them, it SHALL record a `renumbered` event in the worklog of the renumbered work item (for a note: of its work item), naming the old id, the new id and the item that kept the id.

#### Scenario: The renumbering is in the worklog

- **WHEN** a work item was renumbered because another item held its id
- **THEN** its worklog shows a `renumbered` event with the old id, the new id and the title of the item that kept the id

#### Scenario: Nothing to repair

- **WHEN** no two items and no two notes share an id
- **THEN** the repair writes nothing

### Requirement: A renumbered item's own references follow it

The app SHALL rewrite references to the old id inside the renumbered item's own files, and SHALL leave references in other items as they are.

#### Scenario: Own references follow, others stay

- **WHEN** the renumbered item's description or notes mention its old id, and another item mentions that id too
- **THEN** the renumbered item's own mentions show the new id and the other item's mention is unchanged

### Requirement: The board tools repair duplicate ids before reading

The board tools SHALL apply the duplicate-id repair before they read, so an id is never listed twice.

#### Scenario: A duplicate made outside the app

- **WHEN** two change folders hold the same id and a board tool lists the items
- **THEN** each item is listed once, under its own id
