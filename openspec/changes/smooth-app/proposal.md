# Proposal

## Why

Opening a work item takes close to a second, ticking a subtask shows after 600-800 ms, and switching to the Worklog view redraws five times before it settles. The app runs locally on 50 items; the drawing takes a few milliseconds, the rest is re-reading every file after every change and asking git with a new process for every question.

## What Changes

- Work items and notes are cached per folder: a change re-parses only the folder it touched.
- A write no longer re-reads the whole board first, and the duplicate-id scan runs only when a folder appears or goes.
- Git history is read once per HEAD and answered from memory.
- Pages load their data once per navigation and once per change, with nothing expensive during drawing.
- A probe script measures the real app so a regression shows.
