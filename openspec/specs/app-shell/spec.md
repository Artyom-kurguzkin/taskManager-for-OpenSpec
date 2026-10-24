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
