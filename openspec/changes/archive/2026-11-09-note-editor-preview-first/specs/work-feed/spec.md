## ADDED Requirements

### Requirement: Note page edits like a description
The note page SHALL show the note rendered (wiki links, #wi and #n references resolved). Clicking the rendered note SHALL replace it with the same plain text box the work item description uses, focused. The note SHALL autosave shortly after typing stops, and leaving the text box SHALL save at once and show the rendering again. Pasting an image while editing SHALL attach it to the note.

#### Scenario: Read, edit, leave
- **WHEN** the user opens a note, clicks its text, types, and clicks outside
- **THEN** the note was rendered first, a plain text box took its place, the text was saved while typing and on leaving, and the rendering shows the new text

### Requirement: Editors fill their tile
While editing, the description's and the note's text box SHALL fill the height their tile has below its header and SHALL have no border, outline or background of their own; the tile is the frame.

#### Scenario: Long description
- **WHEN** the user clicks a long description to edit it
- **THEN** the text box reaches the bottom of the tile and shows no box of its own

### Requirement: Note title in the frontmatter
A note's title SHALL be stored in its frontmatter as `title:`. Changing the title SHALL write only that key and leave the body, including its first heading, unchanged. A note without `title:` SHALL read its title from its first heading.

#### Scenario: Retitle a note
- **WHEN** the user renames note #n18, whose body starts with "# 28 Oct — editor replaced", to "Separate title"
- **THEN** its frontmatter has `title: Separate title` and its body still starts with "# 28 Oct — editor replaced"
