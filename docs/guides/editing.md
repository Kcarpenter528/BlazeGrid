# Inline editing

One row at a time can be in edit mode. Editors appear in its editable cells; **Enter** saves and **Esc** cancels.

## Starting an edit

| How | Setting |
|---|---|
| Double-click a row | `EditTrigger="EditTrigger.DoubleClick"` (default) |
| A ✎ / ✓ / ✕ command column, locked right | `ShowEditCommands="true"` |
| From code | [`BeginEditAsync(item)`](#/api/BzGrid/BeginEditAsync) |

Clicking another row commits the current edit first. If the commit is rejected, the click is ignored.

## Which cells are editable

A column is editable when its property has a public setter, unless you set `Editable="false"`. The editor is picked from the property type: text box, number box, date picker, checkbox, or a select for enums.

```razor
<BzColumn Field="Id" Editable="false" />
<BzColumn Field="DueDate" Format="MM-dd-yyyy" />   @* date picker *@
<BzColumn Field="Priority" />                       @* enum → select *@
```

## Custom editors

`EditTemplate` replaces the editor. Read and write `context.Value`; the grid converts what you set to the property type:

```razor
<BzColumn Field="AssignedToName" Title="Assigned to" Editable="true">
    <EditTemplate>
        <select class="bz-editor" value="@(context.Value as string)" @onchange="e => context.Value = e.Value">
            <option value="">(unassigned)</option>
            @foreach (var u in users)
            {
                <option value="@u.FullName">@u.FullName</option>
            }
        </select>
    </EditTemplate>
</BzColumn>
```

Add the `bz-editor` class to native inputs to match the built-in editors' styling.

### Read-only (computed) properties

A column bound to a property without a setter, like `AssignedToName => AssignedTo?.FullName` above, can still be edited with `Editable="true"` plus an `EditTemplate`. The grid can't write it back, so it reports the change and you apply it in `OnRowUpdated`:

```csharp
private void RowUpdated(RowEditCommitEventArgs<TaskRow> e)
{
    if (e.Changes.TryGetValue("AssignedToName", out var change))
        e.Item.AssignedTo = users.FirstOrDefault(u => u.FullName == (string?)change.NewValue);
}
```

## Events, in order

1. [`OnRowEditStart`](#/api/BzGrid/OnRowEditStart): before the row opens. Set `Cancel = true` to refuse, e.g. for locked records.
2. [`OnCellValueChanged`](#/api/BzGrid/OnCellValueChanged): each time an editor's value changes. Nothing is written to the item yet.
3. [`OnRowEditCommit`](#/api/BzGrid/OnRowEditCommit): on save, before anything is written. Set `Cancel = true` and `Error` to keep the row open and show a message (validation).
4. The grid writes the changed values to the item.
5. [`OnRowUpdated`](#/api/BzGrid/OnRowUpdated): after writing. Call your API here.

Or [`OnRowEditCancel`](#/api/BzGrid/OnRowEditCancel) when the user cancels.

```razor
<BzGrid TItem="Order" Items="orders" ShowEditCommands="true"
        OnRowEditStart="e => e.Cancel = e.Item.IsLocked"
        OnRowEditCommit="Validate"
        OnRowUpdated="SaveAsync">
```

```csharp
private void Validate(RowEditCommitEventArgs<Order> e)
{
    if (e.Changes.TryGetValue("Quantity", out var q) && (int)q.NewValue! <= 0)
    {
        e.Cancel = true;
        e.Error = "Quantity must be at least 1.";
    }
}

private async Task SaveAsync(RowEditCommitEventArgs<Order> e) =>
    await Http.PutAsJsonAsync($"api/orders/{e.Item.Id}", e.Item);
```

`e.Changes` only contains cells whose value actually changed, keyed by column `Field`. If nothing changed, saving just closes the row and no events fire.

## Values that can't be converted

If a typed value can't be converted (e.g. letters in a number box), the grid shows an error above the rows and keeps the row in edit mode.
