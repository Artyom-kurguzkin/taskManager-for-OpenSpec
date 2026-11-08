## MODIFIED Requirements

### Requirement: Attachments on notes
A note MAY have attachments, listed in its frontmatter as `attachments:` entries: repository-relative file paths or http(s) URLs. Attaching a file SHALL copy it to `<attachments folder>/wi<item>/n<note>/<name>` in the repository the note lives in (a taken name gets `-2`, `-3`…); the attachments folder is set per project in Settings and defaults to `local/task-tracking`. The app SHALL add the folder (`local/` for the default) to that repository's `.gitignore` when it is not ignored yet, so attachments never enter git. Attaching a link SHALL accept only absolute http(s) URLs. Removing an attachment SHALL forget it and delete a copied file.

#### Scenario: Attach a screenshot
- **WHEN** the user attaches `shot.png` to note #n44 of #wi85 in a repository whose `.gitignore` has no `local/` line
- **THEN** the file is at `local/task-tracking/wi85/n44/shot.png`, `.gitignore` ends with `local/`, and the note's frontmatter lists that path

#### Scenario: Paste while another project is open
- **WHEN** two projects are open, demo2's attachments folder is `files/shots`, and the user pastes an image into a demo2 note
- **THEN** the file is under demo2's `files/shots/wi<item>/n<note>/`, nothing is written to the other project, and the note lists that path

## ADDED Requirements

### Requirement: Folders per project in Settings
Settings SHALL list every open project under its name with its location (the repository path, with a control that shows it in the file manager), its plans folder and its attachments folder, each relative to that repository; empty means the default (`DevResources/plans`, `local/task-tracking`). A change SHALL apply to that project only and SHALL rewrite that project's `openspec/REPO-TASKS.md`, whose first line names both folders.

#### Scenario: Every location shown
- **WHEN** demo and demo2 are open and the user opens Settings
- **THEN** each project's row is headed by its name and shows its own folder path, and the top row is an empty "Open another repository" box

#### Scenario: Two projects, two attachments folders
- **WHEN** demo and demo2 are open and the user sets demo2's attachments folder to `files/shots`
- **THEN** demo2's agent guide names `files/shots`, demo keeps `local/task-tracking`, and a note attachment in each project goes to its own folder
