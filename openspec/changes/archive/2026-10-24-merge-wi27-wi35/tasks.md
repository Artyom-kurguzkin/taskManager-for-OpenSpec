# Tasks

## 1. Reorder cards within a column · Reorder cards

- [x] 1.1 Place a dragged card at the target card's position and add the "my order" sort
- [x] 1.2 Send a card dropped on a column's empty space to the end when sorting by my order
- [x] 1.3 Verify dragging within a column in the real app

## 2. Reorder cards within a column · Drop preview

- [x] 2.1 Insert at the gap (in front of the next card or column, or at the end) instead of taking the target's place
- [x] 2.2 Move every selected card when a selected card is dragged
- [x] 2.3 Open a sliding gap at the insertion point while dragging cards and column headers; dim what is dragged
- [x] 2.4 Click a card to open it; Shift-click (or Ctrl/⌘-click) to add or remove from the selection; remove the card tick boxes
- [x] 2.5 Verify click selection, the gap, card drops, multi-card drops and column drops in the real app
- [x] 2.6 Holding Shift shows the select-mode bar and marks cards as pickable before any click

## 3. Reorder cards within a column · Arrow keys and smooth drag

- [x] 3.1 Up/down step the selection inside its column, gathering a scattered selection first; left/right move it to the neighbouring column
- [x] 3.2 Drag with pointer events: the real card follows, neighbours slide with transforms, Escape cancels; FLIP-animate the board after drops and arrow moves
- [x] 3.3 Verify arrow moves and the drag animation in the real app

## 4. Reorder cards within a column · Reusing an archived folder name

- [x] 4.1 A file read earlier but since moved or deleted no longer blocks a write there (new "New work item" after archiving one)

## 5. Reorder cards within a column · Archive feedback

- [x] 5.1 Run the OpenSpec CLI from npx's cached copy with node when present (saves ~0.85 s), else npx
- [x] 5.2 Show a busy panel with a spinner while archiving, from the board and the item page
- [x] 5.3 Verify in the real app: panel appears immediately, archive completes
