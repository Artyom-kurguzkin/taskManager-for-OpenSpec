# Proposal

## Why

The modules keep one current repository per process (OpenSpecStore Current, GitHistory RepoRoot) and one actor. Workspace.In swaps them without a lock, and the UI, the watcher timer and sync passes already race on them. The owner wants one process with the MCP endpoint in the app, which is impossible while an agent's call on repo B switches repo A under the window and signs the user's edits as the agent.

## What Changes

- The current repository and the actor become per call (AsyncLocal overlay or explicit root), set by Workspace.In and the board tools without touching other callers.
- A tool call no longer adds a repository to the open set.
- Features and LinkedTestRuns caches key on the repo generation, use TimeProvider and lock; GitHistory states lock.
