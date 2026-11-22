# Tasks

## 1. Reading history

- [x] 1.1 Wrap the `git` CLI (log, config) with a timeout and "not a repo" detection; verify the "Plain folder" scenario in a test
- [x] 1.3 Build linked commits with whole-token `#wi<id>` matching; verify the "Token match" scenario
- [x] 1.5 "+ link commit" picker of recent commits, stored in `x-rtm.commits`; verify the "Pick a commit" scenario
- [ ] 1.4 Use `user.name` as the default identity, with a one-time prompt as fallback; verify the "No git user" scenario

## 2. Committing

- [ ] 2.1 Add the scoped Commit action with an editable message; verify the "Scoped commit" scenario on a temp git repo
