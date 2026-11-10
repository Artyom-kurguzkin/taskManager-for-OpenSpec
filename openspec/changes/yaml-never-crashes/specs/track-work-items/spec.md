## ADDED Requirements

### Requirement: A broken file never takes the app down
A change folder whose `.openspec.yaml` does not parse SHALL still show as a work item, with a warning naming the file and the line, and writes to its metadata SHALL be refused until the file is fixed. A note whose frontmatter does not parse SHALL show its body with a warning. Files with mixed line endings SHALL be read. An error while opening a repository SHALL be logged and SHALL NOT end the app.

#### Scenario: An unquoted colon in a title
- **WHEN** a change's yaml has `title: Fix: the thing` and the user opens the repository
- **THEN** the app opens, the board shows the item with a warning, and the other items are unaffected

### Requirement: The app writes only valid YAML
Every value the app writes into a yaml file or a note's frontmatter SHALL be quoted when YAML needs it, so what the app writes always reads back identical.

#### Scenario: A title with a colon
- **WHEN** the user names an item "Fix: the thing #1"
- **THEN** the yaml holds the title quoted and the item reads back with that exact title

### Requirement: Common hand-made breakage is repaired
A value broken in a common way (an unquoted colon or hash) SHALL be read as text with a warning saying what was repaired, and the next save SHALL write it quoted.

#### Scenario: Repaired on read
- **WHEN** a change's yaml has `title: Fix: the thing` by hand
- **THEN** the item shows "Fix: the thing" with a repair warning, and renaming it writes the title quoted and clears the warning
