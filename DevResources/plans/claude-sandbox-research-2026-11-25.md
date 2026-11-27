# Research: Claude Code in a Docker sandbox for one project (2026-11-25)

Status: research done 2026-11-25; nothing built; waiting for the owner's answers (end of this summary). Related: #wi121 note #n136.

## The ask

Run Claude Code in a Docker sandbox (VM). It sees only the opened project, may use web search, PowerShell and bash and the connected MCP servers, must not touch `openspec/`, and is reachable from the Claude desktop app and the Claude VS Code extension.

## What the three parts found (details and ~70 source links below)

| Rule | Finding |
|---|---|
| Only the project's files | Met by any container or microVM that mounts just the project. A WSL distro alone is the weakest boundary (C: is automounted). Windows Sandbox does not exist on Home. |
| Web search | WebSearch runs on Anthropic's servers: `api.anthropic.com` is enough. WebFetch downloads from the sandbox itself, so each site must be allowed. Shell tools (`dotnet restore`, `npx`, `git`) need NuGet, npm and GitHub. Open internet from the shell makes an egress filter pointless against leaks. |
| PowerShell and bash | bash is there; `pwsh` 7 installs on Linux (whether the .NET 10 SDK image ships it is unverified). Windows PowerShell 5 and Windows programs do not exist in a Linux sandbox. |
| No `openspec/` | Not writable: a nested read-only mount does it (plain Docker). Not readable: impossible while `.git` is mounted (history holds every file), and a hidden folder makes git show every board file as deleted. `sbx` cannot lock a subfolder of its main workspace; its clone mode protects the host copy only. Claude Code's own deny rules are not a lock (a subprocess walks around them). |
| MCP servers | The app already serves the board tools over HTTP on 127.0.0.1:7431 with a bearer token; a container can call it through `host.docker.internal` with the token and the Windows repo path in headers, no app change. The Windows stdio entry in `.mcp.json` cannot run in Linux. The token is valid for every repository the app can reach. The tools answer only while the app is open. |
| Desktop app and VS Code | Both attach over SSH. `sbx` gives each sandbox an SSH name (`<name>.sbx`, no open port) and Docker documents both clients. Plain Docker: an SSH server in the container on 127.0.0.1, or VS Code Dev Containers. The two clients keep separate session lists. The desktop app writes the Claude login into the sandbox. |
| This repository | Every project targets plain `net10.0`, so the build should work on Linux (not run yet). A handful of tests call Windows programs. The real-app check (WebView2 on a hidden desktop) cannot run in Linux. Sharing one `.git` between Windows and Linux is risky (line endings, worktree paths): the sandbox should work on its own clone. |

## This PC (checked 2026-11-25)

Windows 11 Home 26200. Docker 29.6.1 with the WSL2 backend (the engine was not running during the research). WSL2 Ubuntu. `sbx` v0.53.0 installed but not signed in, so not tried. OpenSSH 9.5. Git for Windows installed, its `sh` not on PATH (the desktop app's SSH needs it). The Claude desktop app looks like the Microsoft Store build (SSH sessions are documented for the download-page build). Whether Windows Hypervisor Platform is on (needed by `sbx`) needs an administrator to check.

## Options

| Option | For | Against |
|---|---|---|
| A. Docker container built for this (Anthropic's dev container as the base, `pwsh`, read-only `openspec/`, an egress allow-list, SSH server on 127.0.0.1) | Works with what is installed today; the only one that can make `openspec/` unwritable; best-documented for the VS Code extension | We build and maintain the image and the firewall; the Claude login lives in the container |
| B. Docker Sandboxes (`sbx`) | Strongest isolation (microVM), login stays on the host, network deny-by-default, both clients documented | Unproven on Windows 11 Home; cannot lock `openspec/` in the main workspace; VS Code extension inside it unconfirmed |
| C. WSL distro plus Claude Code's built-in sandbox | Cheapest | Weakest boundary; covers shell commands only |

Recommendation: A as the base, with a 15-minute trial of B once `sbx login` is done, because B would be better if it runs here and clone mode is acceptable.

## Open questions for the owner

1. Must the sandboxed Claude build and test, or only edit code? (The real-app check stays on Windows either way.)
2. `openspec/`: read-only (recommended) or hidden? Unreadable is not possible with git history mounted.
3. Network: a short allow-list (Anthropic, NuGet, npm, GitHub, excalidraw) or open internet?
4. May the sandbox `git push`, or only commit in its own clone?
5. This repository only, or a setup reusable for any project?
6. No permission prompts inside the sandbox?
7. Is a board token that works on every repository acceptable, or should the app limit a token to one project?
8. Claude login in the sandbox: subscription login (needed for Remote Control) or a one-year token?
9. `sbx login`, the Hypervisor Platform check, and moving to the download-page build of the desktop app if the Store build cannot do SSH.

---

# Sandbox runtime research (R1): where to run Claude Code so it sees only one project and never touches `openspec/`

Researched 2026-11-25. Every link below was fetched on that date. Vendor docs pages carry no publication date, so "live docs, fetched 2026-11-25" is the date unless a page states one. Anything marked **(memory)** or **(unverified)** is not backed by a fetched page. Nothing was changed on the machine. Docker Desktop was not running (engine pipe missing) and I did not start it, so no live mount test was done; section 5 is therefore doc-based and lists a 10-minute test to run.

## Facts about the repo that matter for the choice (read from the repo, not from the web)
- `.mcp.json` declares two MCP servers: `repo-tasks` (stdio, command `C:/Users/artyo/.../dist/TaskManager-small.exe --mcp`, a **Windows exe**) and `excalidraw` (http, `https://mcp.excalidraw.com/mcp`). A Linux container cannot run the Windows exe. This is the single biggest design question (see open questions).
- The app is Photino.Blazor on `net10.0`; CLAUDE.md rule 3 (hidden desktop, `tests/hidden-app.ps1`, WebView2/CDP) is Windows-only and cannot run inside any Linux sandbox. `dotnet test` of the fast suite may or may not run on Linux (not checked).
- CLAUDE.md rule 2 and the board rules require writing `openspec/` through the `board_*` MCP tools. The owner's new rule ("must not touch `openspec/`") contradicts that unless the MCP server stays outside the sandbox as the one door.

---

## 1. Docker Sandboxes (`sbx`, microVM)

Sources:
- Docker blog, "Docker Sandboxes: Run Claude Code and Other Coding Agents Unsupervised (but Safely)", posted 2026-01-30: https://www.docker.com/blog/docker-sandboxes-run-claude-code-and-other-coding-agents-unsupervised-but-safely/
- Install page (live docs): https://docs.docker.com/ai/sandboxes/install.md
- Get started (live): https://docs.docker.com/ai/sandboxes/get-started.md
- Architecture (live): https://docs.docker.com/ai/sandboxes/architecture/
- Usage (live): https://docs.docker.com/ai/sandboxes/usage/
- Security + policy (live): https://docs.docker.com/ai/sandboxes/security/ and https://docs.docker.com/ai/sandboxes/security/policy/
- Claude Code in sandboxes (live): https://docs.docker.com/ai/sandboxes/agents/claude-code.md
- MCP gateway (live): https://docs.docker.com/ai/sandboxes/mcp-gateway.md
- FAQ (live): https://docs.docker.com/ai/sandboxes/faq/
- GitHub issue docker/sbx-releases #179 (opened 2026-05-29, closed 2026-07-10): https://github.com/docker/sbx-releases/issues/179
- Walkthrough on Windows (DEV Community, posted "June 30", year not shown): https://dev.to/ajeetraina/running-docker-sandboxes-on-windows-platform-1ikd

State in late 2026
- It is now a standalone, free CLI (`sbx`), not Docker Desktop. `sbx run claude`. The old `docker sandbox` command inside Docker Desktop is marked "(deprecated)" in the search index of docs.docker.com/ai/sandboxes/docker-desktop; the migration page (could not be fetched, 404) is reported to say old sandboxes do not work with the new CLI. Docker Desktop is not required (get-started/install pages).
- Each sandbox is a microVM with its own kernel and its own private Docker engine. Cloud sandboxes also exist (pay as you go); local ones are free.

Windows support
- Docs list: Windows 11, 64-bit Intel/AMD, and the **Windows Hypervisor Platform** optional feature (`Enable-WindowsOptionalFeature -Online -FeatureName HypervisorPlatform -All`, elevated). Install: `winget install -h Docker.sbx` (per user, no admin) or a machine-wide MSI. The install page says nothing about Home versus Pro. **Home is unverified.** The #179 reporter was on Windows 11 **Pro** build 26200 (this machine is build 26200, Home). Whether the Hypervisor Platform feature can be enabled on Home: from memory yes (it is what WSL2/Docker Desktop on Home rely on), unverified for sbx. The DEV walkthrough also enables VirtualMachinePlatform and WSL features and says a reboot is needed.
- Known Windows incident on exactly this build: #179 "VM did not connect within 30s" on build 26200 with sbx v0.31.0; workaround `-m 2g`; fixed in v0.31.2 (2026-06-02/03); issue closed 2026-07-10 with a request to try v0.35.0. Today the docs mention features of v0.42, v0.45 and v0.48, so the CLI is moving fast.

Workspace mounting
- `sbx run claude <path>` mounts the path read-write at the **same absolute path** inside the VM through a virtiofs passthrough; no sync. "The agent cannot access the rest of your host filesystem, host Docker daemon, or resources outside the workspace and shared skills store."
- Extra workspaces: `sbx run claude ~/proj ~/shared:ro ~/x:/target:ro` (suffix `:ro`, optional target path). Extra workspaces are always direct mounts. The primary workspace takes no target and no `:ro`.
- `--clone` mode: host repo is mounted **read-only** at `/run/sandbox/source`; the agent works in a private git clone inside the VM; changes reach the host only when the owner fetches the `sandbox-<name>` remote. The primary workspace must be a git repo and the main checkout (not a linked worktree).
- Mountless sandbox (v0.42+): no host mount at all.
- **No documented way to make one subfolder of the primary workspace read-only or hidden.** Local `sbx policy` covers network only ("Local policy can't deny or mount paths"); filesystem policy is an organisation (paid) feature. Whether an extra `:ro` workspace can be nested inside the primary path is not documented (unverified); the docs only say targets must not equal the primary path.
- Consequence for rule 4: with direct mount the agent can write `openspec/`. With `--clone`, the agent can edit its clone's `openspec/` but cannot change the host copy, and the host copy is not even mounted writable. It is still *readable* inside the VM (it is in the clone), so "not readable" fails; "never reaches the host" holds.
- Docs warn that in direct mode git hooks, Makefile, package.json scripts etc. execute during normal work and `.git/hooks` do not show in `git diff`.

Network
- Deny-by-default; all outbound TCP goes through a host proxy; UDP and ICMP blocked. Presets: Open (`allow-all`), Balanced (default deny with a baseline allow list: AI provider APIs, package managers, OS repos, code hosts, registries, cloud services), Locked Down (`deny-all`). Per-host rules: `sbx policy allow network api.github.com`, `sbx policy ls`, hostnames, IPs/CIDRs, wildcards; HTTP rules can match method/path/query. The Balanced baseline contains broad wildcards (docs example `*.googleapis.com`), so review it with `sbx policy ls`.

Credentials
- A host-side proxy injects auth headers; raw credential values never enter the VM. API key: `sbx secret set anthropic`. Subscription: run `/login` inside Claude; "the session token stays on your host". Host `~/.claude` is **not** used; only project-level config in the working directory is visible (so user-level skills/memory are not available inside).

Claude launch behaviour
- Default command is `claude --dangerously-skip-permissions` (template `docker/sandbox-templates:claude-code`). Extra args after `--` are appended. `claude.remoteControl` setting enables `/remote-control` (credentials stay on host). `claude.chrome` off by default.

PowerShell
- Not covered by the Docker pages. The VM is Linux. Claude Code's PowerShell tool on Linux needs `pwsh` 7+ on PATH and is opt-in (tools reference, section 3). Installing `pwsh` needs apt access to Microsoft's package host; packages persist across restarts ("installed packages ... persist"). Feasible, not documented as a recipe (unverified).

MCP
- `sbx mcp add <name> ...` registers servers once on the **host**; `--static-mcp <name>` or `sbx mcp load` attaches them; the agent sees one gateway endpoint. **Local stdio servers run on the host outside sandbox isolation**; remote HTTP servers are called from the gateway; header secrets via `sbx secret set mcp:<server>:<placeholder>`. The page does not mention `.mcp.json`. The Docker Desktop MCP Toolkit is separate and not needed. For this repo this is attractive: the Windows `TaskManager-small.exe --mcp` could run on the host as the only door to `openspec/`.

Limits
- Heavier than a container (VM + its own daemon each). Avoid network drives as workspace. Telemetry on by default (`SBX_NO_TELEMETRY=1`). Local MCP unload needs v0.48+. Upstream proxy support experimental. Fast-moving CLI (bugs like #179).

---

## 2. Anthropic's reference dev container

Sources (live docs, fetched 2026-11-25):
- https://code.claude.com/docs/en/devcontainer.md
- https://raw.githubusercontent.com/anthropics/claude-code/main/.devcontainer/init-firewall.sh
- https://code.claude.com/docs/en/network-config.md
- https://code.claude.com/docs/en/sandbox-environments.md

- Docs call it "a working example rather than a maintained base image". Three files: `devcontainer.json`, `Dockerfile`, `init-firewall.sh`. Runs as non-root `node`; adds `NET_ADMIN` and `NET_RAW`. The firewall is optional ("not required for Claude Code itself").
- Recommended minimal path now: the Dev Container Feature `ghcr.io/anthropics/devcontainer-features/claude-code:1.0`; persist auth with a named volume at `~/.claude` plus `CLAUDE_CONFIG_DIR` pointing at it (else `~/.claude.json` is lost on rebuild).
- `init-firewall.sh` (fetched): INPUT/FORWARD/OUTPUT default DROP; allows GitHub `/meta` ranges (web+api+git), and resolves once at start: `registry.npmjs.org`, `api.anthropic.com`, `sentry.io`, `statsig.com`, `marketplace.visualstudio.com`, `vscode.blob.core.windows.net`, `update.code.visualstudio.com`; plus host /24, DNS (UDP 53), **SSH (TCP 22)**, loopback. Blocked traffic is rejected fast. To extend: add the hostname to the `for domain in` list and re-run, or `ipset add allowed-domains <cidr>`. Weaknesses: IP-based and resolved once (CDN IPs drift), whole GitHub range and port 22 are open exfiltration paths, and as fetched it does **not** list `claude.ai` / `platform.claude.com` (OAuth login and refresh) or `downloads.claude.ai`. With an API key or `CLAUDE_CODE_OAUTH_TOKEN` it works; with `/login` you must add those hosts.
- Docs warning: with `--dangerously-skip-permissions` a malicious project can exfiltrate anything inside the container including `~/.claude` credentials; do not mount `~/.ssh` or cloud credentials; use repo-scoped, short-lived tokens. The flag is refused as root.
- MCP in the container: define at project scope in `.mcp.json`, install stdio binaries in the Dockerfile, add remote server domains to the allow list.
- Managed settings in the container: `/etc/claude-code/managed-settings.json` on Linux, highest precedence (see section 4).
- VS Code: the Claude extension runs inside the container ("both run inside the container and share the same `~/.claude`"). The desktop app's Code tab reaches dev containers only as an SSH target (https://code.claude.com/docs/en/desktop.md, "SSH ... such as your own servers, cloud VMs, or dev containers"). Attaching is the other agent's topic.

---

## 3. Claude Code built-in sandbox, sandbox-runtime (`srt`), WSL2

Sources (live docs, fetched 2026-11-25):
- https://code.claude.com/docs/en/sandboxing.md
- https://code.claude.com/docs/en/sandbox-environments.md
- https://github.com/anthropic-experimental/sandbox-runtime (redirects to anthropics/sandbox-runtime; versions seen v0.0.76-0.0.79, no dates shown)
- https://learn.microsoft.com/en-us/windows/wsl/wsl-config (ms.date 2026-10-16)

- Native Windows: **not supported** by the built-in sandbox ("runs commands unsandboxed"). `failIfUnavailable: true` makes Claude exit on native Windows. macOS = Seatbelt; Linux and **WSL2** = bubblewrap + socat (`sudo apt-get install bubblewrap socat`); WSL1 not supported. Ubuntu may need an AppArmor profile or `sysctl kernel.apparmor_restrict_unprivileged_userns=0` for bwrap.
- What it wraps: Bash, PowerShell and Monitor commands and their children. **Not** wrapped: Read/Edit/Write/WebFetch/WebSearch (they follow permission rules), local MCP servers, command hooks, LSP, statusline, `apiKeyHelper`, `!` typed commands (mostly). On WSL2 sandboxed commands cannot launch `powershell.exe`/`cmd.exe`/`/mnt/c/...` unless `allowAllUnixSockets` (which opens every Unix socket).
- Settings (settings.json): `sandbox.enabled`, `sandbox.filesystem.{allowWrite,denyWrite,denyRead,allowRead}` (default: writes = working dir + temp; reads = most of the machine; `denyRead:["~/"]` + `allowRead:["."]` blocks home but keeps the project; narrower path wins), `sandbox.network.{allowedDomains,deniedDomains,strictAllowlist,allowManagedDomainsOnly}` (allowedDomains starts empty = no network), `failIfUnavailable`, `allowUnsandboxedCommands:false` (strict mode: no `dangerouslyDisableSandbox` retry), `excludedCommands`, `credentials.{files,envVars}` with `deny`/`mask`. Protected paths always write-denied: `.claude/settings*.json`, `.mcp.json`, `.git/hooks`, `.git/config`, shell rc files, `~/.claude`.
- Enforcement: the OS enforces it per process regardless of what the model chose; managed-settings booleans override local; `allowManagedDomainsOnly` ignores repo-level loosening.
- Limits the docs state: proxy filters by hostname and does not inspect TLS (domain-fronting risk, "allowing github.com can create exfiltration paths"); docker and watchman incompatible; sandbox off by default.
- `@anthropic-ai/sandbox-runtime` (`srt claude`): wraps the **whole process** (file tools, MCP servers, hooks). Beta research preview. Config in `~/.srt-settings.json`; reads allow-by-default with `denyRead`, writes deny-by-default with `allowWrite`/`denyWrite`, network deny-by-default with `allowedDomains`. On Linux `allowWrite`/`denyWrite` take literal paths only (no globs) and deny lists are built once at launch (does not cover things created later). **Windows is alpha** (dedicated local user, WFP egress fence, NTFS ACEs, one-time elevated `windows-install`; Schannel TLS clients such as `curl.exe`, `Invoke-WebRequest`, `dotnet restore`, `winget` fail inside it). Not recommended to rely on for this owner.
- WSL2 is not a security boundary toward Windows by default: `/mnt/c` is auto-mounted (so the whole C: drive is reachable) and interop lets Linux start Windows processes. wsl.conf can turn these off per distro: `[automount] enabled=false`, `[interop] enabled=false`, `appendWindowsPath=false` (Microsoft doc, ms.date 2026-10-16). A dedicated distro plus these switches plus `srt`/sandbox would narrow it, but the distro would hold its own copy of the repo.
- Could the built-in sandbox alone meet the goal without Docker? No. It covers shell commands only; Read/Edit and MCP are outside; no native Windows; and on WSL2 the distro sees C:. It is a good inner layer *inside* a container or distro (Anthropic documents layering it inside containers; in an unprivileged container it needs `enableWeakerNestedSandbox`, which weakens it).

---

## 4. Permission rules as a second layer

Sources (live docs, fetched 2026-11-25): https://code.claude.com/docs/en/permissions.md , https://code.claude.com/docs/en/managed-settings.md , https://code.claude.com/docs/en/settings.md (via links)

- Order: deny, then ask, then allow; first match wins; allow cannot carve out of deny.
- `Read(path)` deny: "best-effort" applied to Grep, Glob, `@file` mentions, IDE selection context. A Read deny **also blocks Edit/Write on the same path, including creating a new file there** (Write check needs v2.1.228+). NotebookEdit is not covered; add an `Edit` deny. Edit rules cover all built-in editing tools. Path syntax: `Read(openspec/**)` as a deny/ask rule matches a directory with that name at any depth under the working directory; `//abs`, `~/`, `/x` = project-relative (in user settings `/x` is relative to `~/.claude`). Symlinks: a deny applies if either the requested path or its resolved target matches.
- Bash coverage: Read/Edit denies apply to redirection targets (`>`, `>>`, `<`), `tee`, and recognized file commands (`cat`, `head`, `tail`, `sed`). They do **not** apply to "a command that reads files without naming them, such as `grep -r pattern .`", nor to "arbitrary subprocesses ... like a Python or Node script that opens files itself". Bash deny rules are not a boundary either: `Bash(rm *)` does not catch `/bin/rm` or `sh -c '...'`; `git -C . push` escapes `Bash(git push *)`. The docs themselves say: for OS-level enforcement enable the sandbox and turn off the unsandboxed retry. **So deny rules alone cannot meet "not writable even via shell".**
- Windows trap: on Windows with Git Bash, any Bash deny rule silently turns the PowerShell tool off (Claude Code does this because a Bash rule does not restrict PowerShell), unless `CLAUDE_CODE_USE_POWERSHELL_TOOL=1` or a scoped `PowerShell(...)` rule is set. PowerShell has its own permission rules. (tools-reference.md, "Bash deny rules also turn off the PowerShell tool").
- Managed settings: highest precedence; no user/project/local/`--settings` value overrides them (security-sensitive exceptions aside). Linux/WSL file: `/etc/claude-code/managed-settings.json`; in a Dockerfile `COPY` it in root-owned so the non-root agent user cannot edit it (devcontainer doc; note the doc's caveat that anyone with write access to the repository Dockerfile can remove it). Useful keys: `permissions.deny`, `permissions.disableBypassPermissionsMode: "disable"` (conflicts with unattended `--dangerously-skip-permissions`; choose one), `allowManagedPermissionRulesOnly`, `sandbox.*` with `failIfUnavailable`/`allowUnsandboxedCommands:false`/`allowManagedDomainsOnly`, `allowedMcpServers`/`managedMcpServers` (v2.1.259+).
- `additionalDirectories` / `--add-dir` only widen reach; they are not needed for a single-project goal. `permissions.blockReadsOutsideWorkingDirectories` exists to deny reads of home and user-file dirs.
- Bottom line: use rules for good error messages and to stop the file tools, but let the container's read-only mount be the real lock.

---

## 5. Hiding or locking one subfolder of a bind-mounted project

Sources: https://docs.docker.com/engine/storage/bind-mounts/ , https://docs.docker.com/engine/storage/tmpfs/ , https://docs.docker.com/desktop/features/wsl/best-practices/ (all live docs, fetched 2026-11-25); Microsoft WSL file permissions page (search result only, not fetched).

Options
1. **Read-only nested bind mount** (recommended): `docker run -v C:\...\repo:/w -v C:\...\repo\openspec:/w/openspec:ro ...` (or Compose `volumes:` with `read_only: true`). Docs: a mount on a non-empty directory hides what was beneath; `ro` blocks writes; recursive read-only handling is best effort and needs kernel 5.12+ for sub-mounts (the Docker Desktop VM kernel is far newer, so fine, unverified). The docs do not state a mount-ordering rule; the Docker engine orders mounts by destination depth so a nested mount over a bind mount is the common pattern (memory, widely used for `node_modules`; confirm with the test below).
2. **Empty overlay to hide it**: bind-mount an empty host directory, or `--tmpfs /w/openspec`, or an anonymous volume at `/w/openspec`. Docs confirm a tmpfs obscures existing files; tmpfs counts against container memory and is Linux only. Not recommended for a tracked folder, see pitfall (a).
3. A root-owned, mode 0555 mount also works but is weaker than `:ro`.

Pitfalls
a. **Git sees what Docker hides.** If `openspec/` is tracked, hiding it with an empty overlay makes `git status` inside the container show every file deleted, and the agent might commit or "restore" them. A `:ro` mount keeps git quiet. And `.git` still contains all of openspec's history (`git show HEAD:openspec/...`, `git log -p`), so **"not readable" is unachievable while `.git` is mounted**; only "not writable on disk" is. (Fixing it would need a clone without that history, which the owner's tooling probably does not allow.)
b. **Writes go through a second door.** The agent cannot write to the `:ro` mount, but `git checkout`, `git stash`, `git merge` into the working tree fail on it, which will surface as git errors.
c. **Bind mount from `C:\` is slow and has no change events.** Docker's WSL best practices page: much faster when files are bind-mounted from the Linux filesystem (e.g. `~/my-project`), avoid `/mnt/c`; containers get inotify events only when the files live in the Linux filesystem. For a .NET repo (thousands of small files in `obj/`/`bin/`) expect slow restore/build/test and no hot-reload/watch events.
d. **Ownership and permissions.** Docker's bind-mount page does not cover ownership. Community threads (Docker forums, 2024-2025 era, not primary) report root-owned or 0777-looking files and unstable permissions on Windows-side mounts; the usual advice is to move the project into the WSL filesystem. Practical effect: the in-container user (uid 1000 `node`) writes files that show up on Windows normally, but `chmod` is not meaningful. WSL docs: drives under `/mnt` use metadata options in `wsl.conf` (`umask 022` default).
e. **Line endings.** No primary source found. From memory: if Windows git uses `core.autocrlf=true`, files on disk are CRLF and a Linux-side git sees every file as modified; set a `.gitattributes` (`* text=auto eol=lf` or per type) or use the same git settings on both sides. Verify with `git status` after first mount.
f. **Mount propagation does not work on Docker Desktop** (bind-mount docs) and bind mounts go to the daemon host, not the client.
g. **Windows build artefacts collide.** The repo has `bin/obj` from Windows builds; Linux `dotnet` writes `obj/project.assets.json` with Linux paths into the same folders (memory, unverified). Use separate output dirs or overlay `bin`/`obj` with container-only volumes.
h. `.mcp.json`, `.claude/settings*.json` in the repo are agent-editable unless also locked (the sandbox's protected-paths list covers them only inside the built-in sandbox). Consider mounting `.claude/` and `.mcp.json` read-only too, or place policy in managed settings.

Test to run (10 minutes, needs Docker Desktop started by the owner): copy the repo to a scratch folder, run `docker run --rm -v <copy>:/w -v <copy>\openspec:/w/openspec:ro -w /w mcr.microsoft.com/devcontainers/base:ubuntu bash -c "touch openspec/x; echo \$?; git status --short | head; id; stat -c '%U %a' ."` and check: touch fails with "Read-only file system", git status clean, file ownership, speed of `dotnet restore`.

---

## 6. Web search and fetch from a restricted network

Sources (live docs, fetched 2026-11-25): https://code.claude.com/docs/en/network-config.md (Network access requirements), https://code.claude.com/docs/en/tools-reference.md (WebSearch/WebFetch behaviour), https://code.claude.com/docs/en/data-usage.md (WebFetch domain safety check)

Where each tool runs
- **WebSearch runs on Anthropic's backend**: "runs a query against Anthropic's web search backend and returns result titles and URLs. It doesn't fetch the result pages." It reaches the machine only through `api.anthropic.com`. Not available on Bedrock. Limits: 200 calls per interactive session, refill 100/h.
- **WebFetch runs from the client machine**: it downloads the page itself (so the container's egress must allow that host) and a separate small-model call reads it. Before every fetch it sends the **hostname** to `api.anthropic.com` for a safety check (can be disabled with `skipWebFetchPreflight: true`). It refuses `localhost` and dotless hosts. Rules: `WebFetch(domain:example.com)`; `allowedDomains` of the sandbox does NOT limit WebFetch.
- `curl` in the shell is separate again: it goes through the container/sandbox egress.

Domains Claude Code itself needs (table in network-config.md)
- Required: `api.anthropic.com` (model, WebSearch, WebFetch safety check, flags, telemetry).
- Login with subscription: `claude.ai`, `claude.com` (browser redirect), `platform.claude.com` (also token exchange/refresh for claude.ai accounts), `sso.claude.com` only for SSO orgs. With API key or `CLAUDE_CODE_OAUTH_TOKEN` the OAuth hosts can be dropped (sandbox-environments doc: "Runs authenticated with an API key can drop these two").
- Optional/avoidable: `mcp-proxy.anthropic.com` (claude.ai connectors; not available with setup-token anyway; disable with `ENABLE_CLAUDEAI_MCP_SERVERS=false`), `downloads.claude.ai` (installer/auto-updater; set `DISABLE_AUTOUPDATER=1` and bake the version), `storage.googleapis.com`, `registry.npmjs.org` (npm installs, `npx` MCP servers), `github.com` and `raw.githubusercontent.com` (plugins, changelog), Datadog intake hosts `http-intake.logs.us5.datadoghq.com`, `browser-intake-us5-datadoghq.com` (telemetry; off with `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1` or `DISABLE_TELEMETRY=1`), `code.claude.com` (docs lookups), `bridge.claudeusercontent.com` (Chrome extension), `*.frame.claudeusercontent.com` (Artifact tool; off with `CLAUDE_CODE_DISABLE_ARTIFACT=1`). The reference firewall's `sentry.io`/`statsig.com` entries are older names; the current doc lists Datadog instead (the doc and the script differ; not reconciled).
- Project-specific: `mcp.excalidraw.com` (the repo's excalidraw MCP); `registry.npmjs.org`/`api.nuget.org`/`nuget.org` etc. if `dotnet restore` must work inside (NuGet hosts are my addition, not from a fetched page).
- Network requirement notes: Claude Code supports HTTP(S) proxies via `HTTPS_PROXY`/`NO_PROXY`; **no SOCKS**; custom CA via `NODE_EXTRA_CA_CERTS`. A proxy with an allow list (squid/tinyproxy sidecar on an internal-only Docker network) is more robust than the IP-resolving firewall script.

Two variants for "web search allowed"
- **Variant A, tool-level only (recommended)**: egress allow list = `api.anthropic.com` (+ the three OAuth hosts if using `/login`) + any MCP hosts; WebSearch works fully (server-side); WebFetch works for pages you add as `WebFetch(domain:...)` rules AND egress hosts; the shell cannot `curl` arbitrary sites. Small exfiltration surface (data can still leave inside model traffic).
- **Variant B, open internet from the shell**: egress open (or "all 443"); then the firewall protects only the host LAN, not the project's contents, and the agent can POST the repo anywhere. The built-in sandbox's own docs call out the same trade-off. Choose B only if the owner accepts that the project is not secret from the agent's network.

---

## 7. Authentication inside the container

Sources (live docs, fetched 2026-11-25): https://code.claude.com/docs/en/authentication.md , https://code.claude.com/docs/en/devcontainer.md

- Stored credentials on Linux: `~/.claude/.credentials.json`, mode 0600; with `CLAUDE_CONFIG_DIR` set it lives in that directory. Account state/MCP trust live in `~/.claude.json` (outside `~/.claude` unless `CLAUDE_CONFIG_DIR` is set).
- Persist without exposing the host home: a **named Docker volume** mounted at `/home/node/.claude` plus `CLAUDE_CONFIG_DIR=/home/node/.claude` (devcontainer doc; the reference config uses `claude-code-config-${devcontainerId}` so each project gets its own volume). Never bind-mount `C:\Users\<you>\.claude`.
- Options:
  1. `/login` (OAuth) inside the container: the localhost redirect often fails in containers; use the URL + paste-the-code path (docs link "OAuth login fails in WSL2, SSH, or containers"). Needs `claude.ai`, `platform.claude.com`, `claude.com` reachable once, and again at refresh. A claude.ai login has a finite lifetime: `claude auth status --text` shows "Sign-in ends: between ... and ..." and `/login` renews.
  2. `claude setup-token` on any machine: prints a one-year token, **not saved**; set `CLAUDE_CODE_OAUTH_TOKEN` in the container env. Needs Pro/Max/Ultra/Team/Enterprise. It "can only make model requests, so it can't establish Remote Control sessions or fetch claude.ai connectors. MCP servers you configure locally still work." Bare mode does not read it. This is the smallest-surface choice for an egress-locked container but a stolen token is valid for a year; keep it out of the image and out of the repo (Docker secret / `--env-file` outside the project).
  3. `ANTHROPIC_API_KEY`: precedence above OAuth; prompts once to approve; pay-per-token.
- Precedence (docs): cloud provider vars, `ANTHROPIC_AUTH_TOKEN`, `ANTHROPIC_API_KEY`, `apiKeyHelper`, `CLAUDE_CODE_OAUTH_TOKEN`, profiles, then `/login`.
- sbx is different: credentials stay on the host proxy and never enter the VM (section 1), which is stronger than any container option.
- Anything the agent can read it can send out: with skip-permissions the devcontainer doc says credentials in `~/.claude` are exfiltratable. Allow-list egress is the mitigation.
- Side effect to plan for: the repo's workflow depends on user-level things in the host `~/.claude` (the `delegate-workflow` skill, memory files, `settings.json` allow lists). None exist in a fresh container; copy only what is needed (read-only), or put it in the repo.

---

## 8. Alternatives, one line each

- **Dedicated WSL2 distro** (Ubuntu exists, verified stopped): cheap, supports the built-in sandbox, PowerShell via `pwsh`, native-Linux file speed if the repo is cloned inside it. But a distro automounts `C:` and has Windows interop; to meet the goal you must set `[automount] enabled=false` and `[interop] enabled=false` (Microsoft wsl-config, 2026-10-16), run as a non-admin user, and keep a clone in `~`. Not a true boundary: all distros share one VM and kernel, and a root-capable process can remount drives (memory). Weaker than a container.
- **Windows Sandbox**: not on Home. Microsoft (ms.date 2026-03-29, updated 2026-10-08): "Windows Sandbox is currently not supported on Windows Home edition" (supported: Pro, Enterprise, Pro Education/SE, Education). https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/
- **Podman (WSL2 machine)**: same Linux container model as Docker; adds a second engine to maintain; no feature advantage here. Red Hat docs say 6 GB RAM and Windows 11 64-bit (search result, not fetched). A Microsoft Q&A thread from Feb 2026 (search result, unfetched) reports WSL2 failing on some Home 25H2/26200 builds; WSL2 works on this machine, so not a blocker.
- **Claude Code on the web / cloud sessions**: runs in an Anthropic VM; repo must be on GitHub or be uploaded as a bundle (<100 MB; tracked files only; `.env`-like files left out on WSL/macOS/Linux but not on native Windows). The whole checkout including `openspec/` is in the VM, so rule 4 cannot be enforced; local stdio MCP (the Windows exe) and the owner's hidden-desktop app are not available; default network is "Limited". Not a fit for this owner's rules (code.claude.com/docs/en/claude-code-on-the-web.md; sandbox-environments.md).
- **Third-party wrappers** (names only from one search; I did not open them, so treat as unverified): Trail of Bits `claude-code-devcontainer` (https://github.com/trailofbits/claude-code-devcontainer, mounts `~/.gitconfig` and `.devcontainer` read-only, persistent volumes for config), `nikvdp/cco`, `zekker6/devsandbox`, `CaptainMcCrank/SandboxedClaudeCode`, `gilesknap/claude-sandbox`. None is needed; Anthropic's own pieces cover the same ground.
- **Self-hosted environments / corporate launcher / `processWrapper`** (docs mention): organisation features, out of scope.

---

## Comparison for THIS machine (Windows 11 Home 26200, WSL2 + Docker Desktop 29.6.1, repo on C:\)

Rules: R1 project files only; R2 web search; R3 PowerShell + bash; R4 no `openspec/`; R5 MCP servers.

| Option | R1 project files only | R2 web search | R3 PowerShell + bash | R4 no openspec | R5 MCP | Works on Home? | Effort / notes |
|---|---|---|---|---|---|---|---|
| A. Docker container (Anthropic devcontainer pattern) with `-v project:/w` + `-v project/openspec:/w/openspec:ro`, egress proxy, managed-settings deny | Yes (only the bind mount + a named config volume) | Yes (WebSearch server-side; WebFetch needs domain allow) | bash yes; PowerShell needs `pwsh` installed + `CLAUDE_CODE_USE_POWERSHELL_TOOL=1` (opt-in on Linux) | Writes: yes, kernel-enforced `:ro`. Reads: no, `.git` history + the mounted files are readable (reading can only be blocked by an empty overlay, which breaks git) | `excalidraw` http works with egress; Windows-exe `repo-tasks` does not run in Linux; needs a Linux build of `--mcp` or a host-side door | Yes (Docker 29.6.1 already installed; verified WSL2 works) | Medium. C:\ bind mount is slow for .NET. Best control over every rule. Cannot run the Windows app/CDP verification |
| B. Docker Sandboxes `sbx` (microVM) | Yes (strongest: own kernel; nothing else mounted) | Yes via policy (Balanced preset allows AI APIs; add hosts) | bash yes; `pwsh` installable (unverified) | Direct mount: **no** (no subfolder ro; local policy has no filesystem rules). `--clone`: host copy safe, but openspec is readable/writable inside the VM clone | Best: MCP gateway; stdio servers run on the host, so the Windows exe can stay the single door to openspec | **Unverified** (docs: Windows 11 + Hypervisor Platform; Home not mentioned; build-26200 bug #179 fixed in v0.31.2) | Low to try (`winget install Docker.sbx`). Credentials never in VM. Fast-moving tool; attach from desktop app/VS Code not documented |
| C. Dedicated WSL2 distro + built-in sandbox / `srt` | Only if automount+interop disabled and repo cloned inside | Yes (`allowedDomains` + WebSearch rule) | bash yes; PowerShell 7 on Linux (opt-in); no Windows binaries from sandbox | Writes: yes via `denyWrite` (OS-enforced for shell, not for file tools/MCP unless `srt` wraps the whole process); reads via `denyRead` | MCP servers run outside the built-in sandbox; `srt` wraps them | Yes | Low. Weakest boundary (shared WSL VM, interop). Linux file speed good |
| D. Claude Code on the web | Yes (GitHub clone) | Yes | Linux shell only | No (whole repo is cloned) | No local stdio MCP | Yes | Not a fit |
| E. Windows Sandbox | n/a | n/a | n/a | n/a | n/a | **No (Home)** | Excluded |

## Recommendation
1. Build **Option A** now: a container (Anthropic's devcontainer Feature as the base, Debian/Ubuntu, non-root, `pwsh` added) with the repo bind-mounted read-write and `openspec/` (plus `.claude/` and `.mcp.json`) nested-mounted `:ro`, a named volume for `~/.claude` + `CLAUDE_CONFIG_DIR`, `CLAUDE_CODE_OAUTH_TOKEN` supplied as an env secret, `/etc/claude-code/managed-settings.json` (root-owned) carrying `permissions.deny` for `Read/Edit(openspec/**)` as a courtesy layer and the built-in sandbox with `failIfUnavailable` and `allowUnsandboxedCommands:false` as an inner layer (needs `enableWeakerNestedSandbox` in an unprivileged container, which weakens it; the container mount stays the real lock), and egress through an allow-list proxy sidecar (Variant A in section 6). It is the only option verified to be installable here today and the only one where all four of R1-R4 are kernel-enforced (R4 writes only).
2. In parallel spend 15 minutes trying **Option B** (`sbx`) because it is the better security model and solves R5 elegantly. If it installs and runs on Home, use `--clone` (or per-folder extra workspaces) and keep the Windows `repo-tasks` MCP on the host as the only door to `openspec/`. Do not trust it for R4 until a subfolder-read-only workflow is confirmed with Docker (ask in docker/sbx-releases).
3. For performance, if the owner accepts the repo living inside WSL (`\\wsl$\Ubuntu\home\...`), clone it there for the container and keep a separate Windows checkout for the app; otherwise accept slow builds on `C:\`.

## Open questions for the owner
1. Must the agent be unable to **read** `openspec/` too, or only unable to change it? (Reading is impossible to prevent while `.git` is mounted and openspec is committed.)
2. How should the board work? Today the only way to change the board is the `repo-tasks` MCP (a Windows exe) which writes `openspec/`. Options: (a) keep that MCP outside the sandbox as the single door (needs `sbx`, or an SSH/HTTP bridge from the container to a host-run `--serve`/`--mcp`; CLAUDE.md memory mentions `--serve`), (b) make it read-only inside the sandbox, (c) no board access at all. Rule 2 of CLAUDE.md (OpenSpec change with every behaviour change) cannot be met in a sandbox that cannot write `openspec/`.
3. Is the sandbox only for coding/unit tests, with UI verification (rule 3: hidden desktop, Windows WebView2, CDP) staying on the host? It cannot run in a Linux sandbox.
4. Can `dotnet test TaskManager.Tests` run on Linux for this repo (target framework / Windows-only APIs)? If not, the sandbox cannot run the test suite either.
5. "Web search": tool-level only (Variant A) or full internet from the shell (Variant B)? Which extra sites must WebFetch reach (docs.microsoft.com, nuget.org, github.com)?
6. Which login: subscription OAuth, one-year `setup-token`, or API key? (setup-token cannot do Remote Control or claude.ai connectors; Remote Control affects attaching from the desktop app.)
7. Must the agent see user-level skills/memory (`~/.claude/skills/delegate-workflow`, MEMORY.md)? They are not in a fresh container; shall they be copied in read-only?
8. Where should the repo live: `C:\Users\artyo\Documents\5-mine\repo-task-manager` (slow bind mount, shared with the Windows app) or a clone inside WSL (fast, but two copies to sync)?
9. Is `.git` allowed inside the sandbox (agent can commit/branch)? CLAUDE.md says the agent never commits; mount `.git` read-only too, or keep it?
10. Is it acceptable to start Docker Desktop for the 10-minute mount test, and to try `sbx` (needs elevation to enable the Hypervisor Platform feature and probably a reboot)?


---

# R2: connecting the Claude desktop app and the VS Code extension to a sandboxed Claude Code

Research date: 2026-11-25. Windows 11 Home, Docker Desktop + WSL2 assumed. No code or git changes were made.
Convention: "(fetched)" = live doc page read on 2026-11-25; the Anthropic and Docker doc pages carry no page date, so the read date is the date. GitHub issue dates are the real ones. "UNVERIFIED" marks anything I could not confirm from a primary source.

## 0. Headline findings

1. There is an official, documented route for BOTH clients into ONE Docker sandbox: Docker Sandboxes (`sbx` CLI, microVM) publishes an SSH host name `<sandbox>.sbx`. Docker has a page "Connect Claude Desktop to a sandbox" and a page "Connect VS Code to a sandbox" (Remote-SSH). Both clients then run Claude Code INSIDE the sandbox. No sshd and no open port are involved: the SSH stream goes through a ProxyCommand to the local sbx daemon over a Windows named pipe.
2. The two docs disagree on one thing that matters: the Docker VS Code page says nothing about whether the Claude Code extension works inside the sandbox over Remote-SSH (UNVERIFIED). It works in ordinary Dev Containers per Anthropic's docs. A spike is needed.
3. Claude Desktop on Windows needs Git for Windows (an `sh` on PATH) for the ProxyCommand, Microsoft's OpenSSH client 9.4+, and Desktop installed from the download buttons, not the Microsoft Store. Without `sh` you get `ProxyCommand error: spawn sh ENOENT` and a timeout.
4. Remote Control (RC) is a third path that needs nothing from the clients except a subscription login inside the sandbox. It is outbound-HTTPS only, but it does not work with API keys, with `claude setup-token` tokens, or with an `ANTHROPIC_BASE_URL` other than api.anthropic.com. It is a viewer/steerer on claude.ai/code and the mobile app. Its listing in the Windows desktop app is implied, not stated (see 3).
5. Bypass mode is the default only for the sbx-launched `claude` process (`--dangerously-skip-permissions`). The VS Code extension and the Desktop app start their own `claude` process. They need their own toggle or a `defaultMode` in the sandbox's settings; a managed-settings file can forbid bypass.
6. Sessions: VS Code and the CLI share `~/.claude/projects` (same history). Desktop keeps its own session list and its `/resume` import of CLI sessions is NOT available in SSH sessions. Two writers on one session corrupt the transcript; use `--fork-session`.

## 1. VS Code extension in a container / remote window

What the primary sources say:

- Anthropic dev container page (fetched): with Claude Code installed in the container, "commands Claude runs execute inside it", and "In VS Code you can use either the Claude Code extension panel or run `claude` in the integrated terminal; both run inside the container and share the same `~/.claude` configuration." The Dev Container Feature `ghcr.io/anthropics/devcontainer-features/claude-code:1.0` also installs the VS Code extension automatically when opened in VS Code or Codespaces.
- Extension page (fetched): "Claude Code settings in `~/.claude/settings.json`: shared between the extension and CLI" (allowed commands, env, hooks, MCP). The extension bundles its own private CLI copy; it does not put `claude` on PATH. A troubleshooting note says: "In a window connected to WSL, an SSH host, a dev container, or another system, save the certificate file and edit `~/.claude/settings.json` on that system, not on your local computer." So Anthropic itself treats WSL, SSH and dev container windows as places where the extension's config lives on the remote side.
- VS Code remote extension model (code.visualstudio.com/api/advanced-topics/remote-extensions, page dated 11/18/2026): workspace extensions run in the Remote Extension Host on the remote (SSH, container, WSL); UI extensions stay local. `Developer: Show Running Extensions` shows where one runs; `remote.extensionKind` overrides. Attach to Running Container is not covered on that page.
- IDE lock file: the extension writes `~/.claude/ide/<port>.lock` (0600) in the config dir of the machine where it runs (or `$CLAUDE_CONFIG_DIR/ide/`).
- Where `claude` runs per window type (inferred from the extension-kind rule above plus the Anthropic notes; Anthropic does not tabulate it):
  - Dev Container, Attach to Running Container, Remote-SSH, Remote-WSL: extension host and its bundled `claude` run INSIDE the target. Settings: extension settings in VS Code (Remote settings for that target), `~/.claude/settings.json` and `~/.claude.json` inside the target. UNVERIFIED for Attach to Running Container specifically (no Anthropic page names it).
- Login and credentials, known issues:
  - Anthropic container docs: sign in by running `claude` in a terminal in the container. If no browser opens or a `localhost` error appears, copy the login URL, sign in in the host browser, paste the code into the prompt.
  - anthropics/claude-code #38408 (opened 2026-03-24, closed "not planned" 2026-04-23 by a bot, locked 2026-04-30): the extension keeps OAuth tokens in VS Code SecretStorage, but the CLI subprocess it spawns reads `~/.claude/.credentials.json`; in a fresh container Remote Control fails ("bridge not enabled") until the owner runs `claude` once in the container terminal and logs in. Workaround: do that login once; it survives restarts, not rebuilds.
  - #20793 (2026-01-25, closed duplicate of #14528): the OAuth callback server listens on a random localhost port in the container; the browser on the host cannot reach it unless VS Code happens to forward the port. Use the paste-code flow.
  - #39288 (2026-03-26, closed duplicate of #37919): forcing `remote.extensionKind` to `["ui"]` for `anthropic.claude-code` breaks the sidebar in a Dev Container. Do not set it.
  - #21877 (claudeissues.com mirror, extension v2.1.25): extension host crashed a dev container ("terminated unexpectedly 3 times"); old version, not re-verified.
- Persisting login/history in a container (Anthropic): mount a named volume at `~/.claude` AND set `CLAUDE_CONFIG_DIR` to the same path, otherwise `~/.claude.json` lives outside the volume and first-launch setup repeats.
- Does it work with a `docker sandbox` (Docker Sandboxes) or only dev containers? Anthropic documents dev containers only. Docker documents Remote-SSH into a sandbox (see section 4) and lists "whether the Claude Code VS Code extension works inside the sandbox" as NOT covered. Docker's own code-server kit (`docker/sbx-kit-code-server`, Docker Hub) runs a browser VS Code with the Claude Code extension on port 8080 inside a sandbox (search result; UNVERIFIED in detail). So: probably works (it is just a Linux host with VS Code Server), but credential handling inside sbx (placeholder tokens injected by a host proxy) vs the extension's own login is an open point.

## 2. Claude desktop app (Code tab) on Windows

Environments (code.claude.com/docs/en/desktop, fetched): Local, Cloud, SSH ("your own servers, cloud VMs, or dev containers"), WSL (Windows only, WSL 2 only, needs git in the distro; plugins unavailable; file pane read-only; terminal/connectors may be missing).

SSH target requirements (fetched):
- Linux/macOS host, x86_64 or arm64, glibc 2.17+ or musl. SFTP subsystem enabled in sshd. If `MaxSessions 1`, Desktop v3.1623.0+ copes (Windows must use the OpenSSH client then); otherwise set MaxSessions >= 10 or you get `[SESSION_LIMIT]`.
- `~/.claude/remote/` must be writable and not noexec; about 1 GB per connecting user.
- Account shell must be bash/zsh/sh. The host needs network access that Claude Code needs; if it cannot download Claude Code, Desktop downloads locally and uploads.
- Claude Code does NOT have to be pre-installed: "Desktop installs Claude Code on the remote machine automatically on first connect." Node is not required (Desktop deploys its own engine; not stated as needing Node; treat as not needed).
- Key auth: optional key path or ssh-config/agent; on Windows key must be OpenSSH format, not .ppk. Host key is verified and saved ("Trust and connect"); `ProxyJump` fails; `ProxyCommand` works if an `sh` is on PATH (Windows). `CertificateFile` ignored. Without `ConnectTimeout`, 30 s; use 120 s for ProxyCommand hosts.
- Documented container recipe (fetched): no published SSH port, `ProxyCommand ssh build.example.com docker exec -i devbox /usr/sbin/sshd -i`; the container needs OpenSSH server with a stable host key and `authorized_keys`. A container with sshd and a port published on 127.0.0.1 is the plain alternative (SSH host `user@127.0.0.1`, port 2222).
- Windows limits (fetched): for a Claude account via the OpenSSH client you need Desktop 2.2553.0+, Microsoft OpenSSH 9.4+, and the download-button install (Microsoft Store build "doesn't connect through the OpenSSH client"). Desktop does not read PowerShell profiles (set PATH in the environment). Terminal pane does not open if the session folder is a Windows path. Windows remote machines need Desktop 2.31226.0+ and Microsoft OpenSSH Server (not our case).
- Issues: #40967 (opened 2026-03-30, closed "not planned" 2026-06-03, locked 2026-08-06): on Windows ProxyCommand failed with `spawn sh ENOENT`; ProxyJump unsupported on both platforms. Docker's page gives the fix: install Git for Windows so `sh.exe` is on PATH (`where.exe sh`). #59799 (2026-05-16, duplicate of #57418): SSH remote crashed on every prompt on a VPS; `--dangerously-skip-permissions cannot be used with root` was one cause, so the sandbox user must be non-root; hooks in remote settings.json were suspected for the rest.
- Once connected: "SSH sessions support permission modes, connectors, plugins, and MCP servers." Skills are read from the REMOTE `~/.claude/skills/`. Proxy/CA settings for a Linux remote go under `env` in the remote `~/.claude/settings.json`. Permission prompts appear in Desktop and the engine waits on the host (claude.com SSH remote sessions page, fetched, written for the 3P edition; applies in spirit).
- Docker's Claude Desktop page (fetched) adds: "Claude Desktop can write Anthropic credentials directly into the sandbox during OAuth refresh... exposes them inside the sandbox and reduces isolation", and a known issue: "The SSH connection drops when the Anthropic token expires and needs refreshing" (workaround: run `sbx run --name <sandbox>` from the host to refresh).

## 3. Remote Control (`claude remote-control`, `/remote-control`)

From code.claude.com/docs/en/remote-control (fetched):
- Starts: `claude remote-control` (server mode, many sessions, `--spawn worktree`, `--capacity`, `--permission-mode`, `--settings <file>`), `claude --remote-control` (interactive plus remote), `/remote-control` or `/rc` in an existing session, in the VS Code extension and in a Desktop local session. Works from a terminal inside the container (`ssh demo.sbx` then `claude remote-control`). Use tmux/screen: "If you run `claude` from a terminal on a remote machine, start it inside tmux or screen".
- Requirements: Pro/Max/Ultra/Team/Enterprise; API keys NOT supported; claude.ai login via `/login`; not available with Bedrock/Vertex/Foundry, an `ANTHROPIC_BASE_URL` pointing anywhere but api.anthropic.com, or an enterprise apps gateway; unavailable if `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC` or `CLAUDE_CODE_DISABLE_FEATURE_FLAGS` is set (the dev container page says this explicitly); `DISABLE_TELEMETRY`/`DO_NOT_TRACK` alone are fine on v2.1.283+. Workspace trust must be answered in a TTY. ZDR/HIPAA orgs cannot enable it. Team/Enterprise: an Owner must enable it.
- Token limit (authentication page, fetched): a `claude setup-token` / `CLAUDE_CODE_OAUTH_TOKEN` "can only make model requests, so it can't establish Remote Control sessions".
- Connection: "outbound HTTPS requests only and never opens inbound ports"; registers with the Anthropic API and polls; all traffic via Anthropic API over TLS. Domains: api.anthropic.com for the API; claude.ai and platform.claude.com for OAuth; (network-config page, fetched) no separate RC host is listed. The session transcript is stored on Anthropic servers while connected.
- What works from the other device: full conversation, `@` file autocomplete, MCP servers (those configured in the container; "Docker Sandboxes doesn't pass remotely supplied environment variables or MCP configuration into the sandbox"), permission prompts (kept open until answered; other dialogs expire after 5 minutes by default, `dialogExpiry`), diff pane (git repo), model and effort switching, fork a session from the browser (not for a session hosted by the VS Code extension), file attachments up to the container (`~/.claude/uploads/`), subagent and workflow progress. Local-only commands: `/plugin`, `/resume`.
- Limits: the container process must stay alive; server mode exits after about 10 minutes without network; `--sandbox` flags removed in v2.1.300; `claude remote-control --continue` works for about 4 hours after a stop; one remote session per interactive process. Trusted Devices (optional) can require Face ID/Windows Hello/passkey step-up to view or steer from "claude.ai, the Claude mobile apps, or Claude Desktop".
- Surfaces: stated explicitly are claude.ai/code and the iOS/Android app. The Desktop app is named in the Trusted Devices text as a place that can view/steer RC sessions; Desktop's own page says a connected session "appears in the session list at claude.ai/code and in the Claude apps on devices signed in to your claude.ai account". Whether the Windows Code tab lists a container-hosted RC session in its sidebar is UNVERIFIED; claude.ai/code in a browser is the safe assumption.
- Docker (claude-code agent page, fetched): enable with `sbx settings set claude.remoteControl true`; "Remote Control environment and session credentials stay on the host."

## 4. Docker Desktop side

- Docker Sandboxes (docs.docker.com/ai/sandboxes/, fetched): "run AI coding agents in isolated environments" via the standalone `sbx` CLI; each sandbox is a microVM with its own Docker daemon; "You don't need Docker Desktop or Docker Engine to use `sbx`"; the Docker Desktop `docker sandbox` commands are deprecated (docs.docker.com/ai/sandboxes/docker-desktop, search result). Anthropic's sandbox-environments page also calls Docker Sandboxes "a free, standalone product from Docker that does not require Docker Desktop".
- Windows requirements (docs.docker.com/ai/sandboxes/install.md, fetched): "Windows 11" (edition unspecified), 64-bit Intel/AMD, Windows Hypervisor Platform (`Enable-WindowsOptionalFeature -Online -FeatureName HypervisorPlatform -All`, elevated), install `winget install -h Docker.sbx`. WSL2 not mentioned. Windows 11 Home: the page does not exclude it; sources conflict on whether Home has the HypervisorPlatform feature (some say Home/Pro/Ent/Edu have it, others say not Home): UNVERIFIED, test by opening "Turn Windows features on or off". A Docker forum report mentioned all sandboxes failing on Windows 11 build 26200 with sbx v0.31.0 (older; may be fixed). The owner's machine is build 26200.
- Official integrations (docs.docker.com/ai/sandboxes/integrations/, fetched):
  - `sbx setup ssh` writes a managed `Match originalhost "*.sbx"` block into `%USERPROFILE%\.ssh\config` with `ProxyCommand "sbx" ssh proxy '%n'`, `User _default_user_`, key auth disabled, `KnownHostsCommand`, `StrictHostKeyChecking yes`. Then `sbx create --name demo claude .` and `ssh demo.sbx`.
  - Transport: daemon named pipe on Windows; "No network port or SSH key is used"; "No SSH server runs inside the sandbox"; daemon accepts connections only while a Docker login is active; stopped sandboxes start on connect; port forwarding via SSH `-L` to sandbox loopback only.
  - VS Code page (fetched): install `ms-vscode-remote.remote-ssh`; Remote-SSH: Connect to Host and type `demo.sbx` manually (host pickers do not list Match blocks); first connection installs VS Code Server in the sandbox; macOS reconnect-loop fix `remote.SSH.useLocalServer=false`; if `KnownHostsCommand` / host-key errors, delete managed blocks and rerun `sbx setup ssh`. Windows-specific steps and Claude extension behaviour: not covered.
  - Claude Desktop page (fetched): create with agent type `claude` (`sbx create --name demo claude .`), `ssh demo.sbx` to test, then Desktop > environment dropdown > "+ Add SSH connection", SSH Host `demo.sbx`, leave port and identity empty, pick the mounted workspace folder (picker may open at `/home/agent`). Windows: install Git for Windows for `sh`.
  - Editor note: "Connecting an editor or desktop app over SSH doesn't supply the sandbox with the credential": the model credential must already be configured in Docker Sandboxes (`sbx secret set anthropic`, or `/login` OAuth inside).
- Credentials in sbx (docs.docker.com/ai/sandboxes/security/credentials, fetched): the sandbox sees a sentinel placeholder; a host proxy swaps in the real key for api.anthropic.com, console.anthropic.com, claude.ai, mcp-proxy.anthropic.com. Claude OAuth: "the session token stays on your host and is never stored inside the sandbox" (get-started page) which conflicts with the Desktop-writes-credentials warning above: treat Desktop's OAuth refresh as the exception.
- Workspace (architecture page, fetched): host folder mounted by filesystem passthrough with no sync, same absolute path inside; `sbx` workspaces persist until `sbx rm`; sandboxes "don't pick up user-level configuration from your host, such as `~/.claude`" (claude agent page): the sandbox has its own empty `~/.claude`; a "session mixin" kit preserves conversations when a sandbox is removed and recreated. Network: all outbound goes through a host proxy with policy presets (Balanced recommended).
- Other Docker integrations found: T3 Code page, `docker/sbx-kit-code-server`. No Docker Desktop extension that lists or opens sandboxed Claude sessions in Claude Desktop or VS Code was found. Docker MCP Toolkit: not researched, UNVERIFIED; it is an MCP gateway and not a session viewer as far as I know.
- A plain Docker Desktop (WSL2) container is the other route and is what Anthropic's dev container docs cover. With it there is no `.sbx` host name, so Desktop needs an sshd in the container (or the `docker exec ... sshd -i` ProxyCommand from Anthropic's page), and VS Code can use Dev Containers / Attach to Running Container / Remote-SSH.

## 5. Sharing one sandbox between both clients

- Storage: transcripts are `~/.claude/projects/<cwd-with-non-alphanumerics-as--> /<id>.jsonl` on the machine where the engine runs (sessions page, fetched). In the sandbox that is `/home/agent/.claude` (sbx) or the volume path you chose. The project folder name depends on the in-sandbox working directory, so both clients must open the same path (the workspace is mounted at the same absolute path as on the host in sbx).
- VS Code: "The extension and CLI share the same conversation history. ... run `claude --resume`." Session history in the panel lists past sessions; a session counts as open when it has a tab or runs in another Claude process on the machine.
- Desktop: "Each keeps its own session list" (desktop page, fetched). It can import CLI sessions on the same computer with `/resume`, but that command "is available in local sessions, not in SSH, WSL, or cloud sessions". `claude --resume` in a terminal finds Desktop sessions again only for local sessions (Desktop continues the same session). For SSH sessions Desktop says the task resumes "from the conversation's saved history" and installs its engine in `~/.claude/remote/`; it does not say whether those transcripts land in the same `~/.claude/projects` the CLI/extension use (UNVERIFIED; check with a spike by listing the directory after a Desktop SSH session). Desktop transcripts have their own retention rule (`desktopSessionCleanupPeriodDays`).
- Two clients at once: "If you resume the same session in two terminals ... both write to one transcript. When you resume that session later, messages from one of them can be missing. To keep both conversations, add `--fork-session`." RC adds nothing new here: one RC connection per process; resuming the same conversation in a second terminal prints "Remote Control not started here" and leaves RC with the first.
- Config shared across clients inside the sandbox: `~/.claude/settings.json`, `~/.claude.json`, `.mcp.json`, CLAUDE.md, hooks, skills. Desktop-only: `claude_desktop_config.json` MCP servers load in local Code sessions, not in SSH sessions (those run on the remote and use its own config).
- Persistence: sbx keeps the VM, packages and `~/.claude` until `sbx rm`; use the Docker session mixin or a named volume + `CLAUDE_CONFIG_DIR` in a plain container to survive rebuilds.

## 6. Permission modes in a sandbox

- Anthropic: bypass is meant for isolated environments. Devcontainer page: pass `--dangerously-skip-permissions` for unattended work; the CLI refuses it as root, so `remoteUser` must be non-root (sandbox-environments page: "On Linux and macOS, Claude Code refuses to start with this flag when running as root"). The flag does not stop a malicious project exfiltrating anything inside the container, including `~/.claude` credentials; avoid mounting host secrets.
- Docker sbx: the `claude` agent runs `claude --dangerously-skip-permissions` by default; args after `--` are added after it.
- VS Code extension: bypass appears only with the extension setting `claudeCode.allowDangerouslySkipPermissions` (docs: "Use it only in sandboxes with no internet access"); otherwise a `bypassPermissions` default silently starts in Manual. Start mode: `initialPermissionMode` (`default|plan|acceptEdits|bypassPermissions`) read from USER settings only; workspace values ignored. Mode picker also offers Auto, Manual, Plan, Edit automatically.
- Desktop: modes Manual, Accept edits, Plan, Auto, Bypass; bypass needs "Allow bypass permissions mode" in Settings > Code on Pro/Max/Ultra, organisation policy on Team/Enterprise; not in cloud sessions.
- CLI: bypass cannot be entered mid-session if not started with it; `permissions.defaultMode: "bypassPermissions"` in user/`--settings`/managed settings sets it. On resume, a session that ended in bypass restarts in the default mode unless bypass is enabled at launch again.
- Even in bypass: explicit `ask` rules, `AskUserQuestion`, dangerous `rm` of critical paths still prompt; deny rules still block; plan-mode blocks are not reliable when bypass is available.
- Locking: `permissions.disableBypassPermissionsMode: "disable"` in managed settings; managed file on Linux is `/etc/claude-code/managed-settings.json` (copy it in the image; only root can change it, which in a non-root sandbox user context is a real lock). Server-managed settings apply only if signed in to the organisation. Whether Desktop-over-SSH and the extension honour the file inside the sandbox is expected (same engine) but UNVERIFIED.
- Auto mode is the middle path: a classifier reviews actions; not an isolation boundary.

## 7. Sources (primary, read 2026-11-25 unless dated)

1. https://code.claude.com/docs/en/vs-code (extension; settings, modes, sessions, remote notes)
2. https://code.claude.com/docs/en/devcontainer (dev containers, CLAUDE_CONFIG_DIR volume, managed settings, bypass)
3. https://code.claude.com/docs/en/desktop (environments, SSH requirements, Windows limits, CLI comparison)
4. https://code.claude.com/docs/en/desktop-wsl (WSL sessions)
5. https://code.claude.com/docs/en/remote-control (RC)
6. https://code.claude.com/docs/en/authentication (credential storage, setup-token limit, precedence)
7. https://code.claude.com/docs/en/sessions (resume, forks, transcript storage, two-writer warning)
8. https://code.claude.com/docs/en/permission-modes (bypass, root, restrictions, resume)
9. https://code.claude.com/docs/en/sandbox-environments (Docker Sandboxes, dev containers, VMs)
10. https://code.claude.com/docs/en/network-config (domain list)
11. https://code.claude.com/docs/en/claude-directory (what `~/.claude` holds, Desktop transcript retention)
12. https://claude.com/docs/third-party/claude-desktop/ssh-remote-sessions (Desktop SSH, 3P edition)
13. https://docs.docker.com/ai/sandboxes/ and /get-started/, /architecture/, /usage/, /agents/claude-code/, /security/credentials/, /troubleshooting/ (Docker Sandboxes)
14. https://docs.docker.com/ai/sandboxes/install.md (Windows requirements, winget)
15. https://docs.docker.com/ai/sandboxes/integrations/ (SSH, `.sbx` host, ports)
16. https://docs.docker.com/ai/sandboxes/integrations/vscode/ (Remote-SSH to sandbox)
17. https://docs.docker.com/ai/sandboxes/integrations/claude-desktop/ (Desktop to sandbox, Windows `sh` fix)
18. https://code.visualstudio.com/api/advanced-topics/remote-extensions (extension kinds; page dated 2026-11-18)
19. https://github.com/anthropics/claude-code/issues/38408 (2026-03-24; extension credentials vs CLI in devcontainer)
20. https://github.com/anthropics/claude-code/issues/20793 (2026-01-25; OAuth callback in container)
21. https://github.com/anthropics/claude-code/issues/39288 (2026-03-26; extensionKind ui)
22. https://github.com/anthropics/claude-code/issues/40967 (2026-03-30; Desktop ProxyCommand on Windows)
23. https://github.com/anthropics/claude-code/issues/59799 (2026-05-16; Desktop SSH crash, root user)
24. https://code.claude.com/docs/en/troubleshoot-install (OAuth fails in WSL2/SSH/containers; page fetched but only index read; content cited via sources 2 and 6)
Not read in full: the second half of the Desktop page and the VS Code page's last 7k characters; the Docker "Preserve Claude Code and Codex conversations" kit page.

## 8. Connection paths (client x mechanism)

| # | Client | Mechanism | Works? | Install in the sandbox | Open / needed on the host |
|---|---|---|---|---|---|
| A | Desktop (Windows) | SSH to `demo.sbx` (Docker Sandboxes, daemon pipe) | Yes, documented by Docker; Desktop also needs token-refresh workaround | `sbx` agent template already has claude; nothing else (Desktop deploys its engine into `~/.claude/remote/`) | `sbx` CLI + login, `sbx setup ssh`, Git for Windows (`sh`), MS OpenSSH client 9.4+, Desktop from the download page; no ports; sandbox egress to Anthropic domains |
| B | VS Code | Remote-SSH to `demo.sbx` | Remote-SSH part documented by Docker; Claude extension inside: UNVERIFIED (expected yes) | VS Code Server (auto-installed on first connect) + extension (VS Code installs it into the remote); node not needed for the extension's bundled CLI | `ms-vscode-remote.remote-ssh`, `sbx setup ssh`; sandbox egress to VS Code server/marketplace hosts (UNVERIFIED which) |
| C | Desktop | SSH to a Docker Desktop container with sshd | Yes (documented generic SSH target; Anthropic names dev containers) | openssh-server with SFTP subsystem, `authorized_keys`, non-root user, bash, glibc, writable exec-able `~/.claude/remote`, git | publish `127.0.0.1:2222:22` (or ProxyCommand `docker exec -i ... sshd -i`), OpenSSH key; no ProxyJump |
| D | VS Code | Dev Containers (Reopen/Attach to Running Container) on Docker Desktop WSL2 | Yes for dev container (Anthropic documented); Attach to Running Container UNVERIFIED but same mechanism | Claude Code via Feature `ghcr.io/anthropics/devcontainer-features/claude-code:1.0`; optional named volume + `CLAUDE_CONFIG_DIR` | Docker Desktop, Dev Containers ext., none inbound; firewall optional (NET_ADMIN/NET_RAW) |
| E | VS Code | Remote-SSH to the same container as C | Yes (standard) | same as C | same port as C |
| F | Desktop app (WSL env) | WSL 2 distro session | Works for a distro, not a container. Only helps if Docker Desktop's WSL distro were the sandbox, which Docker Desktop internal distros are not | git in distro | n/a, not recommended |
| G | Any (claude.ai/code, mobile, maybe Desktop) | Remote Control from a terminal in the sandbox | Yes with a subscription login; viewer/steerer, not a full IDE | `claude` + a claude.ai OAuth login done inside (not API key / setup-token); tmux; for sbx `sbx settings set claude.remoteControl true` | outbound 443 to api.anthropic.com, claude.ai, platform.claude.com; no inbound; do not set DISABLE_NONESSENTIAL_TRAFFIC or ANTHROPIC_BASE_URL |
| H | Either | `claude` in a terminal over `ssh demo.sbx` or `sbx exec -it demo bash` | Yes, trivially | claude | none |

## 9. Recommended setup for "both clients, one sandbox"

Primary: Docker Sandboxes (`sbx`), one named sandbox, both clients through `demo.sbx`.
1. Spike first (about an hour): enable HypervisorPlatform, `winget install -h Docker.sbx`, `sbx setup ssh`, `sbx create --name demo claude <project-folder>`, `ssh demo.sbx`, run `claude` once and complete `/login` (paste-code flow if no browser) or `sbx secret set anthropic`.
2. Desktop: install Git for Windows (put `C:\Program Files\Git\bin` on the user Path), confirm MS OpenSSH 9.4+ and the non-Store Desktop build, add SSH connection host `demo.sbx`, pick the workspace path. Expect to refresh with `sbx run --name demo` when the token expires.
3. VS Code: Remote-SSH to `demo.sbx`, open the workspace path, install the Claude Code extension on the SSH target, check `Developer: Show Running Extensions` that it is a remote-side workspace extension, do not set `remote.extensionKind`. If sign-in does not stick, rely on the one-time `claude` login in the sandbox terminal (issue #38408 pattern).
4. Permissions: leave the sbx launcher at bypass; for the two clients set `permissions.defaultMode`/toggles deliberately, and add `/etc/claude-code/managed-settings.json` (root-owned) in the sandbox with `permissions.disableBypassPermissionsMode` only if the owner wants prompts back. Run as the non-root `agent` user.
5. Sessions: use one client at a time per session; use `--fork-session` or a second named session for the other; resume with names (`/rename`) not default labels. Keep the same working directory path in both.
6. Add RC (`sbx settings set claude.remoteControl true`, then `claude remote-control` in tmux) only if phone/browser access is wanted; remember it needs the subscription login and sends the transcript to Anthropic servers.
Fallback if sbx will not run on Win 11 Home (no HypervisorPlatform) or the extension fails there: a Docker Desktop (WSL2) container built from the Anthropic dev container feature, plus openssh-server, bind-mount only the project folder, named volume on `~/.claude` with `CLAUDE_CONFIG_DIR`, port 127.0.0.1:2222; VS Code via Dev Containers/Remote-SSH, Desktop via SSH. This is the best-documented path for the extension; the cost is that you build and harden the container (egress firewall) yourself.

## 10. Open questions for the owner

1. Is Windows Hypervisor Platform available on your Home build (Turn Windows features on or off), and are you willing to use `sbx` (standalone, replaces `docker sandbox`) rather than a plain Docker Desktop container? This chooses path A/B versus C/D/E.
2. Which login will the sandbox use: claude.ai subscription (needed for Remote Control, and Desktop needs it anyway) or an API key? Are you fine with Desktop writing OAuth credentials into the sandbox, which weakens isolation (Docker's warning)?
3. Do you need Remote Control (phone/browser) or only the two clients on this PC? RC rules out API keys, gateways and `DISABLE_NONESSENTIAL_TRAFFIC`.
4. Should the sandbox run in bypass (no prompts) with the network policy as the only guard, or keep prompts in the clients? Which egress allowlist (Balanced preset vs custom)?
5. Is it acceptable that Desktop cannot `/resume` CLI sessions in SSH sessions and keeps its own list, so cross-client history may be partial? Is "one client at a time per session" acceptable?
6. Is Git for Windows (for `sh`) acceptable on this PC, and do you have the Desktop build from the download page rather than the Store?
7. Which MCP servers must run inside the sandbox (they will, since tools execute there) and do any need host resources, such as the repo's `repo-tasks` MCP, `.mcp.json`, hidden-desktop CDP tooling that needs the Windows desktop? Those cannot run in a Linux sandbox without a bridge; this needs a separate answer.


---

# R3: Can this repo run Claude Code in a Docker/Linux sandbox, with the board reachable only over MCP? (2026-11-25)

Scope: read-only research. Nothing run, nothing changed. Paths are relative to C:\Users\artyo\Documents\5-mine\repo-task-manager. "Verified" = read in the code; "unverified" = inferred or not testable without running.

## 1. The MCP server the sandbox must reach

### 1.1 What the app exposes
- Stateless Streamable HTTP MCP at `/mcp`, started inside the desktop app process (Kestrel). `TaskManager/Api/AgentEndpoint.cs:62-64` (`WithHttpTransport(o => o.Stateless = true)`), `:92` (`MapMcp("/mcp")`). No separate server exists: the old `--serve` mode is gone (test asserts it, `TaskManager.Tests/ServingAgentsFromTheAppTests.cs:96`). **The endpoint only exists while the app window is open**; `Program.cs:126-129` starts it behind the first window.
- Bind: `http://127.0.0.1:{port}` only, `AgentEndpoint.cs:56`. Port: `Paths.Port` = `RTM_PORT` else 7431, `TaskManager/Helpers/Paths.cs:10,52`. Started from `Program.cs:129`.
- If the port is taken (second app copy), no endpoint and no `server.json` change (`AgentEndpoint.cs:94-101`).
- Token: random 32 bytes base64url, created on first use in `Paths.Home\token`, `Paths.cs:55-72`. `Paths.Home` = `RTM_HOME` else `%LOCALAPPDATA%\RepoTaskManager`, `Paths.cs:12-14`. README says "readable by you only" (README.md "Agents over MCP", the HTTP paragraph); that is the default LocalAppData ACL, the code sets no ACL itself.
- Auth middleware, `AgentEndpoint.cs:69-90`:
  - Origin: if an `Origin` header is present it must be `http://127.0.0.1` or `http://localhost` (`IsLocalOrigin`, `:153-154`), else 403. No Origin header (Node/Claude Code HTTP client) passes.
  - Bearer: `Authorization: Bearer <token>` on everything except `GET /health`, fixed-time compare (`:156-158`), else 401.
  - **There is no Host-header check in the code.** Kestrel host filtering is also off unless `AllowedHosts` is configured, and TaskManager has no appsettings.json (listing of `TaskManager/`). So `Host: host.docker.internal:7431` would be accepted (inferred; one `curl` from the container confirms).
- How a call names its repository and agent:
  - Tool argument `repo` (any path inside a repo or worktree) wins; else header `X-Rtm-Repo` (percent-encoded, decoded with `Uri.UnescapeDataString`, `AgentEndpoint.cs:85,160`); else error "Pass `repo`" because the app serves every repo (`TaskManager/Mcp/BoardTools.cs:105-114`, `DefaultToWorkingDirectory: false` at `AgentEndpoint.cs:64`).
  - Agent name: tool argument `agent`, else header `X-Rtm-Agent`, else env `RTM_AGENT` (server side), else "Claude" (`BoardTools.Write.cs:264`, `BoardTools.cs:177`).
  - `FindRepoRoot(start)` walks up on the HOST filesystem looking for `openspec/` (`BoardTools.cs:113`). A container path like `/workspace` resolves to nothing on the host: "No repository found" error.
- The stdio shim (`TaskManager/Mcp/McpShim.cs`) is just a relay: reads `server.json` + `token` from `Paths.Home`, POSTs to `http://127.0.0.1:{port}/mcp` with the bearer and the two headers (`McpShim.cs:118-125,146-150`); it answers initialize/tools/list itself when no app runs (`:87-100`). Its repo is `RTM_REPO` or its cwd (`:33`). It is a Windows exe (`dist/TaskManager-small.exe`, win-x64 profile) and cannot run in a Linux container. It is not needed: the same JSON-RPC can go straight to HTTP.

### 1.2 Can a container call it via host.docker.internal:7431?
- Docker Desktop plain container (WSL2 backend): the name resolves to the host; forum answers say the traffic arrives as the host's "localhost", so a 127.0.0.1-bound service is reachable. Docs do not state the bind-address rule (Docker docs networking how-tos, fetched 2026-11-25: "use the special DNS name"; silent on 127.0.0.1 vs 0.0.0.0). A Jan 2026 forum thread (Docker Desktop 4.55.0) reports one failure resolving to 192.168.65.254 with no confirmed fix. **Unverified for this machine: needs one `curl -v http://host.docker.internal:7431/health` from a container.**
- Docker Sandboxes (`sbx`, microVM): the docs are explicit. "Services running on your host are reachable from inside a sandbox using the hostname `host.docker.internal`"; "the sandbox proxy converts `host.docker.internal` to `localhost` before forwarding", so the policy needs `sbx policy allow network localhost:7431`; 127.0.0.1 and the LAN IP do not work from inside (https://docs.docker.com/ai/sandboxes/workflows/development/, fetched 2026-11-25). Since the proxy forwards as localhost, **the loopback-only bind can stay**, the right fit for the owner's "nothing wider" rule.
- Smallest safe change set, in order:
  1. App: nothing, if the sandbox is `sbx` (or the plain-Docker curl works). Do NOT change the bind to 0.0.0.0: that exposes the token-guarded endpoint to the LAN.
  2. If plain Docker fails: a host-side port proxy bound to the Docker/WSL vEthernet address only (for example `netsh interface portproxy add v4tov4 listenaddress=<vEthernet IP> listenport=7431 connectaddress=127.0.0.1 connectport=7431` plus a firewall rule scoped to that interface; admin needed). Risk: the proxy is reachable by every container and WSL distro, token is the only guard.
  3. Token into the container: do not copy `%LOCALAPPDATA%\RepoTaskManager\` (it also holds `server.json`, logs, the index). Pass only the token string as an env var (`RTM_TOKEN`) at container start, read from the file by a host launcher script.
  4. Repo mapping: in the container-side config set header `X-Rtm-Repo` to the percent-encoded HOST path (for example `C%3A%5CUsers%5Cartyo%5CDocuments%5C5-mine%5Crepo-task-manager`) via env expansion. Agents then never pass `repo`. Per call, a model could still pass `repo` pointing at any other repo on the host that has `openspec/`.
- Client config (verified in Claude Code docs, https://code.claude.com/docs/en/mcp, fetched 2026-11-25): `.mcp.json` entry `{"type":"http","url":"http://host.docker.internal:7431/mcp","headers":{"Authorization":"Bearer ${RTM_TOKEN}","X-Rtm-Repo":"${RTM_HOST_REPO}","X-Rtm-Agent":"Claude"}}`. `${VAR}` and `${VAR:-default}` expand in command, args, env, url and headers. Unset without a default leaves the literal text and warns. Credential-like names (ANTHROPIC_API_KEY, ANTHROPIC_AUTH_TOKEN, AWS_BEARER_TOKEN_BEDROCK, HTTPS_PROXY, NPM_TOKEN) read as empty in url/headers; `RTM_TOKEN` is not on that list (documented list as fetched; the page was read to char 100,000 of 178,357).
- Wider-than-intended facts (risks): the one token authorises every tool on EVERY repository the host can reach (the server serves all repos; `BoardTools.cs:105-114`), including write tools on other projects' boards. Writes are signed with a caller-supplied agent name. The token never rotates (created once). A container that can read the token env var can also be tricked (prompt injection) into using `repo` for another project. Mitigation needing an app change is listed in section "What must change in the app".

### 1.3 Does the server write openspec/ itself, so the container needs no write access?
Yes. All board writes happen in the app process on the host: `BoardTools.Run` opens the repo root, takes a global gate, runs the module facades (`BoardTools.cs:68-102`), then tells the window (`AfterWrite`). Ids come from a file lock in `.git/rtm/` taken by the host process (`OpenSpecStore/IdAllocator.cs:19,35`, `FolderLock.cs:5-16`, `FileShare.None`). `board_validate` runs `WorkItemsApi.Validate` which shells out to the OpenSpec CLI on the host (`BoardTools.cs:59`; CLI pinned by `WorkItemsApi.OpenSpecCliVersion`, `GuidingAgentsTests.cs:82`). So the container needs **no** access to `openspec/` for board writes, validation or id allocation. Bonus: the Windows-lock-vs-Linux-lock problem disappears because only Windows processes lock.

READ needs: the tools return everything an agent needs as JSON: item header, description, subtasks, notes, worklog, specs, tests, links (`board_get`, README table); spec text via `board_features` detailed with `feature`/`requirement` (`BoardMcp.cs:39`). Results also carry host `Path` strings (`BoardDtos.cs:64,79`), meaningless inside the container but harmless. Plans live in `DevResources/plans` (outside `openspec/`), linked with `plan`; attachments default `local/task-tracking` (REPO-TASKS.md line 1). Nothing forces direct reads of `openspec/`. The fallback guide `openspec/REPO-TASKS.md` is only for when the tools are missing; the server instructions (`BoardMcp.cs:31-45`) carry the rules.

## 2. What breaks if openspec/ is read-only or absent in the sandbox

Things that touch the repo's own openspec/:
- `.claude/skills/openspec-*` and `/opsx:*` commands (6 SKILL.md files mention it) read and write change folders by hand. They conflict with "only through MCP" anyway; they would fail under both variants (hidden: not found; read-only: write refused). Treat as disabled in the sandbox.
- CLAUDE.md rule 2 ("an OpenSpec change folder ... validated with `npx ... validate`") is replaced by `board_create` / `board_update specs` / `board_validate` (README "Agents over MCP"). The CLAUDE.md "Board" section already says tools are the only way.
- Existing guard: PreToolUse hook `.claude/hooks/deny-board-edits.js` (wired in `.claude/settings.json`) denies Edit/Write on `<repo>/openspec/changes` and Bash commands matching `openspec/changes` plus a write verb. It is a regex on the command text and `RTM_ALLOW_DIRECT_EDIT=1` bypasses it; a model can evade it with a script or `cd`. It guards only `openspec/changes`, not `openspec/specs`, `config.yaml` or REPO-TASKS.md. It is a seat belt, not isolation. Inside a container it still works (node needed, hook is dependency-free).
- Tests: the suite builds its own repos (`TestRepo`, temp folders) and fixture copies (`TaskManager.Tests/Fixtures/sample-repo`, copied to output by the csproj). The tests that read the real checkout read only source: `SourceFiles.Root()` (finds `RepoTaskManager.slnx` upward) for csproj/source lint (`ReadingProjectSettingsFromConfigTests.cs:189-192`), `release.yml`, `version.json`, `Directory.Build.props` (`ReleasingFromMainTests.cs:9-30`), `tests/startup-time.ps1` (`StartupScriptTests.cs:14`). I found no test that reads `openspec/` of the real repo (a search of the test folder for the literal `"openspec"` only hit test-made repos; unverified for the 101 files read by pattern, not line by line). So tests do not need the real `openspec/`.
- `tests/hidden-app.ps1` `New-DemoRepo` copies the real `openspec\*` (line 54-57 of the script). It needs openspec/ READABLE and non-empty; it is Windows-only anyway (see 3).
- The app, if ever run in the container, would create/refresh `openspec/REPO-TASKS.md` and write `.git/rtm` on open (`SwitchingProjectsTests.cs:40-67`). Not applicable: the app stays on the host.
- Test links check names against `TaskManager.Tests/TestResults/latest.trx` (gitignored, `.gitignore:47`), written by the host MCP server's `LinkedTestRunsApi`. A trx produced inside the container lands in the shared mount, so that works if `TestResults` is writable. A Linux run only produces results for tests that run on Linux, fewer than the Windows run, so board_update test links of Windows-only tests would warn "unknown".

Variant choice:
- **Read-only (visible, not writable) fits better.** The agent can still list/read specs for context if it wants, `New-DemoRepo`-style copies and any source linter keep working, and a write attempt fails loudly (EROFS), which is clear to the model. A nested read-only bind over the writable workspace is standard (`-v repo:/workspace -v repo/openspec:/workspace/openspec:ro`; Docker docs confirm a bind mount over a directory obscures what is there and `ro` is a supported option; the exact nested-ro-inside-rw example is not in the page, unverified but widely used). In sbx: extra workspace with `:ro` at `PATH:TARGET:ro` syntax; "targets can't match the primary workspace path" so whether a ro target INSIDE the primary workspace is allowed is unverified (https://docs.docker.com/ai/sandboxes/usage/, fetched 2026-11-25).
- Hidden (empty) variant: removes the agent's sight of the board entirely (stronger against prompt-injection copying), but breaks `FindRepoRoot` on the container side (irrelevant, server resolves on host), the OpenSpec skills, `git status` shows `openspec/` as deleted (if tracked and hidden by an empty mount, git in the container sees all 1000+ tracked files there as deleted), and any `git add -A` / commit from the container would stage the deletion. Use only with the read-only variant if git is used in the container; otherwise avoid.
- Either way, `.git` is the other hole: with `.git` writable, an agent can `git checkout`/`git restore`/`git stash` an `openspec/` path through the index even if the folder is read-only? (A ro mount blocks the write itself, EROFS.) But `git commit` into the shared `.git` changes history/refs. See section 4.

## 3. Build and test in a Linux container

- Target frameworks: root `Directory.Build.props` sets `net10.0` for every project (no `net10.0-windows`, no `RuntimeIdentifier`, no `EnableWindowsTargeting`). Nine csproj; TaskManager is `Microsoft.NET.Sdk.Razor`, `OutputType=WinExe`, `ApplicationManifest`, `ApplicationIcon` (`TaskManager/TaskManager.csproj:5-10`); `win-x64` appears only in publish profiles (`TaskManager/Properties/PublishProfiles/*`, `release.yml` on `windows-latest`).
- Windows-only packages: `Photino.Blazor 4.0.13` (cross-platform wrapper; native webview needs `libwebkit2gtk` on Linux at RUN time, not build time; unverified), `System.Security.Cryptography.ProtectedData` and `Microsoft.Win32.SystemEvents` (compile fine; runtime guarded by `OperatingSystem.IsWindows()` at `BoardStorage.cs:598,605`, `Program.cs:90`). P/Invokes (kernel32/user32/dwmapi/dbghelp/shlwapi) are compiled but only called under Windows guards (`WindowFrame.cs:35,42`, `WebViewHealth.cs:71`, `AppLog.cs:116`; `UiWatchdog.cs`/`FolderOpener.cs` not checked for guards).
- Conclusion by command (all UNVERIFIED, nothing was run):
  - `dotnet build`: **should work** on `mcr.microsoft.com/dotnet/sdk:10.0` (Ubuntu 24.04 "noble" is the default for `10.0`, https://github.com/dotnet/dotnet-docker/blob/main/README.sdk.md, fetched 2026-11-25; the .NET 10 breaking-change page "default images use Ubuntu" agrees). Risks: WinExe + `app.manifest`/`.ico` Win32 resources on Linux (Roslyn emits them cross-platform; I expect OK), Razor SDK static web assets, Nerdbank.GitVersioning (below).
  - Fast test suite (1,415 `[Fact]/[Theory]` lines in 101 files, bUnit + xunit; filter `Category!=OpenSpecCli`): **mostly should run**, with a known set that will fail or must be skipped on Linux:
    - `cmd.exe /c mklink /J` junction tests: `KeepingFilesInOneFolderTests.cs:156`, `SecurityOfBoardSyncTests.cs:89`.
    - `powershell.exe` dummy process: `UpdatingTheAppTests.cs:321` (the updater itself shells to `powershell.exe` and `robocopy.exe`, `UpdateHelper.cs:20,50`).
    - `notepad.exe` expectation, `LoggingTests.cs:248-251` (FolderOpener is OS-switched, test may pass or fail).
    - Tests that start `TaskManager.exe` or `dist` exes (`ServingTheBoardTests.cs:160-168`): fall back to `dotnet TaskManager.dll`, so OK on Linux if `TaskManager.dll` + `runtimeconfig` sit beside the test output.
    - `StartupScriptTests` runs `pwsh -File tests/startup-time.ps1 -WhatIf` (`:20`): needs pwsh in the image, and the script itself may use Windows APIs even in -WhatIf (not read).
    - Windows path literals (`C:\repo`, `@"C:\apps\TaskManager.exe"`) in 4 files (`GuidingAgentsTests.cs:96`, `RememberingSettingsTests.cs:16`, ...): tests mostly use strings, so many pass, but `Path.GetFullPath`/`IsPathFullyQualified` behave differently on Linux (e.g. the "C:/elsewhere" case at `ReadingProjectSettingsFromConfigTests.cs:134`). Case sensitivity: code switches on `OperatingSystem.IsWindows()` in `LocalTree.cs:129,212`, `AttachmentsFolder.cs:15`, `BoardStorage.cs:116`; other places hard-code `OrdinalIgnoreCase` (`Commands.cs:53,113`, `FolderLock.cs:34`), fine on Linux but different semantics from Windows.
    - The project says "Must run on Windows, macOS and Linux" in `openspec/config.yaml` context line, but the evidence that the suite is green on Linux is absent. Expect a first-run list of dozens of failures to triage. Count is unknown; **an agent should be given the job to run it once and report**. (The CLAUDE.md rule: no full suite from the main chat; this is for the merge agent.)
  - `OpenSpecCli` tests (npx openspec): need node + npm registry access; `GuidingAgentsTests.cs:81-84` uses `cmd.exe /c npx` only on Windows, plain npx otherwise. Registry egress required.
  - Real-app verification (`tests/hidden-app.ps1`, Photino + WebView2 + CDP on a `CreateDesktop` hidden desktop): **cannot run in Linux**. It uses user32 `CreateDesktop`, `CreateProcess` with a desktop name, `WEBVIEW2_*` env, WebView2 under `TaskManager\bin\Debug\net10.0\TaskManager.exe` (script lines 9-29, 102-103). WebView2 does not exist on Linux; a Linux Photino run would use WebKitGTK, a different engine than the one the owner's CDP checks target, and would need X/Wayland (xvfb) inside the container. Not equivalent evidence for a WebView2 UI. CLAUDE.md rule 3 ("verify UI in the real app ... hidden desktop") therefore stays on the Windows host.
- `tests.runsettings` (`TaskManager.Tests/tests.runsettings`): 300 s session timeout and a hang blame collector (`CollectDumpOnTestSessionHang`). Platform-neutral. Heavy parallelism in a microVM with few CPUs may hit the 5-minute abort; size the VM.
- Nerdbank.GitVersioning 3.9.50 (`Directory.Build.props`): needs `.git` with enough history (`release.yml` uses `fetch-depth: 0`; `git rev-list --count HEAD` here = 51). In a container with the repo bind-mounted from Windows: git "dubious ownership" (owner mismatch) makes libgit2/git refuse unless `safe.directory` is set (`git config --global --add safe.directory '*'` in the image); with `sbx --clone` mode the clone's history is whatever clone depth gives (NBGV height may be off; only the version string, not build correctness). NBGV can also be disabled for sandbox builds; exact property unverified.
- Image: `mcr.microsoft.com/dotnet/sdk:10.0` (Ubuntu 24.04). PowerShell: Microsoft's page (ms.date 2026-03-12) says "Only the image for the .NET SDK contains PowerShell" with examples on 9.0/8.0 tags (https://learn.microsoft.com/powershell/scripting/install/powershell-in-docker); I could NOT confirm that the 10.0 tag still ships `pwsh` (run `docker run --rm mcr.microsoft.com/dotnet/sdk:10.0 pwsh --version`). Fallback: install from packages.microsoft.com via apt (the documented preferred route for Ubuntu, https://learn.microsoft.com/powershell/scripting/install/install-ubuntu). Add `nodejs`/`npm` (for the hook, `npx openspec`, and Claude Code's npm install), `git`, `gh`. Image size: not measured; the SDK image alone is roughly 0.8-1 GB in my memory (unverified), plus node and Claude Code.

## 4. What the agent workflow in CLAUDE.md needs from the machine

| Need | In a Linux container, repo bind-mounted from Windows |
|---|---|
| git worktrees under `.claude/worktrees` | **Breaks.** `git worktree add` from Linux writes absolute Linux paths into `.git/worktrees/*/gitdir` and the worktree's `.git` file (`gitdir: /workspace/.git/worktrees/x`); Windows git on the host then sees broken paths (and the reverse for host-made worktrees). `IdAllocator` also parses the `gitdir:` line (`IdAllocator.cs:54`). Currently `.claude/worktrees` is empty and `.git/worktrees` does not exist. Fix: sbx clone mode (`sbx run --clone`; "rejected from a Git worktree other than the main one", usage doc) or worktrees on a container-local path with a private clone, never inside the shared `.git`. |
| `.git/rtm` locks (id allocator) | Only the host MCP server needs them (see 1.3). If the container never calls the allocator, no cross-OS lock problem. Without MCP (tests with `TestRepo`), they use temp folders. |
| line endings | Host has `core.autocrlf=true` (from global/system Git for Windows config, not in `.gitattributes`; only `TaskManager.Tests/Fixtures/** text eol=lf`). Working files are CRLF on disk. A container git without autocrlf shows the whole tree as modified. Fix: pass the same config (`GIT_CONFIG_COUNT`/`GIT_CONFIG_KEY_0=core.autocrlf` / `VALUE_0=true`) and `core.fileMode=false`; agent-written files get LF unless autocrlf applies. (Inferred, unverified here.) |
| git index sharing | Linux git rewrites `.git/index` stat data (inode/dev/mode); Windows git then re-hashes or shows changes; slow `git status` and the "staging area" workflow in CLAUDE.md step 7 (supervisor merges diffs into the user's index) share one index. Safest: the container has NO write access to `.git` (mount `.git` read-only, or clone mode) and returns work as a patch/branch. |
| file locks | Windows `FileShare.None` locks do not exist across the mount; irrelevant if only the host locks. |
| case sensitivity | Mount from NTFS: the Linux view is case-insensitive in practice via the mount layer (unverified); do not rely on it. |
| file watcher events | `OpenSpecStoreModule.cs:246` and `GitWatcher.cs:72` use `FileSystemWatcher`, but those run in the HOST app, which sees the host files. Writes made in the container to the mount are seen by Windows watchers (normal NTFS writes). The reverse (container watchers seeing host edits) is not reliable on Windows mounts: Docker's WSL2 best-practices page says "Linux containers only receive file change events" if the files are in the Linux filesystem (https://docs.docker.com/desktop/features/wsl/best-practices/, fetched 2026-11-25); the same page says performance is "much higher" from the Linux filesystem. Does not affect the build/test path, which does not watch. |
| node / `npx @fission-ai/openspec@1.14.3` | Works in the image; needs registry egress. Only for the `OpenSpecCli` tests; board validation is host-side. |
| PowerShell 7 | `pwsh` for Linux exists (apt/PMC). Scripts in `tests/*.ps1` are Windows-only in substance. |
| `gh` | Installable; needs a token, which must not be broadly mounted. The owner's docs warn: no `~/.ssh` or cloud credential files in the container (https://code.claude.com/docs/en/devcontainer, fetched 2026-11-25). |
| `.claude/settings.json` hooks | `UserPromptSubmit` echo and the `deny-board-edits.js` PreToolUse hook use `$CLAUDE_PROJECT_DIR` and `node`: work on Linux. The hook's `norm()` lowercases and swaps backslashes; for `/workspace` it matches. |
| Build output | `bin/obj` shared via the mount would mix Windows and Linux outputs in `TaskManager/bin` and `obj`, and `dist/` and `tests/out`. Fix: `DOTNET_CLI_HOME`, and `-p:BaseOutputPath`/`BaseIntermediateOutputPath` or `ArtifactsPath` on a container volume, so the Windows Debug exe the owner runs is never touched (also the "never kill the user's app / build to an alternate OutDir" rule). |
| .mcp.json (project scope) | The committed entry runs `${RTM_APP:-C:/Users/artyo/.../dist/TaskManager-small.exe} --mcp` (stdio). In Linux that command does not exist: shown as failed. Scope precedence is local > project > user (code.claude.com/docs/en/mcp): add `repo-tasks` at LOCAL or USER scope in the container with the HTTP entry; "the entire server entry from that source is used; fields are not merged". Project servers need interactive approval except in `-p` runs. |

## 5. Other MCP servers

- `excalidraw`: `"type":"http","url":"https://mcp.excalidraw.com/mcp"` (`.mcp.json`). Needs outbound HTTPS to `mcp.excalidraw.com:443` (allow-list it). It is a remote hosted server; diagram content leaves the container (sandbox policy matters).
- User-level servers: not in the repo. The environment lists `claude-vscode` (IDE), `claude.ai Claude Docs` (account connector) and `repo-tasks` (project). User-scope (`~/.claude.json`) servers and claude.ai connectors are the owner's account state; a fresh container has none unless `~/.claude.json` is mounted (do not mount it whole: it holds credentials-adjacent state). README and CLAUDE.md name no others besides `repo-tasks`; `memory` mentions the excalidraw MCP.
- Claude Code itself needs the Anthropic API/auth domains (https://code.claude.com/docs/en/network-config.md#network-access-requirements). Web search is a server-side Claude tool (no container egress); WebFetch/curl/npm/nuget/github need the allow-list: `api.nuget.org`, `nuget.org` (restore: Photino, Radzen, ModelContextProtocol...), `registry.npmjs.org`, `github.com` (if gh/clone), `mcr.microsoft.com` (image pulls happen on the host side).

## 6. Web findings: MCP client inside Docker reaching the Windows host

- Docker Desktop: `host.docker.internal` is the documented name for host services (https://docs.docker.com/desktop/features/networking/networking-how-tos/, fetched 2026-11-25). Bind-address behaviour not documented; forum reports say loopback services work because Docker Desktop's backend proxies as localhost (https://forums.docker.com/t/cant-access-localhost-api-from-within-docker-container-using-host-docker-internal/150788, Jan 2026 thread has a counter-report). Unverified here.
- Docker Sandboxes (`sbx`): microVM per sandbox with its own Docker daemon, filesystem and network (https://docs.docker.com/ai/sandboxes/get-started/, fetched 2026-11-25). Windows: Windows 11 x86_64, Windows Hypervisor Platform enabled, install `winget install -h Docker.sbx`; Docker Desktop not required (web search summary of https://docs.docker.com/ai/sandboxes/install/; the install page itself returned 404 via fetch, so this is second-hand). **Whether Windows 11 Home is supported is not stated anywhere I found**; WHP/VirtualMachinePlatform exist on Home, and a community guide (https://dev.to/ajeetraina/running-docker-sandboxes-on-windows-platform-1ikd) tested on Hyper-V (unverified for Home). Mounts: working dir mounted read-write; extra workspaces `PATH[:TARGET][:ro]`; `--clone` gives a private git clone; ports: `sbx port publish` (usage doc). Network: presets Open / Balanced / Locked Down; `sbx policy allow network <host[:port]>`; local allow rules are TCP by default (https://docs.docker.com/ai/governance/local/network-rules/). Host services need `sbx policy allow network localhost:<port>` (development workflow page above).
- Claude Code MCP config in a container: project-scope `.mcp.json` committed with the repo is the documented way; "Install any binaries that local stdio servers depend on in your Dockerfile, and add remote server domains to your network allowlist" (devcontainer page above). HTTP entries support `headers` and `${VAR}`/`${VAR:-default}` in url and headers (mcp page above). Local scope > project scope > user scope.
- Claude Code dev-container warning: with `--dangerously-skip-permissions` a container does not stop exfiltration of anything accessible inside it, including `~/.claude` credentials; use trusted repositories, no host secrets mounted (devcontainer page). The reference firewall `init-firewall.sh` needs `NET_ADMIN`/`NET_RAW`.
- Docker Desktop on Home + WSL2: bind mounts of Windows paths work but are slower and give no inotify to the container (WSL2 best-practices page above). For heavy `dotnet build`/test runs, keep a working clone on the container filesystem (sbx clone mode, or a named volume) instead of building on the NTFS mount.

## 7. Blockers for a Linux sandbox with this repo, each with the smallest way around

1. **`.mcp.json` stdio entry runs a Windows exe.** Way around: container-local HTTP entry for `repo-tasks` (local scope) to `http://host.docker.internal:7431/mcp` with bearer and `X-Rtm-Repo`. No app change. Risk: app must be open; endpoint absent while it is closed.
2. **Reaching 127.0.0.1:7431.** sbx: allow `localhost:7431`; plain Docker: test `curl`; else a scoped host port proxy. Never bind 0.0.0.0.
3. **Token and repo path mapping.** Token via env var, repo via `X-Rtm-Repo` (host path, percent-encoded); both set by a host launcher script, not by the agent.
4. **Blast radius of the token (all repos, all tools).** Smallest fix without an app change: none (accept). With an app change: see below.
5. **Hiding/guarding `openspec/`.** Read-only nested mount (or sbx extra workspace `:ro`), plus `permissions.deny` for `Edit/Write(openspec/**)` in sandbox settings as a second layer, plus the existing hook. All three; none alone is sufficient.
6. **`.git` shared with Windows git.** Worktrees, index, autocrlf, commits break the host. Way around: clone mode or a read-only `.git`; hand the work back as a patch/branch that the supervisor applies, as CLAUDE.md step 7 already does.
7. **Build outputs colliding with the host's Windows build (`bin/obj/dist/tests/out`).** Redirect outputs to a container volume; do not mount `dist/`.
8. **Linux test failures** (mklink, powershell.exe/robocopy, notepad, backslash paths, case rules). Way around: first run as a measured job; tag Windows-only tests (`[SkippableFact]`/trait filter `Platform!=Windows`) or accept the list; the Windows suite stays the authoritative run (merge agent on the host).
9. **Real-app UI verification (WebView2/CDP, hidden desktop).** Cannot run in Linux. Stays on the host, done by the supervisor outside the sandbox.
10. **Nerdbank.GitVersioning + `safe.directory` + CRLF.** `git config --system safe.directory '*'` in the image, autocrlf/fileMode via env config, history depth sufficient.
11. **Egress.** Allow-list: Anthropic endpoints, `host.docker.internal:7431` (localhost:7431 in sbx), `mcp.excalidraw.com`, nuget, npm, github (as needed).
12. **sbx on Windows 11 Home** is undocumented; the other route (Docker Desktop + a custom Dockerfile/devcontainer) is documented for WSL2 and Home.

## 8. What must change in the app for MCP from the sandbox

Nothing is required for a first working version (sbx or a working `host.docker.internal`). Optional, each small:
- **Repo allow-list per token** (the only piece that narrows the blast radius): e.g. a second token file or `RTM_AGENT_REPOS` env var read at `AgentEndpoint.StartAsync` (`AgentEndpoint.cs:52-90`): the middleware checks the decoded `X-Rtm-Repo` and the tool's `repo` argument resolve under an allowed root, else 403. Needs a test first (CLAUDE.md rule 1), an OpenSpec change, and a check in `BoardTools.ResolveRoot` (`BoardTools.cs:105`) because the `repo` argument is read there, not in the middleware.
- **Separate sandbox token** (revocable, write-board-only, no `board_validate` shell-outs if undesired): the token compare is a single `FixedTimeEquals` (`AgentEndpoint.cs:156-158`); accepting a second token with a role is a few lines.
- **Host/Origin**: Host header accepted today; if the owner wants to pin it, add a check for `127.0.0.1`, `localhost`, `host.docker.internal`. Only worth it with a LAN-reachable proxy.
- **Path mapping** (`/workspace` to host path) in the server, so the container never needs a host path in config: accept `X-Rtm-Repo` containing a mount-relative marker. Not recommended: keeps host paths out of the container is nicer, but adds parsing; the env-expanded header already works.
- Documentation of the sandbox entry in `AgentGuide` (the Settings page shows only the stdio entry, `Helpers/AgentGuide.cs:22-39`).

## 9. Open questions for the owner

1. Must the sandboxed agent build and run tests (then: Linux suite triage, a container with the SDK image, outputs redirected) or only edit code and use the board (then: a much lighter image, no SDK)? Who runs the authoritative Windows suite and the hidden-desktop UI check: the host supervisor?
2. Is it acceptable that the sandbox token can touch every repository the app can reach, or should the app get a repo allow-list (section 8)?
3. Is `git` allowed in the sandbox (commit, branch, worktree)? CLAUDE.md has agents commit on branches in worktrees; that cannot share `.git` with Windows. Choose: clone mode with patches handed back, or no git writes in the sandbox.
4. sbx or Docker Desktop + own image? sbx documents host reach and policies well but Windows 11 Home support is unconfirmed; Docker Desktop is the known-good route on Home/WSL2 but needs its own firewall (`NET_ADMIN`) or accepted open egress. Run `winget show Docker.sbx` / `sbx` version check and a one-line `curl host.docker.internal:7431/health` to decide.
5. Should the `openspec/` mount be read-only (readable) or empty? Recommendation: read-only. Any need for the sandbox to read `REPO-TASKS.md` or specs by file (then read-only; otherwise either)?
6. May the sandbox see `.claude/skills/openspec-*` (hand-edit workflow that contradicts the board rule)? Suggest a sandbox settings file that disables them.
7. Which user-level MCP servers and claude.ai connectors must the sandbox have besides `repo-tasks` and `excalidraw`? (Not visible from the repo.)
8. How is the Claude login given to the container (OAuth inside, or a scoped token); the dev-container warning says anything inside the container is exfiltratable under bypass mode.
9. Should the app auto-start (or a headless mode return) so the endpoint exists while the sandbox works? Today closing the app makes every board call fail from the container.


---

