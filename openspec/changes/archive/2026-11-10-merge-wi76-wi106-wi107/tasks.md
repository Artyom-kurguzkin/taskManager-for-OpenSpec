# Tasks

## 1. #wi76 Calendar view, due dates and time periods · Dates · merged 2026-11-10

- [x] 1.1 `x-rtm.started` / `x-rtm.due` (yyyy-MM-dd) in the model; `SetStatus` to In Progress stamps `started` once; `SetStarted` / `SetDue`
- [x] 1.2 Work item page: native date inputs for started and due
- [x] 1.3 Cards: due tag, red when overdue; list: Started and Due columns

## 2. #wi76 Calendar view, due dates and time periods · Periods · merged 2026-11-10

- [x] 2.1 `BoardModel.Period` / `StartPeriod` / `InPeriod`; group by due date and start date; the period filter in the side menu

## 3. #wi76 Calendar view, due dates and time periods · Calendar · merged 2026-11-10

- [x] 3.1 `CalendarModel`: weeks of a month, bars clipped per week, first-fit lanes, unscheduled open items
- [x] 3.2 Calendar view in the Layout menu: month grid, bars, No dates list, ‹ › Today, bar opens the item
- [x] 3.3 Agent guide v2 mentions the keys
- [x] 3.4 Verify on the hidden desktop, light and dark
- [x] 3.5 I should be able to filter out projects i dont want to see at the mement — an eye next to each project in the project switcher hides or shows it without switching (same setting as "Hide project"); no second project list in the view options

## 4. #wi106 transition date · merged 2026-11-10

- [x] 4.1 `x-rtm.moved` stamped on creation and on every status change; absent reads as the creation date
- [x] 4.2 Item page "in <status> since" and list column "In status since"; checked in the real app, light and dark

## 5. calendar patches

- [x] 5.1 Roadmap component and model: window, zoom, bars clipped at the edges, group spans
- [x] 5.2 Calendar view renders the roadmap in the board's groups; checked in the real app, light and dark
