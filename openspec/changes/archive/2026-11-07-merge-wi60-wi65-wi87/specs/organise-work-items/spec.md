## MODIFIED Requirements

### Requirement: Create and delete features
When grouped by feature, "+ feature" at the end of the row of columns SHALL add a feature, listed in `openspec/config.yaml` under `x-rtm.features` (OpenSpec only accepts a capability spec once it has requirements). Every feature column SHALL offer "Delete feature…", which asks for confirmation and unlinks the feature from active work items, which then show as Unassigned. A feature listed in `x-rtm.features` is removed from there; a feature with a spec under `openspec/specs/` is deleted with its spec folder. When open changes still have a delta spec for the feature, the confirmation SHALL name them and their delta folders for it SHALL be deleted too, leaving each change valid; archived changes SHALL NOT be touched. Deleting SHALL be refused when the feature's folder holds other features.

#### Scenario: New feature
- **WHEN** the user adds feature "Exports" with "+ feature"
- **THEN** `x-rtm.features` lists `exports` and an empty Exports column appears at the end of the row

#### Scenario: Delete own feature
- **WHEN** the user deletes "Payments", which #wi21 links to, and confirms
- **THEN** the Payments column is gone, `payments` is removed from config.yaml, and #wi21 shows under Unassigned

#### Scenario: Delete a spec-backed feature
- **WHEN** the user deletes "Test links", which has the folder `openspec/specs/test-links`, and confirms
- **THEN** the dialog said the folder goes, the folder is deleted and the column leaves the board

#### Scenario: An open change still has a spec for it
- **WHEN** the user deletes a feature for which #wi123 has a delta spec, and confirms
- **THEN** the dialog said "#wi123 still describes it in its specs", that delta folder is deleted with the feature, and #wi123 still validates
