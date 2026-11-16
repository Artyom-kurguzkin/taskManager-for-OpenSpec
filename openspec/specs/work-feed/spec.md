# notes Specification

## Purpose
TBD - created by archiving change note-editor-preview-first. Update Purpose after archive.

## Requirements

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

### Requirement: One scrollbar
Every scrollable area of the app SHALL use one thin, rounded scrollbar drawn from the colour tokens, in both themes; areas that hide their scrollbar keep doing so.

#### Scenario: A long description
- **WHEN** the description in the editor is longer than the window
- **THEN** the editor shows the app's thin scrollbar, not the system one

### Requirement: The description editor fills the window
While the description is edited, the editor SHALL run from just under the page header to the bottom of the window and follow the window's size.

#### Scenario: Open the editor
- **WHEN** the user Shift-clicks the description and picks Edit
- **THEN** the editor's bottom is at the bottom of the window

### Requirement: Only the user's text
The app SHALL NOT show or ask for empty "Why" and "What Changes" sections: the editor, card, item page and feed show the user's text only, saving writes only that text, and a new work item starts with an empty description. Non-empty sections stay.

#### Scenario: Edit an old item
- **WHEN** an item's proposal has the user's text followed by empty Why and What Changes sections and the user edits it
- **THEN** the editor shows only their text and the saved file has no empty sections, and the item still passes openspec validate --strict
