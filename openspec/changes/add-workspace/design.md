# Design

## Context

The repository is greenfield. The UI direction is fixed by `design/mockup.html` (the board, work item and note screens) and the original sketch in `docs/mockup.excalidraw`. The target repo's OpenSpec layout (CLI 1.14) is:

```
openspec/
  config.yaml
  specs/<group>/<capability>/spec.md          ← features
  changes/<name>/                             ← work items (active)
    .openspec.yaml                            ← schema, created, + x-rtm: block (ours)
    proposal.md  design.md  tasks.md          ← description / subtasks
    specs/<group>/<capability>/spec.md        ← delta specs = linked features
    notes/n<id>.md                            ← notes (add-notes)
  changes/archive/YYYY-MM-DD-<name>/          ← Completed work items
```

## Goals / Non-Goals

**Goals:**
- One process, one window, no server and no database.
- The repo stays valid for the `openspec` CLI after every write the app makes.

**Non-Goals:**
- Multi-user sync. Git is the sync mechanism.
- Editing `openspec/config.yaml` or the schema.

## Decisions

- **Photino.Blazor host instead of .NET MAUI.** MAUI has no Linux target. Photino runs the same Razor components in the OS webview (WebView2, WKWebView, WebKitGTK). Alternative: a local Blazor Server app opened in the browser, which works but has no native window or folder picker.
- **In-memory index with LINQ instead of DuckDB.** A repo has hundreds of markdown files at most, and parsing all of them takes milliseconds. Group, sort and filter are LINQ over a `List<WorkItem>`. DuckDB would add a native binary per platform for no gain at this size. Revisit only for full-text search across thousands of files.
- **Metadata in `.openspec.yaml` under `x-rtm:` instead of frontmatter in proposal.md.** The CLI already creates `.openspec.yaml` per change, and frontmatter would show up in proposal.md, which people also read on GitHub. The CLI ignores keys prefixed `x-` without a warning, so one `x-rtm:` key keeps its own keys untouched.
- **Line-level write-back.** Edits change only the affected line (a checkbox) or YAML key. YamlDotNet's representation model or a targeted line edit keeps comments. The file is rewritten only when the bytes on disk still match what was last read; otherwise the edit is rejected (spec: Conflicting edit).
- **FileSystemWatcher with a 300 ms debounce, re-parsing per change folder.** Paths the app itself just wrote are ignored for one debounce window.
- **Archive through the CLI.** Moving a work item to Completed runs `openspec archive <name> --yes` so that delta specs merge into the main specs exactly as the CLI does it. This requires `openspec` on PATH; if it is missing, Completed is disabled with an explanation.

## Risks / Trade-offs

- [The OpenSpec CLI changes its folder layout] → Keep all path knowledge in one `OpenSpecLayout` class and add a parser test against a fixture repo.
- [A future CLI version drops the `x-` key convention] → Fall back to a `.x-rtm.yaml` sidecar. Task 2.4 keeps a validate test that catches this.
- [The Linux webview (WebKitGTK) renders differently] → Check the board on Linux in CI with a screenshot test before release.
