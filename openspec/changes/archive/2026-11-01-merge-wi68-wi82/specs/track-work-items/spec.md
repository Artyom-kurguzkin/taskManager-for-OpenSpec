## MODIFIED Requirements

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

## MODIFIED Requirements

### Requirement: Keyboard hints
Pressing F while the cursor is not in a text field SHALL show a letter badge on every clickable element, subtask texts included; typing a badge's letters SHALL click that element. Typing them with Shift held SHALL Shift-click it instead, selecting a card or subtask, and keep the badges showing so several can be selected in a row. Pressing F again, or Esc, SHALL hide the badges. F SHALL never be a badge letter. In a text box, text area, dropdown or editable area, F SHALL type as usual. Holding F SHALL not toggle repeatedly, and Ctrl+Alt SHALL not toggle hints. Side menu buttons SHALL always get the same letters, whatever the page shows.

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

## ADDED Requirements

### Requirement: Keyboard hints
Pressing Ctrl+Alt, in either order, SHALL show a letter badge on every clickable element; typing a badge's letters SHALL click that element. Pressing Ctrl+Alt again, or Esc, SHALL hide the badges. Holding the keys SHALL not toggle repeatedly, and Alt on its own SHALL not show hints. Side menu buttons SHALL always get the same letters, whatever the page shows.

#### Scenario: Toggle hints
- **WHEN** the user presses Ctrl+Alt, then Ctrl+Alt again
- **THEN** the badges appear, then disappear

#### Scenario: Alt alone
- **WHEN** the user presses only Alt
- **THEN** no badges appear

## MODIFIED Requirements

### Requirement: Keyboard hints
Pressing F while the cursor is not in a text field SHALL show a letter badge on every clickable element; typing a badge's letters SHALL click that element. Pressing F again, or Esc, SHALL hide the badges. F SHALL never be a badge letter. In a text box, text area, dropdown or editable area, F SHALL type as usual. Holding F SHALL not toggle repeatedly, and Ctrl+Alt SHALL not toggle hints. Side menu buttons SHALL always get the same letters, whatever the page shows.

#### Scenario: Toggle hints
- **WHEN** the user presses F, then F again
- **THEN** the badges appear, then disappear

#### Scenario: Typing in a field
- **WHEN** the cursor is in the subtask box and the user presses F
- **THEN** "f" is typed and no badges appear

#### Scenario: Alt alone
- **WHEN** the user presses only Alt, or Ctrl+Alt
- **THEN** no badges appear

## ADDED Requirements

### Requirement: Saved toast
Every write to the repository made from a page SHALL show a "Saved" toast, unless a more specific toast was shown for it; it SHALL never show twice at once. A note's autosave SHALL show it once the text is on disk.

#### Scenario: Title
- **WHEN** the user changes a work item's title
- **THEN** a "Saved" toast appears

#### Scenario: Specific message wins
- **WHEN** the user links a dependency
- **THEN** the toast says what was linked, not "Saved"

## MODIFIED Requirements

### Requirement: Window look
The title bar and window border SHALL take the theme's colours, scrollbars SHALL be thin and in the theme's colours, and the window icon SHALL be the app icon (Artyom's signature) in the light theme or its inverted version in the dark theme, switching when the theme changes. The exe file SHALL carry the light icon. The title bar SHALL draw neither the icon nor the window title; it keeps dragging and the minimise, maximise and close buttons, and the taskbar and Alt-Tab still show the icon and title.

#### Scenario: Switch to dark
- **WHEN** the user picks Dark in Settings
- **THEN** the title bar turns dark and the window icon becomes the light-on-dark signature

#### Scenario: Bare title bar
- **WHEN** the app starts
- **THEN** the title bar shows only the minimise, maximise and close buttons, while the taskbar shows "Repo tasks" with the app icon
