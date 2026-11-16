## ADDED Requirements

### Requirement: Attachment images are read safely and once
The worklog and the note page SHALL show an attached image only when it is inside the repository (no junction leading out) and under the size limit, and SHALL NOT read attachment files again while the user types.

#### Scenario: Image through a junction
- **WHEN** a note's attachment resolves through a junction outside the repository
- **THEN** it is shown as a file name, not as an image

### Requirement: Opening a list pill does not examine its row
A plain click on a status, label or priority pill in the list SHALL open its picker without examining or selecting the row; a Shift-click SHALL still select the row.

#### Scenario: Pill click with the side menu expanded
- **WHEN** the user clicks a status pill in the list with the side menu expanded
- **THEN** the picker opens and the examined item is unchanged
