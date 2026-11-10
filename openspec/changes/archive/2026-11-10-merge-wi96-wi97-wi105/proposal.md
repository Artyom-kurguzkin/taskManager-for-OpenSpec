# Proposal

## Why

Completing an item failed with the CLI's raw text: "module-boundaries: target spec does not exist; only ADDED requirements are allowed for new specs". The change modified a requirement of a feature whose spec was never created. The user's rule: the app handles such cases itself; an archive error never reaches them.

## What Changes

- The repair before archiving covers every refusal the CLI has that can be fixed mechanically: a MODIFIED or RENAMED requirement against a spec or requirement that does not exist, names that differ only in case or spacing, a requirement both removed and changed, duplicate and unpaired entries, requirements outside any delta section. A feature with no spec yet counts as an empty spec.
- When the CLI still refuses, the app retries without validation, then without merging the specs; the item completes either way. An item archived without its specs gets a note that says so and why.
- Only "the OpenSpec CLI could not run" (Node.js missing, timeout) is still shown.

## Capabilities

### Modified Capabilities

- `track-work-items`: completing an item never stops on its specs.
