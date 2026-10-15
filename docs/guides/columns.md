# Columns & templates

Each [`BzColumn`](#/api/BzColumn) inside a grid declares one column. Columns render nothing themselves; the grid reads their parameters once, when they register.

## Binding a field

```razor
<BzColumn Field="Customer.Address.City" Title="City" />
```

- `Field` is a property path. Nested paths work, and a `null` anywhere along the path shows as blank.
- `Field` is also the column's **key** in sorts, filters, groups and saved state. Renaming a property changes the key, so saved layouts will drop that column's settings.
- Without `Field`, the column is template-only (e.g. action buttons). It can't be sorted, filtered or grouped.

## Formatting

```razor
<BzColumn Field="Amount" Format="C2" />
<BzColumn Field="OrderDate" Format="MM-dd-yyyy" />
<BzColumn Field="Ratio" Format="P1" FormatProvider="@CultureInfo.GetCultureInfo("fr-CA")" />
```

`Format` is a regular .NET format string. Dates without a format show the short date (or date and time when there is a time part).

For text that isn't a straight format of one property, use `DisplayText`. It's also what text filters, multi-select filters and grouping see:

```razor
<BzColumn Field="AssignedTo" Title="Assigned to" DisplayText="@(t => t.AssignedTo?.FullName ?? "(Unassigned)")" />
```

## Width, alignment and locking

| Parameter | What it does |
|---|---|
| `Width` | Initial width in px. Leave it out and the grid measures the header plus a 300-row sample once data arrives. |
| `MinWidth` / `MaxAutoWidth` | Bounds for user resizing / automatic sizing. |
| `Align` | `Auto` right-aligns numbers; `Left`, `Center`, `Right` force it. |
| `Locked` | `ColumnLock.Left` or `Right` keeps the column in place while scrolling sideways. |
| `Visible` | Initial visibility (users can hide and show from the column menu). |

## Cell templates

Use `Template` for any markup or components. `context` is the row's item:

```razor
<BzColumn Field="Status">
    <Template>
        <span class="badge badge-@context.Status.ToLowerInvariant()">@context.Status</span>
    </Template>
</BzColumn>
```

For simple markup, `HtmlTemplate` is lighter: no component per cell. Values are HTML-encoded:

```razor
<BzColumn Field="SalesRep" HtmlTemplate="<b>{{SalesRep}}</b> <small>#{{Id}}</small>" />
<BzColumn Field="Total" HtmlTemplate="{{Total:C2}} <small>({{Count}} items)</small>" />
```

Sorting, filtering and grouping still use the column's `Field`, whatever the template shows.

### Template-only columns

```razor
<BzColumn Width="90" Locked="ColumnLock.Left" Sortable="false" Filterable="false" Groupable="false" Reorderable="false">
    <Template>
        <button class="icon-btn" @onclick="() => Open(context)">⋮</button>
    </Template>
</BzColumn>
```

## Footer row

Give any column a `FooterTemplate` and a footer row appears pinned under the data. `context` is a [`GridFooterContext`](#/api/GridFooterContext):

```razor
<BzColumn Field="Id" Title="Task ID">
    <FooterTemplate>Total: @context.Count.ToString("N0")</FooterTemplate>
</BzColumn>
```

`Count` follows filtering; `PageCount` is the number of rows on the current page.

## Group header template

When the grid is grouped by a column, its `GroupHeaderTemplate` replaces the default *Title: value (count)*. `context` is a [`GroupHeaderContext`](#/api/GroupHeaderContext):

```razor
<BzColumn Field="Status">
    <GroupHeaderTemplate>
        <StatusIcon Status="@context.Text" /> @context.Text — <b>@context.Count</b>
    </GroupHeaderTemplate>
</BzColumn>
```

## Turning features off per column

`Sortable`, `Filterable`, `Groupable`, `Resizable`, `Reorderable` and `Editable` all default to on (`Editable` defaults to "if the property has a setter"). See [`BzColumn`](#/api/BzColumn) for the full list.

> **Note:** column parameters are read once, when the column registers. Changing one afterwards (e.g. `Title`) is not picked up yet. Put a `@key` on the grid to rebuild it if you need that.
