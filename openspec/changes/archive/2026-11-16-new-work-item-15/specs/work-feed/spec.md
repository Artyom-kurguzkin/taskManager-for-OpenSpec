## ADDED Requirements

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
