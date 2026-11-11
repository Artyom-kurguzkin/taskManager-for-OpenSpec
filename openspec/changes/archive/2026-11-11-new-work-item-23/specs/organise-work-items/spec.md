## ADDED Requirements

### Requirement: Times on the changed period
The changed-period picker SHALL offer an optional from time and to time once a day or range is picked. With times set, only items with an event, note or file write inside the exact window SHALL be shown, and the field and the legend SHALL show the times; without times whole days count. "Show all" SHALL clear dates and times.

#### Scenario: Overnight window
- **WHEN** the user picks 10 Nov to 11 Nov and sets from 20:00 and to 06:00
- **THEN** only items touched between 10 Nov 20:00 and 11 Nov 06:00 are shown and the legend names that window
