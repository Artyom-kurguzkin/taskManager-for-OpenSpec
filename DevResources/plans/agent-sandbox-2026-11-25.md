# Agent sandbox: Claude Code in a locked Docker container, reusable for any project

Status: built 2026-11-25 on sbx — test-sandbox.ps1 Passed: 10, Failed: 0; owner steps left (login, attach from the desktop app and VS Code); In Review

Research: `DevResources/plans/claude-sandbox-research-2026-11-25.md` (about 70 sources).

## Why

The owner wants "completely controlled agents": an editing agent that can only see one project, cannot reach the outside world except to search, and cannot touch chosen folders (for this project `openspec/`, which is only to be changed through the board's MCP tools). Testing and commits are done by another, unsandboxed agent or the owner.

## Decisions (owner, 2026-11-25)

- Reusable, not part of this repository: the tool lives in `C:\Users\artyo\Documents\5-mine\agent-sandbox` and is started on a project folder.
- The project is the only host folder mounted (read-write). Protected paths (per project; `openspec` here) are readable but not writable inside; they are changed only through MCP (owner, 2026-11-25: reading specs is fine, writing only through MCP).
- Network: deny by default. Out: Claude itself (api.anthropic.com and the login hosts) and web search (runs on Anthropic's side). Everything else is blocked at the network, not by prompt rules: curl, package registries, GitHub. The agent asks; the owner allows a host from outside the sandbox (a host-side allow-list the agent cannot edit).
- `git push` is never possible: no credentials inside, no route to a remote, `.git` mounted read-only (history and diffs readable; no commits).  
- Inside the sandbox bash and PowerShell 7 run without prompts.
- Clients: the Claude desktop app and the VS Code extension attach over SSH (an SSH server reachable from this PC only).
- MCP: HTTP servers from a host-side per-project config; for this project the board at `host.docker.internal:7431` with the app's token and the Windows repo path in headers. No app change.
- Runtime: Docker Sandboxes (`sbx`, microVM). Owner, 2026-11-25: "i said sandbox" — the earlier plain-Docker default was the supervisor's mistake.
- Defaults the owner did not answer: subscription login; the board token stays valid for every repository (follow-up item).

## Design

- An sbx sandbox per project, from a custom template that adds PowerShell 7 and the .NET SDK.
- Egress: sbx network policy, Locked Down plus an allow-list (`sbx policy allow network <host>`); attach over `<name>.sbx` (`sbx setup ssh`).
- A launcher (`sandbox.ps1 up|down|allow|status|ssh`) and a host-side config folder per project (`%USERPROFILE%\.agent-sandbox\<name>\`: allowed hosts, hidden paths, MCP servers), outside the mount.
- A root-owned managed settings file in the image: bypass mode, `git push` denied, WebFetch asks.

## Chunk

| Chunk | Owner | Test |
|---|---|---|
| Image, proxy, launcher, config, attach instructions | one agent (sonnet), in the new folder | `test-sandbox.ps1`: the checks below, run against a scratch project |

## Verification (the script asserts each)

1. Inside: the project's files are listed and writable; a file written inside appears on the host.
2. Inside: nothing of the host outside the project is visible (no `/mnt/c`, no home folder).
3. Inside: each protected path is readable and a write to it fails (create, change, delete); on the host its files are untouched.
4. Inside: `curl https://example.com` fails; `curl https://api.anthropic.com` gets an HTTP answer; after `sandbox.ps1 allow example.com` the first succeeds, with no restart of the sandbox.
5. Inside: `git log` works; `git commit` and `git push` fail.
6. Inside: `pwsh`, `bash`, `dotnet --version`, `claude --version` answer.
7. From the host: `ssh <sandbox>` runs a command inside.
8. MCP: from inside, the host app's `/health` on port 7431 answers through the proxy (when the app is open).
9. By the owner (GUI): the desktop app and VS Code attach; a Claude session inside edits a file and cannot fetch a blocked site.
