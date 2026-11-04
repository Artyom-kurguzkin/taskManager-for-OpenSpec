## Why

Opening a folder that had never used OpenSpec was refused with "Run `openspec init` first". The app knows what the folder needs; the user should not have to leave it.

## What Changes

- Opening an existing folder without `openspec/` creates it: `config.yaml` (`schema: spec-driven` and a context stub naming the folder), `specs/` and `changes/archive/`, then opens it as usual (the agent guide is written too). A toast says what was set up.
- A folder that does not exist is still refused, naming it.

## Capabilities

### Modified Capabilities

- `workspace`: opening a repository.
