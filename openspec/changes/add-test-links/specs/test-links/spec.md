# Spec Delta

## Purpose

Links test suites and individual tests to work items and shows their most recent result, so verification status is visible from the work item page.

## ADDED Requirements

### Requirement: Link tests to a work item
The user SHALL be able to link a test to a work item by suite name and test name, and optionally attach a note that describes the test. Links SHALL be stored in `x-rtm.tests` in the change's `.openspec.yaml`.

#### Scenario: Link with note
- **WHEN** the user links "suite 543 / ping test" to #wi123 with note #n123
- **THEN** `x-rtm.tests` holds that entry, and the Linked tests panel lists "ping test", which opens #n123 when clicked

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
