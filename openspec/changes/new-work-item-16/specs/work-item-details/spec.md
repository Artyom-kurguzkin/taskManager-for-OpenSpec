## ADDED Requirements

### Requirement: Notes edit in place
A note's title is its first `#` heading and SHALL be editable in the note header; a blank title SHALL be refused. The note page SHALL have no Edit or Done button: clicking the rendered note SHALL open the editor, and clicking outside the editor SHALL save and show the rendered note again.

#### Scenario: Retitle and edit
- **WHEN** the user changes the title field to "Decided on X", clicks the note text, types, and clicks outside the editor
- **THEN** the note file's first heading is "# Decided on X", the body holds the typed text, and the rendered note shows both

### Requirement: Paste an image as an attachment
While the note page is open, an image pasted from the clipboard SHALL become an attachment of the note, stored under `local/task-tracking/wi<item>/n<note>/` as `pasted-<timestamp>.<ext>`, with the same `.gitignore` handling as a picked file.

#### Scenario: Paste a screenshot
- **WHEN** the user presses Ctrl+V with a PNG on the clipboard
- **THEN** the note lists a new image attachment and the file exists under `local/task-tracking/`
