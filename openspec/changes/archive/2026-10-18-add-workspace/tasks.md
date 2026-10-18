# Tasks

## 1. Solution and shell

- [x] 1.1 Create `RepoTaskManager.sln` with a Photino.Blazor app project and a `RepoTaskManager.Tests` xunit project; verify `dotnet build` and `dotnet test` pass
- [x] 1.2 Add MudBlazor and a `MudTheme` built from the `:root` tokens in `design/mockup.html` (light + dark); verify the app window opens on Windows with Kalam/Nunito type and the paper background
- [x] 1.3 Add the open-folder picker and remember the last folder in a user settings file; verify reopening the app restores it and that a folder without `openspec/` shows the spec's message

## 2. Index

- [x] 2.1 Create a fixture repo under `tests/fixtures/sample-repo/openspec/` with grouped capabilities, active and archived changes, and one change without an `x-rtm:` block; verify it passes `npx @fission-ai/openspec validate --all`
- [x] 2.2 Parse changes, archived changes, capabilities and tasks.md into `WorkItem`, `Feature` and `Subtask` records; verify with tests against the fixture covering every scenario in the workspace spec
- [x] 2.3 Implement id allocation and duplicate detection; verify the "New number" and "Duplicate on disk" scenarios in tests
- [x] 2.4 Keep a regression test that `openspec validate --strict` passes on a fixture change with an `x-rtm:` block (verified manually on CLI 1.14.3: unprefixed keys warn, `x-` keys are accepted)

## 3. Write-back and sync

- [x] 3.1 Implement targeted writes for an `x-rtm:` key and a tasks.md checkbox; verify with a test that the rest of the file stays byte-for-byte identical
- [x] 3.2 Add stale-read detection; verify the "Conflicting edit" scenario in a test
- [x] 3.3 Add a debounced FileSystemWatcher that ignores the app's own writes; verify an outside edit shows within 2 s and the app's own write produces no notice
