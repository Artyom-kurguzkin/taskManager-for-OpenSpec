## ADDED Requirements

### Requirement: Note page reads first, edits on request
The note page SHALL open with the note rendered (wiki links, #wi and #n references resolved) and an Edit button. Edit SHALL replace the rendering with a markdown editor that has a toolbar, a design mode and a source mode, and SHALL autosave as the user types; Done SHALL save at once and show the rendering again.

#### Scenario: Read, edit, done
- **WHEN** the user opens a note, presses Edit, types, and presses Done
- **THEN** the note was rendered first, the editor took its place, the text was saved while typing and on Done, and the rendering shows the new text
