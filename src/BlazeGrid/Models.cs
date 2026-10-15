namespace BlazeGrid;

/// <summary>Which edge a column is locked to.</summary>
public enum ColumnLock
{
    /// <summary>Scrolls horizontally with the other columns.</summary>
    None,
    /// <summary>Stays at the left edge.</summary>
    Left,
    /// <summary>Stays at the right edge.</summary>
    Right
}

/// <summary>The filter UI a column offers in its menu.</summary>
public enum FilterKind
{
    /// <summary>Chosen from the property type: Date for dates, MultiSelect for bool and enums, Number for numbers, else Text.</summary>
    Auto,
    /// <summary>No filter.</summary>
    None,
    /// <summary>Text with an operator (contains, equals, starts with, …). Case-insensitive.</summary>
    Text,
    /// <summary>Number with a comparison (=, ≠, &gt;, ≥, &lt;, ≤, between, blank).</summary>
    Number,
    /// <summary>Date with a comparison (on, before, after, between, blank). Compares whole days.</summary>
    Date,
    /// <summary>A checklist of the column's distinct values, with counts and search.</summary>
    MultiSelect
}

/// <summary>Sort or group direction.</summary>
public enum SortDirection
{
    /// <summary>A → Z, 0 → 9, oldest first.</summary>
    Ascending,
    /// <summary>Z → A, 9 → 0, newest first.</summary>
    Descending
}

/// <summary>Horizontal alignment of a column's cells.</summary>
public enum ColumnAlign
{
    /// <summary>Numbers right-aligned, everything else left-aligned.</summary>
    Auto,
    /// <summary>Left-aligned.</summary>
    Left,
    /// <summary>Centered.</summary>
    Center,
    /// <summary>Right-aligned.</summary>
    Right
}

/// <summary>Where filtering, sorting and grouping are computed.</summary>
public enum DataEngineMode
{
    /// <summary>.NET for small data, a Web Worker from <c>WorkerThreshold</c> rows up.</summary>
    Auto,
    /// <summary>Always compute in .NET on the UI thread.</summary>
    Managed,
    /// <summary>Always compute in a background Web Worker.</summary>
    Worker
}

/// <summary>What puts a row into edit mode.</summary>
public enum EditTrigger
{
    /// <summary>Only code (<c>BeginEditAsync</c>) or the edit command button.</summary>
    None,
    /// <summary>Double-clicking the row.</summary>
    DoubleClick
}

/// <summary>Operator of a <see cref="FilterKind.Text"/> filter. Comparisons ignore case.</summary>
public enum TextOperator
{
    /// <summary>The text appears anywhere in the value.</summary>
    Contains,
    /// <summary>The text does not appear in the value.</summary>
    NotContains,
    /// <summary>The value equals the text.</summary>
    Equals,
    /// <summary>The value differs from the text.</summary>
    NotEquals,
    /// <summary>The value starts with the text.</summary>
    StartsWith,
    /// <summary>The value ends with the text.</summary>
    EndsWith,
    /// <summary>The value is null or empty (the text is ignored).</summary>
    Empty,
    /// <summary>The value is not null or empty (the text is ignored).</summary>
    NotEmpty
}

/// <summary>Operator of a <see cref="FilterKind.Number"/> or <see cref="FilterKind.Date"/> filter.</summary>
public enum CompareOperator
{
    /// <summary>Equal to the first value (for dates: on that day).</summary>
    Equals,
    /// <summary>Not equal to the first value (for dates: not on that day).</summary>
    NotEquals,
    /// <summary>Greater than the first value (for dates: after that day).</summary>
    GreaterThan,
    /// <summary>Greater than or equal to the first value (for dates: on or after).</summary>
    GreaterOrEqual,
    /// <summary>Less than the first value (for dates: before that day).</summary>
    LessThan,
    /// <summary>Less than or equal to the first value (for dates: on or before).</summary>
    LessOrEqual,
    /// <summary>Between the two values, inclusive. Their order doesn't matter.</summary>
    Between,
    /// <summary>The value is null.</summary>
    Empty,
    /// <summary>The value is not null.</summary>
    NotEmpty
}

/// <summary>One sort or grouping: a column key and a direction.</summary>
public sealed class SortDescriptor
{
    /// <summary>The column key (its Field, else its Title).</summary>
    public string Field { get; set; } = "";

    /// <summary>The direction.</summary>
    public SortDirection Direction { get; set; }

    /// <summary>Returns a copy.</summary>
    public SortDescriptor Clone() => new() { Field = Field, Direction = Direction };
}

/// <summary>
/// A filter on one column. Which properties apply depends on <see cref="Kind"/>: Text uses
/// <see cref="TextOperator"/> and <see cref="Text"/>; Number uses <see cref="Operator"/>, <see cref="Number"/> and
/// <see cref="Number2"/>; Date uses <see cref="Operator"/>, <see cref="Date"/> and <see cref="Date2"/>;
/// MultiSelect uses <see cref="Values"/>. Filters on different columns are combined with AND.
/// </summary>
public sealed class ColumnFilter
{
    /// <summary>The column key. Filled in by <c>SetFilterAsync</c>.</summary>
    public string Field { get; set; } = "";

    /// <summary>The kind of filter, which decides which other properties are used.</summary>
    public FilterKind Kind { get; set; }

    /// <summary>Text filters: the operator.</summary>
    public TextOperator TextOperator { get; set; }

    /// <summary>Text filters: the text to look for.</summary>
    public string? Text { get; set; }

    /// <summary>Number and date filters: the comparison.</summary>
    public CompareOperator Operator { get; set; }

    /// <summary>Number filters: the value (the lower bound for Between).</summary>
    public double? Number { get; set; }

    /// <summary>Number filters: the upper bound for Between.</summary>
    public double? Number2 { get; set; }

    /// <summary>Date filters: the day (the first day for Between). The time of day is ignored.</summary>
    public DateTime? Date { get; set; }

    /// <summary>Date filters: the last day for Between.</summary>
    public DateTime? Date2 { get; set; }

    /// <summary>Multi-select filters: the display values to keep. An empty list matches nothing; null means no filter.</summary>
    public List<string>? Values { get; set; }

    /// <summary>Returns a deep copy.</summary>
    public ColumnFilter Clone() => new()
    {
        Field = Field, Kind = Kind, TextOperator = TextOperator, Text = Text, Operator = Operator,
        Number = Number, Number2 = Number2, Date = Date, Date2 = Date2,
        Values = Values is null ? null : new List<string>(Values)
    };

    /// <summary>True when the filter has enough input to apply (e.g. a Between filter needs both values).</summary>
    public bool IsActive => Kind switch
    {
        FilterKind.Text => TextOperator is TextOperator.Empty or TextOperator.NotEmpty || !string.IsNullOrEmpty(Text),
        FilterKind.Number => Operator is CompareOperator.Empty or CompareOperator.NotEmpty
                             || (Number.HasValue && (Operator != CompareOperator.Between || Number2.HasValue)),
        FilterKind.Date => Operator is CompareOperator.Empty or CompareOperator.NotEmpty
                           || (Date.HasValue && (Operator != CompareOperator.Between || Date2.HasValue)),
        FilterKind.MultiSelect => Values is not null,
        _ => false
    };
}

/// <summary>The persisted layout of one column.</summary>
public sealed class ColumnState
{
    /// <summary>The column key.</summary>
    public string Field { get; set; } = "";

    /// <summary>Width in pixels, or null when the column is still sized automatically.</summary>
    public double? Width { get; set; }

    /// <summary>The lock side.</summary>
    public ColumnLock Lock { get; set; }

    /// <summary>Display position.</summary>
    public int Order { get; set; }

    /// <summary>Whether the column is shown.</summary>
    public bool Visible { get; set; } = true;
}

/// <summary>
/// Everything the grid persists: column layout, sorts, filters, groups, group value orders and page size.
/// Plain data that serializes to JSON, so you can store named layouts on your server.
/// </summary>
public sealed class GridState
{
    /// <summary>Format version.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Per-column layout.</summary>
    public List<ColumnState> Columns { get; set; } = new();

    /// <summary>Active sorts, highest priority first.</summary>
    public List<SortDescriptor> Sorts { get; set; } = new();

    /// <summary>Active column filters.</summary>
    public List<ColumnFilter> Filters { get; set; } = new();

    /// <summary>Active groupings, outermost first.</summary>
    public List<SortDescriptor> Groups { get; set; } = new();

    /// <summary>User-defined group value order per column key (overrides the column's GroupOrder).</summary>
    public Dictionary<string, List<string>> GroupOrders { get; set; } = new();

    /// <summary>The page size the user picked (only restored when the grid has paging on).</summary>
    public int PageSize { get; set; }
}

/// <summary>A snapshot of the grid's query, suitable for sending to an API.</summary>
public sealed class GridQuery
{
    /// <summary>Active column filters (combine them with AND).</summary>
    public IReadOnlyList<ColumnFilter> Filters { get; init; } = Array.Empty<ColumnFilter>();

    /// <summary>Active sorts, highest priority first.</summary>
    public IReadOnlyList<SortDescriptor> Sorts { get; init; } = Array.Empty<SortDescriptor>();

    /// <summary>Active groupings, outermost first. Sort by these before <see cref="Sorts"/>.</summary>
    public IReadOnlyList<SortDescriptor> Groups { get; init; } = Array.Empty<SortDescriptor>();

    /// <summary>Zero-based page.</summary>
    public int Page { get; init; }

    /// <summary>Rows per page (0 = paging off).</summary>
    public int PageSize { get; init; }

    /// <summary>Rows to skip: <c>Page × PageSize</c>.</summary>
    public int Skip => Page * PageSize;

    /// <summary>What triggered the query: "filter", "sort", "group", "page", "pagesize" or "state".</summary>
    public string Reason { get; init; } = "";
}

/// <summary>Arguments of <c>OnFilterChanged</c>.</summary>
public sealed class FilterChangedEventArgs
{
    /// <summary>The column whose filter changed ("" when all filters were cleared).</summary>
    public required string Field { get; init; }

    /// <summary>The column's new filter, or null when it was cleared.</summary>
    public ColumnFilter? Filter { get; init; }

    /// <summary>All active filters after the change.</summary>
    public required IReadOnlyList<ColumnFilter> Filters { get; init; }

    /// <summary>The full query after the change.</summary>
    public required GridQuery Query { get; init; }
}

/// <summary>Arguments of <c>OnRowClick</c> and <c>OnRowDoubleClick</c>.</summary>
public sealed class RowEventArgs<TItem>
{
    /// <summary>The row's item.</summary>
    public required TItem Item { get; init; }

    /// <summary>The item's index in <c>Items</c>.</summary>
    public int Index { get; init; }
}

/// <summary>Arguments of <c>OnRowEditStart</c>.</summary>
public sealed class RowEditStartEventArgs<TItem>
{
    /// <summary>The row about to be edited.</summary>
    public required TItem Item { get; init; }

    /// <summary>Set to true to keep the row read-only.</summary>
    public bool Cancel { get; set; }
}

/// <summary>One edited value: the column key with its value before and after.</summary>
/// <param name="Field">The column key.</param>
/// <param name="OldValue">The value when editing started.</param>
/// <param name="NewValue">The edited value, converted to the property type.</param>
public sealed record CellChange(string Field, object? OldValue, object? NewValue);

/// <summary>Arguments of <c>OnRowEditCommit</c> and <c>OnRowUpdated</c>.</summary>
public sealed class RowEditCommitEventArgs<TItem>
{
    /// <summary>The edited item.</summary>
    public required TItem Item { get; init; }

    /// <summary>The changed values, keyed by column key. Unchanged cells are not included.</summary>
    public required IReadOnlyDictionary<string, CellChange> Changes { get; init; }

    /// <summary>OnRowEditCommit only: set to true to keep the row in edit mode (e.g. validation failed).</summary>
    public bool Cancel { get; set; }

    /// <summary>OnRowEditCommit only: the message shown above the grid when <see cref="Cancel"/> is set.</summary>
    public string? Error { get; set; }
}

/// <summary>Arguments of <c>OnCellValueChanged</c>.</summary>
public sealed class CellValueChangedEventArgs<TItem>
{
    /// <summary>The row being edited.</summary>
    public required TItem Item { get; init; }

    /// <summary>The column key.</summary>
    public required string Field { get; init; }

    /// <summary>The editor's previous value.</summary>
    public object? OldValue { get; init; }

    /// <summary>The editor's new value, already converted to the property type.</summary>
    public object? NewValue { get; init; }
}

/// <summary>The <c>context</c> of a column's EditTemplate.</summary>
public sealed class CellEditContext<TItem>
{
    private readonly Func<object?> _get;
    private readonly Action<object?> _set;

    internal CellEditContext(TItem item, string field, Func<object?> get, Action<object?> set)
    {
        Item = item; Field = field; _get = get; _set = set;
    }

    /// <summary>The row being edited (still holding its original values).</summary>
    public TItem Item { get; }

    /// <summary>The column key.</summary>
    public string Field { get; }

    /// <summary>The pending value. Setting it converts to the property type and raises OnCellValueChanged.</summary>
    public object? Value { get => _get(); set => _set(value); }
}

/// <summary>Arguments of <c>OnRenderStats</c>: timings for the last data load.</summary>
public sealed class GridRenderStats
{
    /// <summary>Data rows after filtering.</summary>
    public int Rows { get; init; }

    /// <summary>Display rows (data rows on the page plus group rows).</summary>
    public int VisibleRows { get; init; }

    /// <summary>The engine that ran the query: "managed" or "worker".</summary>
    public string Engine { get; init; } = "";

    /// <summary>Filter, sort and group compute time in milliseconds.</summary>
    public double QueryMs { get; init; }

    /// <summary>Data received → DOM updated, in milliseconds.</summary>
    public double DomMs { get; init; }

    /// <summary>Data received → next painted frame, in milliseconds.</summary>
    public double PaintMs { get; init; }
}

/// <summary>The <c>context</c> of a column's FooterTemplate.</summary>
public sealed class GridFooterContext
{
    /// <summary>Rows after filtering (all pages).</summary>
    public int Count { get; init; }

    /// <summary>Rows on the current page (equals Count when paging is off).</summary>
    public int PageCount { get; init; }
}

/// <summary>The <c>context</c> of a column's GroupHeaderTemplate.</summary>
public sealed class GroupHeaderContext
{
    /// <summary>The grouped column's key.</summary>
    public required string Field { get; init; }

    /// <summary>The grouped column's title.</summary>
    public required string Title { get; init; }

    /// <summary>The group value's display text ("" for blank values).</summary>
    public required string Text { get; init; }

    /// <summary>Data rows in the group (all pages).</summary>
    public int Count { get; init; }

    /// <summary>Nesting depth (0 = outermost grouping).</summary>
    public int Level { get; init; }

    /// <summary>True when the header is repeated at the top of a page that starts mid-group.</summary>
    public bool Continued { get; init; }
}
