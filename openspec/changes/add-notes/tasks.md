# Tasks

## 1. Storage

- [x] 1.1 Index `notes/n<id>.md` files with frontmatter and allocate repo-wide note ids; verify the "Create a note" scenario against the fixture
- [ ] 1.2 Add notes to the fixture change and confirm `openspec validate --all` and `openspec archive` still pass on a temp copy

## 2. Editor and links

- [x] 2.1 Build the note page with the editor, a Markdig preview, metadata, and debounced autosave; verify the "Autosave" scenario
- [ ] 2.2 Implement a Markdig extension for `[[path]]`, `#wi<id>` and `#n<id>` with resolution; verify the "Unresolved reference" and "Follow a link" scenarios with unit tests
- [x] 2.3 Build the "Linked docs and commits" panel with manual entries stored in frontmatter; verify the "Live update" scenario
- [ ] 2.4 List notes and "Add note" on the work item page; verify the "Open note from work item" scenario
- [ ] 2.5 notes should go to change log
- [ ] 2.6 you should be able to move subtasks within the list to identify priorities
- [ ] 2.7 you should be able to attach screenshots to task descriptions
