# Saving layouts

## In the browser: `StateKey`

```razor
<BzGrid TItem="Order" Items="orders" StateKey="orders-grid">
```

With a `StateKey`, the grid saves its layout to `localStorage` under that key on every change and restores it on the next visit:

- column order, width, lock side and visibility;
- sorts, filters and groupings;
- user-defined group value orders;
- page size (only when the grid has paging on).

Give every grid its own key. Columns are matched by their `Field`, so adding or removing columns is safe. Entries for columns that no longer exist are ignored.

The grid has no built-in reset button. Put one in your toolbar that calls [`ResetStateAsync`](#/api/BzGrid/ResetStateAsync): it clears the stored state and goes back to the column definitions.

## On your server: named or shared layouts

[`GridState`](#/api/GridState) is plain data that serializes to JSON, so you can store layouts per user, name them and share them:

```csharp
// Save the current layout
var json = JsonSerializer.Serialize(grid.GetState());
await Http.PostAsJsonAsync("api/layouts", new { name = "My open tasks", json });

// Apply a saved one
var state = JsonSerializer.Deserialize<GridState>(saved.Json)!;
await grid.SetStateAsync(state);
```

[`OnStateChanged`](#/api/BzGrid/OnStateChanged) fires with the new state whenever the layout changes. Use it to show a "modified" badge next to the active layout name, or to auto-save.

```razor
<BzGrid … OnStateChanged="s => layoutModified = !LayoutEquals(s, activeLayout)">
```

`GridState.Version` is there so you can detect layouts saved by an older version of your page.
