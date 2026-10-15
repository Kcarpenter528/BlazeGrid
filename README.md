# BlazeGrid

A virtualized data grid for Blazor WebAssembly, built for the shortest path from **data received → pixels on screen**.

```
src/BlazeGrid/              Razor class library (the package)
samples/BlazeGrid.Demo/     Standalone WASM demo + benchmark harness (100k–500k rows, server mode)
docs/                       API reference + guides (static site, GitHub Pages)
tools/DocGen/               Generates docs/data/api.json from the library
```

## Documentation

The full API reference and guides live in [`docs/`](docs/): a static site with search, one page per type, and guides for
columns, filtering, editing, server mode, state, styling, performance and migrating from Telerik.

- **Preview locally**: `python -m http.server 5180 --directory docs`, then open http://localhost:5180. It reads JSON, so opening `index.html` as a file won't work.
- **After changing the public API** (parameters, methods, XML comments, `#region` names, or `--bz-*` variables in `bzgrid.css`), regenerate the data:

  ```bash
  dotnet run --project tools/DocGen
  ```

  It warns about public members without a `<summary>`. Members marked `[EditorBrowsable(EditorBrowsableState.Never)]` are internal plumbing and are left out.
- **Guides** are Markdown files in `docs/guides/`, listed in `docs/guides/guides.json`.

## Quick start

```html
<!-- index.html -->
<link rel="stylesheet" href="_content/BlazeGrid/bzgrid.css" />
```

```razor
@using BlazeGrid

<BzGrid TItem="Order" Items="orders" Height="600px" StateKey="orders-grid" ShowEditCommands="true"
        OnRowUpdated="SaveRow" OnFilterChanged="FiltersChanged">
    <BzColumn Field="Id" Title="#" Locked="ColumnLock.Left" Editable="false" />
    <BzColumn Field="Customer" Filter="FilterKind.MultiSelect" />
    <BzColumn Field="Status" GroupOrder="@(new[] { "New", "Processing", "Shipped" })">
        <Template><StatusBadge Status="context.Status" /></Template>
    </BzColumn>
    <BzColumn Field="OrderDate" Format="yyyy-MM-dd" />
    <BzColumn Field="Amount" Format="C2" />
    <BzColumn Field="SalesRep" HtmlTemplate="<b>{{SalesRep}}</b> <small>#{{Id}}</small>" />
</BzGrid>
```

Run the demo:

```bash
dotnet run --project samples/BlazeGrid.Demo
```

## Features

| Feature | How |
|---|---|
| Virtualization | Fixed row height; only visible rows (+ overscan) exist in the DOM. Scrolling re-renders only the body component. |
| Custom styling | Everything is a `--bz-*` CSS variable. `Class`, `Style`, `Theme` (`dark`, `compact`), `RowClass`, `CellClass`, `CellClassSelector`, `HeaderClass`. |
| Auto column width | Text is measured once with canvas `measureText` from a 300-row sample. Double-click a resize handle, or use "Fit width" / "Fit all". |
| Loading indicator | `Loading`, `LoadingText`, `LoadingClass`, `LoadingTemplate`. Also shows automatically when a worker query takes >120 ms. |
| Per-column filters | Text (contains / equals / starts / ends / blank…), Number (= ≠ > ≥ < ≤ between), Date (on / before / after / between), Multi-select with counts and search. Chosen from the property type, or override with `Filter="FilterKind.X"`. |
| State persistence | `StateKey` → localStorage: order, width, lock, visibility, sorts, filters, groups, group orders, page size. `GetState` / `SetStateAsync` / `ResetStateAsync`, `OnStateChanged`. |
| Column reorder | HTML5 drag & drop on headers. Dropping into a locked zone locks the column. |
| Column resize | Pointer drag that updates CSS variables live, with zero Blazor renders while dragging. |
| Grouping | Drag headers to the group panel, or use the column menu. Multi-level, expand/collapse, works across virtualization **and** paging (a page that starts mid-group repeats the parent headers marked *continued*). |
| Group value order | `GroupOrder` on a column = fixed order. Users can drag/▲▼ values in the column menu's **Group order** section; that order is persisted and overrides the fixed one. |
| Column locking | Left / right, via `Locked` or the column menu (`position: sticky`, no scroll syncing). |
| Row editing | Double-click (or the ✎ command column). Enter saves, Esc cancels. Typed editors (text / number / date / checkbox / enum select) or `EditTemplate`. Events: `OnRowEditStart` (cancelable), `OnCellValueChanged`, `OnRowEditCommit` (cancel + error message for validation), `OnRowUpdated`, `OnRowEditCancel`. |
| Cell templating | `Template` (any component) or `HtmlTemplate` with `{{Field}}` / `{{Field:format}}` (HTML-encoded). |
| Paging | `PageSize`, `PageSizeOptions`; pager with first/prev/pages/next/last and a page-size select. |
| Server-side data | `ServerMode="true"`: filters, sorts and paging raise `OnQueryChanged(GridQuery)` with Filters/Sorts/Groups/Page/Skip. Bind the returned page to `Items` and the total to `TotalCount`. `FilterOptionsProvider` supplies multi-select values. `OnFilterChanged` fires in both modes. |

## Architecture

```
Items ──► GridEngine ──► EngineResult (view: int[], groupStarts[][]) ──► flat display list ──► GridBody (visible window only)
            │
            ├─ ManagedEngine  (.NET, in-process)          ┐ identical algorithms,
            └─ WorkerEngine   (Web Worker, bzgrid.worker) ┘ verified to produce identical output
```

The design decisions that matter for speed:

1. **No query = no work.** With no filter, sort or group active, the view is the identity, so new data goes straight to rendering ~30 visible rows. The engine isn't touched.
2. **Lazy, columnar extraction.** A column is copied out of your objects only when a query first needs it, as a `double[]` (numbers and dates) or a `string[]` (display text). Getters are open-instance delegates (`Delegate.CreateDelegate`), because `Expression.Compile` is *interpreted* on WASM.
3. **Rank once, sort integers.** Each column gets a dense rank (numeric, or case-insensitive text). A multi-key sort then packs all ranks plus the row index into one 64-bit key (53-bit in JS) and does a single primitive `Array.Sort` / `Float64Array.sort`. Sorts are stable, and ranks are cached until data changes or a cell is edited.
4. **Worker for big data.** `Engine="Auto"` switches to the worker at `WorkerThreshold` (20k rows). Columns are shipped once as raw bytes (a Float64 buffer, or one `\u001F`-joined UTF-8 blob). Results come back as a single Int32 buffer, so no JSON for row data in either direction.
5. **Grouping = boundaries, not trees.** The engine returns the start offsets of each group level in the sorted view. Expand/collapse and paging rebuild a flat `int[]` display list in .NET without re-querying.
6. **Render isolation.** Scrolling only re-renders `GridBody`. Row and cell events are delegated in JS, with one listener for the whole grid. Column widths are a CSS-variable grid template, so resizing never re-renders cells.

### Measured (Debug build, interpreted .NET, 100k rows, 2-level grouping + filter active)

| Operation (click → DOM updated) | Managed | Worker |
|---|---|---|
| Re-sort cached text column | 149 ms | 40 ms |
| Re-sort cached date column | 225 ms | 37 ms |
| First sort on a new text column (includes extraction) | 464 ms | 257 ms |

Release + AOT (`-p:RunAOTCompilation=true`) speeds up the managed engine and all rendering a lot. Benchmark with the demo's stat tiles (`OnRenderStats`).

## Known limits / next steps

- **Deserialization dominates real "receipt → screen" time.** Use System.Text.Json source generation. A future `ItemsJson` mode could hand the raw response bytes to the worker in parallel with .NET deserialization.
- First use of a text column in worker mode spends most of its time building strings in .NET; AOT helps most here.
- Browsers cap element height at ~33M px, i.e. about 1M rows at 34 px. Beyond that, scroll-position scaling is needed.
- Column parameters are read at registration. Changing a `BzColumn` parameter later (e.g. `Title`) is not picked up yet.
- No automated test project yet. Engine parity was verified manually in the demo.
- Trimming: if your app trims user assemblies, preserve the properties of your row type (reflection is used to build accessors).
