## ADDED Requirements

### Requirement: Settings are tabs

The Settings page SHALL show a row of tabs: General, then one per open project named after it. General SHALL hold every setting that is not per project; a project's tab SHALL hold its Location, Plans, Images, Board storage and Statuses, in that order. The chosen tab SHALL be remembered in the window and SHALL NOT be saved in the settings file.

#### Scenario: Opening Settings

- **WHEN** the user opens Settings in a window that has not chosen a tab
- **THEN** the tabs are General and one per open project, General is chosen, and its rows are the settings that are not per project

#### Scenario: Choosing a project's tab

- **WHEN** the user chooses a project's tab
- **THEN** only that project's rows are shown, in the order Location, Plans, Images, Board storage, Statuses, and General's rows and the other projects' rows are not

#### Scenario: Coming back

- **WHEN** the user leaves Settings from a project's tab and opens Settings again in the same window
- **THEN** that project's tab is chosen; if the project is no longer open, General is

### Requirement: Every setting is a table row

Inside a tab every setting SHALL be one row of a two-column table, the label on the left and the control on the right, under the tab title with at most one short line of help. A setting SHALL NOT sit in a card or frame.

#### Scenario: A row

- **WHEN** a tab is shown
- **THEN** each setting is one row with its label in the left cell and its control in the right cell, and no card surrounds a setting
