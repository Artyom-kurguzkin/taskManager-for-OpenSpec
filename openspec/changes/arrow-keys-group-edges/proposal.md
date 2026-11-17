# Proposal

## Why

The exploratory pass built for #wi195 (tests/exploratory.ps1 on the used-board preset) found 13 failing steps out of 52 on the current code, all arrow-key moves at the edge of a group. #wi194 fixed the middle-row swap under a header sort; the edge cases still go wrong because BoardModel.EdgeMove decides "at the edge" from the board's internal order while Step works on the shown order, and because dropping the header sort re-sorts without saving the shown order as the manual one. Evidence: tests/out/exploratory-summary.txt and tests/out/shots/exploratory-*.png; notes #n640 and #n651 on #wi195.

## What Changes

- Header sort on, Up on the first shown row or Down on the last: the sort is dropped and the whole list or group falls back to the board's order instead of nothing moving (flat list `61 55 103 ...` became `126 154 150 195 ...`).
- Header sort on, a row first in the board's order but shown last in its group: Up sends it into the group above instead of swapping with the row shown above it (EdgeMove uses the internal order).
- Header sort off, first row Up into the group above: rows named in the saved stale `Order` jump to the front of their own groups and the pressed group loses more than the moved row.
- After an arrow key drops the header sort, clicking the Updated header no longer sorts (`aria-sort` stays unset) until the app restarts.
- Fix: EdgeMove and Step share one shown-order source; dropping the sort first seeds the manual order with every group's shown order; the grid's sort state is reset in a way Radzen picks up. tests/exploratory.ps1 must pass 52 of 52.
