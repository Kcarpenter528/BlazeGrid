# Styling & themes

Every color, border, font and spacing in the grid comes from a `--bz-*` CSS variable. Most restyling is a handful of variable overrides; the class names below cover the rest.

## Built-in themes

```razor
<BzGrid … Theme="dark" />
<BzGrid … Theme="compact" />
```

`Theme="x"` adds the class `bz-theme-x` to the grid, so you can define your own themes the same way.

## Your own theme

Give the grid a class and override variables on it:

```razor
<BzGrid … Class="my-grid" RowHeight="46" />
```

```css
/* Load this AFTER _content/BlazeGrid/bzgrid.css */
.my-grid {
    --bz-font: 13px/1.3 system-ui, sans-serif;
    --bz-header-bg: #fff;
    --bz-row-alt: #fff;            /* no zebra stripes */
    --bz-row-hover: #eef5ff;
    --bz-accent: #e11d48;          /* sort arrows, active filters, current page */
    --bz-border: #ececee;
    --bz-radius: 0;
}
```

> **Load order matters.** Your class and `.bz-grid` sit on the same element with equal specificity, so the stylesheet that loads last wins. Either include yours after `bzgrid.css`, or write `.bz-grid.my-grid { … }`.

For a single page, the `Style` parameter works too: `Style="--bz-accent: #16a34a"`.

The full list, with default and dark values, is on the [CSS variables](#/css) page.

## Row height

Rows must all be the same height for virtualization, so set it with `RowHeight` (default 34px), **not** with CSS. The header and footer heights can be changed in CSS; the grid measures the header.

## Conditional formatting

| Hook | Use it for |
|---|---|
| `RowClass="@(item => …)"` on the grid | Whole-row states: cancelled, overdue, highlighted. |
| `CellClass="…"` on a column | A fixed class on every cell of the column. |
| `CellClassSelector="@(item => …)"` on a column | Per-cell states: negative numbers, past-due dates. |
| `HeaderClass="…"` on a column | Styling one header cell. |

```razor
<BzGrid … RowClass="@(t => t.Status == "Cancelled" ? "row-cancelled" : null)">
    <BzColumn Field="DueDate" Format="MM-dd-yyyy"
              CellClassSelector="@(t => t.CompleteDate is null && t.DueDate < DateTime.Today ? "past-due" : null)" />
</BzGrid>
```

```css
.row-cancelled .bz-cell { color: #9ca3af; }
.past-due { background: #fefce8; color: #c2410c; }
```

Cell classes are applied to the cell element, so a background set there covers the row's hover color for that cell.

## Class reference

Targets for styling beyond the variables. Scope them under your grid's class (`.my-grid .bz-hcell { … }`) so other grids are unaffected.

| Class | Element |
|---|---|
| `bz-grid` | The root element (also gets `Class` and `bz-theme-{Theme}`). |
| `bz-group-panel`, `bz-chip`, `bz-group-hint` | The group panel above the header, its chips and its placeholder text. |
| `bz-header`, `bz-hcell` | The header row and a header cell. |
| `bz-htext`, `bz-sort` | Header text and the sort arrow / priority number. |
| `bz-hbtn` (+ `bz-active` when filtered) | The column-menu button in a header cell. |
| `bz-resizer` | The drag handle on a header's right edge. |
| `bz-row` (+ `bz-alt`, `bz-selected`, `bz-editing`) | A data row and its states. |
| `bz-cell` (+ `bz-right`, `bz-center`) | A data cell and its alignment. |
| `bz-lock`, `bz-lock-edge-l`, `bz-lock-edge-r` | Cells of locked columns; the edge classes sit on the last left-locked / first right-locked column. |
| `bz-grow` (+ `bz-collapsed`) | A group header row. Inside: `bz-gtoggle`, `bz-gtitle`, `bz-gkey`, `bz-gcount`, `bz-gcont` (*continued*). |
| `bz-footer` | The footer row (`FooterTemplate`). |
| `bz-pager`, `bz-pbtn` (+ `bz-on`), `bz-pager-info`, `bz-pager-size` | The pager, its buttons, the "1 - 100 of N items" text and the page-size selector. |
| `bz-cmd` with `data-act="edit"`, `"save"` or `"cancel"` | Buttons in the `ShowEditCommands` column. |
| `bz-editor`, `bz-ecell`, `bz-edit-error` | Built-in editors, cells in edit mode, and the validation message bar. |
| `bz-menu` | The column menu (rendered in a fixed overlay). |
| `bz-loading`, `bz-loading-box`, `bz-spinner` | The loading overlay. |
| `bz-empty` | The "No records to display" message. |

### Example: an always-visible header menu

Replace the funnel icon with an always-visible ⋮ and make headers small uppercase monospace:

```css
.my-grid .bz-hcell {
    height: 30px;
    font: 600 10px/1 ui-monospace, monospace;
    letter-spacing: .06em;
    text-transform: uppercase;
}
.my-grid .bz-hbtn { opacity: 1; }
.my-grid .bz-hbtn svg { display: none; }
.my-grid .bz-hbtn::before { content: "⋮"; font: 700 15px/1 sans-serif; }
```
