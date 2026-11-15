## ADDED Requirements

### Requirement: One files folder per project

A project SHALL have one files folder, saved with the project as `filesDir` under `x-rtm` in openspec/config.yaml (default `attachments`). Inside it the app SHALL keep `images` (what notes and descriptions attach) and `plans`. Only `images` SHALL be kept out of git and synced through the board storage; `plans` SHALL stay in git. A project that never chose a files folder SHALL keep its old plans and attachments folders.

#### Scenario: Images and plans under the files folder

- **WHEN** a project has the files folder `attachments`
- **THEN** new images go to `attachments/images`, new plans to `attachments/plans`, and only `attachments/images/` is ignored by git

#### Scenario: A project that never chose

- **WHEN** a project has no `filesDir`
- **THEN** its plans and attachments stay in their old folders and Settings offers Put both in one folder

### Requirement: Choosing the files folder moves what exists and rewrites links

When the files folder is changed and images or plans already exist, the app SHALL ask before moving. On Move it SHALL move the images and the plans to the new places and rewrite the links that point to them: `x-rtm.plan`, `x-rtm.docs`, `[[...]]` references and markdown links. Moving plans SHALL never overwrite a file: an identical file is dropped and a different one is kept beside it. Keep them where they are SHALL change only where new images and plans go.

#### Scenario: Move

- **WHEN** the files folder is changed and Move is chosen
- **THEN** images and plans are in the new folder, and notes show their images, work items open their plans and doc links still resolve

#### Scenario: Keep them where they are

- **WHEN** the files folder is changed and Keep them where they are is chosen
- **THEN** existing files stay where they are and new images and plans go to the new folder

#### Scenario: A file already there

- **WHEN** a plan to move has the same name as a different file at the target
- **THEN** neither is overwritten and both are kept

### Requirement: No default board view setting

The settings page SHALL NOT offer a default board view, and settings files that still hold one SHALL load without error.

#### Scenario: Settings page

- **WHEN** the Settings page is open
- **THEN** it has no Default board view row

#### Scenario: An old settings file

- **WHEN** a settings file written before this change is loaded
- **THEN** it loads and the old value is ignored

### Requirement: A project's folders and its board storage are one group

The Settings page SHALL show a project's files folder and its Board storage as one group, with no dividing line between them.

#### Scenario: Files folder above Board storage

- **WHEN** a project's settings are shown
- **THEN** the Board storage section follows the files folder row without a divider
