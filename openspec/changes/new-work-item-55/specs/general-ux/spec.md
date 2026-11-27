## ADDED Requirements

### Requirement: Ctrl+Shift+< folds and expands the side menu
Ctrl+Shift+< SHALL fold the side menu to its rail when it is expanded and expand it when it is folded, as the menu's own collapse button does, and that button's title SHALL name the key. The key SHALL work while the focus is in a text field and SHALL be matched on the physical key, so other keyboard layouts work.

#### Scenario: Ctrl+Shift+< folds and expands the menu
- **WHEN** the menu is folded and Ctrl+Shift+< is pressed, then pressed again
- **THEN** the menu expands (its toggle reports aria-expanded true) and then folds again

#### Scenario: The key works while typing
- **WHEN** the focus is in a text field and Ctrl+Shift+< is pressed
- **THEN** the menu changes and the field keeps its focus and its text

### Requirement: Ctrl+Shift+> opens a right panel
Ctrl+Shift+> SHALL open and close a right panel: an empty column of the window's own, closed when the window starts and not remembered across restarts. Open, it SHALL sit at the window's right edge beside the panes and push them, not cover them. The key SHALL work while the focus is in a text field. In a window too narrow for panes side by side the panel SHALL NOT show.

#### Scenario: Ctrl+Shift+> opens and closes the right panel
- **WHEN** Ctrl+Shift+> is pressed in a wide window, then pressed again
- **THEN** a panel headed Panel appears at the window's right edge with the panes narrowed beside it, and then goes away

#### Scenario: Split panes keep the panel at the edge
- **WHEN** the window is split into panes and the right panel is open
- **THEN** the panel is the last column at the window's right edge, after all the panes

#### Scenario: A narrow window has no right panel
- **WHEN** the window is narrower than the width at which panes sit side by side
- **THEN** the right panel is not shown
