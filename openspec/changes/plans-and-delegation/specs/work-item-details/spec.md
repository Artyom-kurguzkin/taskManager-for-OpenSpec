## ADDED Requirements

### Requirement: Plan link
A work item MAY link a plan with `x-rtm.plan`, a repository-relative path. While no plan is linked, the work item page SHALL offer "+ link plan", listing the markdown files in the plans folder (newest first, filterable); picking one links it. A linked plan SHALL show as a chip with its file name that opens the plan rendered inside the app, and a × that unlinks it (the file stays). `x-rtm.plan` SHALL refuse a path outside the repository or with `..`.

#### Scenario: Link and open
- **WHEN** the user picks `health-2026-11-05.md` from "+ link plan" and clicks the chip
- **THEN** `x-rtm.plan` is `DevResources/plans/health-2026-11-05.md` and the plan opens rendered, headed by its path

### Requirement: Document page
`/doc/<path>` SHALL render the markdown file at that repository-relative path, with the path shown and a way back. A missing file, or a path that climbs out of the repository, SHALL show "not found".

#### Scenario: Outside the repository
- **WHEN** `/doc/../outside.md` is opened
- **THEN** the page says not found and reads nothing
