# Working rules for this repository

These apply to every change, every session.

## Every behaviour change

1. **Tests first.** Write the failing test before the implementation. Fast suite: `dotnet test TaskManager.Tests --filter "Category!=OpenSpecCli"`.
2. **Spec with the change.** An OpenSpec change folder under `openspec/changes/<name>/` (`.openspec.yaml` with `x-rtm` id/title/status, `proposal.md`, `tasks.md`, `specs/<capability>/spec.md`) in the same step as the code, validated with `npx -y @fission-ai/openspec@1.14.3 validate <name> --strict`. Compute the next `x-rtm.id` from the max over every `.openspec.yaml` at write time; the user creates and merges items concurrently.
3. **Verify UI in the real app**, one feature at a time, over the Chrome DevTools Protocol (not synthetic OS input), in light and dark; screenshots are the evidence. Tests alone are not enough.

## After the work: update the board

When a piece of work lands, find the work items it touches — the change you made **and** any older item whose topic it advances — in every status except Completed (archived), and update each:

- **Change-log note**: `openspec/changes/<change>/notes/n<id>.md`, id = max over every `notes/n*.md` in the repo + 1; frontmatter `id`, `by: Claude`, `created: yyyy-MM-ddTHH:mm:ss`; then `# <title>` and what changed, in plain words.
- **Linked tests**: `x-rtm.tests: [{suite: <test class short name>, test: "<display name>"}]` on one line in the change's `.openspec.yaml`. Names must match `TaskManager.Tests/TestResults/latest.trx`; refresh it with `dotnet test TaskManager.Tests --filter "Category!=OpenSpecCli" --logger "trx;LogFileName=latest.trx"`.
- **Linked commits**: `x-rtm.commits: [<7-char hash>]` for commits that don't mention `#wiN` (those link themselves). Never commit unasked; say which commits could be linked.
- **Spec and tasks text** of those items, where the work changed the behaviour they describe.
