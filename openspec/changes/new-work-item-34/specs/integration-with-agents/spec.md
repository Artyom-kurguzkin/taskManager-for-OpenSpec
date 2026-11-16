## ADDED Requirements

### Requirement: Tools use the project's statuses
The board tools SHALL check a status against the status list of the project they act on (x-rtm.statuses, else the built-in list): board_update SHALL accept any status of that list except Completed, board_create any except Blocked, In Review and Completed, and an unknown status SHALL be refused with a message that names the project's allowed statuses.

#### Scenario: A status the project added
- **WHEN** an agent sets a status the project's list has, such as Review ready
- **THEN** the item gets it

#### Scenario: A status the project does not have
- **WHEN** an agent sets a status that is not in the project's list
- **THEN** the tool refuses it and the message lists the project's statuses
