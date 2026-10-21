# Tasks

## 1. Storage

- [x] 1.1 Index `notes/n<id>.md` files with frontmatter and allocate repo-wide note ids; verify the "Create a note" scenario against the fixture
- [ ] 1.2 Add notes to the fixture change and confirm `openspec validate --all` and `openspec archive` still pass on a temp copy

## 2. Editor and links

- [x] 2.1 Build the note page with the editor, a Markdig preview, metadata, and debounced autosave; verify the "Autosave" scenario
- [ ] 2.2 Implement a Markdig extension for `[[path]]`, `#wi<id>` and `#n<id>` with resolution; verify the "Unresolved reference" and "Follow a link" scenarios with unit tests
- [x] 2.3 Build the "Linked docs and commits" panel with manual entries stored in frontmatter; verify the "Live update" scenario
- [x] 2.4 List notes as the change log with "+ Add entry" on the work item page; verify the "Open an entry" and "Add an entry" scenarios
- [x] 2.5 notes should go to change log
- [ ] 2.6 you should be able to move subtasks within the list to identify priorities
- [ ] 2.7 you should be able to attach screenshots to task descriptions
- [x] 2.8 "Edit in editor" button on the note page; asks which command to use the first time, remembered in Settings; verify the "First use" and "Already configured" scenarios
