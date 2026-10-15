# Coming from Telerik

How the parts of a typical `TelerikGrid` translate. **Gaps** are features BlazeGrid doesn't have yet, with a workaround where there is one.

## Grid

| TelerikGrid | BlazeGrid |
|---|---|
| `Data` | `Items` (assign a new list to refresh, or call `RefreshAsync`) |
| `Height`, `Width`, `Class` | `Height`, `Class`, `Style` (width follows the container) |
| `RowHeight` | `RowHeight` (fixed for every row) |
| `Pageable`, `PageSize` | `PageSize` (0 = virtual scrolling), `PageSizeOptions`, `PagerButtonCount`, `ShowPageSizeSelector` |
| `Sortable`, `SortMode.Multiple` | Always on. Multi-sort with **Shift+click** rather than plain click. |
| `Reorderable`, `Resizable` | Always on; per column via `Reorderable` / `Resizable` |
| `Groupable` | `ShowGroupPanel`; per column `Groupable` |
| `ShowColumnMenu` | Always on (filter, sort, lock, group, fit, hide, group order) |
| `EnableLoaderContainer` | `Loading`, `LoadingText`, `LoadingTemplate` (also shown automatically for slow queries) |
| `EditMode="Inline"` | Inline is the only mode: `EditTrigger`, `ShowEditCommands` |
| `OnRowDoubleClick` | `OnRowDoubleClick` |
| `OnEdit` | `OnRowEditStart` (set `Cancel` to refuse) |
| `OnUpdate` | `OnRowEditCommit` (validate / cancel) and `OnRowUpdated` (save) |
| `OnCancel` | `OnRowEditCancel` |
| `OnStateInit`, `OnStateChanged` | `StateKey` for automatic local persistence; `GetState`, `SetStateAsync`, `OnStateChanged` for your own storage |
| `OnRead` (manual data operations) | `ServerMode="true"` + `OnQueryChanged` + `TotalCount` |

## Columns

| GridColumn | BzColumn |
|---|---|
| `Field` (incl. nested paths) | `Field` |
| `Title`, `Width`, `Visible`, `Locked="true"` | `Title`, `Width`, `Visible`, `Locked="ColumnLock.Left"` |
| `DisplayFormat="{0:MM-dd-yyyy}"` | `Format="MM-dd-yyyy"` |
| `Editable`, `Groupable`, `Filterable` | Same names |
| `FilterMenuType.CheckBoxList` | `Filter="FilterKind.MultiSelect"` |
| `<Template>` | `<Template>` (`context` is typed: no cast needed) |
| `<EditorTemplate>` | `<EditTemplate>`: read/write `context.Value` |
| `<GroupHeaderTemplate>` | `<GroupHeaderTemplate>` (`context.Text`, `context.Count`) |
| `<FooterTemplate>` with `GridAggregate Count` | `<FooterTemplate>` (`context.Count`): counts only |
| `OnCellRender` | `CellClassSelector` |
| `Id` | Columns are keyed by `Field` |
| `GridCommandColumn` with Edit/Save/Cancel | `ShowEditCommands="true"` |
| `GridCommandColumn` with custom buttons | A template-only `BzColumn` (no `Field`) |

## Gaps

| Telerik feature | Status / workaround |
|---|---|
| Excel export (`GridExcelExport`) | **Not available.** Export from your data source. |
| `GridSearchBox` | **Not available.** Filter `Items` in your page (debounced input). |
| `OnRowContextMenu` | **Not available.** |
| `GridToolBarTemplate` | Put your toolbar above the grid. |
| Aggregates other than count (sum, avg) | **Not available.** Compute them from your data in a `FooterTemplate`. |
| Column chooser (show one hidden column) | The column menu has *Hide column* and *Show all columns* only. Call `SetVisible` from your own chooser. |
| Per-row enable/disable of the edit button | Refuse in `OnRowEditStart` instead. |
| Composite (OR) filter descriptors | Column filters AND together. Pre-filter `Items` for OR views. |
| Saved Telerik layouts | Not convertible. `GridState` is a different format, keyed by `Field`. |
| Variable row heights / text wrapping | Rows have a fixed height (`RowHeight`). |

See [Styling & themes](#/guide/styling) for restyling the grid to match an existing Telerik look.
