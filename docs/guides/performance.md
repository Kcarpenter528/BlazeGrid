# Performance

## What the grid does for you

- **Virtualization.** Only the visible rows (plus `Overscan`) are in the DOM. Scrolling re-renders the body component only.
- **No query, no work.** With no filter, sort or group active, new `Items` go straight to rendering. The query engine isn't touched.
- **Columnar, lazy extraction.** A column's values are copied out of your objects only when a query first needs them, then cached until the data changes or a cell is edited.
- **Rank once, sort integers.** Multi-column sorts pack per-column ranks into one numeric key and do a single primitive sort.
- **Web Worker for big data.** From `WorkerThreshold` rows (20,000 by default) filtering, sorting and grouping run off the UI thread. Columns are shipped as raw bytes, not JSON.
- **CSS-only resizing.** Column widths are CSS variables; dragging a column edge never re-renders cells.

Use [`OnRenderStats`](#/api/BzGrid/OnRenderStats) to measure *data received → painted* in your own pages.

## Where the time actually goes

In a real application the grid is rarely the slow part. Rendering a page of rows typically takes a fraction of a second. On WebAssembly without AOT, .NET runs through an interpreter, and turning a large JSON response into objects can take several seconds. That happens before the grid sees any data, whatever grid you use.

Measure your own page before optimizing: time the HTTP call, the deserialization and [`OnRenderStats`](#/api/BzGrid/OnRenderStats) separately. If deserialization dominates:

1. **Send only what the grid displays.** API responses often carry whole nested objects (user records, lookup definitions, repeated parent objects) that no column shows. Dropping them usually gives the biggest single win: fewer bytes to download and parse.
2. **Flatten.** Even with the same fields, one flat object per row deserializes noticeably faster than a nested graph. On interpreted WebAssembly the cost is mostly in creating objects and setting properties, not in reading the bytes.
3. **Don't expect much from serializer settings.** Switching between source-generated and reflection-based System.Text.Json changes little next to the size and shape of the payload.

A good default: a grid-specific endpoint that returns one flat object per row, with exactly the displayed columns plus the IDs needed for editing and links.

## Other levers

- **AOT compilation** (`dotnet publish -c Release -p:RunAOTCompilation=true`) speeds up all interpreted .NET code in WebAssembly, deserialization included. It stacks with a smaller payload.
- **Release builds.** Debug builds of WebAssembly apps are much slower; don't benchmark them.
- **Server mode** when the full data set is too large to download. See [Paging & server-side data](#/guide/paging-server).
- **`HtmlTemplate` over `Template`** for simple formatted cells: no component instance per cell.
- **Fixed `Width`s** skip the initial text measurement for those columns.

## Limits

- Browsers cap element height at about 33 million pixels: roughly 1 million rows at 34 px. Use paging or server mode beyond that.
- If your app trims assemblies, preserve the properties of your row type. The grid uses reflection to build its accessors.
