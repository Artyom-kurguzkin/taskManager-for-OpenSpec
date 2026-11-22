## ADDED Requirements

### Requirement: A subtask group that was merged in always says so
The Linked tasks view SHALL show the heading of a task group whose name says it was merged in ("#wiN Title · merged yyyy-MM-dd"), also when it is the item's only group.

#### Scenario: Only one source had subtasks
- **WHEN** an item without subtasks is merged with one that has some
- **THEN** Linked tasks lists those subtasks under the heading "#wiN Title · merged <date>"

### Requirement: Related lists the files of a merged item under their source
When any file in the Related list came with a note that a merge brought in, the list SHALL be grouped: the item's own files first under "this item", then each source's files under "from #wiN". An item that nothing was merged into SHALL show one flat list.

#### Scenario: Files of two merged items
- **WHEN** two items that each have a note with an attachment are merged
- **THEN** Related shows the main item's file under "this item" and the other item's file under "from #wiN"
