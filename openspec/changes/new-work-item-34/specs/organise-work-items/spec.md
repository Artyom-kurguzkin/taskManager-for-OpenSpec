## ADDED Requirements

### Requirement: Statuses per project
Each project SHALL keep its own ordered list of statuses in its openspec/config.yaml under x-rtm.statuses, and the board's columns, the list's groups, the status pills and the commands that set a status SHALL use that list. A project without the line SHALL have the built-in list.

#### Scenario: A project with its own list
- **WHEN** a project's config.yaml lists statuses Backlog, Planned, In Progress, Review ready, Completed
- **THEN** its board shows those five columns in that order and a work item with status Review ready is in the fourth

#### Scenario: No list
- **WHEN** a project's config.yaml has no x-rtm.statuses
- **THEN** its board shows Backlog, Planned, Confirmed, In Progress, Blocked, In Review and Completed

### Requirement: Fixed statuses and removing a status
Backlog, In Progress and Completed SHALL always be in a project's list and SHALL NOT be removed or renamed; Completed still means archived. Any other status SHALL be removable, and only when no work item has it: otherwise the Settings page SHALL ask which status the work items move to, move them and then remove it.

#### Scenario: Fixed statuses stay
- **WHEN** the user looks at a project's status list in Settings
- **THEN** Backlog, In Progress and Completed have no remove button, and every status can be moved up and down

#### Scenario: Removing a status that work items have
- **WHEN** the user removes a status that work items have
- **THEN** the app asks which status they move to, moves them there and then removes the status

### Requirement: Merging status columns across projects
With several projects open and the board grouped by status, the board SHALL show one column per distinct status of the projects' lists, and the user SHALL be able to Shift-select two or more status column headers and choose Merge from a header menu. The merged column SHALL hold the work items of every merged status in every project and be named after the first selected column unless another name is typed. Unmerge on its header SHALL show the statuses as separate columns again.

#### Scenario: Merging completed and Completed
- **WHEN** one project has the status completed, another has Completed, and the user Shift-selects both headers and chooses Merge
- **THEN** one column Completed shows the work items of both statuses

#### Scenario: Unmerge
- **WHEN** the user chooses Unmerge on the merged column's header
- **THEN** the two statuses are separate columns again

#### Scenario: Dropping a card into a merged column
- **WHEN** the user drops a card into a merged column and its project has more than one of the merged statuses
- **THEN** the app asks which of them to use; with one it uses that one, and with none it says so and changes nothing

### Requirement: A merge is a view setting
A merge SHALL be saved per user and per board in the user's settings (BoardOptions.Merges), SHALL match statuses by their exact text in each project, and SHALL NOT change any project's files or work items.

#### Scenario: Nothing in the projects changes
- **WHEN** the user merges two status columns
- **THEN** the user's settings file records the merge and no project's config.yaml or work item changes

### Requirement: Look-alikes of the fixed statuses
A status that equals Backlog, In Progress or Completed when case, spaces and dashes are ignored (completed, InProgress, in progress) SHALL be offered in its column header menu as Merge with the fixed status, and the fixed status's column SHALL offer Merge look-alikes, each merging in one click.

#### Scenario: One click
- **WHEN** the user chooses Merge with Completed in the menu of the completed column
- **THEN** completed and Completed become one column called Completed
