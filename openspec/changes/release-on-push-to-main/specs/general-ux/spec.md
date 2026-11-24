## ADDED Requirements

### Requirement: Every push to main is released
Every push to the main branch SHALL build the app, run the fast tests and publish a GitHub Release tagged v<major.minor.patch>, the version coming from version.json and the commit height, with one asset named RepoTaskManager-win-x64.zip holding the published app; builds of other branches SHALL carry a prerelease suffix and SHALL NOT be released.

#### Scenario: A push to main
- **WHEN** a commit is pushed to main and the tests pass
- **THEN** a release v0.1.N with RepoTaskManager-win-x64.zip appears on the repository's Releases page

#### Scenario: The release already exists
- **WHEN** the workflow runs again for a version that is already released
- **THEN** it creates nothing and succeeds
