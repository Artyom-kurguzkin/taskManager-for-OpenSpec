## ADDED Requirements

### Requirement: Hiding is the app's, never the repository's
The eye in the side menu, a column's "Hide column", a project's "Hide project" and the selection bar's "Delete (hide)" SHALL hide a column, a project or a work item in this app's settings, per repository, and SHALL write nothing under `openspec/`. A work item SHALL be hidden by its number within its repository, so the same number in another repository stays shown. The settings SHALL list the hidden work items (`HiddenItems`) beside the hidden projects and columns.

#### Scenario: Hide a work item from the selection bar
- **WHEN** the user selects a work item and clicks "Delete (hide)"
- **THEN** the item leaves the board and the list, the Hidden section of the side menu lists it with an eye, and no file under `openspec/` changes (the item's `.openspec.yaml` is byte for byte what it was)

#### Scenario: Restore a hidden work item
- **WHEN** the user clicks the eye beside the work item in the Hidden section
- **THEN** the item is back on the board and in the list, and the repository is still untouched

#### Scenario: Show all hidden
- **WHEN** "show all hidden" is on
- **THEN** a work item hidden in the app is drawn dashed like any hidden item, and a column or project hidden in the app stays in view with "(hidden)" after its name, so a hide is visible whatever the switch says; switching it off removes them

### Requirement: A resolution still hides, and says so
A work item an agent ended without doing it (`resolution`: cancelled, duplicate of #wiN, superseded by #wiN) SHALL stay hidden through `x-rtm.hidden`, which only a resolution (or an agent's `hidden`) writes. The Hidden section SHALL list such an item under the same heading, labelled by its resolution, and its page SHALL say it is hidden by that resolution.

#### Scenario: A cancelled item
- **WHEN** an agent resolves a work item as cancelled
- **THEN** it leaves the board, the Hidden section lists it as "#wiN title" labelled "Cancelled", and "show all hidden" draws it dashed

### Requirement: An old hidden flag counts as hidden in the app
A work item with `x-rtm.hidden` and no resolution note (hidden by an earlier version of the app) SHALL be treated as hidden in the app: the Hidden section SHALL list it as "#wiN title" with an eye like any item hidden in the app, and its page SHALL show the app-hidden banner and a Restore button. Restoring it SHALL clear the flag in its `.openspec.yaml` (the only write, because the flag is already there) and SHALL add nothing to the settings.

#### Scenario: Restore an item hidden by an old flag
- **WHEN** the user clicks the eye beside such an item in the Hidden section, or Restore on its page
- **THEN** `x-rtm.hidden` is gone from its `.openspec.yaml`, the settings list no hidden item, and the item is back on the board

#### Scenario: A resolution is not an old flag
- **WHEN** an item has `x-rtm.hidden` and a resolution note
- **THEN** it keeps the resolution behaviour: labelled by the resolution, no eye, its page names the resolution

### Requirement: The item page says where a hidden item comes back
The page of a work item hidden in the app (or by an old flag with no resolution) SHALL show "Hidden in this app. Restore it from the side menu's Hidden section." and a Restore button that shows it again. The page of an item hidden by a resolution SHALL say which resolution hid it. No hidden-item message SHALL mention openspec.

#### Scenario: Open an item hidden in the app
- **WHEN** the user opens a work item they hid
- **THEN** the banner reads "Hidden in this app. Restore it from the side menu's Hidden section."
