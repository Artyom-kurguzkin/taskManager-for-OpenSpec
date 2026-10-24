## MODIFIED Requirements

### Requirement: Window look
The title bar and window border SHALL take the theme's colours, scrollbars SHALL be thin and in the theme's colours, and the window icon SHALL be the app icon (Artyom's signature) in the light theme or its inverted version in the dark theme, switching when the theme changes. The exe file SHALL carry the light icon. The title bar SHALL draw neither the icon nor the window title; it keeps dragging and the minimise, maximise and close buttons, and the taskbar and Alt-Tab still show the icon and title.

#### Scenario: Switch to dark
- **WHEN** the user picks Dark in Settings
- **THEN** the title bar turns dark and the window icon becomes the light-on-dark signature

#### Scenario: Bare title bar
- **WHEN** the app starts
- **THEN** the title bar shows only the minimise, maximise and close buttons, while the taskbar shows "Repo tasks" with the app icon
