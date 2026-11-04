# Proposal

## Why

Notes could not carry images, files or links to resources, and anything heavy must stay out of the repository.

## What Changes

- A note has attachments (`attachments:` in its frontmatter): files and links. A file is copied to `local/task-tracking/wi<item>/n<note>/`; the app adds `local/` to `.gitignore` when missing, so attachments never enter git. A link is kept as a URL.
- The note page lists them: images inline, files by name (opening in the OS), links as links, each removable; "Attach file…" opens a file picker and a field adds a link.
- `openspec/REPO-TASKS.md` tells agents where attachments go.
