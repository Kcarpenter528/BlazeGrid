using System.ComponentModel;
using Microsoft.AspNetCore.Components;

namespace BlazeGrid;

/// <summary>Declares a column of a <see cref="BzGrid{TItem}"/>. Place it inside the grid; it renders nothing itself.</summary>
/// <remarks>
/// Parameters are read once, when the column registers with the grid. Changing a parameter afterwards (for example
/// <see cref="Title"/>) is not picked up yet; use <c>@key</c> on the grid to rebuild it if you need to.
/// </remarks>
/// <example><code>
/// &lt;BzColumn Field="Customer.Name" Title="Customer" Filter="FilterKind.MultiSelect" Width="180" /&gt;
/// &lt;BzColumn Field="Amount" Format="C2" Locked="ColumnLock.Right" /&gt;
/// </code></example>
public sealed class BzColumn<TItem> : ComponentBase, IDisposable
{
    [CascadingParameter, EditorBrowsable(EditorBrowsableState.Never)] public BzGrid<TItem>? Grid { get; set; }

    #region Data

    /// <summary>
    /// The property to show. Nested paths such as <c>"Customer.Name"</c> work, and a null anywhere along the path
    /// shows as blank. Also the column's key in state, sorts, filters and groups.
    /// </summary>
    /// <remarks>Leave it out for a template-only column (e.g. action buttons); such a column can't be sorted, filtered or grouped.</remarks>
    [Parameter] public string? Field { get; set; }

    /// <summary>Header text. Defaults to <see cref="Field"/>.</summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>.NET format string applied to the value, e.g. <c>"C2"</c>, <c>"N0"</c> or <c>"yyyy-MM-dd"</c>.</summary>
    [Parameter] public string? Format { get; set; }

    /// <summary>Culture used with <see cref="Format"/>. Defaults to the current culture.</summary>
    [Parameter] public IFormatProvider? FormatProvider { get; set; }

    /// <summary>Custom display text for a row. Also used for text filtering, multi-select values and grouping.</summary>
    [Parameter] public Func<TItem, string?>? DisplayText { get; set; }

    #endregion

    #region Size & position

    /// <summary>Initial width in pixels. Null measures the header and a sample of the data once the data arrives.</summary>
    [Parameter] public double? Width { get; set; }

    /// <summary>The narrowest the user can resize the column to.</summary>
    [Parameter] public double MinWidth { get; set; } = 48;

    /// <summary>The widest automatic sizing (initial measure or "Fit width") will make the column.</summary>
    [Parameter] public double MaxAutoWidth { get; set; } = 420;

    /// <summary>Locks the column to the left or right edge so it stays put while scrolling horizontally.</summary>
    [Parameter] public ColumnLock Locked { get; set; }

    /// <summary>Initial visibility. Users can hide and show columns from the column menu.</summary>
    [Parameter] public bool Visible { get; set; } = true;

    /// <summary>Text alignment. <c>Auto</c> right-aligns numbers and left-aligns everything else.</summary>
    [Parameter] public ColumnAlign Align { get; set; }

    #endregion

    #region Features

    /// <summary>The filter UI in the column menu. <c>Auto</c> picks it from the property type (text, number, date, or multi-select for bool and enums).</summary>
    [Parameter] public FilterKind Filter { get; set; } = FilterKind.Auto;

    /// <summary>Clicking the header sorts by this column.</summary>
    [Parameter] public bool Sortable { get; set; } = true;

    /// <summary>The column menu offers a filter for this column.</summary>
    [Parameter] public bool Filterable { get; set; } = true;

    /// <summary>The column can be grouped by (dragged to the group panel, or from its menu).</summary>
    [Parameter] public bool Groupable { get; set; } = true;

    /// <summary>The user can drag the header edge to resize the column.</summary>
    [Parameter] public bool Resizable { get; set; } = true;

    /// <summary>The user can drag the header to move the column.</summary>
    [Parameter] public bool Reorderable { get; set; } = true;

    /// <summary>
    /// Whether the cell gets an editor in edit mode. Null means editable when the property has a public setter.
    /// Set <c>true</c> with an <see cref="EditTemplate"/> to edit a read-only (computed) property.
    /// </summary>
    [Parameter] public bool? Editable { get; set; }

    /// <summary>
    /// A fixed order for group values (by display text), e.g. a status workflow. Values not listed follow in natural
    /// order. An order the user sets in the column menu overrides this one.
    /// </summary>
    [Parameter] public IReadOnlyList<string>? GroupOrder { get; set; }

    #endregion

    #region Styling

    /// <summary>CSS class(es) on every cell of this column.</summary>
    [Parameter] public string? CellClass { get; set; }

    /// <summary>Returns extra CSS class(es) for one cell, or null. Use it for conditional formatting.</summary>
    /// <example><code>CellClassSelector="@(o => o.DueDate &lt; DateTime.Today ? "overdue" : null)"</code></example>
    [Parameter] public Func<TItem, string?>? CellClassSelector { get; set; }

    /// <summary>CSS class(es) on the header cell.</summary>
    [Parameter] public string? HeaderClass { get; set; }

    #endregion

    #region Templates

    /// <summary>Renders the cell with any markup or components. <c>context</c> is the row's item.</summary>
    /// <example><code>
    /// &lt;BzColumn Field="Status"&gt;
    ///     &lt;Template&gt;&lt;StatusBadge Status="@context.Status" /&gt;&lt;/Template&gt;
    /// &lt;/BzColumn&gt;
    /// </code></example>
    [Parameter] public RenderFragment<TItem>? Template { get; set; }

    /// <summary>
    /// A lighter alternative to <see cref="Template"/>: HTML with <c>{{Field}}</c> or <c>{{Field:format}}</c>
    /// placeholders. Values are HTML-encoded.
    /// </summary>
    /// <example><code>HtmlTemplate="&lt;b&gt;{{SalesRep}}&lt;/b&gt; &lt;small&gt;#{{Id}}&lt;/small&gt;"</code></example>
    [Parameter] public string? HtmlTemplate { get; set; }

    /// <summary>Editor used for this cell while its row is in edit mode. Read and set <c>context.Value</c>.</summary>
    /// <example><code>
    /// &lt;EditTemplate&gt;
    ///     &lt;select class="bz-editor" value="@context.Value" @onchange="e =&gt; context.Value = e.Value"&gt;…&lt;/select&gt;
    /// &lt;/EditTemplate&gt;
    /// </code></example>
    [Parameter] public RenderFragment<CellEditContext<TItem>>? EditTemplate { get; set; }

    /// <summary>
    /// Content of this column's cell in a footer row pinned under the data. The footer appears when any visible
    /// column has one.
    /// </summary>
    /// <example><code>&lt;FooterTemplate&gt;Total: @context.Count&lt;/FooterTemplate&gt;</code></example>
    [Parameter] public RenderFragment<GridFooterContext>? FooterTemplate { get; set; }

    /// <summary>Replaces the default "Title: value (count)" content of group rows when grouping by this column.</summary>
    /// <example><code>&lt;GroupHeaderTemplate&gt;@context.Text — @context.Count tasks&lt;/GroupHeaderTemplate&gt;</code></example>
    [Parameter] public RenderFragment<GroupHeaderContext>? GroupHeaderTemplate { get; set; }

    #endregion

    protected override void OnInitialized()
    {
        if (Grid is null) throw new InvalidOperationException($"{nameof(BzColumn<TItem>)} must be placed inside a BzGrid.");
        Grid.AddColumn(this);
    }

    protected override bool ShouldRender() => false;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public void Dispose() => Grid?.RemoveColumn(this);
}
