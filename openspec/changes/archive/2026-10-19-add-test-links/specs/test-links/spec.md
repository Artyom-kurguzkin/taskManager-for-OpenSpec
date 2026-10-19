# Spec Delta

## Purpose

Links test suites and individual tests to work items and shows their most recent result, so verification status is visible from the work item page.

## ADDED Requirements

### Requirement: Link tests to a work item
The Linked tests panel SHALL offer "+ link test", which lists the tests found in the newest results file (suite and test name, sorted) to pick from, narrowed by typing. A linked test SHALL be removable, and SHALL offer "+ note", which creates a note describing the test and attaches it; clicking a test with a note SHALL open that note. Links SHALL be stored in `x-rtm.tests` in the change's `.openspec.yaml` as suite, test and optional note id.

#### Scenario: Link with note
- **WHEN** the user picks "suite 543 / ping test" from "+ link test" on #wi123, then clicks its "+ note"
- **THEN** `x-rtm.tests` holds that entry with the new note's id, and clicking "ping test" opens the note

#### Scenario: Nothing to pick
- **WHEN** no results file is found
- **THEN** "+ link test" says where it looked (the configured globs) instead of listing tests

### Requirement: Show last results
For each linked suite the panel SHALL show the latest run time and an overall result (all passed, or N failed). For each test it SHALL show passed, failed, skipped or no result. Results SHALL be read from the newest JUnit XML or TRX file matching the configured globs.

#### Scenario: All passed
- **WHEN** the newest results file has suite 543 with ping test and page load test both passed at 2026-10-17 14:02
- **THEN** the panel shows "Test suite 543 · last ran 2026-10-17 14:02 · all passed" and a pass mark on each test

#### Scenario: No results file
- **WHEN** no file matches the configured globs
- **THEN** the panel shows "No results file found at <glob>" and each test shows "no result"

### Requirement: Board signal
An expanded card SHALL show a failing-tests marker when any linked test failed in its latest result.

#### Scenario: Failing marker
- **WHEN** #wi123's page load test failed in the latest run
- **THEN** its card on the board shows the failing-tests marker
