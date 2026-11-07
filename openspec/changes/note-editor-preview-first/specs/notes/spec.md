## ADDED Requirements

### Requirement: Note page edits like a description
The note page SHALL show the note rendered (wiki links, #wi and #n references resolved). Clicking the rendered note SHALL replace it with the same plain text box the work item description uses, focused. The note SHALL autosave shortly after typing stops, and leaving the text box SHALL save at once and show the rendering again. Pasting an image while editing SHALL attach it to the note.

#### Scenario: Read, edit, leave
- **WHEN** the user opens a note, clicks its text, types, and clicks outside
- **THEN** the note was rendered first, a plain text box took its place, the text was saved while typing and on leaving, and the rendering shows the new text
