# Getting started

BlazeGrid is a virtualized data grid for **Blazor WebAssembly**. Only the rows on screen exist in the DOM, so it stays fast with hundreds of thousands of rows, and filtering, sorting and grouping can run in a background Web Worker.

## 1. Reference the package

BlazeGrid isn't published to a NuGet feed yet. Reference the project directly:

```xml
<ItemGroup>
  <ProjectReference Include="..\BlazeGrid\src\BlazeGrid\BlazeGrid.csproj" />
</ItemGroup>
```

…or build a package and add it from a local folder feed:

```bash
dotnet pack src/BlazeGrid -c Release -o ./packages
```

## 2. Add the stylesheet

In `wwwroot/index.html`:

```html
<link rel="stylesheet" href="_content/BlazeGrid/bzgrid.css" />
```

The grid's JavaScript module and its Web Worker are loaded automatically. Nothing else goes in `index.html`.

## 3. Import the namespace

In `_Imports.razor`:

```razor
@using BlazeGrid
```

## 4. Your first grid

```razor
<BzGrid TItem="Order" Items="orders" Height="600px" PageSize="100" StateKey="orders-grid">
    <BzColumn Field="Id" Title="#" Locked="ColumnLock.Left" Editable="false" />
    <BzColumn Field="Customer.Name" Title="Customer" Filter="FilterKind.MultiSelect" />
    <BzColumn Field="Status" GroupOrder="@(new[] { "New", "Processing", "Shipped" })" />
    <BzColumn Field="OrderDate" Title="Ordered" Format="yyyy-MM-dd" />
    <BzColumn Field="Amount" Format="C2" />
</BzGrid>

@code {
    private List<Order>? orders;

    protected override async Task OnInitializedAsync() =>
        orders = await Http.GetFromJsonAsync<List<Order>>("api/orders");
}
```

Out of the box, users can:

- **sort** by clicking a header (Shift+click adds a second sort);
- **filter, group, lock, hide and auto-fit** from each column's menu;
- **reorder** columns by dragging headers, and **resize** them by dragging header edges (double-click an edge to fit);
- **group** by dragging a header onto the group panel.

With `StateKey` set, all of that is remembered in the browser between visits.

## Updating data

`Items` is compared by reference. To show new data, assign a new list:

```csharp
orders = await LoadOrdersAsync();   // new instance → the grid picks it up
```

If you change the same list in place (add, remove), call [`RefreshAsync`](#/api/BzGrid/RefreshAsync):

```csharp
orders.Add(newOrder);
await grid.RefreshAsync();
```

## Getting a reference to the grid

```razor
<BzGrid @ref="grid" TItem="Order" Items="orders">…</BzGrid>

@code {
    private BzGrid<Order>? grid;

    private Task ClearFilters() => grid!.ClearFiltersAsync();
}
```

## Next steps

- [Columns & templates](#/guide/columns): formats, widths, templates.
- [Styling & themes](#/guide/styling): make it match your app.
- [`BzGrid` API reference](#/api/BzGrid): every parameter, event and method.
- The sample app in `samples/BlazeGrid.Demo` (`dotnet run --project samples/BlazeGrid.Demo`) has local, server-mode and real-world pages.
