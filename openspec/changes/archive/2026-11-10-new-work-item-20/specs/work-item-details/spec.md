## ADDED Requirements

### Requirement: New feature from the item page
The work item page's feature list SHALL list every feature and end with "+ New feature…". Picking it SHALL ask for the new feature as the board does, create it and set it on the item; cancelling SHALL leave the item unchanged.

#### Scenario: Create and set
- **WHEN** the user picks "+ New feature…" on #wi115 and names it "Exports"
- **THEN** the feature "Exports" exists and #wi115's feature is "Exports"
