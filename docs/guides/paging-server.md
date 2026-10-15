# Paging & server-side data

## Virtual scrolling or pages

With `PageSize="0"` (the default) every row is in one scrolling list; only the visible rows are rendered, so 100k+ rows scroll smoothly. Set a page size to show a pager instead:

```razor
<BzGrid TItem="Order" Items="orders" PageSize="100" PageSizeOptions="@(new[] { 50, 100, 500 })"
        PagerButtonCount="10" ShowPageSizeSelector="false">
```

| Parameter | Default | |
|---|---|---|
| `PageSize` | `0` | Rows per page; `0` = no paging. |
| `PageSizeOptions` | `{ 25, 50, 100, 250, 1000 }` | Choices in the page-size selector. |
| `PagerButtonCount` | `5` | Numbered buttons, shown in fixed blocks (1-10, 11-20, …) with "…" to jump between blocks. |
| `ShowPageSizeSelector` | `true` | Hide it to lock the page size. |

From code: [`SetPageAsync`](#/api/BzGrid/SetPageAsync) and [`SetPageSizeAsync`](#/api/BzGrid/SetPageSizeAsync). [`PageIndex`](#/api/BzGrid/PageIndex), [`PageCount`](#/api/BzGrid/PageCount) and [`TotalItemCount`](#/api/BzGrid/TotalItemCount) read the current position. Any filter, sort or group change returns to the first page.

## Server mode

When the data is too big to send to the browser, let your API filter, sort and page. With `ServerMode="true"` the grid stops doing those locally and raises [`OnQueryChanged`](#/api/BzGrid/OnQueryChanged) with a [`GridQuery`](#/api/GridQuery). You fetch the page and hand it back through `Items` and `TotalCount`:

```razor
<BzGrid TItem="Order" Items="page" TotalCount="total" ServerMode="true" PageSize="100"
        Loading="loading" LoadingText="Fetching…"
        OnQueryChanged="FetchAsync" FilterOptionsProvider="DistinctValuesAsync">
    <BzColumn Field="Customer" Filter="FilterKind.MultiSelect" />
    <BzColumn Field="OrderDate" Format="yyyy-MM-dd" />
    <BzColumn Field="Amount" Format="C2" />
</BzGrid>

@code {
    private IReadOnlyList<Order>? page;
    private int total;
    private bool loading;

    protected override Task OnInitializedAsync() => FetchAsync(new GridQuery { PageSize = 100 });

    private async Task FetchAsync(GridQuery q)
    {
        loading = true;
        var response = await Http.PostAsJsonAsync("api/orders/query", q);
        var result = await response.Content.ReadFromJsonAsync<PageResult<Order>>();
        page = result!.Items;     // a new list instance
        total = result.Total;
        loading = false;
    }

    private async Task<IEnumerable<string>> DistinctValuesAsync(string field) =>
        await Http.GetFromJsonAsync<string[]>($"api/orders/distinct/{field}") ?? [];
}
```

What the API receives (`GridQuery` serializes as plain JSON):

| Property | Meaning |
|---|---|
| `Filters` | Column filters to AND together. See [`ColumnFilter`](#/api/ColumnFilter) for which fields each `Kind` uses. |
| `Groups` | Groupings, outermost first. Order by these **before** `Sorts` so groups stay together across pages. |
| `Sorts` | Sorts, highest priority first. |
| `Page`, `PageSize`, `Skip` | The page to return. |
| `Reason` | What changed: `filter`, `sort`, `group`, `page`, `pagesize` or `state`. |

Notes:

- **Grouping** in server mode is applied by the grid to the rows it received. Group counts therefore cover the current page only.
- **Multi-select filters** need the column's distinct values. Locally the grid computes them; in server mode supply `FilterOptionsProvider`.
- **Restored state**: with `StateKey` set, a saved filter or sort is sent to `OnQueryChanged` (with `Reason = "state"`) as soon as the grid starts, so your first request should come from there, or be replaced by it.
- [`OnFilterChanged`](#/api/BzGrid/OnFilterChanged) also fires in server mode, if you only care about filters.
