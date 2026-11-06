## ADDED Requirements

### Requirement: Deltas that do not fit the main spec are repaired
Before archiving, the app SHALL repair what the CLI would refuse and can be fixed mechanically. A feature with no spec yet counts as an empty spec. A MODIFIED requirement the main spec lacks becomes ADDED; a name that differs from the main spec's only in case or spacing takes the main spec's name; a RENAMED entry that cannot apply and a REMOVED entry for a requirement the same delta adds or changes are dropped; requirements outside any delta section become ADDED.

#### Scenario: Change modifies a feature that has no spec
- **WHEN** a change's delta has `## MODIFIED Requirements` for `module-boundaries`, `openspec/specs/module-boundaries/` does not exist, and the user completes the item
- **THEN** the item is archived, `openspec/specs/module-boundaries/spec.md` is created with the requirement, and no error is shown

#### Scenario: Name differs only in case
- **WHEN** a delta adds "Send Money" and the main spec has "Send money"
- **THEN** the block is archived as a modification of "Send money"

### Requirement: Completing an item never stops on its specs
If the CLI refuses a change after the repair, the app SHALL retry without validation and then without merging the specs, so the item completes. An item archived without merging SHALL get a note with the CLI's reason. Only a CLI that cannot run (Node.js missing, timeout) SHALL be reported to the user.

#### Scenario: Specs cannot be merged at all
- **WHEN** the CLI refuses the change even without validation
- **THEN** the item is archived with its specs left in its own folder, and a note "Archived without merging its specs" on the item gives the CLI's reason

#### Scenario: Node.js is missing
- **WHEN** the CLI cannot be started
- **THEN** the item stays where it was and the app says the OpenSpec CLI needs Node.js
