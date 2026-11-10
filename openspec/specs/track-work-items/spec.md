# app-shell Specification

## Purpose
The frame around every page: the side menu (or header bar on narrow windows) used to move around the app and set the board's view, plus the window's look (title bar, icon, scrollbars) in both themes.

## Requirements

### Requirement: Side menu
The app SHALL have a left side menu instead of a top header. Collapsed, it SHALL be a narrow icon rail with: ☰ (expand), a Board/List layout dropdown showing the current layout's icon, "+ new work item", and Settings at the bottom. Expanded (☰), it SHALL show labels, the repository name, the board's view options and the hidden-column list. Page content SHALL start at the top of the window.

#### Scenario: Expand
- **WHEN** the user clicks ☰ on the board
- **THEN** the menu widens to show labels, group by / sort by / contributor, the subtask and compact options, and any hidden columns

#### Scenario: New work item from anywhere
- **WHEN** the user clicks + in the side menu
- **THEN** a Backlog work item assigned to them is created and its page opens

### Requirement: Small windows
On narrow windows (tablet and phone widths, including half and quarter of a desktop screen) the side menu SHALL lay out as a top header bar, where a view icon shows the view options as a row under it. The board, list, work item and note pages SHALL stay usable: the list keeps id, title, status and priority, and the work item panels stack in one column.

#### Scenario: Quarter screen
- **WHEN** the window is a quarter of the screen
- **THEN** the controls fit in one header row and the board scrolls sideways

### Requirement: Window sizing
The window SHALL open maximized and DPI-aware (PerMonitorV2). The content area SHALL fill the window exactly, with no page-level scrollbar; individual pages SHALL scroll internally.

#### Scenario: Maximized start
- **WHEN** the app starts
- **THEN** the window fills the screen and the board content extends to the window edges with no gap

### Requirement: Window look
The title bar and window border SHALL take the theme's colours, scrollbars SHALL be thin and in the theme's colours, and the window icon SHALL be the app icon (Artyom's signature) in the light theme or its inverted version in the dark theme, switching when the theme changes. The exe file SHALL carry the light icon. The title bar SHALL draw neither the icon nor the window title; it keeps dragging and the minimise, maximise and close buttons, and the taskbar and Alt-Tab still show the icon and title.

#### Scenario: Switch to dark
- **WHEN** the user picks Dark in Settings
- **THEN** the title bar turns dark and the window icon becomes the light-on-dark signature

#### Scenario: Bare title bar
- **WHEN** the app starts
- **THEN** the title bar shows only the minimise, maximise and close buttons, while the taskbar shows "Repo tasks" with the app icon

### Requirement: Keyboard hints
Keyboard hints SHALL be a setting, off by default, switched on in Settings. While off, F SHALL be an ordinary key and no badges SHALL show. While on: pressing F while the cursor is not in a text field SHALL show a letter badge on every clickable element, subtask texts included; typing a badge's letters SHALL click that element. Typing them with Shift held SHALL Shift-click it instead, selecting a card or subtask, and keep the badges showing so several can be selected in a row. Pressing F again, or Esc, SHALL hide the badges. F SHALL never be a badge letter. In a text box, text area, dropdown or editable area, F SHALL type as usual. Holding F SHALL not toggle repeatedly, and Ctrl+Alt SHALL not toggle hints. Side menu buttons SHALL always get the same letters, whatever the page shows.

#### Scenario: Off by default
- **WHEN** the user has never touched the setting and presses F on the board
- **THEN** no badges appear

#### Scenario: Switched on in Settings
- **WHEN** the user ticks "Keyboard hints" in Settings and presses F on the board
- **THEN** the badges appear; unticking it hides any badges and F does nothing again

#### Scenario: Toggle hints
- **WHEN** the user presses F, then F again
- **THEN** the badges appear, then disappear

#### Scenario: Typing in a field
- **WHEN** the cursor is in the subtask box and the user presses F
- **THEN** "f" is typed and no badges appear

#### Scenario: Alt alone
- **WHEN** the user presses only Alt, or Ctrl+Alt
- **THEN** no badges appear

#### Scenario: Select with Shift
- **WHEN** the badges are showing and the user types Shift+A, then Shift+S, where A and S are on two cards
- **THEN** both cards are selected, neither opens, and the badges stay until F or Esc

### Requirement: Saved toast
Every write to the repository made from a page SHALL show a "Saved" toast, unless a more specific toast was shown for it; it SHALL never show twice at once. A note's autosave SHALL show it once the text is on disk.

#### Scenario: Title
- **WHEN** the user changes a work item's title
- **THEN** a "Saved" toast appears

#### Scenario: Specific message wins
- **WHEN** the user links a dependency
- **THEN** the toast says what was linked, not "Saved"

### Requirement: Deltas that do not fit the main spec are repaired
Before archiving, the app SHALL repair what the CLI would refuse and can be fixed mechanically. A feature with no spec yet counts as an empty spec. A MODIFIED requirement the main spec lacks becomes ADDED; a name that differs from the main spec's only in case or spacing takes the main spec's name; a RENAMED entry that cannot apply and a REMOVED entry for a requirement the same delta adds or changes are dropped; requirements outside any delta section become ADDED.

#### Scenario: Change modifies a feature that has no spec
- **WHEN** a change's delta has `## MODIFIED Requirements` for `module-boundaries`, `openspec/specs/module-boundaries/` does not exist, and the user completes the item
- **THEN** the item is archived, `openspec/specs/module-boundaries/spec.md` is created with the requirement, and no error is shown

#### Scenario: Name differs only in case
- **WHEN** a delta adds "Send Money" and the main spec has "Send money"
- **THEN** the block is archived as a modification of "Send money"

### Requirement: Completing an item never stops on its specs
If the CLI refuses a change after the repair, the app SHALL retry without validation and then without merging the specs, so the item completes. An item archived without merging SHALL get a note with the CLI's reason. Only a CLI that cannot run (Node.js missing, timeout) SHALL be reported to the user.

#### Scenario: Specs cannot be merged at all
- **WHEN** the CLI refuses the change even without validation
- **THEN** the item is archived with its specs left in its own folder, and a note "Archived without merging its specs" on the item gives the CLI's reason

#### Scenario: Node.js is missing
- **WHEN** the CLI cannot be started
- **THEN** the item stays where it was and the app says the OpenSpec CLI needs Node.js

### Requirement: Change folder names never collide
A new change folder (created, merged or split) SHALL take a name that no folder under `openspec/changes/` or `openspec/changes/archive/` (ignoring the date prefix) has, numbering duplicates `-2`, `-3`, …. Completing an item whose name was already archived today SHALL rename its folder to a free name before the CLI archives it, keeping its `x-rtm.id`.

#### Scenario: Archived name reused the same day
- **WHEN** `archive/2026-11-05-new-work-item-13` exists and the user completes another item whose folder is `new-work-item-13`
- **THEN** the folder is renamed `new-work-item-13-2`, the archive succeeds, and the item keeps its number

### Requirement: Delta specs are repaired before archiving
Before archiving, the app SHALL repair the change's delta specs so the CLI never refuses them: an ADDED requirement the main spec already has becomes a MODIFIED block carrying every main-spec scenario; a requirement listed twice in one delta is merged into one block; a REMOVED requirement the main spec no longer has is dropped; a MODIFIED block gets the scenarios it lacks.

#### Scenario: Two changes add the same requirement
- **WHEN** two open changes both add "Export" and the user completes them one after the other
- **THEN** both archive, and the main spec has "Export" once with both changes' scenarios

### Requirement: Note ids, subtask numbers and subgroups never collide
A new note SHALL take the next id after every note in the repository, archived ones included. Two notes with one id SHALL resolve on read: the newer takes the next free id and its file follows; a frontmatter id that disagrees with the file name follows the file name. Duplicate subtask numbers SHALL read as the next free number and be renumbered on the next write. Adding a subgroup that exists SHALL be a no-op.

#### Scenario: Agent wrote a stale note number
- **WHEN** an agent adds `notes/n40.md` to a change while `n40.md` already exists in an archived change
- **THEN** the newer note becomes `n<next free>.md` with matching frontmatter and nothing else changes

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
