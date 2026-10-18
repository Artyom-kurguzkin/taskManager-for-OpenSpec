# Tasks

## 1. Page and header

- [x] 1.1 Build the work item page layout and empty-state panels from the mockup; verify a bUnit test renders #wi123 from the fixture with every panel
- [x] 1.2 Implement inline editing of title, features, contributors, status and priority; verify the "Change status" and "Remove a manual feature" scenarios write the expected file lines
- [ ] 1.3 Add a back stack with Esc handling that keeps board state; verify the "Esc from page" scenario

## 2. Description and subtasks

- [x] 2.1 Implement the description editor over the proposal.md body; verify the title heading is untouched in a test
- [x] 2.2 Implement subtask tick and add; verify the "Add a subtask" scenario and the byte-level diff of tasks.md
- [x] 2.3 Implement subtask selection and "Move into a new work item" with renumbering; verify the "Split out" scenario against the fixture
