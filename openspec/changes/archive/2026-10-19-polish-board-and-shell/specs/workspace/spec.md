## MODIFIED Requirements

### Requirement: Work item metadata
The system SHALL read and write work item metadata in an `x-rtm:` mapping inside the change's `.openspec.yaml`, with the keys `id`, `title`, `status`, `priority`, `contributors`, `features`, `lanes` and `hidden`. `contributors` holds the people assigned in the app. A missing key SHALL fall back to a default: status Backlog, priority low, contributors empty.

#### Scenario: Change created outside the app
- **WHEN** a change folder has a `.openspec.yaml` without an `x-rtm:` block
- **THEN** the work item shows as Backlog with lower priority, and it gets an `id` written on its first edit in the app

#### Scenario: Unknown keys survive
- **WHEN** the app updates `x-rtm.status` in a file that also has other keys and comments
- **THEN** every other key, comment and line in that file stays byte-for-byte unchanged

### Requirement: Capabilities are features
The system SHALL treat every capability under `openspec/specs/`, plus every path listed in `openspec/config.yaml` under `x-rtm.features`, as a feature. When a path has two segments, the first segment SHALL be its side (for example `non-functional/availability` is Availability on side Non-Functional); a single-segment path SHALL be on side "Ungrouped". Features from `x-rtm.features` without a spec SHALL be marked as created in the app.

#### Scenario: Grouped feature
- **WHEN** `openspec/specs/functional/remittances/spec.md` exists
- **THEN** feature Remittances is listed on side Functional

#### Scenario: Feature created in the app
- **WHEN** `x-rtm.features` lists `functional/payments` and no spec exists for it
- **THEN** feature Payments is listed on side Functional and can be deleted from the app

## ADDED Requirements

### Requirement: Contributors from git
In a git repository, a work item's contributors SHALL be the authors of commits that changed its change folder (following it into the archive), newest first, followed by the people assigned in `x-rtm.contributors`, without duplicates. Outside git, contributors SHALL be the assigned people only. Commit authors SHALL be read with one `git log` over `openspec/changes` and re-read only when the branch head moves, so a new commit shows without reopening the repository.

#### Scenario: Same person once
- **WHEN** Artyom-kurguzkin committed to a change and is also assigned in `x-rtm.contributors`
- **THEN** the work item lists Artyom-kurguzkin once

#### Scenario: Newcomer
- **WHEN** someone who just cloned the repository (no commits yet) assigns themselves to #wi12
- **THEN** their git `user.name` is added to `x-rtm.contributors` and #wi12 shows them as a contributor
