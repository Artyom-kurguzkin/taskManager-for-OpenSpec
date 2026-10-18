## MODIFIED Requirements

### Requirement: Edit header fields
Title, features, contributors, status and priority SHALL be editable in place. Each edit SHALL be saved when the control loses focus or a value is picked. The title SHALL be stored in `x-rtm.title`. Contributors who are commit authors of the change SHALL be shown without a remove control; assigned people SHALL be removable, and "+ add" SHALL offer the current user's name so a newcomer can assign themselves.

#### Scenario: Change status
- **WHEN** the user picks Blocked in the status control
- **THEN** `x-rtm.status` becomes Blocked and returning to the board shows the card under Blocked

#### Scenario: Remove a manual feature
- **WHEN** the user removes Security, which is listed in `x-rtm.features`
- **THEN** it is removed from `x-rtm.features`, while features from delta specs show no remove control

#### Scenario: Commit author stays
- **WHEN** Alice has commits in #wi123's folder and Tom is only assigned
- **THEN** Tom shows a remove control and Alice does not
