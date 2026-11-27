## ADDED Requirements

### Requirement: Choosing a theme
Settings SHALL offer the themes System, Light, Dark, Novita and Ayu Mirage. The choice SHALL be saved in the user's settings, applied at once, and applied again at the next start before the first paint. System SHALL follow the operating system's light or dark preference and change with it. A saved theme the app does not know SHALL show as Light.

#### Scenario: Choose a theme
- **WHEN** the user picks Ayu Mirage in Settings and restarts the app
- **THEN** the app shows the Ayu Mirage colours at once and again after the restart

#### Scenario: Follow the system
- **WHEN** the theme is System and the operating system switches to dark
- **THEN** the app shows the Dark theme

### Requirement: A theme is one set of colours
Every colour of the app SHALL come from the chosen theme's set of named colours; each theme SHALL set every one of them and say whether it is light or dark. The title bar SHALL take its colours from the theme, and its icon SHALL be the light one on a dark title bar and the dark one on a light title bar.

#### Scenario: Novita
- **WHEN** the theme is Novita
- **THEN** the page is light with plum outlines, and the title bar is plum with white text and the light icon
