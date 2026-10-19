# Spec Delta

## Purpose

Shows the commits that implement each work item straight from git, and lets the user commit the app's edits with a traceable message.

## ADDED Requirements

### Requirement: Linked commits
The work item page SHALL list commits whose message contains `#wi<id>` as a whole token, showing the short hash and subject.

#### Scenario: Token match
- **WHEN** commits say "Add /health route #wi123" and "Fix #wi1234"
- **THEN** #wi123 lists only the first

### Requirement: Link commits by hand
The Linked commits panel SHALL offer "+ link commit", which lists the repository's most recent commits (short hash, subject, author) to pick from, narrowed by typing. Picked commits SHALL be stored in `x-rtm.commits` in the change's `.openspec.yaml` and listed with the ones that mention `#wi<id>`. Picked commits SHALL be removable; commits that mention `#wi<id>` SHALL NOT be.

#### Scenario: Pick a commit
- **WHEN** the user opens "+ link commit" on #wi123 and picks "a1b2c3d Fix uptime probe"
- **THEN** `x-rtm.commits` lists a1b2c3d and the commit shows under Linked commits with a remove control

### Requirement: Git identity
The default contributor for new work items and the author of new notes SHALL be the repository's `user.name`. When git or `user.name` is unavailable, the user SHALL be asked for a name once per repository.

#### Scenario: No git user
- **WHEN** `git config user.name` returns nothing
- **THEN** the app asks for a display name and uses it from then on

### Requirement: Commit app edits
The user SHALL be able to commit the uncommitted changes inside one work item's change folder from its page. The message SHALL be editable and default to "Update #wi<id> <title>". Only files in that change folder SHALL be staged.

#### Scenario: Scoped commit
- **WHEN** #wi123 and #wi104 both have unsaved-to-git edits and the user commits from #wi123
- **THEN** the commit contains only files under #wi123's folder and its message includes `#wi123`

### Requirement: Works without git
If the folder is not a git repository or git is missing, history panels SHALL say why they are empty, and every other feature SHALL keep working.

#### Scenario: Plain folder
- **WHEN** the opened folder has `openspec/` but no `.git`
- **THEN** Linked commits shows "Not a git repository" and the board works normally
