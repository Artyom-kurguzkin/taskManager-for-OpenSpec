# Research: carrying a task board through git with an event log

Date: 2026-11-26. Research only. No code changed.

How to read this file. Numbers in square brackets point to the Sources list at the end. "Date" on a source is the date shown on the page. Where a page showed no date I write "no date shown" and give the day I read it (2026-11-26). "Not verified" means I could not confirm the point from a primary source. Some pages were reached only through a search snippet; those are marked "snippet only".

## Summary (10 lines)

1. Every tool that syncs through git without the user committing keeps its data off the user's branch: in a custom ref (git-bug, Radicle, git-appraise, Dolt/Beads), on a separate branch with its own checkout (Beads sync branch, tbd, TaskChampion git server), or in its own repository (git-issue).
2. The robust ones write with plumbing (hash-object, a temporary index, write-tree, commit-tree, update-ref) into a bare or ref-only store. Dolt's team tried a working checkout first and dropped it because writes to a checkout "would not land atomically under concurrency" [9].
3. Concurrent writers converge in one of three ways: a DAG of operations ordered by Lamport clock plus a hash tie-break (git-bug [12], Radicle [15]); one linear chain where a push is a compare-and-swap and the loser rebases (TaskChampion [31][32][33]); or append-only lines with a timestamp that any reader sorts and de-duplicates (git-annex [29]).
4. Nobody infers deletions from a missing file. Deletion is a recorded event: a `0` or `X` line (git-annex [29]), a tombstone or soft delete (Beads [5], tbd [11]), a Delete operation (TaskChampion [31]), or "shunning" as a rare admin act (Fossil [19]). git-bug and Radicle docs do not describe deletion at all (not verified beyond that).
5. Offline id allocation has one answer in all the tools: no central counter. Use a hash or random id (Beads [4], Fossil [18], git-bug [12], git-issue [20]), with a short alias for humans that can be re-issued on collision (tbd [11]). Beads dropped sequential ids and its renumbering flag in v0.20.1 [4].
6. Ordering: wall-clock time alone is the weak spot. git-bug uses Lamport clocks and rejects clocks that jump [12]; Linear and Replicache let the server assign order [36][37]; a hybrid logical clock [42][43] is the standard fix when there is no server.
7. The biggest warning is Beads: its SQLite-and-JSONL-in-git sync was called "too fragile to be workable" [6], and the project moved to Dolt on a custom ref (Feb 2026) [1][8]. Two stores kept in step by a sync routine is what failed, not git itself.
8. Auto-commit tools (Obsidian Git, gitwatch, git-annex assistant) put many commits on the user's own branch, hit `index.lock`, and give weak conflict feedback [22][24][25][26]. The owner ruled this out anyway.
9. Git costs are small for text: GitHub recommends under 10 GB, 1 MB per object, 6 pushes per minute and 15 fetches per second per repository [56]. A board of text files fits well inside that. Loose objects from plumbing writes need a pack step (`git gc --auto` or `git maintenance`) [53][54].
10. Binaries do not belong in the text history. Keep them in blob storage and put only a pointer (hash, size, name) in the log, as Git LFS does [59] and as git-annex does with special remotes [28]. That is option d for bytes, whatever carries the text.

## 1. Tools that sync through git without the user committing

For each: where the data lives, how it gets in and out without disturbing the user's branch, how writers converge, how deletion is shown, what people complain about.

### git-bug
- Data: one chain of git commits per entity, exposed as a ref `refs/<namespace>/<id>` (for bugs, `refs/bugs/<id>`). Each commit's tree holds an `ops` blob (a JSON "operation pack"); media goes under `/media` [12].
- Write path: plumbing only, no checkout and no index of the user. The ref is what is pushed and fetched, so the user's branches are never touched [12][13].
- Convergence: a pull with no local edits moves the ref forward. A pull with local edits makes a merge commit, so the history is a DAG with one head. Operations are replayed in order of their Lamport clock; ties break on the lexicographic order of the pack id [12]. Clocks are stored as tree entry names (`create-clock-14`, `edit-clock-137`). A child commit must carry a higher clock than its parent or the commit is discarded [12]. The Go code also guards against clocks that jump far ahead (snippet only; not verified in code) [12].
- Ids: the entity id is the hash of its first operation; shown as a 7-character prefix [12].
- Deletion: the data-model doc does not cover it [12]; the README covers only removing a bridge [13]. Not verified for bugs.
- Bridges: GitHub, GitLab, Jira, Launchpad import and export [13].
- State: 10.9k stars, 112 open issues on the page I read; releases v0.9.0 (2025-05-12) and v0.10.1 (2025-05-19) [14]. Complaint found: the web UI is labelled work in progress [13]. I found no first-hand report of push conflicts or slowness (not verified).

### Radicle collaborative objects (COBs)
- Data: refs under `refs/cobs`, one namespace per type (`xyz.radicle.issue`, `xyz.radicle.patch`). Each change is one git commit; a COB is a DAG of commits [15].
- Replication: nodes gossip only metadata. Data moves with git's own fetch. Each peer's node id is a git namespace and only that peer writes to it. All namespaces share one object database. Each node signs its refs under `refs/rad/sigrefs` [15].
- Convergence: syncing unions the commit graphs, which is idempotent. State is rebuilt by reducing the graph in topological order. Where order is only partial a merge function applies (simplest: sort by commit hash). A COB can report a conflict instead [15].
- Deletion: the guide says nothing about it (not verified elsewhere) [15].
- The "each writer owns a namespace" idea is why Radicle never has two writers on one ref. Worth noting for two computers plus agents: one namespace per writer avoids push races entirely.

### git-appraise and git notes
- Data: reviews live in git notes refs: `refs/notes/devtools/reviews`, `/ci`, `/analyses`, `/discuss`. One JSON item per line [16].
- Merge: notes can be merged automatically with `cat_sort_uniq` (concatenate, sort, de-duplicate) or `union` [17]. Default `manual` leaves a conflict in `.git/NOTES_MERGE_WORKTREE` [17].
- Fetch and push: `refs/notes/*` is not in git's default refspecs; you add them yourself [17] (the page lists the examples, not as a rule of the tool).
- Reading the repo page I could not see a last-commit date, so maintenance status is not verified [16].
- Caution from the notes doc: automatic strategies "avoid conflicts but can silently merge content you didn't want" (my wording of the doc's caveat) [17].

### Beads (steveyegge), 2025 designs and the 2026 redesign
- Classic design (to early 2026): SQLite file per machine (git-ignored) plus `.beads/beads.jsonl` tracked in git as the source of truth. A daemon debounced writes for 5 seconds, exported changed entities, and auto-committed if hooks were installed. Importing compared content hashes [2].
- Hash ids: v0.20.1 (commits dated 2025-10-30/31) replaced sequential ids with short hash ids because concurrent agents and branches produced duplicate `bd-10` ids. Ids start at 4 characters and grow. The commits also removed `--resolve-collisions` and the renumbering code [2][4].
- Merge driver: `bd init` set up a git merge driver for the JSONL (snippet only; page now returns 404) [5].
- Sync branch: issue data goes to a branch (default `beads-metadata`) through a git worktree at `.git/beads-worktrees/beads-metadata/`, sparse so only `.beads/` is present. "Your main working directory is never affected." Data reaches `main` only by a deliberate merge or pull request [3].
- Deletion: tombstones as an anti-resurrection marker, and a `.beads/deletions.jsonl` manifest that propagates through git (snippet only; pages later removed) [5].
- Complaints, from the project's own docs [3] and snippets [5]: "refusing to merge unrelated histories"; a "worktree already exists" error needing manual prune; every clone must use the same `sync.branch` name or changes do not sync; `--auto-push` bypasses branch protection; `fatal: 'main' is already checked out` caused by the sync-branch worktree; a stale sync branch can push old data back over newer. The recovery guide said keep one side of a conflicted JSONL, which can undo a deletion made on the other side (that last link is the snippet author's reading, not confirmed) [5].
- Redesign: DoltHub says the SQLite-to-JSONL-in-git sync "proved too fragile to be workable" [6]. A secondary source puts Dolt as default at v0.50.0 on 2026-02-14 and removal of the old code at v0.51.0 (not verified from a primary source) [7]. The current README: sync is `bd dolt push` and `bd dolt pull` against `refs/dolt/data` on the git remote; `issues.jsonl` is "an export for viewers and interchange, not the source of truth or a backup" [1]. A frozen Rust port of the classic design (`br`) exists for people who want it [7].
- Cost of the new design, from the README: schema skew across clones gives "cryptic SQL errors"; one clone must migrate and push, the others bootstrap; embedded mode is single-writer [1].

### Dolt git remotes (the custom-ref pattern Beads now uses)
- Announced 2026-02-13. Remote data sits on a custom ref (default `refs/dolt/data`) in the same repository as the code. It is not a branch; normal clone, fetch and pull do not bring it; GitHub's UI does not show it. The git binary must be on PATH. The repository must already have one branch [8].
- Write path (2026-02-19): a bare local repo with no working tree. `git hash-object -w --stdin` for blobs; a temporary `GIT_INDEX_FILE` with `read-tree`, `update-index --cacheinfo`, `write-tree` for trees; `git commit-tree` for commits (first commit has no parent, later ones use `-p`); `git update-ref` for the ref [9].
- Races: fetch the remote head, build a commit on it, push with `--force-with-lease` against the tracked state. If rejected, fetch, rebuild, retry with exponential backoff. Large blobs are split into numbered parts because hosts cap object size [9].
- Why not a checkout: the team rejected `clone`, `fetch`, `reset --hard`, `push --force-with-lease` around a working tree because writes to it "would not land atomically under concurrency" [9].
- Delete the data by pushing an empty ref [8].

### tbd (jlevy)
- Plain files per entity, one hidden sync worktree (later "shared common-dir sync worktree"). Sync retries use jitter and backoff. Conflicts are found by content hash. Last-writer-wins uses `updated_at` plus entity id as a deterministic tie-break. Labels use a 3-way set merge. Writes are temp file plus rename. Deletion is soft by default; hard delete is discouraged [11].
- Ids: 10-character internal hash id plus a short external id with a project prefix. Short length grows with count (3 characters up to 100 issues). Short-id collisions resolve last-write-wins and the loser regenerates its alias on sync [11].
- Dated notes in the PR: opened and merged 2026-01-09; comparison with Beads 2026-01-17; PR #287 on 2026-09-14 "fixing three silent data-loss paths and a dishonest dry run" [11]. This is a small young project; treat it as a design to read, not proof.

### Fossil tickets
- A ticket is a list of append-only change artifacts (ticket id, time, user, key/value pairs). The current state is the changes applied oldest to newest; each value replaces or appends. The TICKET and TICKETCHNG tables are derived and rebuilt whenever new artifacts arrive. Only the artifacts sync, not the tables. Ticket ids are large random hex values [18].
- Deletion: Fossil only adds. "Deleting" adds an empty version; the old one stays. Real removal is "shunning": a list of artifact hashes that Fossil refuses to push or pull. The shun list is local and does not spread by normal sync (admin copies it) [19].

### Sciit
- I could not open the repository page (HTTP 404 on the URL I tried). What I know is from memory: issues live in code comments and history is read from git. Not verified.

### git-issue (dspinellis)
- A `.issues` directory with its own `.git`, kept out of the project repo (the README suggests ignoring it). One directory per issue, plain text files. `git issue push` and `pull` sync with a remote. Issue id is the SHA of the commit that opened it. Conflict handling is not explained in the README. No delete command; close swaps a tag [20].

### jj (Jujutsu) operation log
- Every command that changes the repo records an operation with a "view" snapshot (bookmarks, heads, working-copy commits) and a pointer to its parent operations. Concurrent commands do not corrupt the repo and need no lock; each command loads the latest operation and divergence is reported later. Over a shared file system it needs "a write is only visible once previous writes are visible" [21]. It does not describe merging diverged operations on that page (the guides do; not read).
- Relevance: it is an operation log as source of truth with state as a view. Contrast, not a model for git transport.

### Auto-commit tools: Obsidian Git, gitwatch, git-annex assistant
- Obsidian Git: commit, pull, push on a schedule; desktop uses the git binary, mobile uses isomorphic-git and the docs call mobile "very unstable". Merge conflicts are resolved by the user in the editor [22]. Users report `index.lock` errors (including when no lock file exists) [24]. A blog says the plugin does not warn after a pull, so files can end up with conflict markers (snippet only) [24]. A 2024-11-08 forum post: a two-device vault could not pull because of a merge conflict [23].
- gitwatch: waits 2 seconds after a change, `git add`, commit "Scripted auto-commit on change", optional push after each commit. History noise is my inference, not stated. Conflict handling is only `PULL_BEFORE_PUSH` (`git pull --rebase`) [25].
- git-annex assistant: watches files, commits automatically, syncs through `git annex sync` [27]. Its `sync` pushes to `synced/master` because a non-bare remote will not accept a push to its checked-out branch, then merges `synced/<branch>` on the other side. Concurrent pushes can fail non-fast-forward and it "does not retry in a loop" [28]. Bug titles on its design page include "random files vanishing when assistant gets restarted" and "Files disappear from locally paired annexes when edited" (titles only) [26]. A wishlist item is "disable automatic commits" [26].
- Pattern for the owner's constraint: git-annex's `synced/` branches show how to keep the checked-out branch untouched; an app can do the same with a ref it owns.

## 2. Event-sourced and append-only sync

### Operation logs as the source of truth, state as a projection
- git-bug: operations in git are the truth; bug state is rebuilt by replay [12].
- Fossil: ticket change artifacts are the truth; SQL tables are rebuilt [18].
- git-annex: per-key `.log` files, one line per event: timestamp, `1` present / `0` not present / `X` dead, repository UUID. "Designed to be auto-merged by simply concatenating them". Readers find the newest line. Drop is a new `0` line; the old `1` stays. Removed metadata values are kept as `-value` entries so an old line cannot add them back after a merge [29].
- Linear: each workspace has an "immutable ordered sync action log"; clients replay it into a local DB and send a checkpoint (the id of the last action applied) to get only newer ones; duplicates between two read sources are removed by sync action id (2026-08-18) [36].
- jj: operation log with snapshots, undo and restore [21].
- Contrast, not for this board: Automerge keeps changes in a DAG and offers compaction that "serialize[s] the current state without the history", but a compacted copy cannot sync with a document that lacks a common ancestor [45]. Yjs keeps metadata for every edit so conflicts can be resolved, garbage-collects deleted content, and an undo manager can switch collection off so size grows without bound (forum posts, snippet only) [46]. Lesson: a general CRDT is not needed when the events are already domain events.

### TaskChampion (Taskwarrior 3), the closest model
- Operations: `Create(uuid)`, `Delete(uuid)`, `Update(uuid, property, value, timestamp)`. No-ops when the task exists or does not exist, so replays are harmless. A `None` value removes a property [31].
- Order: versions form one branch-free chain named by UUID. The server accepts a new version only if its parent is the current latest. The loser gets the expected parent back, rebases (transforms) its operations over the newer versions, and tries again. Two conflicts with the same expected base mean the replica diverged, treated as a serious error [31][32].
- Replicas never talk directly; a server holds opaque, client-encrypted bytes [31][32].
- Snapshots: a replica with no unsynchronised operations uploads a JSON map of the whole database at a version. The server then may delete older versions. A new replica downloads the snapshot plus later versions. If a replica asks for a version that was deleted, its sync fails and local changes can be lost [34].
- Git backend exists in 3.1.0 (crate source, no date shown): `GitSyncServer` keeps a normal checked-out clone at `local_path` on a configured branch and drives porcelain git (`add`, `commit`, `push`, `fetch`, `reset --hard`, `clean`, `ls-remote`). Files: `v-{parent}-{child}` (encrypted history), `snapshot`, `meta` (latest version, salt). A write commits, pushes, and if the push is rejected runs `reset HEAD~1 --soft`, resets to the remote and returns "expected parent" so the caller rebases. Snapshots are requested at urgency Low after 50 versions and High after 100. Cleanup removes covered version files older than 180 days with `git rm` [33].
- Cloud backends (S3 and Google Cloud Storage) exist too; docs describe manual bucket setup (search result, page not opened) [35]. Useful for the owner: the same event chain runs over a git ref or over a blob bucket.
- TaskChampion's 2024 to 2025 changes: I did not confirm a dated 2024 or 2025 change log (not verified).

### Server-ordered engines (contrast)
- Replicache: each mutator call is a pending mutation with a sequential mutation id per client; the server re-runs them in id order and records the client's last processed id; the client pulls a patch, rewinds, applies it and replays pending mutations [37]. Replicache is in maintenance mode and its successor is Zero (Zero 1.0 reported June 2026, via snippet) [38][39].
- Linear: server-assigned ordered sync action ids [36].
- Figma: the server orders events; last writer wins per property; object ids contain the creating client's id so ids can be made offline [41] (2019 source, kept because it is the standard reference).
- Local-first essay (April 2019): same-property conflicts are rare in practice; CRDTs merge concurrent edits to different objects without help [40]. Old, but still the reference the 2024 to 2026 tools quote.

### Ordering without a server
- Lamport clock (git-bug): counter per replica, bumped to max seen plus one; equal means concurrent; tie-break on a hash [12].
- Hybrid logical clock (HLC): wall-clock plus a tick counter. Compare wall-clock first, then ticks. Guarantees causal order if physical clocks stay within a bound of each other [42][43]. The Fowler page does not say how big a skew is tolerated; the 2014 paper assumes a bound (paper not re-read; not verified here).
- Tbd uses `updated_at` plus entity id as a deterministic tie-break [11]. That is plain wall-clock with a tie-break; clock skew would pick the wrong winner.
- Dedup: give every event a globally unique id; ignore an id already seen. Linear dedups by action id [36]; git-annex reads the newest line and tolerates duplicate lines [29][30]; TaskChampion makes ops idempotent [31].

### Offline id allocation
- Sequential then renumber on merge (the owner's current way): Beads did this first and removed it in 2025 because concurrent agents collided [2][4]. tbd keeps an alias that is re-issued on collision [11].
- Hash or random id plus short alias: Beads (4 characters growing) [2][4]; git-bug (7-character prefix of a hash, extended when ambiguous) [12]; git-issue (commit SHA, abbreviations accepted) [20]; Fossil (large random hex) [18]; tbd (10-character internal id, short alias with a project prefix) [11].
- Client id in the id: Figma [41].
- Time-sortable ids: UUIDv7 is a standard (RFC 9562, May 2024) [44]. ULID has no RFC. Both can be made offline. Both reveal creation time. ULIDs in one millisecond need a monotonic variant to stay ordered [44].
- Note on human ids: Beads moved away from numbers entirely; the alias approach (tbd) is the one that keeps a short readable number while the real id never changes [4][11].

## 3. Git mechanics that matter

### Custom refs, fetch, push
- Push a non-branch ref with a full refspec, `refs/app/x:refs/app/x`. Git adds no prefix to refs outside `refs/heads` and `refs/tags` [49].
- Outside branches and tags, a fast-forward update to a commit is allowed; a non-fast-forward needs `+` or force; if the source is a tree or blob any update is refused [49]. So a ref must point to a commit.
- `--force-with-lease=<ref>:<expect>` does compare-and-swap on the remote. An empty `<expect>` means the ref must not exist. The docs say its semantics "may still change". Plain `--force-with-lease` is weak if a background process runs `git fetch` [49]. Dolt uses it against a tracked state [9]. With an explicit expected value you do not depend on tracking refs.
- `--atomic` updates several refs on one remote all or nothing [49].
- Local compare-and-swap: `git update-ref <ref> <new> <old>`; an all-zero old value means "create only"; `--stdin` with `start`/`commit` makes a multi-ref transaction [48]. Reflogs for custom namespaces are off unless `--create-reflog` or the log file exists [48].
- A pushed custom ref is invisible in the GitHub UI and not fetched by a normal clone or `git fetch` with default refspecs [8]. A normal `git gc` keeps what refs reach (the gc page lists branches, tags, index, remote-tracking and reflogs; custom refs are, I believe, roots too, and Dolt depends on it, but I did not find the sentence; not verified) [53].

### Merging log files
- `merge=union` is built in: a text merge that keeps lines from both sides with no markers. The docs warn it "tends to leave added lines in random order" and "do not use this if you do not understand the implications" [47].
- Duplicates: git-annex's location logs showed duplicate lines after union merges (forum, snippet only) [30]. A git mailing list thread reports a union merge duplicating a line (snippet only) [30].
- A union merge only runs when git needs a file-level three-way merge; the attribute page does not say when that happens [47]. If one side did not touch the file, there is no merge to run (general git behaviour; the page does not state it).
- Custom driver: defined in `.git/config` or `~/.gitconfig`, not in `.gitattributes`. So every clone must be set up. `%O %A %B %L %P` are base, ours, theirs, marker size and path; exit 0 means merged, 1 to 128 conflict, above 128 failure [47]. `recursive=` names the driver for virtual ancestors [47].
- Only log lines that are self-sorting and self-deduplicating are safe under union: unique event id, HLC, one JSON object per line. This is exactly what git-annex logs [29] and git-appraise notes [16] do; `cat_sort_uniq` does the sort and dedup on read [17].
- If the app reads and writes through its own ref and merges events in code, no merge driver is needed at all.

### Cost of many small commits
- Plumbing writes make loose objects. `git gc --auto` runs after porcelain commands that create objects, when loose objects pass about 6,700 or packs pass 50 [53]. A tool that only runs plumbing may never trigger it; run `git gc --auto` or `git maintenance run` yourself (inference from [53]).
- `git maintenance` tasks: commit-graph, prefetch (puts refs under `refs/prefetch`), gc, loose-objects, incremental-repack [54]. On Windows `git maintenance start` creates Task Scheduler jobs; they run only when the user is logged in and show a console window unless set up otherwise [54].
- Unreachable objects are pruned after `gc.pruneExpire` (default 2 weeks). gc running with other writers carries "some risk of corruption", which the docs call low in practice [53].
- Event commits can be batched: one commit per sync, not per event (design choice, not from a source).

### Hosting limits (GitHub)
- Recommended on-disk size 10 GB; recommended object size 1 MB; hard limit 100 MB; a push is capped at 2 GB; recommended 6 pushes per minute and 15 fetches or clones per second per repository; merges about one pull request per minute [56].
- GitHub's page does not give a limit on refs per push [56].

### Detecting remote change cheaply
- `git ls-remote <url> <pattern>` lists only matching refs; `--exit-code` returns 2 if none match. Whether the server returns fewer refs (protocol v2 `ref-prefix`) is not on the doc page; not verified [50].
- Polling cost is real: a mailing-list thread (2018-12) says over half of Google's git server bandwidth was ls-remote polling, and proposed If-Modified-Since style checks. That was a proposal [62].
- GitHub's stated rate guidance is 15 reads per second per repository [56]; one poll every 30 to 60 seconds from two or three machines is far below that.
- ETag or conditional requests on GitHub's REST API for refs: not found, not verified.

### Libraries for .NET
- LibGit2Sharp: NuGet 0.32.0, last published 2026-07-24, targets .NET 8 and .NET Framework 4.7.2, depends on LibGit2Sharp.NativeBinaries 2.0.324 [60]. The GitHub page showed 2,837 commits, 406 open issues, 54 open pull requests, no releases listed [60]. I found no 2025 or 2026 maintenance statement (not verified).
- Worktrees: supported since libgit2 0.25; `git_worktree_add` always makes a new branch named after the worktree, so adding a worktree for an existing branch fails (old issue thread, undated) [61].
- libgit2 has a built-in union merge driver (reference page seen in a search result, version 1.8.6) [47].
- Shelling out: Dolt, TaskChampion and Obsidian Git (desktop) all call the git binary [8][22][33]. The git binary gives credential helpers, hooks and the same behaviour as the user's own git. Cost: git must be installed and on PATH; Dolt lists this as a requirement [8].
- Not verified: LibGit2Sharp support for SSH on Windows and for credential manager in 0.32.

### Credentials on Windows for a background push
- `GIT_TERMINAL_PROMPT=0` stops git prompting on the terminal [55].
- `credential.interactive=false` (git config, added 2024 so background jobs fail instead of blocking; patch series 2024-09-19, git maintenance's prefetch was the trigger) [58]. Git Credential Manager honours it and also has `GCM_INTERACTIVE` (search snippet) [58].
- Git maintenance's Windows task runs only while the user is logged in [54].
- Dolt noted a bug in v1.81.10 where remotes needing username and password on STDIN fail [8].
- Not verified: the exact GCM behaviour for a process started from a desktop app on a locked screen.

### Running plumbing while the user or an agent runs porcelain
- Git uses pessimistic lock files (`*.lock`). A second process that needs the index while `index.lock` exists fails [57]. GitHub Desktop (2015-10-20) serialised destructive operations in its own app and ran reads in parallel [57]. Old, but it states the model.
- Background readers should use `git --no-optional-locks` or `GIT_OPTIONAL_LOCKS=0` so `git status` does not take the index lock [55]; a 2021 mailing-list reply from Jeff King suggests this for status pollers (snippet only) [68].
- `hash-object -w`, `write-tree`, `commit-tree` on objects, and a temporary `GIT_INDEX_FILE` never touch `.git/index` [9][52][55]. `update-ref` locks only the one ref file and can compare-and-swap [48]. So a ref-only writer cannot hit the user's `index.lock`. It can still meet a ref lock from a concurrent `update-ref` on the same ref, which is the point of the CAS.
- `git commit-tree` does not update any ref and does not read the index [52].
- Per-worktree files are `HEAD` and `index`; all other refs are shared. A branch can be checked out in only one worktree. `git worktree add --no-checkout` and `--orphan` exist [51]. A separate worktree for a sync branch gets its own index, but the branch name is then reserved and `git worktree prune` can remove its entry. This is the source of the Beads errors above [3][5][51].
- gc and `git maintenance` can run while the app writes; objects newer than the prune date are kept [53].

## 4. Large binary attachments

- Git LFS: the pointer file is plain text under 1,024 bytes with `version`, `oid sha256:...`, `size`; objects live outside git (`.git/lfs/objects/...`); a `clean` filter makes the pointer, `smudge` downloads on checkout, a pre-push hook uploads [59]. It needs an LFS-speaking server. GitHub has per-file and bandwidth limits by plan; the 2025 figures were not found (not verified) [56].
- git-annex: a symlink or pointer in git, content in any special remote (S3 and others), location logs in the `git-annex` branch as union-merged lines [29]. More options than LFS, more setup. No LFS-style lock feature (forum, mirrored with git-annex 10.20250721) [66][67].
- Plain pointers in the event log: put `{name, sha256, size}` in the event; keep the bytes in R2 under the hash. Content addressing means two machines uploading the same file write the same key, so uploads need no merge. This is a design suggestion drawn from [59] and [29]; no tool in the research does exactly this.
- Dolt splits big blobs into numbered parts because hosts cap object size [9]. That is a sign not to push large files into a custom ref.
- GitHub: recommended object size 1 MB, hard limit 100 MB, repository 10 GB [56].

## 5. Known failure stories

### Auto-commit and history
- A scheduled action committed every hour and "quietly polluted my commit history"; the fix was a rebase from 1,091 to 969 commits and a force push (blog, no date shown) [64].
- Replit users complained that their branch flooded with a commit per agent action (forum, no date shown) [65].
- Obsidian Git on mobile: crashes, buffer overflow, the author says such reports will not be fixed [22]. `index.lock` errors reported [24]. Pull merge conflicts left unresolved or unclear [23][24].
- gitwatch history noise: my inference [25].
- git-annex assistant: design page lists bugs about vanishing files and uploads to dead remotes (titles only) [26].

### Merge drivers and union
- Union keeps duplicates and random order [30][47].
- Custom drivers live outside the repository, so a second machine or a fresh clone silently falls back to the default [47].
- A merge driver runs only when both sides changed the file (general git behaviour; not stated on the attribute page) [47].
- A tool that sets up its own merge driver on first run must set it per clone; Beads did this in `bd init` [5].

### Sync branches that diverge or block
- Beads: unrelated histories; worktree already exists; main already checked out; every clone must share the same sync branch name; auto-push bypasses protection; stale remote branch can overwrite newer data [3][5].
- TaskChampion git backend: concurrent snapshot writes can overwrite each other; the author accepts it because a replica with newer state overwrites again [33]. It works on its own clone and uses `reset --hard`, which is safe only because nobody else uses that checkout [33].
- Dolt: rejected a checkout design for atomicity; with plumbing it needs fetch, rebuild, retry, with backoff [9].

### Dual stores and cut-over
- Beads: SQLite plus JSONL plus git; DoltHub calls it "too fragile"; Yegge's own Medium piece (snippet only) described "SQLite + JSONL + awkward syncing and tons of merge conflicts" [6][10]. The move to Dolt then needed per-clone migration steps and a "schema skew" guard [1].
- Some users want the old design back: a Rust port of the classic Beads and a DoltHub post about "restoring Beads classic" for solo users who did not want an external server [7].

### Clock skew
- git-bug: wall time can be wrong across machines, so Lamport clocks decide; equal clocks mean concurrent; a child with a lower clock is rejected [12].
- HLC needs clocks within a bound [42][43]. tbd uses `updated_at` and so inherits skew [11].
- Not verified: any public incident report of skew corrupting a git-based board.

### Deletions
- Fossil: no deletion except shunning, and the shun list does not spread unless an admin copies it [19].
- git-annex: dead and not-present are lines, not removals [29].
- Beads: resurrection risk when a stale clone is imported; fixed by tombstones and a deletions manifest [5].
- TaskChampion: a Delete operation is an event; the git backend removes only covered version files after 180 days [31][33].
- The owner's own failure ("would delete 48 of 77 files") is the generic listing-diff failure that none of the researched tools use.

## Options for carrying a board through git

| Option | What it is | Pros | Cons | Who does it |
|---|---|---|---|---|
| a. Auto-commit on the user's branch | A watcher commits board files on whatever branch is checked out, then pulls and pushes | No new concepts. Board shows in normal git log. Works with any git GUI | Writes into the user's history and index. `index.lock` collisions with the user and agents. Noisy history. Weak conflict feedback. Breaks the owner's rule that only they commit | Obsidian Git [22], gitwatch [25], git-annex assistant [26] |
| b. Dedicated branch plus separate worktree | A branch such as `board` checked out in a hidden worktree (or its own clone); the app commits there and pushes it | Plain files, normal merges, visible on GitHub, own index so the user's index is safe. Sparse checkout keeps it small | Branch is locked to one worktree. Worktree errors after prune or manual moves. Every clone must agree on the branch name. Unrelated-histories errors. Porcelain in a shared checkout is fragile. Stale branch can overwrite newer data | Beads sync branch [3], tbd [11], TaskChampion git server (own clone) [33], git-issue (own repo) [20] |
| c. Custom ref written with plumbing, no worktree | Objects built with hash-object, a temporary index or mktree, commit-tree; moved with update-ref; pushed as `refs/app/...` with an explicit lease | Never touches the user's branch, index or working files. Compare-and-swap push gives clean retry. No checkout to corrupt. Hidden from branch lists and normal fetch | Not visible in GitHub's UI. Needs git on PATH (or a library). You write the merge logic. Needs a `gc` or maintenance step. Custom ref names and refspecs must be configured on each machine. Host support: GitHub works (Dolt) [8], other hosts not verified | git-bug [12], Radicle [15], git-appraise (notes refs) [16], Dolt and current Beads [1][9] |
| d. Git as history and base; blob storage carries the bytes | Keep the R2 sync but make it additive: never infer deletions; use git commits (made by the owner) only as the base for a three-way view | Smallest change. No git credentials in the app. Works offline. Large files already handled | Still two systems. The owner's complaint (commit and pull to see updates) stays unsolved for git. Needs strict rules: only add, never delete from a listing. Compare-and-swap on the bucket is not verified for R2 | The owner's current design; TaskChampion over S3 or GCS buckets [35]; git-annex special remotes [28] for bytes |
| e. Event log is the only synced thing, files rebuilt | Each item's `events.log` is the truth; files in `openspec/` are a projection rebuilt after sync. Transport can be (c) or (d) | Merge is a union of events by id. Deletion is an event. Ids can be hash-based with aliases. Replays are idempotent. Rebuilds are deterministic | Needs event ids, a clock, a snapshot or compaction rule, and a rebuild step. Hand edits of files must be turned into events or be disallowed. Bigger one-time rewrite | git-bug [12], Fossil [18], TaskChampion [31], git-annex logs [29], Linear [36], jj [21] |

Notes on combining: (e) is a data design; (b), (c) and (d) are transports. TaskChampion shows the same event chain over a git branch (b) and over buckets (d) [33][35]. Dolt shows (c) as a store with a CAS push [9]. git-bug and Radicle show (c) with (e) [12][15].

## What I could not confirm (not verified)

- Sciit's storage and status (page 404).
- git-bug: first-hand reports of push conflicts or deletion semantics.
- Radicle: how COB deletion works.
- Beads: sync branch and merge driver details for the exact versions between v0.20 and v0.50 beyond the v0.30.0 docs; the Beads doc site pages for jsonl-sync, merge-conflicts and troubleshooting have since moved (404 or 410) and were read only as search snippets.
- TaskChampion: 2024 and 2025 change notes; date of the 3.1.0 git backend.
- `ls-remote` pattern filtering on the server side (protocol v2 `ref-prefix`).
- GitHub conditional requests for refs.
- LibGit2Sharp's 2025 and 2026 maintenance stance and SSH on Windows.
- Whether R2 supports a conditional write usable as a compare-and-swap.
- Whether gc treats custom ref namespaces as roots (expected yes; no sentence found).
- GitHub LFS 2025 quotas.

## Sources

Dates: "no date shown" means the page had none; all pages were read on 2026-11-26. "Snippet only" means I saw the text in a search result but did not open the page.

1. Beads README (current main), no date shown. https://github.com/steveyegge/beads
2. Beads ARCHITECTURE.md at tag v0.30.0, tag date not verified. https://raw.githubusercontent.com/steveyegge/beads/v0.30.0/docs/ARCHITECTURE.md
3. Beads PROTECTED_BRANCHES.md at tag v0.30.0 (sync branch, worktree, known problems), tag date not verified. https://raw.githubusercontent.com/steveyegge/beads/v0.30.0/docs/PROTECTED_BRANCHES.md
4. Beads release v0.20.1, hash-based IDs, commits dated 2025-10-30 and 2025-10-31. https://newreleases.io/project/github/steveyegge/beads/release/v0.20.1
5. Beads docs on JSONL sync, merge conflicts and troubleshooting (tombstones, deletions manifest, worktree errors, merge driver), snippet only, pages later 404 or 410, no date shown. https://steveyegge.github.io/beads/core-concepts/jsonl-sync ; https://steveyegge.github.io/beads/recovery/merge-conflicts ; https://www.mintlify.com/steveyegge/beads/resources/troubleshooting
6. DoltHub, "Multi-agent persistence", 2026-03-13. https://www.dolthub.com/blog/2026-03-13-multi-agent-persistence/
7. DoltHub, "Restoring Beads classic", 2026-04-02 (also the secondary Korean timeline for v0.50.0 and v0.51.0, snippet only). https://www.dolthub.com/blog/2026-04-02-restoring-beads-classic/
8. DoltHub, "Announcing Git remote support in Dolt", 2026-02-13. https://www.dolthub.com/blog/2026-02-13-announcing-git-remote-support-in-dolt/
9. DoltHub (Dustin Brown), "Supporting git remotes as Dolt remotes", 2026-02-19. https://www.dolthub.com/blog/2026-02-19-supporting-git-remotes-as-dolt-remotes/
10. Steve Yegge, "Gas Town: from clown show to v1.0" (Medium), snippet only, date not verified. https://steve-yegge.medium.com/gas-town-from-clown-show-to-v1-0-c239d9a407ec
11. jlevy/tbd, pull request 1 (design of sync worktree, ids, soft delete), opened and merged 2026-01-09, later notes to 2026-09-19. https://github.com/jlevy/tbd/pull/1
12. git-bug data model (`doc/design/data-model.md`), no date shown. https://github.com/git-bug/git-bug/blob/master/doc/design/data-model.md (also raw: https://raw.githubusercontent.com/MichaelMure/git-bug/master/doc/design/data-model.md)
13. git-bug README, footer 2026, no other date. https://github.com/git-bug/git-bug
14. git-bug releases v0.9.0 (2025-05-12) and v0.10.1 (2025-05-19), via SourceForge mirror and a changelog mirror, snippet only. https://www.sourceforge.net/projects/git-bug.mirror/files/v0.10.1
15. Radicle protocol guide, no date shown (page footer 2018 to 2026). https://radicle.dev/guides/protocol
16. google/git-appraise README, no date shown. https://github.com/google/git-appraise
17. git-notes manual (merge strategies, rewriteRef), current git docs. https://git-scm.com/docs/git-notes
18. Fossil, "Tickets" (append-only ticket changes), no date shown. https://fossil-scm.org/home/doc/trunk/www/tickets.wiki
19. Fossil, "Shunning", no date shown. https://fossil-scm.org/home/doc/trunk/www/shunning.wiki
20. dspinellis/git-issue README, no date shown. https://github.com/dspinellis/git-issue
21. Jujutsu documentation, "Operation log", no date shown. http://docs.jj-vcs.dev/latest/operation-log/
22. Obsidian Git README, no date shown. https://github.com/Vinzent03/obsidian-git
23. Obsidian forum, "Merge conflicts when I pull my vault", 2024-11-08. https://forum.obsidian.md/t/merge-conflicts-when-i-pull-my-vault/91334
24. Obsidian forum, "Obsidian Git complaining about index.lock even when file doesn't exist" (and a blog on conflict markers, snippet only), date not verified. https://forum.obsidian.md/t/bug-obsidian-git-complaining-about-index-lock-even-when-file-index-lock-doesn-t-exist/110021 ; https://renerocksai.github.io/blog/obsidian-gh-merges
25. gitwatch README, no date shown. https://github.com/gitwatch/gitwatch
26. git-annex, assistant design index, no date shown. https://git-annex.branchable.com/design/assistant/
27. git-annex, assistant syncing design, no date shown. https://git-annex.branchable.com/design/assistant/syncing/
28. git-annex-sync manual (synced/ branches), no date shown. https://git-annex.branchable.com/git-annex-sync/
29. git-annex internals (location logs, union-merged lines), no date shown. https://git-annex.branchable.com/internals/
30. git-annex forum, "Duplicate entries in location tracking logs", and git mailing list "Git union issue", snippet only, dates not verified. https://git-annex.branchable.com/forum/Duplicate_entries_in_location_tracking_logs ; https://lore.kernel.org/git/xmqqpn8c4eme.fsf@gitster.c.googlers.com/T/
31. TaskChampion book, "Synchronization model" (operations, rebase), no date shown. https://gothenburgbitfactory.org/taskchampion/sync-model.html
32. TaskChampion book, "Server-replica protocol" (version chain, AddVersion conflict), no date shown. https://gothenburgbitfactory.org/taskchampion/sync-protocol.html
33. taskchampion crate 3.1.0, `src/server/gitsync/mod.rs`, release date not shown. https://docs.rs/crate/taskchampion/3.1.0/source/src/server/gitsync/mod.rs
34. taskchampion crate 3.1.0, `docs/src/snapshots.md`, release date not shown. https://docs.rs/crate/taskchampion/3.1.0/source/docs/src/snapshots.md
35. Taskwarrior, "Syncing Tasks" (cloud backends), snippet only, no date shown. https://taskwarrior.org/docs/sync/
36. Linear (Peter Travers), "Rebuilding delta sync read path", 2026-08-18. https://linear.app/now/rebuilding-delta-sync-read-path
37. Replicache docs, "How it works", no date shown. https://doc.replicache.dev/concepts/how-it-works
38. Zero docs, introduction, no date shown. https://zero.rocicorp.dev/docs/introduction
39. Replicache home page (maintenance mode) and InfoQ on Zero 1.0 (June 2026 from URL), snippet only. https://replicache.dev/ ; https://infoq.com/news/2026/06/zero-version-1
40. Kleppmann, Wiggins, van Hardenberg, McGranaghan, "Local-first software", April 2019. https://www.inkandswitch.com/essay/local-first/
41. Evan Wallace, "How Figma's multiplayer technology works", 2019-10-16. https://www.figma.com/blog/how-figmas-multiplayer-technology-works/
42. Martin Fowler, "Patterns of distributed systems: Hybrid clock", no date shown. https://martinfowler.com/articles/patterns-of-distributed-systems/hybrid-clock.html
43. Kulkarni, Demirbas et al., "Logical physical clocks" (HLC paper), 2014 (year from memory), snippet only. https://www.cse.buffalo.edu//~demirbas/publications/hlc.pdf
44. IETF RFC 9562, UUIDs (UUIDv7), May 2024. https://www.rfc-editor.org/rfc/rfc9562
45. Automerge docs, glossary (change, compaction), no date shown. https://automerge.org/docs/reference/glossary/
46. Yjs forum, "Should size of binary ydoc be monotonically increasing?" and "Undo manager disables GC", snippet only, dates not verified. https://discuss.yjs.dev/t/should-size-of-binary-ydoc-be-monotonically-increasing/2325 ; https://discuss.yjs.dev/t/undo-manager-disables-gc-makes-doc-size-grow-unbounded/3797
47. gitattributes manual (merge attribute, union, custom drivers), current git docs; libgit2 union driver reference v1.8.6 (snippet only). https://git-scm.com/docs/gitattributes ; https://libgit2.org/docs/reference/v1.8.6/sys/merge/GIT_MERGE_DRIVER_UNION.html
48. git-update-ref manual, current git docs. https://git-scm.com/docs/git-update-ref
49. git-push manual (refspecs, non-fast-forward rules, force-with-lease, atomic), current git docs. https://git-scm.com/docs/git-push
50. git-ls-remote manual, current git docs. https://git-scm.com/docs/git-ls-remote
51. git-worktree manual, current git docs. https://git-scm.com/docs/git-worktree
52. git-commit-tree manual, current git docs. https://git-scm.com/docs/git-commit-tree
53. git-gc manual, current git docs. https://git-scm.com/docs/git-gc
54. git-maintenance manual, current git docs. https://git-scm.com/docs/git-maintenance
55. git(1) environment variables (GIT_OPTIONAL_LOCKS, GIT_TERMINAL_PROMPT, GIT_INDEX_FILE), current git docs. https://git-scm.com/docs/git
56. GitHub Docs, "Repository limits", no date shown. https://docs.github.com/en/repositories/creating-and-managing-repositories/repository-limits
57. Amy Palamountain, "Git concurrency in GitHub Desktop", 2015-10-20. https://github.blog/news-insights/git-concurrency-in-github-desktop
58. Git patch series "credential: add new interactive config option", 2024-09-19 (from URL timestamp), snippet only; GCM configuration docs. https://patchwork.kernel.org/project/git/cover/pull.1798.git.1726790423.gitgitgadget@gmail.com/mbox ; https://github.com/GitCredentialManager/git-credential-manager/blob/main/docs/configuration.md
59. Git LFS specification, no date shown. https://github.com/git-lfs/git-lfs/blob/main/docs/spec.md
60. NuGet, LibGit2Sharp 0.32.0, published 2026-07-24; GitHub repository page. https://www.nuget.org/packages/LibGit2Sharp/ ; https://github.com/libgit2/libgit2sharp
61. LibGit2Sharp issue 1471 on worktrees, no date shown, snippet only. https://github.com/libgit2/libgit2sharp/issues/1471
62. git mailing list, ls-remote polling bandwidth (Eric Wong), 2018-12 (from URL), snippet only. https://lore.kernel.ime.usp.br/git/20181229061352.GA5935@dcvr/t/
63. a3nm, "Automatic git conflict resolution on logs and sets", no date shown, snippet only. https://a3nm.net/blog/git_auto_conflicts.html
64. omgmog, "Reducing commit log noise caused by a GitHub Action", no date shown, snippet only. https://blog.omgmog.net/post/reducing-github-action-commit-noise/
65. Replit forum, "Turn off agent and assistant auto-commit", no date shown, snippet only. https://replit.discourse.group/t/turn-off-agent-assistant-auto-commit/2684
66. Antoine Beaupre, "Large files with Git: LFS and git-annex", 2018-12-21, snippet only. https://anarc.at/blog/2018-12-21-large-files-with-git/
67. git-annex forum, "Git LFS lock feature" (mirrored in git-annex 10.20250721 docs), snippet only. https://sources.debian.org/src/git-annex/10.20250721-1/doc/forum/Git_LFS_lock_feature.mdwn
68. git mailing list, Jeff King on `--no-optional-locks` for status pollers, 2021-09 (from URL), snippet only. https://lore.kernel.org/git/YTpsQtJa2rX691c0@coredump.intra.peff.net/T/

Count: 68 numbered entries (some entries group two or three pages). Roughly half carry a date from 2024 or later or are current docs of tools shipping in 2024 to 2026; the rest are older reference works kept because they state the model (marked above), or undated project docs.
