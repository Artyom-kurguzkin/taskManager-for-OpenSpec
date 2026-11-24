## ADDED Requirements

### Requirement: The description editor previews its images
While the user edits a work item's description, the editor SHALL show a preview of each image the text embeds, as the note and bug editors show their attachments.

#### Scenario: Editing a description with an image
- **WHEN** the user edits a description that embeds a pasted image
- **THEN** the image's preview is shown under the editor

#### Scenario: Pasting an image while editing
- **WHEN** the user pastes an image into the description editor
- **THEN** its preview appears at once
