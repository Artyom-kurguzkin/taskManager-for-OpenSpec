## ADDED Requirements

### Requirement: One reading of the project's config.yaml
The app SHALL read and write every x-rtm section of openspec/config.yaml through one reader, and a section that cannot be read SHALL fall back to its defaults with a warning on the board instead of stopping the project from opening.

#### Scenario: Broken statuses section
- **WHEN** the statuses section of config.yaml is not valid YAML
- **THEN** the project opens with the default statuses and the board warns which section could not be read

### Requirement: References link the same way everywhere
References written as #wiN, #nN and [[doc]] SHALL be found by one rule and SHALL link the same way on the note page, in the worklog and in documents; renumbering an item SHALL rewrite only exact references to it.

#### Scenario: Same note on three screens
- **WHEN** a note mentions #wi12, #n3 and [[guide]]
- **THEN** the note page, the worklog and a document show the same three links
