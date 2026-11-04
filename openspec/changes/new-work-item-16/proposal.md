# Proposal

## Why

Change-log notes were awkward: every title said "New note" and could not be changed, editing needed an Edit button first, and a screenshot had to be saved to disk before it could be attached.

## What Changes

- The note's title (its first `#` heading) is an input in the note header; a blank title is refused.
- No Edit/Done buttons: clicking the rendered note opens the editor, clicking outside it saves and shows the rendered note again, as the work item description does.
- Pasting an image from the clipboard while on the note page attaches it (`local/task-tracking/wi<item>/n<note>/pasted-<timestamp>.<ext>`), like a file picked with "Attach file…".
