# Releases and self-update (2026-11-24)

Status: done 2026-11-24 — R2, R3 and the auto-hide removal staged; regression 1444/1445 passed (1 skipped). R1 (cursor and hover hints) was for another project: removed from this repo, kept as a patch.

## Why
- The owner builds by hand (`dotnet publish -p:PublishProfile=dist`); no release exists that a running copy could update from.
- Asked: build a release on every push to `main`, versioned semantically like the finance-tools project; on start the app checks whether it is out of date, shows its version in Settings, and updates itself with a popup.

## What the industry does
- Nerdbank.GitVersioning (finance-tools uses it: `version.json`, `publicReleaseRefSpec`): the version comes from `version.json` plus the git height, so every commit on the release branch gets the next patch number with no manual bump. https://github.com/dotnet/Nerdbank.GitVersioning
- GitHub Actions release: `gh release create vX.Y.Z asset.zip` on `windows-latest`, `fetch-depth: 0` for the git height. https://cli.github.com/manual/gh_release_create
- Self-update for desktop apps without an installer: query `GET /repos/{owner}/{repo}/releases/latest`, compare SemVer, download the zip, then a small helper waits for the app to exit, swaps the files and starts it again (the pattern Velopack and Squirrel automate). https://docs.github.com/en/rest/releases/releases#get-the-latest-release, https://docs.velopack.io/

## Contract between R2 and R3
- Version: Nerdbank.GitVersioning, `version.json` `{"version": "0.1", "publicReleaseRefSpec": ["^refs/heads/main$"]}`; the app reads `AssemblyInformationalVersion` (strip `+commit`).
- Release: tag `v{SemVer2}`, one asset named exactly `RepoTaskManager-win-x64.zip` holding the `dist` publish (TaskManager.exe + wwwroot/).
- Repo: `Artyom-kurguzkin/taskManager-for-OpenSpec`.
- A local or Debug build (non-public version, has a `-g<commit>` prerelease suffix or Debug configuration) never updates itself.

## Chunks
| Chunk | What | Tests |
|---|---|---|
| R2 release-ci | version.json + NB.GV, `.github/workflows/release.yml` | build check: `dotnet build` shows the version; workflow lint |
| R3 self-update | update check on start, Settings version row, popup, download + swap helper | unit tests with a fake HTTP handler and temp folders |
