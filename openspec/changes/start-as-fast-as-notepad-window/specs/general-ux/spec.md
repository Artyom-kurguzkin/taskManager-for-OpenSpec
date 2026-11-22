## ADDED Requirements

### Requirement: The window appears before the repository is read

The app SHALL show its window with a skeleton of the board before any change folder is read. The board SHALL fill in as the headers arrive from a load off the window's thread; git, the duplicate scan and worklog tracking SHALL run after the first paint, and an agent call during the load SHALL wait for it.

#### Scenario: Start on a repository with many changes

- **WHEN** the app is started on a repository with hundreds of change folders
- **THEN** its window is up with the board's skeleton before any change folder is read, the cards appear once the headers are in while the contributor and commit filters still say loading, git becomes ready after the first paint, and the run log shows the marks main, window-created, first-render, headers-read, repo-loaded, git-ready and scan-done in that order, each with the milliseconds since the process started

#### Scenario: An agent calls during the load

- **WHEN** a board tool call reaches the agent endpoint while the project's headers are still being read
- **THEN** the call waits for the headers and answers from the loaded board

#### Scenario: A header sort saved with the list

- **WHEN** the list opens with a header sort saved in its settings and git becomes ready after the rows were first drawn
- **THEN** the rows are sorted as saved from the first draw and stay so after the git-ready redraw

### Requirement: Only what the first screen needs is read

The board SHALL be built from each change's yaml header, tasks.md and the newest worklog time only; bodies SHALL be read on demand into a bounded cache; an unchanged file SHALL not be parsed twice; a file written in the same instant as it was read SHALL be checked again.

#### Scenario: Building the board

- **WHEN** the board is built for a repository
- **THEN** only the yaml header, tasks.md and the worklog's newest time of each change are read, and no proposal, note body, event list or spec text is opened

#### Scenario: Opening one item

- **WHEN** one item's page is opened
- **THEN** that item's proposal, notes and events are read and nothing of the other items is

#### Scenario: Building again with nothing changed

- **WHEN** the board is built again and no file's name, size or modified time changed
- **THEN** no file is parsed again

#### Scenario: A write in the instant of a read

- **WHEN** a change's file is written within the same second as the board read it
- **THEN** the next build reads that change again

#### Scenario: The body cache is bounded

- **WHEN** more items' bodies are opened than the cache holds (64)
- **THEN** the cache never exceeds its bound and the least recently used item's bodies are dropped first
