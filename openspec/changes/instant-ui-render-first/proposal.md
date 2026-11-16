# Proposal

## Why

Every action that writes (arrow key, drag, status pill, subtask tick, note save) does its disk writes, lock waits of up to 5 s, directory walks of every change folder, a synchronous git status process and two or three full page reloads on the UI thread before the first frame. The arrow-key fix measured 180 to 275 ms key to redraw; a drag or a status change is slower and freezes the window. The owner wants every action to react instantly. Phase 1 of the render rules (#wi80, #wi89, #wi90, #wi91) made clicks that write nothing cheap; this item covers the actions that write.

## What Changes

- Pages apply a command's result to their in-memory projection and render before any disk write; the write runs on the thread pool, a failure rolls the projection back with a toast.
- A page skips the `Changed` echo of its own write (version match); agent, git and watcher changes still reload, one item where possible.
- Lock waits, the duplicate scan, `git status` and feature rescans leave the UI thread; `Workspace.Notify` gets a lock around its state.
- `BoardPage.Set` skips equal options and debounces the settings save; `Load` bumps the render versions only when the projection changed.
- config.yaml reads cached behind the store version; FeaturesCache keyed on the specs folder stamp.
- The verification routine: a used-board preset for the hidden app, list-row and swap-assert helpers, an exploratory script run after every integration.

Plan: DevResources/plans/instant-ui-2026-11-17.md. Research: local/research/ui-responsiveness-2026-11-17.md.
