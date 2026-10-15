namespace BlazeGrid.Internal;

// These DTOs are sent verbatim to the Web Worker (camelCase JSON), so names are kept short.

/// <summary>A normalized query. Filters are reduced to primitive predicates; groups come first in K.</summary>
public sealed class EngineQuery
{
    /// <summary>Predicates (AND-ed).</summary>
    public List<EnginePredicate> P { get; set; } = new();
    /// <summary>Ordering keys: group keys first, then sort keys.</summary>
    public List<EngineKey> K { get; set; } = new();
    /// <summary>Number of leading keys in K that are group levels.</summary>
    public int G { get; set; }

    public bool IsTrivial => P.Count == 0 && K.Count == 0;
}

public sealed class EnginePredicate
{
    /// <summary>Column engine index.</summary>
    public int C { get; set; }
    /// <summary>"t" text op, "r" numeric range, "s" set membership, "e" empty.</summary>
    public string K { get; set; } = "t";
    /// <summary>Text op: "contains", "eq", "sw", "ew".</summary>
    public string? Op { get; set; }
    /// <summary>Lower-cased text operand.</summary>
    public string? V { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public bool MinI { get; set; }
    public bool MaxI { get; set; }
    public bool Neg { get; set; }
    public string[]? Vals { get; set; }
    /// <summary>For "e": test the numeric representation (NaN) rather than empty text.</summary>
    public bool Num { get; set; }
}

public sealed class EngineKey
{
    public int C { get; set; }
    /// <summary>Descending.</summary>
    public bool D { get; set; }
    /// <summary>Sort by numeric representation.</summary>
    public bool Num { get; set; }
    /// <summary>Optional explicit value order (display text); unlisted values follow naturally.</summary>
    public string[]? Ord { get; set; }
}

public sealed class DistinctValue
{
    public string T { get; set; } = "";
    public int C { get; set; }
}

public sealed class EngineResult
{
    public static readonly EngineResult Empty = new(null, 0, Array.Empty<int[]>());

    public EngineResult(int[]? view, int count, int[][] groupStarts)
    {
        View = view; Count = count; GroupStarts = groupStarts;
    }

    /// <summary>Row indices in display order; null = identity (0..Count-1).</summary>
    public int[]? View { get; }
    public int Count { get; }
    /// <summary>Per group level, the view positions where a new group begins.</summary>
    public int[][] GroupStarts { get; }
    public double Ms { get; set; }
}

public abstract class GridEngine<TItem> : IAsyncDisposable
{
    protected GridEngine(IReadOnlyList<ColumnModel<TItem>> columns) => Columns = columns;

    protected IReadOnlyList<ColumnModel<TItem>> Columns { get; }
    protected IReadOnlyList<TItem> Items { get; private set; } = Array.Empty<TItem>();
    protected int N { get; private set; }

    public abstract string Name { get; }

    public virtual ValueTask SetDataAsync(IReadOnlyList<TItem> items)
    {
        Items = items;
        N = items.Count;
        return default;
    }

    public abstract ValueTask<EngineResult> QueryAsync(EngineQuery q);
    public abstract ValueTask<List<DistinctValue>> DistinctAsync(int column, EngineQuery q);
    /// <summary>Drop cached data for a column (after an edit).</summary>
    public abstract void Invalidate(int column);
    public virtual ValueTask DisposeAsync() => default;

    protected static int BitsFor(int count) => count <= 1 ? 1 : 32 - System.Numerics.BitOperations.LeadingZeroCount((uint)(count - 1));
}
