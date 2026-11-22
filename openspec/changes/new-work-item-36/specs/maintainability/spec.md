# Spec Delta

## Purpose

A project keeps its plans and its images (note and description attachments) in folders of their own, each set from Settings by a typed path or the folder dialog.

## ADDED Requirements

### Requirement: Plans and images each have their own folder line in Settings
The project card in Settings SHALL show two lines, Plans and Images, each with a path field (relative to the repository; an absolute path inside the repository is shown relative), a Choose folder button that opens the operating system's folder dialog, and the folder in effect. Each folder SHALL be saved with the project in `openspec/config.yaml` (`x-rtm.plansDir` and `x-rtm.attachmentsDir`) independently of the other. The Settings page SHALL NOT offer a single folder for both.

#### Scenario: Two lines, no merged option
- **WHEN** the user opens Settings
- **THEN** every open project shows a Plans line and an Images line, each with a Choose folder button, and no field or link that puts both in one folder

#### Scenario: Choose a folder
- **WHEN** the user clicks Choose folder on the Images line and picks a folder inside the repository
- **THEN** the path field shows the folder relative to the repository and the line offers Move files here and Point here, and nothing is saved yet

#### Scenario: A folder that is not allowed
- **WHEN** the user types a folder inside openspec/ or outside the repository and clicks Point here
- **THEN** the line says why it is refused and the folder in effect does not change

### Requirement: Move files here and Point here
When the path on a line differs from the folder in effect, the line SHALL offer Move files here, which moves the existing files of that kind (and updates the notes, descriptions and plan links that name them) and then points the setting at the new folder, and Point here, which only changes the setting and leaves every existing file and link where it is. A move on one line SHALL NOT move or change the other kind.

#### Scenario: Move plans only
- **WHEN** the user types a new plans folder and clicks Move files here
- **THEN** the plans and the drawings beside them are in the new folder, work items' plan links point to it, the images folder and its files are untouched, and `plansDir` is written to config.yaml without an `attachmentsDir` line

#### Scenario: Point images only
- **WHEN** the user types a new images folder and clicks Point here
- **THEN** `attachmentsDir` is written, the existing images and their links stay where they are, and new images are saved in the new folder

### Requirement: A repository with the old one-folder key still resolves both folders
A repository whose config.yaml has the old `x-rtm.filesDir` key SHALL resolve its plans folder as `<filesDir>/plans` and its images folder as `<filesDir>/images` until `plansDir` or `attachmentsDir` is set; each of those keys, once set, SHALL win over the old key for its own kind. The app SHALL NOT write or remove `filesDir`.

#### Scenario: Old key
- **WHEN** config.yaml has `filesDir: files/all` and the user points the Plans line at `docs/plans`
- **THEN** plans resolve to `docs/plans`, images still resolve to `files/all/images`, and `filesDir` is still in config.yaml
