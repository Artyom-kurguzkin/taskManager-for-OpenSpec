## ADDED Requirements

### Requirement: Keyboard hints
Pressing Ctrl+Alt, in either order, SHALL show a letter badge on every clickable element; typing a badge's letters SHALL click that element. Pressing Ctrl+Alt again, or Esc, SHALL hide the badges. Holding the keys SHALL not toggle repeatedly, and Alt on its own SHALL not show hints. Side menu buttons SHALL always get the same letters, whatever the page shows.

#### Scenario: Toggle hints
- **WHEN** the user presses Ctrl+Alt, then Ctrl+Alt again
- **THEN** the badges appear, then disappear

#### Scenario: Alt alone
- **WHEN** the user presses only Alt
- **THEN** no badges appear
