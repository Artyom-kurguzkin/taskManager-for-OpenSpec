## MODIFIED Requirements

### Requirement: Edit header fields
Title, features, contributors, status, priority and label SHALL be editable in place. Every linked feature SHALL have a remove control; removing one that comes from the change's specs/ folder SHALL record it in `x-rtm.unlinkedFeatures` and leave the spec files alone, and "+ add" SHALL offer it again. Each edit SHALL be saved when the control loses focus or a value is picked. The title SHALL be stored in `x-rtm.title` and the label in `x-rtm.label` (values: bug, feature, enhancement, or none). Contributors SHALL be stored in `x-rtm.contributors`; while that list is absent, the git commit authors of the change SHALL stand in for it. Every contributor SHALL be removable, and "+ add" SHALL offer everyone who has ever committed to the repository (any branch), people already on other items and the current user, and SHALL accept any typed name. Commit authors whose names differ only in case and `-`, `_` or `.`, or whose emails differ only in case, SHALL be listed once, under the name they committed with most.

#### Scenario: Change status
- **WHEN** the user picks Blocked in the status control
- **THEN** `x-rtm.status` becomes Blocked and returning to the board shows the card under Blocked

#### Scenario: Remove a manual feature
- **WHEN** the user removes Security, which is listed in `x-rtm.features`
- **THEN** it is removed from `x-rtm.features`

#### Scenario: Unlink a spec feature
- **WHEN** the user removes Availability, which comes from the change's specs/ folder
- **THEN** it is added to `x-rtm.unlinkedFeatures`, disappears from the item and its board column, and the spec file is unchanged; choosing it in "+ add" removes it from that list

#### Scenario: Commit author stays
- **WHEN** Alice has commits in a change's folder and its `x-rtm.contributors` is absent
- **THEN** Alice shows as a contributor, with a remove control like everyone else

#### Scenario: Remove yourself
- **WHEN** the user removes the only contributor, who has commits on the change
- **THEN** `x-rtm.contributors` becomes `[]` and the item shows no contributors

#### Scenario: Add someone new
- **WHEN** the user opens "+ add", types "Zed" and presses Enter
- **THEN** Zed is added to `x-rtm.contributors`

#### Scenario: Offer the repo's committers
- **WHEN** Alice, Bob and Carol have commits anywhere in the repository and the user opens "+ add" on an item without them
- **THEN** Alice, Bob and Carol are offered

#### Scenario: Same person once
- **WHEN** someone committed as "Ann Lee" and, more often, as "ann-lee" under another email
- **THEN** "+ add" lists only "ann-lee"
