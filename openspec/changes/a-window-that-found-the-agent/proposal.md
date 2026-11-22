# Proposal

## Why

Two copies of the app started two seconds apart (09:04:03 and 09:04:06 on 20 Nov 2026). The second bound 127.0.0.1:7431 and served agents; the first logged "could not listen on 127.0.0.1:7431: address already in use" and showed that another window serves agents. The serving copy then exited and removed server.json; the surviving copy kept running with no endpoint and never retried, so the board tools answered "The Repo Tasks app is not running" while the app was open, until the user restarted it.

## What Changes

- A window that could not bind the agent port retries on an interval (and when server.json disappears) and takes the port over once it is free, writing server.json and the token as the first window would.
- Settings shows the state honestly: "another window serves agents" only while that window is alive; "serving agents on 7431" once this window took over.
- A test: endpoint A holds the port, B starts and reports "served by another window"; A disposes; B is serving within the retry interval.
