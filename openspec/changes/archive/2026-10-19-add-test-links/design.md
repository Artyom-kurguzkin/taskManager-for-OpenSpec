# Design

## Context

See proposal.md. The mockup shows "Test Suite 543 · last ran (date/time) · all passed" with tests "ping test" and "page load test", each opening a note.

## Goals / Non-Goals

**Goals:**
- Read results the repo's existing test runners already produce.

**Non-Goals:**
- Running tests from the app.
- A test results server or CI API integration.

## Decisions

- **Results come from files, not a service.** The app reads JUnit XML (Playwright, Jest, pytest and most CI tools) and TRX (`dotnet test`) from paths listed in `openspec/config.yaml` under `x-rtm.testResults` (glob patterns). Both formats carry suite name, test name, outcome and timestamp.
- **Globs stay simple:** a fixed folder plus a file pattern (`TaskManager.Tests/TestResults/*.trx`), with `**` meaning "and every subfolder". No globbing library.
- **TRX suite = test class name, test = display name** (what `dotnet test --logger trx` writes); JUnit suite = `testsuite@name`, test = `testcase@name`.
- **Links live in `x-rtm.tests`** in the change's `.openspec.yaml`: a list of `{ suite, test, note }`. Matching is by suite and test name, so renamed tests show as "no result".

## Risks / Trade-offs

- [Result files are often git-ignored and only exist on the machine that ran the tests] → The panel shows "No results file found at <glob>" instead of a stale or false pass.
