# Proposal

## Why

The mockup's work item page shows linked test suites with their last run result, and each test opens a note describing the black-box check a developer or Playwright performs. Without this, you can't tell from the board whether a work item is actually verified.

## What Changes

- Tests linked to a work item through `x-rtm.tests` (suite name plus test names, each test optionally pointing to a note).
- Last-run result per suite, read from a test results file in the repo.
- The Linked tests panel on the work item page, with pass/fail per test and click-through to its note.

## Capabilities

### New Capabilities
- `test-links`: linking tests to work items and showing their latest results.

### Modified Capabilities
- none

## Impact

- Depends on `add-workspace`, `add-work-item-editor` and `add-notes`.
- **Deferred.** Build this after the other five changes. The results source is still open; see design.md.
