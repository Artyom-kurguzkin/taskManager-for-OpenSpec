## ADDED Requirements

### Requirement: Adding a property suggests other projects' values
When the user adds a status or a feature to one project, the app SHALL offer the values other open projects have and this project lacks, alongside a choice to type a new name; with nothing to suggest it SHALL ask for the name directly. The add button SHALL stay clear of the window's buttons.

#### Scenario: Picking a status from another project
- **WHEN** the user adds a status to a project that lacks Planned while another open project has it
- **THEN** Planned is offered, and picking it adds it to that project only

#### Scenario: Typing a new status
- **WHEN** the user picks New status
- **THEN** the usual name prompt opens
