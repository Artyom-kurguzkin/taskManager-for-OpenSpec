## ADDED Requirements

### Requirement: A folder without openspec/ is set up on open
Opening an existing folder that has no `openspec/` SHALL create `openspec/config.yaml` (`schema: spec-driven` with a context stub), `openspec/specs/` and `openspec/changes/archive/`, open the folder as the workspace and tell the user what was set up. The result SHALL be valid for the OpenSpec CLI. A folder that does not exist SHALL be refused with a message naming it.

#### Scenario: First open
- **WHEN** the user opens a project folder that has only source code
- **THEN** `openspec/` exists with those files, the board is empty, a notice says it was set up, and the first work item created there validates with `openspec validate --all --strict`

#### Scenario: Wrong path
- **WHEN** the user opens a path that does not exist
- **THEN** nothing is created, the previous workspace stays, and the message names the path
