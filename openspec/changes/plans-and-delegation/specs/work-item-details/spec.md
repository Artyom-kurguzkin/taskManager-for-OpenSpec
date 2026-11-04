## ADDED Requirements

### Requirement: Plan link
A work item MAY link a plan with `x-rtm.plan`, a repository-relative path. The work item page SHALL show a plan field: a typed path is saved (slashes normalised), a blank clears it, and a path outside the repository or with `..` SHALL be refused with a message. While a plan is linked a chip SHALL show its file name and open the plan rendered inside the app.

#### Scenario: Link and open
- **WHEN** the user types `DevResources\plans\health-2026-11-05.md` in the plan field and clicks the chip
- **THEN** `x-rtm.plan` is `DevResources/plans/health-2026-11-05.md` and the plan opens rendered, headed by its path

### Requirement: Document page
`/doc/<path>` SHALL render the markdown file at that repository-relative path, with the path shown and a way back. A missing file, or a path that climbs out of the repository, SHALL show "not found".

#### Scenario: Outside the repository
- **WHEN** `/doc/../outside.md` is opened
- **THEN** the page says not found and reads nothing
