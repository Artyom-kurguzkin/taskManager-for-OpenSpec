## ADDED Requirements

### Requirement: Images in the description
Pasting an image into the description editor SHALL save it as `pasted-<yyyyMMdd-HHmmss>.<ext>` under the project's attachments folder in `wi<item>/` (a taken name gets `-2`, `-3`…; the folder is git-ignored) and SHALL insert `![](<path>)` with that repository-relative path at the caret. In view mode the description SHALL show the image, scaled to the tile's width.

#### Scenario: Paste a screenshot
- **WHEN** the user pastes a screenshot into #wi119's description and clicks outside
- **THEN** the file is under local/task-tracking/wi119/, the description holds a link to it, and the image shows in the description
