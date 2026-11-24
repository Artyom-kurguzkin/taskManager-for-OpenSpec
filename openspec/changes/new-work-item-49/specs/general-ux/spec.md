## ADDED Requirements

### Requirement: The side menu keeps secondary views behind icons
The side menu SHALL open the saved arrangements from an icon on the Organise header and show the colour legend only after its icon next to the colour dropdown is clicked; opening the current log file SHALL be an action of the Settings page's Logs folder row, not of the side menu.

#### Scenario: Saved arrangements from the Organise header
- **WHEN** the user clicks the arrangements icon on the Organise header
- **THEN** a popup lists the saved arrangements with open, delete, and a name box with save as

#### Scenario: The colour legend
- **WHEN** the board is coloured by a property
- **THEN** the legend is hidden until the icon next to the colour dropdown is clicked, and a second click hides it

#### Scenario: Opening the log
- **WHEN** the user opens Settings
- **THEN** the Logs folder row has Open current log and the side menu has no such entry
