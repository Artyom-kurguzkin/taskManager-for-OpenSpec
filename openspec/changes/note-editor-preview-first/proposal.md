# Proposal

Notes editor looks busy and sluggish in comarison to description. It should feel the same as the description of work items one.

## Why

The note page used Radzen's markdown editor (toolbar, design and source modes). Next to the work item description, a plain text box, it looked busy and felt slow.

## What Changes

- The note page shows the rendered note (wiki links, #wi and #n references resolved). Clicking it opens the same plain text box as the description; leaving it saves and renders again. Autosave while typing stays.
- Radzen's markdown editor and its styles are gone from the note page.
- Strikethrough and task lists still render in previews (Markdig extras).

## Capabilities

### Modified Capabilities

- `notes`: the note page.
