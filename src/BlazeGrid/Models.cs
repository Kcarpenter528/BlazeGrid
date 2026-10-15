namespace BlazeGrid;

public enum ColumnLock { None, Left, Right }

public enum FilterKind { Auto, None, Text, Number, Date, MultiSelect }

public enum SortDirection { Ascending, Descending }

public enum ColumnAlign { Auto, Left, Center, Right }

/// <summary>Where filtering / sorting / grouping is computed.</summary>
public enum DataEngineMode
{
    /// <summary>Managed for small data, Web Worker above <c>WorkerThreshold</c> rows.</summary>
    Auto,
    /// <summary>Always compute in .NET on the UI thread.</summary>
    Managed,
    /// <summary>Always compute in a background Web Worker.</summary>
    Worker
}

public enum EditTrigger { None, DoubleClick }

public enum TextOperator { Contains, NotContains, Equals, NotEquals, StartsWith, EndsWith, Empty, NotEmpty }

public enum CompareOperator { Equals, NotEquals, GreaterThan, GreaterOrEqual, LessThan, LessOrEqual, Between, Empty, NotEmpty }

public sealed class SortDescriptor
{
    public string Field { get; set; } = "";
    public SortDirection Direction { get; set; }

    public SortDescriptor Clone() => new() { Field = Field, Direction = Direction };
}

public sealed class ColumnFilter
{
    public string Field { get; set; } = "";
    public FilterKind Kind { get; set; }

    public TextOperator TextOperator { get; set; }
    public string? Text { get; set; }

    public CompareOperator Operator { get; set; }
    public double? Number { get; set; }
    public double? Number2 { get; set; }
    public DateTime? Date { get; set; }
    public DateTime? Date2 { get; set; }

    /// <summary>Selected display values for a multi-select filter.</summary>
    public List<string>? Values { get; set; }

    public ColumnFilter Clone() => new()
    {
        Field = Field, Kind = Kind, TextOperator = TextOperator, Text = Text, Operator = Operator,
        Number = Number, Number2 = Number2, Date = Date, Date2 = Date2,
        Values = Values is null ? null : new List<string>(Values)
    };

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

public sealed class ColumnState
{
    public string Field { get; set; } = "";
    public double? Width { get; set; }
    public ColumnLock Lock { get; set; }
    public int Order { get; set; }
    public bool Visible { get; set; } = true;
}

/// <summary>Everything the grid persists to local storage.</summary>
public sealed class GridState
{
    public int Version { get; set; } = 1;
    public List<ColumnState> Columns { get; set; } = new();
    public List<SortDescriptor> Sorts { get; set; } = new();
    public List<ColumnFilter> Filters { get; set; } = new();
    public List<SortDescriptor> Groups { get; set; } = new();
    /// <summary>User-defined group value order per field (overrides a column's GroupOrder).</summary>
    public Dictionary<string, List<string>> GroupOrders { get; set; } = new();
    public int PageSize { get; set; }
}

/// <summary>A snapshot of the grid's query, suitable for sending to an API.</summary>
public sealed class GridQuery
{
    public IReadOnlyList<ColumnFilter> Filters { get; init; } = Array.Empty<ColumnFilter>();
    public IReadOnlyList<SortDescriptor> Sorts { get; init; } = Array.Empty<SortDescriptor>();
    public IReadOnlyList<SortDescriptor> Groups { get; init; } = Array.Empty<SortDescriptor>();
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int Skip => Page * PageSize;
    /// <summary>What triggered the query: "filter", "sort", "group", "page", "pagesize", "state".</summary>
    public string Reason { get; init; } = "";
}

public sealed class FilterChangedEventArgs
{
    public required string Field { get; init; }
    public ColumnFilter? Filter { get; init; }
    public required IReadOnlyList<ColumnFilter> Filters { get; init; }
    public required GridQuery Query { get; init; }
}

public sealed class RowEventArgs<TItem>
{
    public required TItem Item { get; init; }
    public int Index { get; init; }
}

public sealed class RowEditStartEventArgs<TItem>
{
    public required TItem Item { get; init; }
    public bool Cancel { get; set; }
}

public sealed record CellChange(string Field, object? OldValue, object? NewValue);

public sealed class RowEditCommitEventArgs<TItem>
{
    public required TItem Item { get; init; }
    public required IReadOnlyDictionary<string, CellChange> Changes { get; init; }
    /// <summary>Set to keep the row in edit mode (e.g. validation failed).</summary>
    public bool Cancel { get; set; }
    public string? Error { get; set; }
}

public sealed class CellValueChangedEventArgs<TItem>
{
    public required TItem Item { get; init; }
    public required string Field { get; init; }
    public object? OldValue { get; init; }
    public object? NewValue { get; init; }
}

/// <summary>Context passed to a column's EditTemplate.</summary>
public sealed class CellEditContext<TItem>
{
    private readonly Func<object?> _get;
    private readonly Action<object?> _set;

    internal CellEditContext(TItem item, string field, Func<object?> get, Action<object?> set)
    {
        Item = item; Field = field; _get = get; _set = set;
    }

    public TItem Item { get; }
    public string Field { get; }
    public object? Value { get => _get(); set => _set(value); }
}

public sealed class GridRenderStats
{
    public int Rows { get; init; }
    public int VisibleRows { get; init; }
    public string Engine { get; init; } = "";
    /// <summary>Filter / sort / group compute time.</summary>
    public double QueryMs { get; init; }
    /// <summary>Data received → DOM updated.</summary>
    public double DomMs { get; init; }
    /// <summary>Data received → next painted frame.</summary>
    public double PaintMs { get; init; }
}
