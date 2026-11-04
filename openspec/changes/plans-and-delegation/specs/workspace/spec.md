## ADDED Requirements

### Requirement: Plans folder
Each repository SHALL have a plans folder, a per-user setting (Settings → Plans folder) relative to the repository, `DevResources/plans` unless changed, stored in the user's settings file and not in the repository. The agent conventions file SHALL state the folder, and changing the setting SHALL rewrite the conventions file in every open project at once, with a notice.

#### Scenario: Private plans
- **WHEN** the user sets the plans folder of a repository to `local/plans`
- **THEN** the setting survives a restart, other repositories keep `DevResources/plans`, and that repository's `openspec/REPO-TASKS.md` names `local/plans`

### Requirement: The conventions file states the workflow
The agent conventions file SHALL open with the workflow for a task that is more than a quick fix: research how the industry solves it, write a plan in the plans folder named `<title-slug>-<yyyy-MM-dd>.md` and link it with `x-rtm.plan`, split the work into testable chunks (subtasks or dependent work items), delegate them to cheaper models each in its own git worktree, verify and merge them and run the whole suite, keep the board current, and leave the result uncommitted for review.

#### Scenario: An agent reads it
- **WHEN** an agent opens `openspec/REPO-TASKS.md`
- **THEN** the first section tells it those steps and the plans folder to use
