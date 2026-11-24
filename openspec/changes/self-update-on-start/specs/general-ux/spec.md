## ADDED Requirements

### Requirement: The app keeps itself up to date
After its window shows, the app SHALL ask for the latest release of its repository and compare it with its own version without delaying the start; Settings SHALL show the version and the result. When a newer release exists, the app SHALL say so in a popup, download and check it, and replace itself on restart or at exit, keeping the previous copy for one rollback. Debug and development builds SHALL show their version and never update themselves.

#### Scenario: A newer release
- **WHEN** the app starts and the latest release is newer than its version
- **THEN** a popup says it is updating, the release is downloaded and checked, and Restart now or Later is offered

#### Scenario: No network
- **WHEN** the check cannot be made
- **THEN** Settings says why and the app works as before

#### Scenario: A development build
- **WHEN** a Debug or development build starts
- **THEN** Settings shows its version and that it never updates itself
