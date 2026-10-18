# Tasks

## 1. Storage

- [ ] 1.1 Index `notes/n<id>.md` files with frontmatter and allocate repo-wide note ids; verify the "Create a note" scenario against the fixture
- [ ] 1.2 Add notes to the fixture change and confirm `openspec validate --all` and `openspec archive` still pass on a temp copy

## 2. Editor and links

- [ ] 2.1 Build the note page with the editor, a Markdig preview, metadata, and debounced autosave; verify the "Autosave" scenario
- [ ] 2.2 Implement a Markdig extension for `[[path]]`, `#wi<id>` and `#n<id>` with resolution; verify the "Unresolved reference" and "Follow a link" scenarios with unit tests
- [ ] 2.3 Build the "Linked docs and commits" panel with manual entries stored in frontmatter; verify the "Live update" scenario
- [ ] 2.4 List notes and "Add note" on the work item page; verify the "Open note from work item" scenario
