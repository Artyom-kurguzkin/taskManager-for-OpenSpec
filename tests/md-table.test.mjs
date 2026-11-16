// Pure table operations of the markdown editor's table helper (TaskManager/wwwroot/js/md-table.js). Run: node --test tests/md-table.test.mjs
// Model rows: 0 is the header, 1.. the body (the separator row is not a row of the model). Documents are plain strings, carets are offsets.
import { test } from "node:test";
import assert from "node:assert/strict";
import { createRequire } from "node:module";
const T = createRequire(import.meta.url)("../TaskManager/wwwroot/js/md-table.js");

const L = (...l) => l.join("\n");
const cells = (line) => line.split("|").slice(1, -1).map(c => c.trim());
const grid = (text) => text.split("\n").map(cells);
const pipes = (line) => [...line].map((c, i) => c === "|" ? i : -1).filter(i => i >= 0).join();
const base = () => T.parse(L("| a | b |", "| --- | --- |", "| 1 | 2 |"));

test("format aligns a ragged table: every column padded to its widest cell, the separator as wide", () => {
  const out = T.format(T.parse(L("| a | bbb |", "|-|-|", "| cccc |")));
  assert.equal(out, L("| a    | bbb |", "| ---- | --- |", "| cccc |     |"));
});

test("format keeps the pipes of every line in the same columns", () => {
  const lines = T.format(T.parse(L("|x|yy|", "|---|---|", "|1|"))).split("\n");
  assert.equal(new Set(lines.map(pipes)).size, 1);
});

test("insert row below puts an empty row after the caret row; below the header it becomes the first body row", () => {
  const below = grid(T.format(T.insertRow(base(), 1, true)));
  assert.equal(below.length, 4);
  assert.deepEqual(below[3], ["", ""]);
  const fromHeader = grid(T.format(T.insertRow(base(), 0, true)));
  assert.deepEqual(fromHeader[2], ["", ""]);
  assert.deepEqual(fromHeader[3], ["1", "2"]);
});

test("insert row above puts an empty row before the caret row; above the header is refused", () => {
  const above = grid(T.format(T.insertRow(base(), 1, false)));
  assert.deepEqual(above[2], ["", ""]);
  assert.deepEqual(above[3], ["1", "2"]);
  assert.equal(T.insertRow(base(), 0, false).rows.length, 2);
});

test("insert column right of the caret column adds an empty column with a header placeholder; left goes before it", () => {
  const right = grid(T.format(T.insertColumn(base(), 0, true)));
  assert.deepEqual(right[0], ["a", "Column 3", "b"]);   // no "Column N" header yet: the column count + 1
  assert.deepEqual(right[2], ["1", "", "2"]);
  const left = T.insertColumn(base(), 0, false);
  assert.equal(left.rows[0][1], "a");
  assert.equal(left.rows[0].length, 3);
});

test("a new column's header is Column N, one more than the highest Column number in the header row; never a copy of its neighbour", () => {
  const t = T.parse(L("| Column 1 | Column 2 | Column 3 | Column 4 |", "| --- | --- | --- | --- |", "| | | | |"));
  assert.equal(T.insertColumn(t, 3, true).rows[0][4], "Column 5");
  assert.equal(T.insertColumn(t, 0, false).rows[0][0], "Column 5");   // left of the first: still the next number
  const twice = T.insertColumn(T.insertColumn(t, 3, true), 4, true);
  assert.deepEqual(twice.rows[0].slice(3), ["Column 4", "Column 5", "Column 6"]);   // the screenshot of #n406: no second "Column 4"
  const gap = T.parse(L("| Column 7 | x |", "| --- | --- |", "| | |"));
  assert.equal(T.insertColumn(gap, 1, true).rows[0][2], "Column 8");
  const named = T.parse(L("| name | qty | Column 2 |", "| --- | --- | --- |", "| | | |"));
  assert.equal(T.insertColumn(named, 0, true).rows[0][1], "Column 3");
  assert.equal(T.insertColumn(base(), 1, true).rows[0][2], "Column 3");   // a, b: the count + 1
});

test("applyOp and apply give every new column a fresh header, several selected columns get consecutive numbers", () => {
  const t = T.parse(L("| Column 1 | Column 2 | Column 3 | Column 4 |", "| --- | --- | --- | --- |", "| | | | |"));
  const one = T.applyOp(t, { r0: 1, c0: 3, r1: 1, c1: 3 }, "insertColumnRight");
  assert.deepEqual(one.table.rows[0], ["Column 1", "Column 2", "Column 3", "Column 4", "Column 5"]);
  const two = T.applyOp(t, { r0: 1, c0: 1, r1: 1, c1: 2 }, "insertColumnLeft");
  assert.deepEqual(two.table.rows[0], ["Column 1", "Column 5", "Column 6", "Column 2", "Column 3", "Column 4"]);
  const doc = T.format(t);
  const r = T.apply(doc, doc.indexOf("Column 4"), "insertColumnRight");
  assert.equal(r.text.split("\n")[0], "| Column 1 | Column 2 | Column 3 | Column 4 | Column 5 |");
});

test("the ribbon has no move buttons: its operations are insert, delete, align, sort and the mode switch", () => {
  const ops = T.ribbon.filter(b => b !== "|").map(b => b[0]);
  assert.deepEqual(ops, ["insertRowAbove", "insertRowBelow", "insertColumnLeft", "insertColumnRight", "deleteRow", "deleteColumn",
    "align:left", "align:center", "align:right", "sortAsc", "sortDesc", "mode"]);
  assert.equal(ops.some(o => o.startsWith("move")), false);
  assert.equal(T.ribbon.some(b => b !== "|" && /[↑↓←→]/.test(b[1] ?? "")), false);
});

test("the moves stay as operations for the grid's Alt+arrow keys", () => {
  assert.deepEqual(T.altMove("ArrowUp"), "moveRowUp");
  assert.deepEqual(T.altMove("ArrowDown"), "moveRowDown");
  assert.deepEqual(T.altMove("ArrowLeft"), "moveColumnLeft");
  assert.deepEqual(T.altMove("ArrowRight"), "moveColumnRight");
  assert.equal(T.altMove("a"), null);
});

test("delete row removes the caret row; the header cannot be deleted", () => {
  const t = T.parse(L("| a | b |", "| --- | --- |", "| 1 | 2 |", "| 3 | 4 |"));
  assert.deepEqual(grid(T.format(T.deleteRow(t, 1))).map(r => r[0]), ["a", "---", "3"]);
  assert.equal(T.deleteRow(t, 0).rows.length, 3);
});

test("delete column removes the caret column; the last column stays", () => {
  const out = grid(T.format(T.deleteColumn(base(), 0)));
  assert.deepEqual(out[0], ["b"]);
  assert.deepEqual(out[2], ["2"]);
  const one = T.parse(L("| a |", "| --- |", "| 1 |"));
  assert.equal(T.deleteColumn(one, 0).rows[0].length, 1);
});

test("alignment markers: left, centre and right put : on the separator of the caret column, none clears it", () => {
  const sep = (c, al) => cells(T.format(T.setAlign(base(), c, al)).split("\n")[1]);
  assert.deepEqual(sep(0, "left"), [":---", "---"]);
  assert.deepEqual(sep(0, "center"), [":---:", "---"]);
  assert.deepEqual(sep(1, "right"), ["---", "---:"]);
  assert.deepEqual(sep(0, "none"), ["---", "---"]);
  // a marker survives a parse and a format
  const again = T.format(T.parse(T.format(T.setAlign(base(), 0, "center")))).split("\n")[1];
  assert.deepEqual(cells(again)[0], ":---:");
});

test("right aligned text pads on the left, centred text on both sides", () => {
  const t = T.setAlign(T.setAlign(T.parse(L("| aaaa | bbbb |", "| --- | --- |", "| 1 | 2 |")), 0, "right"), 1, "center");
  assert.equal(T.format(t).split("\n")[2], "|    1 |   2   |");   // a centred column is at least 5 wide: ":---:"
});

test("insertAt swaps /table for a table with its first header cell selected, and leaves a blank line after text above", () => {
  const doc = "intro\n/table";
  const r = T.insertAt(doc, T.slashAt(doc, doc.length), 2, 2);
  assert.equal(r.text, L("intro", "", "| Column 1 | Column 2 |", "| -------- | -------- |", "|          |          |", "|          |          |"));
  assert.equal(r.text.slice(r.start, r.end), "Column 1");
  const alone = T.insertAt("/table", { start: 0, end: 6 }, 1, 1);
  assert.equal(alone.text.split("\n").length, 3);
  assert.equal(alone.text.slice(alone.start, alone.end), "Column 1");
});

test("Tab from the last cell adds a row and lands in its first cell; Tab inside moves on and skips the separator row", () => {
  const doc = T.format(base());
  const last = T.nextCell(doc, doc.lastIndexOf("2"));
  assert.equal(last.text.split("\n").length, 4);
  assert.equal(last.start, last.text.split("\n").slice(0, 3).join("\n").length + 3);   // "\n" + "| "
  assert.equal(last.end, last.start);   // an empty cell: a caret
  const a = T.nextCell(doc, doc.indexOf("a"));
  assert.equal(a.text, doc);
  assert.equal(doc.slice(a.start, a.end), "b");
  const h = T.nextCell(doc, doc.indexOf("b"));
  assert.equal(doc.slice(h.start, h.end), "1");
});

test("Shift+Tab moves to the previous cell and stays in the first one", () => {
  const doc = T.format(base());
  const p = T.prevCell(doc, doc.indexOf("b"));
  assert.equal(doc.slice(p.start, p.end), "a");
  const first = T.prevCell(doc, doc.indexOf("a"));
  assert.equal(doc.slice(first.start, first.end), "a");
  const fromBody = T.prevCell(doc, doc.indexOf("1"));
  assert.equal(doc.slice(fromBody.start, fromBody.end), "b");   // back over the separator
});

test("Tab outside a table does nothing (null)", () => {
  assert.equal(T.nextCell("plain text", 3), null);
});

test("Enter at the end of a row adds a row below and lands in its first cell; mid-cell it is left alone", () => {
  const doc = T.format(base());
  const e = T.enter(doc, doc.length);
  assert.equal(e.text.split("\n").length, 4);
  assert.equal(T.enter(doc, doc.indexOf("1")), null);
});

test("find ignores lines inside code fences and reports the table around the caret with its row and column", () => {
  const doc = L("text", "```", "| a | b |", "| - | - |", "```", "", "| x | y |", "| --- | --- |", "| 1 | 2 |", "after");
  assert.equal(T.find(doc, doc.indexOf("| a")), null);
  const f = T.find(doc, doc.indexOf("2"));
  assert.equal(f.startLine, 6);
  assert.equal(f.endLine, 8);
  assert.equal(f.row, 1);
  assert.equal(f.col, 1);
  assert.equal(doc.slice(f.start, f.end), L("| x | y |", "| --- | --- |", "| 1 | 2 |"));
  assert.equal(T.find(doc, doc.indexOf("after")), null);
});

test("a run of pipe lines without a separator row is not a table", () => {
  assert.equal(T.find(L("| a | b |", "| 1 | 2 |"), 2), null);
});

test("escaped pipes and pipes in code spans stay inside their cell", () => {
  const t = T.parse(L("| a \\| b | `c|d` |", "| --- | --- |", "| 1 | 2 |"));
  assert.equal(t.rows[0].length, 2);
  assert.equal(t.rows[0][0], "a \\| b");
});

test("apply runs an operation on the table at the caret and returns the whole text with the caret in the cell it worked on", () => {
  const doc = L("intro", "", "| a | b |", "| --- | --- |", "| 1 | 2 |", "", "outro");
  const r = T.apply(doc, doc.indexOf("2"), "insertRowBelow");
  assert.equal(r.text, L("intro", "", "| a   | b   |", "| --- | --- |", "| 1   | 2   |", "|     |     |", "", "outro"));
  assert.equal(r.start, r.end);
  assert.equal(r.text.slice(0, r.start).split("\n").length, 6);   // the caret is on the new line
  assert.equal(T.apply("no table", 2, "deleteRow"), null);
});

test("create makes a header, a separator and the chosen empty body rows, aligned", () => {
  const out = T.create(3, 4).split("\n");
  assert.equal(out.length, 2 + 3);
  assert.equal(out[0], "| Column 1 | Column 2 | Column 3 | Column 4 |");
  assert.equal(out[1], "| -------- | -------- | -------- | -------- |");
  assert.equal(new Set(out.map(pipes)).size, 1);
});

test("slashAt finds /table alone on its line, or after indentation only", () => {
  assert.deepEqual(T.slashAt("hello\n/table", 12), { start: 6, end: 12 });
  assert.deepEqual(T.slashAt("  /table", 8), { start: 2, end: 8 });
  assert.equal(T.slashAt("a /table", 8), null);
  assert.equal(T.slashAt("/tabl", 5), null);
});

// ── the grid overlay: operations on a selection (rows r0..r1, columns c0..c1 of the model) ──

const sample = () => T.parse(L("| name | qty |", "| --- | --- |", "| pear | 10 |", "| apple | 9 |", "| fig | 100 |"));
const body = (t, c = 0) => t.rows.slice(1).map(r => r[c]);

test("move rows down and up swaps a block of body rows with its neighbour and the selection follows", () => {
  const down = T.applyOp(sample(), { r0: 1, c0: 0, r1: 1, c1: 1 }, "moveRowDown");
  assert.deepEqual(body(down.table), ["apple", "pear", "fig"]);
  assert.deepEqual([down.sel.r0, down.sel.r1], [2, 2]);
  const up = T.applyOp(sample(), { r0: 2, c0: 0, r1: 3, c1: 1 }, "moveRowUp");
  assert.deepEqual(body(up.table), ["apple", "fig", "pear"]);
  assert.deepEqual([up.sel.r0, up.sel.r1], [1, 2]);
});

test("move rows never moves the header, never moves a row above the first body row or below the last", () => {
  assert.equal(T.applyOp(sample(), { r0: 0, c0: 0, r1: 0, c1: 1 }, "moveRowDown"), null);
  assert.equal(T.applyOp(sample(), { r0: 1, c0: 0, r1: 1, c1: 1 }, "moveRowUp"), null);
  assert.equal(T.applyOp(sample(), { r0: 3, c0: 0, r1: 3, c1: 1 }, "moveRowDown"), null);
});

test("move column right and left carries the header, the cells and the alignment with it", () => {
  const t = T.setAlign(sample(), 1, "right");
  const r = T.applyOp(t, { r0: 0, c0: 1, r1: 4, c1: 1 }, "moveColumnLeft");
  assert.deepEqual(r.table.rows[0], ["qty", "name"]);
  assert.deepEqual(r.table.align, ["right", ""]);
  assert.deepEqual([r.sel.c0, r.sel.c1], [0, 0]);
  assert.equal(T.applyOp(t, { r0: 0, c0: 1, r1: 4, c1: 1 }, "moveColumnRight"), null);
  const back = T.applyOp(r.table, { r0: 0, c0: 0, r1: 4, c1: 0 }, "moveColumnRight");
  assert.deepEqual(back.table.rows[0], ["name", "qty"]);
});

test("sort ascending and descending reorders the body only: the header and the separator row stay on top", () => {
  const asc = T.applyOp(sample(), { r0: 1, c0: 0, r1: 1, c1: 0 }, "sortAsc").table;
  assert.deepEqual(asc.rows[0], ["name", "qty"]);
  assert.deepEqual(body(asc), ["apple", "fig", "pear"]);
  assert.deepEqual(body(asc, 1), ["9", "100", "10"]);   // rows move whole
  const out = T.format(asc).split("\n");
  assert.match(out[1], /^\| -+ \| -+ \|$/);
  const desc = T.applyOp(sample(), { r0: 0, c0: 0, r1: 0, c1: 0 }, "sortDesc").table;
  assert.deepEqual(body(desc), ["pear", "fig", "apple"]);
});

test("sort compares numbers as numbers, keeps empty cells last and equal cells in their order", () => {
  const t = T.parse(L("| n | tag |", "| - | - |", "| 10 | a |", "|  | b |", "| 9 | c |", "| 10 | d |"));
  const asc = T.applyOp(t, { r0: 1, c0: 0, r1: 1, c1: 0 }, "sortAsc").table;
  assert.deepEqual(body(asc, 1), ["c", "a", "d", "b"]);
  const desc = T.applyOp(t, { r0: 1, c0: 0, r1: 1, c1: 0 }, "sortDesc").table;
  assert.deepEqual(body(desc, 1), ["a", "d", "c", "b"]);
});

test("clear empties the cells of the selected range and nothing else", () => {
  const r = T.applyOp(sample(), { r0: 1, c0: 0, r1: 2, c1: 0 }, "clear");
  assert.deepEqual(body(r.table), ["", "", "fig"]);
  assert.deepEqual(body(r.table, 1), ["10", "9", "100"]);
  assert.deepEqual(r.table.rows[0], ["name", "qty"]);
});

test("paste of a tab-separated block fills cells from the selection and grows the table in both directions", () => {
  const r = T.pasteRange(sample(), 3, 1, "x\ty\tz\r\n1\t2\t3\r\n");
  assert.equal(r.table.rows.length, 5);   // header + 3 body rows + 1 new
  assert.equal(r.table.rows[0].length, 4);
  assert.deepEqual(r.table.rows[3], ["fig", "x", "y", "z"]);
  assert.deepEqual(r.table.rows[4], ["", "1", "2", "3"]);
  assert.equal(r.table.rows[0][2], "Column 3");   // new columns get header placeholders
  assert.deepEqual([r.sel.r0, r.sel.c0, r.sel.r1, r.sel.c1], [3, 1, 4, 3]);
  const taller = T.pasteRange(sample(), 3, 0, "1\n2\n3");
  assert.equal(taller.table.rows.length, 6);
  assert.deepEqual(taller.table.rows[5], ["3", ""]);
});

test("paste of a 2x2 block lands where the selection starts and a pipe in pasted text is escaped", () => {
  const r = T.pasteRange(sample(), 1, 0, "a|b\tc\nd\te");
  assert.deepEqual(r.table.rows[1], ["a\\|b", "c"]);
  assert.deepEqual(r.table.rows[2], ["d", "e"]);
  assert.deepEqual([r.sel.r0, r.sel.c0, r.sel.r1, r.sel.c1], [1, 0, 2, 1]);
  assert.equal(T.parse(T.format(r.table)).rows[1][0], "a\\|b");
});

test("toTsv gives the selected rectangle as tab-separated lines, pipes unescaped", () => {
  const t = T.setCell(sample(), 1, 0, "p|q");
  assert.equal(T.toTsv(t, { r0: 0, c0: 0, r1: 1, c1: 1 }), "name\tqty\np|q\t10");
});

test("setCell trims, flattens line breaks and escapes pipes", () => {
  const t = T.setCell(sample(), 2, 1, "  a\nb | c ");
  assert.equal(t.rows[2][1], "a b \\| c");
  assert.equal(sample().rows[2][1], "9");   // the original is untouched
});

test("multi-row operations: insert as many rows as selected, delete a block of rows or columns, align several columns", () => {
  const ins = T.applyOp(sample(), { r0: 2, c0: 0, r1: 3, c1: 0 }, "insertRowAbove");
  assert.equal(ins.table.rows.length, 6);
  assert.deepEqual([ins.sel.r0, ins.sel.r1], [2, 3]);
  assert.deepEqual(ins.table.rows[4], ["apple", "9"]);
  const del = T.applyOp(sample(), { r0: 1, c0: 0, r1: 2, c1: 1 }, "deleteRow");
  assert.deepEqual(body(del.table), ["fig"]);
  assert.equal(T.applyOp(sample(), { r0: 0, c0: 0, r1: 0, c1: 1 }, "deleteRow"), null);   // the header stays
  assert.equal(T.applyOp(sample(), { r0: 0, c0: 0, r1: 4, c1: 1 }, "deleteColumn"), null);   // the last columns stay
  const al = T.applyOp(sample(), { r0: 0, c0: 0, r1: 4, c1: 1 }, "align", "center");
  assert.deepEqual(al.table.align, ["center", "center"]);
  const col = T.applyOp(sample(), { r0: 1, c0: 1, r1: 1, c1: 1 }, "insertColumnRight");
  assert.deepEqual(col.table.rows[0], ["name", "qty", "Column 3"]);
  assert.deepEqual([col.sel.c0, col.sel.c1], [2, 2]);
});

test("apply runs the new operations on the table at the caret too (text mode of the ribbon)", () => {
  const doc = T.format(sample());
  const r = T.apply(doc, doc.indexOf("pear"), "moveRowDown");
  assert.deepEqual(r.text.split("\n").slice(2).map(l => l.split("|")[1].trim()), ["apple", "pear", "fig"]);
  const s = T.apply(doc, doc.indexOf("name"), "sortDesc");
  assert.deepEqual(s.text.split("\n").slice(2).map(l => l.split("|")[1].trim()), ["pear", "fig", "apple"]);
  assert.equal(T.apply(doc, doc.indexOf("pear"), "moveRowUp"), null);
});

test("cellOffset is where a cell's text is in the document, for a table that is not formatted", () => {
  const doc = L("x", "|a|bb|", "|-|-|", "| 1 |  22 |");
  const f = T.find(doc, doc.indexOf("bb"));
  const h = T.cellOffset(doc, f, 0, 1), c = T.cellOffset(doc, f, 1, 1);
  assert.equal(doc.slice(h.start, h.end), "bb");
  assert.equal(doc.slice(c.start, c.end), "22");
});

// ── leaving the grid and moving in it ──

test("navigate: arrows move one cell and stay inside the table sideways", () => {
  assert.deepEqual(T.navigate("ArrowDown", { r: 1, c: 1 }, 4, 3), { r: 2, c: 1 });
  assert.deepEqual(T.navigate("ArrowUp", { r: 2, c: 1 }, 4, 3), { r: 1, c: 1 });
  assert.deepEqual(T.navigate("ArrowLeft", { r: 2, c: 0 }, 4, 3), { r: 2, c: 0 });
  assert.deepEqual(T.navigate("ArrowRight", { r: 2, c: 2 }, 4, 3), { r: 2, c: 2 });
});

test("navigate: ArrowDown on the last row leaves below the table, ArrowUp on the first row leaves above it", () => {
  assert.deepEqual(T.navigate("ArrowDown", { r: 3, c: 1 }, 4, 3), { leave: "after" });
  assert.deepEqual(T.navigate("ArrowUp", { r: 0, c: 1 }, 4, 3), { leave: "before" });
  assert.deepEqual(T.navigate("ArrowDown", { r: 0, c: 0 }, 1, 1), { leave: "after" });   // a header-only table
});

test("navigate: Home and End go to the first and last cell of the row, with Ctrl of the table", () => {
  assert.deepEqual(T.navigate("Home", { r: 2, c: 2 }, 4, 3), { r: 2, c: 0 });
  assert.deepEqual(T.navigate("End", { r: 2, c: 0 }, 4, 3), { r: 2, c: 2 });
  assert.deepEqual(T.navigate("Home", { r: 2, c: 2 }, 4, 3, true), { r: 0, c: 0 });
  assert.deepEqual(T.navigate("End", { r: 1, c: 0 }, 4, 3, true), { r: 3, c: 2 });
  assert.equal(T.navigate("a", { r: 1, c: 0 }, 4, 3), null);
});

test("leave below puts the caret on the line after the table; at the end of the text an empty line is made", () => {
  const doc = L("intro", "", "| a | b |", "| - | - |", "| 1 | 2 |", "after", "more");
  const r = T.leave(doc, T.find(doc, doc.indexOf("1")), "after");
  assert.equal(r.replacement, "");
  assert.equal(r.text, doc);
  assert.equal(doc.slice(r.start), L("after", "more"));
  const end = L("| a |", "| - |", "| 1 |");
  const e = T.leave(end, T.find(end, end.indexOf("1")), "after");
  assert.equal(e.text, end + "\n");
  assert.equal(e.start, e.text.length);
  assert.deepEqual([e.rangeStart, e.rangeEnd, e.replacement], [end.length, end.length, "\n"]);
  const nl = end + "\n";   // a newline already ends the text: the caret goes on that empty last line, nothing is added
  const n = T.leave(nl, T.find(nl, nl.indexOf("1")), "after");
  assert.equal(n.text, nl);
  assert.equal(n.start, nl.length);
});

test("leave above puts the caret at the end of the line before the table; at the start of the text an empty line is made", () => {
  const doc = L("intro", "", "| a | b |", "| - | - |", "| 1 | 2 |");
  const r = T.leave(doc, T.find(doc, doc.indexOf("a")), "before");
  assert.equal(r.text, doc);
  assert.equal(r.start, doc.indexOf("| a") - 1);
  assert.equal(doc.slice(0, r.start), "intro\n");
  const top = L("| a |", "| - |", "| 1 |");
  const t = T.leave(top, T.find(top, 2), "before");
  assert.equal(t.text, "\n" + top);
  assert.equal(t.start, 0);
  assert.deepEqual([t.rangeStart, t.rangeEnd, t.replacement], [0, 0, "\n"]);
  const text = L("intro", "| a |", "| - |", "| 1 |");   // text directly above: the caret ends that line
  const x = T.leave(text, T.find(text, 8), "before");
  assert.equal(text.slice(0, x.start), "intro");
});
