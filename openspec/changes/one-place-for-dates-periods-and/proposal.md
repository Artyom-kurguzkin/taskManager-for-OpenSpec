# Proposal

## Why

About 20 date format strings are written inline across pages and models, DateRange reimplements Ui.Period with a different answer across years, and "today" comes from four clocks (TimeProvider, DateTime.Today, DateTime.Now, UtcNow). Colours are computed in four places (Ui, Pill, BoardModel, AnalyticsModel).

## What Changes

- Ui.cs owns every day, stamp, period and time format and every colour map; pages and view models call it.
- "Today" comes from the injected TimeProvider everywhere in the host.
- DateRange uses Ui.Period.
