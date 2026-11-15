# Proposal

## Why

One full fast-suite run took 6 m 10 s (934 tests, nearly all in series because the modules keep their state in statics and 25 test classes sat in one non-parallel collection). The owner: a suite that takes longer than 5 minutes is useless and needs rework. Tests must stay deterministic (no sleeps, polls or real timers).

## What Changes

- Each module's mutable statics live in a state object behind an `AsyncLocal` seam (`Isolated`); the app uses the one shared state, so behaviour is unchanged. A custom xunit test framework (`IsolatedModuleState.cs`) gives every test fresh state, so test classes run in parallel.
- Only tests that need real time (watchers, locks, measured durations) stay serial, in `RealTimeCollection`.
- The 50 ms reload timer in RepoPage and the 250/600 ms preview and autosave delays in NotePage take a `TimeProvider`; their tests use `FakeTimeProvider` instead of `Task.Delay`.
- Fixes found by the parallel run: `DataFile.Delete` no longer retries for 420 ms when the credentials folder does not exist; YAML quoting is thread-safe; `WriteText` retries the temp-file move like BoardSync does.
- Result: one run 1 m 57 s, 934 passed.
