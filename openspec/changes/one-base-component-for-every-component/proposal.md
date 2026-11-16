# Proposal

## Why

Render control is decided per component today, so every screen redraws on every click and every changed parameter. The plan for #wi180 puts it in one place: a base component every component derives from. It starts in legacy mode, rendering exactly as today, so screens can move to the strict rules one at a time with render-budget tests.

## What Changes

- RtmComponent: IHandleEvent with an opt-in strict mode (no render after events unless Invalidate, exceptions to DispatchExceptionAsync), RenderKey, render counter per type behind the test isolation seam.
- Every .razor component inherits it through _Imports.razor; RepoPage derives from it.
- An architecture test checks every component derives from it, with an allowlist for strict mode.
