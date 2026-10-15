# Sorting, filtering & grouping

## What users can do

- **Sort**: click a header to cycle ascending → descending → off. **Shift+click** adds the column as an extra sort; the header shows its priority number.
- **Filter**: open a column's menu. The filter UI depends on the column's [`FilterKind`](#/api/FilterKind):
  - **Text**: contains / not contains / equals / not equals / starts with / ends with / blank / not blank. Ignores case.
  - **Number**: = ≠ > ≥ < ≤, between (inclusive), blank / not blank.
  - **Date**: on / not on / after / on or after / before / on or before / between, blank / not blank. Whole days; the time is ignored.
  - **Multi-select**: a searchable checklist of the column's distinct values with counts.
- **Group**: drag a header onto the group panel, or use *Group by* in the column menu. Groups nest; chips in the panel can be reordered, reversed and removed. Group rows expand and collapse; *Expand all* / *Collapse all* sit at the right of the panel.

Filters on different columns combine with **AND**.

## Choosing the filter kind

`Filter="FilterKind.Auto"` (the default) picks from the property type: dates → Date, `bool` and enums → MultiSelect, numbers → Number, everything else → Text. Override it per column, typically to get a checklist for low-cardinality text:

```razor
<BzColumn Field="Region" Filter="FilterKind.MultiSelect" />
<BzColumn Field="Notes" Filter="FilterKind.None" />
```

## Doing it from code

Column methods take a [`ColumnModel`](#/api/ColumnModel). Get one with [`FindColumn`](#/api/BzGrid/FindColumn), passing the column's `Field`:

```csharp
var status = grid.FindColumn("Status")!;

await grid.SetSortAsync(grid.FindColumn("OrderDate")!, SortDirection.Descending);
await grid.SetSortAsync(grid.FindColumn("Customer")!, SortDirection.Ascending, append: true);

await grid.SetFilterAsync(status, new ColumnFilter
{
    Kind = FilterKind.MultiSelect,
    Values = new() { "New", "Processing" }
});

await grid.SetFilterAsync(grid.FindColumn("Amount")!, new ColumnFilter
{
    Kind = FilterKind.Number,
    Operator = CompareOperator.Between,
    Number = 100,
    Number2 = 500
});

await grid.GroupByAsync(status);
await grid.ClearFiltersAsync();
```

Pass `null` to [`SetFilterAsync`](#/api/BzGrid/SetFilterAsync) to clear one column's filter, or `null` as the direction to [`SetSortAsync`](#/api/BzGrid/SetSortAsync) to remove a sort.

### Preset views

To offer one-click views ("Assigned to me", "Overdue"), either apply a set of filters as above, or filter the data **before** it reaches the grid. That's also the way to express an **OR** across columns, which column filters can't do:

```csharp
private void ShowMine() =>
    view = all.Where(t => t.AssignedToId == me || t.OpenedById == me).ToList();
```

To apply a whole saved combination at once (filters, sorts, groups and layout), use [`SetStateAsync`](#/api/BzGrid/SetStateAsync). See [Saving layouts](#/guide/state).

## Reacting to changes

- [`OnFilterChanged`](#/api/BzGrid/OnFilterChanged) fires when a filter is set or cleared, with the changed column and all active filters.
- [`OnQueryChanged`](#/api/BzGrid/OnQueryChanged) fires for any filter, sort, group, page or page-size change, with a [`GridQuery`](#/api/GridQuery) snapshot.

## Group value order

Groups sort by value. For workflow-like values, give the column a fixed order; values not listed follow alphabetically:

```razor
<BzColumn Field="Status" GroupOrder="@(new[] { "New", "In Progress", "Blocked", "Done" })" />
```

Users can drag values into their own order in the column menu's **Group order** section. Their order is saved with the layout and wins over `GroupOrder`. From code: [`SetGroupOrderAsync`](#/api/BzGrid/SetGroupOrderAsync).

## Grouping and paging

Grouping works with both virtual scrolling and paging. When a page starts in the middle of a group, its parent group headers are repeated at the top, marked *continued*. Group counts always cover the whole group, not just the current page.

A sort on a column that is also grouped is ignored. The group's direction wins.
