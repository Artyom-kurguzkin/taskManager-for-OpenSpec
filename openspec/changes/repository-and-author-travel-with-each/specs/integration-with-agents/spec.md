## ADDED Requirements

### Requirement: A tool call works on its own repository and signs as its agent
A board tool call SHALL read and write only the repository it names, without switching the repository the app shows, without adding it to the app's open projects, and SHALL sign only its own writes with the agent's name; edits made in the app at the same time SHALL stay signed by the app's user.

#### Scenario: Agent writes to another repository while the board loads
- **WHEN** an agent writes to repository B while the app's board loads repository A
- **THEN** the board shows repository A and repository B is not added to the open projects

#### Scenario: App edit during an agent call
- **WHEN** the user edits an item while an agent's call is running
- **THEN** the edit is signed with the user's name
