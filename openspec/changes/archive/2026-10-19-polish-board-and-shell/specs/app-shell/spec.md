## ADDED Requirements

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

### Requirement: Window look
The title bar and window border SHALL take the theme's colours, scrollbars SHALL be thin and in the theme's colours, and the window icon SHALL be the app icon (Artyom's signature) in the light theme or its inverted version in the dark theme, switching when the theme changes. The exe file SHALL carry the light icon.

#### Scenario: Switch to dark
- **WHEN** the user picks Dark in Settings
- **THEN** the title bar turns dark and the window icon becomes the light-on-dark signature
