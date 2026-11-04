## ADDED Requirements

### Requirement: Attachments on notes
A note MAY have attachments, listed in its frontmatter as `attachments:` entries: repository-relative file paths or http(s) URLs. Attaching a file SHALL copy it to `local/task-tracking/wi<item>/n<note>/<name>` (a taken name gets `-2`, `-3`…) and SHALL add `local/` to the repository's `.gitignore` when it is not ignored yet, so attachments never enter git. Attaching a link SHALL accept only absolute http(s) URLs. Removing an attachment SHALL forget it and delete a copied file.

#### Scenario: Attach a screenshot
- **WHEN** the user attaches `shot.png` to note #n44 of #wi85 in a repository whose `.gitignore` has no `local/` line
- **THEN** the file is at `local/task-tracking/wi85/n44/shot.png`, `.gitignore` ends with `local/`, and the note's frontmatter lists that path

### Requirement: Attachments on the note page
The note page SHALL list a note's attachments: an image inline, another file by its name opening in the operating system, a URL as a link, each with a remove control. "Attach file…" SHALL open the system file picker; a field SHALL add a link.

#### Scenario: Add a link
- **WHEN** the user types `https://example.com/spec` in the link field and adds it
- **THEN** the note lists it as a link and its frontmatter has the URL
