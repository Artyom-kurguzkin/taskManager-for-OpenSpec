## ADDED Requirements

### Requirement: Pick an item to wait on
The Subtasks tile of an active work item SHALL offer "+ wait on another work item", a list of the other active work items (number, title, status) with a filter box that matches number, title and status. Choosing one SHALL link it as a dependency exactly as typing its number does. The list SHALL leave out the item itself, items it already waits on, and items that wait on it.

#### Scenario: Choose from the list
- **WHEN** the user opens the picker on #wi107, types "health" and picks #wi123
- **THEN** #wi107 waits on #wi123, shows it under Subtasks, and is Blocked until #wi123 is in review or done

#### Scenario: No loops
- **WHEN** #wi107 waits on #wi123 and the user opens the picker on #wi123
- **THEN** neither #wi123 nor #wi107 is listed
