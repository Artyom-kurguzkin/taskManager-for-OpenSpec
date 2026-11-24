# Proposal

## Why

Builds are made by hand, so there is no release a running copy could update from. The owner wants each push to origin main to build a release, versioned semantically the way the finance-tools project does (Nerdbank.GitVersioning).

## What Changes

- version.json with Nerdbank.GitVersioning (0.1 + git height); public releases from main only; other branches carry a -g<commit> suffix and are never released.
- .github/workflows/release.yml: on every push to main, build, run the fast tests, publish the dist build, zip it as RepoTaskManager-win-x64.zip and create the GitHub Release vX.Y.Z (skipped when it exists).
- README: a Releases section.
