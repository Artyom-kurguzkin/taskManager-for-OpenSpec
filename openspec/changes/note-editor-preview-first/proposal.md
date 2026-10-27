## Why

The note page showed a raw textarea beside a preview. A note is read far more often than written, and the editor should be a real one.

## What Changes

- The note page opens with the rendered note (wiki links, #wi and #n references resolved). Edit replaces it with Radzen's markdown editor — toolbar, design mode with a source switch — which autosaves as you type; Done saves at once and shows the rendered note again.
- Strikethrough and task lists render in previews (Markdig extras), since the toolbar can write them.

## Capabilities

### Modified Capabilities

- `notes`: the note page.