# Proposal

## Why

#wi179 fixed render cost on the list only, one screen at a time. The owner: "come up with a more general model that would centralise any such issues in one spot and lets us address it once and for all. research how UI is done properly! also read through open source projects".

## What Changes

- Research how UI state and rendering are done properly: Blazor guidance, stores and selectors, signals, MVU. Read how open-source apps do it in code.
- Map this app's state holders, events and render triggers, and measure the cost on every screen.
- Write a plan for one model: a base component, a state store, subscriptions, and rules for events and parameters. Include a screen-by-screen migration and render-budget tests that enforce it.
