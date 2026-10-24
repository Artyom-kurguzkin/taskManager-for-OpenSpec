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
