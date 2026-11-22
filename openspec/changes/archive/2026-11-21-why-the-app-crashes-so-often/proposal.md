# Proposal

## Why

The owner reports the app crashes frequently. The run markers log every unclean exit at the next start ("did not exit cleanly: a crash in native code or a kill"), but they cannot tell a crash from a kill (a publish to dist stops the app, test scripts stop hidden apps, a second instance). This item collects the evidence from the app's logs, the Windows event log, WER reports and crash dumps, separates real crashes from kills, and names the causes and fixes.

## What Changes

- A table of every unclean exit (time, pid, crash or kill, evidence).
- The distinct crash causes ranked by frequency, each with its code path and a proposed fix.
- Follow-up items for the fixes once the causes are known.
