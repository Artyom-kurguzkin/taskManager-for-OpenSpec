# Tasks

## 1. Tasks

- [x] 1.1 merge: pure merge functions per file type against the base, with unit tests per rule <!-- agent: Claude -->
- [x] 1.2 provider: the contract, the Folder provider and the S3-compatible provider (R2), one test set run against both <!-- agent: Claude -->
- [x] 1.3 loop: local base, manifest swap and retry, triggers, deletion brake, first connection (needs merge and provider) <!-- agent: Claude -->
- [x] 1.4 git: stale-content rule, pause during a merge, reconcile when HEAD moved (the .gitattributes line and the triggers are in the host chunk) <!-- agent: Claude -->
- [ ] 1.5 ids: reserved number blocks per device (deferred: duplicate ids are repaired by #wi159 after a sync) <!-- agent: Claude -->
- [x] 1.6 ui: per-project Board storage settings, key storage, test connection, sync state, conflict notes; checked on the hidden desktop in light and dark (needs loop) <!-- agent: Claude -->
- [x] 1.7 trash: a file the sync removes is moved to a trash folder kept 30 days, never erased (decision #n285) <!-- agent: Claude -->
- [x] 1.8 old content: recognised from this computer's uncapped memory and from a refused upload; objects named by path plus content; event logs always union-merged (decision #n289) <!-- agent: Claude -->
- [x] 1.9 host: triggers (on change, timer, on open), conflicts as events and notes on the item, .gitattributes union line <!-- agent: Claude -->
- [x] 1.10 after integration: full suite, hidden-desktop check of the merged build, spec deltas, first run against a real R2 bucket (needs the owner's bucket and token) <!-- agent: Claude -->
- [x] 1.11 security fixes, host: repository settings are a proposal until approved on this computer; keys bound to their address; https only; links in the board not followed; empty provider; link and image addresses in notes; secrets removed on disconnect; key id protected <!-- agent: Claude -->
- [x] 1.12 security fixes, engine: junctions not followed on read, write or trash; a copy kept before every overwrite; cumulative deletion and overwrite brake; clocks validated and bounded; redirects off; manifest nulls, sizes, history and path names validated; conflict markers in code fences; merge time bounds <!-- agent: Claude -->
