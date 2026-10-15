namespace BlazeGrid.Internal;

/// <summary>Converts user-facing filters/sorts/groups into a normalized <see cref="EngineQuery"/>.</summary>
internal static class QueryBuilder
{
    private const double Day = 86_400_000d;

    public static EngineQuery Build<TItem>(
        Func<string, ColumnModel<TItem>?> find,
        IEnumerable<ColumnFilter> filters,
        IEnumerable<SortDescriptor> sorts,
        IReadOnlyList<SortDescriptor> groups,
        Func<ColumnModel<TItem>, string[]?> groupOrder,
        bool includeFiltersAndSorts)
    {
        var q = new EngineQuery();
        if (includeFiltersAndSorts)
        {
            foreach (var f in filters)
            {
                var col = find(f.Field);
                if (col is null || !col.CanFilter || !f.IsActive) continue;
                var p = Predicate(col, f);
                if (p is not null) q.P.Add(p);
            }
        }

        var grouped = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in groups)
        {
            var col = find(g.Field);
            if (col is null || !col.CanGroup || !grouped.Add(col.Key)) continue;
            q.K.Add(new EngineKey
            {
                C = col.EngineIndex,
                D = g.Direction == SortDirection.Descending,
                Num = col.IsNumeric,
                Ord = groupOrder(col)
            });
        }
        q.G = q.K.Count;

        if (includeFiltersAndSorts)
        {
            foreach (var s in sorts)
            {
                var col = find(s.Field);
                if (col is null || !col.CanSort || grouped.Contains(col.Key)) continue;
                q.K.Add(new EngineKey { C = col.EngineIndex, D = s.Direction == SortDirection.Descending, Num = col.IsNumeric });
            }
        }
        return q;
    }

    private static EnginePredicate? Predicate<TItem>(ColumnModel<TItem> col, ColumnFilter f)
    {
        int c = col.EngineIndex;
        switch (f.Kind)
        {
            case FilterKind.Text:
            {
                var v = (f.Text ?? "").ToLowerInvariant();
                return f.TextOperator switch
                {
                    TextOperator.Contains => new() { C = c, K = "t", Op = "contains", V = v },
                    TextOperator.NotContains => new() { C = c, K = "t", Op = "contains", V = v, Neg = true },
                    TextOperator.Equals => new() { C = c, K = "t", Op = "eq", V = v },
                    TextOperator.NotEquals => new() { C = c, K = "t", Op = "eq", V = v, Neg = true },
                    TextOperator.StartsWith => new() { C = c, K = "t", Op = "sw", V = v },
                    TextOperator.EndsWith => new() { C = c, K = "t", Op = "ew", V = v },
                    TextOperator.Empty => new() { C = c, K = "e" },
                    TextOperator.NotEmpty => new() { C = c, K = "e", Neg = true },
                    _ => null
                };
            }
            case FilterKind.Number:
                if (!col.IsNumeric) return null;
                return Range(c, f.Operator, f.Number, f.Number2, null);
            case FilterKind.Date:
                if (!col.IsNumeric) return null;
                return Range(c, f.Operator,
                    f.Date.HasValue ? NumberConv.DateToNumber(f.Date.Value.Date) : null,
                    f.Date2.HasValue ? NumberConv.DateToNumber(f.Date2.Value.Date) : null,
                    Day);
            case FilterKind.MultiSelect:
                return new() { C = c, K = "s", Vals = f.Values?.ToArray() ?? Array.Empty<string>() };
        }
        return null;
    }

    // span: for dates, the width of one unit (a day) so "equals" means "on that day".
    private static EnginePredicate? Range(int c, CompareOperator op, double? a, double? b, double? span)
    {
        double s = span ?? 0;
        bool day = span.HasValue;
        switch (op)
        {
            case CompareOperator.Empty: return new() { C = c, K = "e", Num = true };
            case CompareOperator.NotEmpty: return new() { C = c, K = "e", Num = true, Neg = true };
        }
        if (!a.HasValue) return null;
        double x = a.Value;
        return op switch
        {
            CompareOperator.Equals => day
                ? new() { C = c, K = "r", Min = x, MinI = true, Max = x + s, MaxI = false }
                : new() { C = c, K = "r", Min = x, MinI = true, Max = x, MaxI = true },
            CompareOperator.NotEquals => day
                ? new() { C = c, K = "r", Min = x, MinI = true, Max = x + s, MaxI = false, Neg = true }
                : new() { C = c, K = "r", Min = x, MinI = true, Max = x, MaxI = true, Neg = true },
            CompareOperator.GreaterThan => day
                ? new() { C = c, K = "r", Min = x + s, MinI = true }
                : new() { C = c, K = "r", Min = x, MinI = false },
            CompareOperator.GreaterOrEqual => new() { C = c, K = "r", Min = x, MinI = true },
            CompareOperator.LessThan => new() { C = c, K = "r", Max = x, MaxI = false },
            CompareOperator.LessOrEqual => day
                ? new() { C = c, K = "r", Max = x + s, MaxI = false }
                : new() { C = c, K = "r", Max = x, MaxI = true },
            CompareOperator.Between when b.HasValue => day
                ? new() { C = c, K = "r", Min = Math.Min(x, b.Value), MinI = true, Max = Math.Max(x, b.Value) + s, MaxI = false }
                : new() { C = c, K = "r", Min = Math.Min(x, b.Value), MinI = true, Max = Math.Max(x, b.Value), MaxI = true },
            _ => null
        };
    }
}
