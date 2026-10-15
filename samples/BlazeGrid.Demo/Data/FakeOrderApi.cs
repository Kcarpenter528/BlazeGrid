namespace BlazeGrid.Demo.Data;

/// <summary>Simulates a server endpoint that applies the grid's GridQuery (what your real API would do).</summary>
public static class FakeOrderApi
{
    private static readonly Order[] Db = OrderGenerator.Create(200_000, seed: 7);

    public sealed record PageResult(IReadOnlyList<Order> Items, int Total);

    public static async Task<PageResult> QueryAsync(GridQuery q)
    {
        await Task.Delay(350); // network latency

        IEnumerable<Order> data = Db;
        foreach (var f in q.Filters) data = data.Where(o => Matches(o, f));

        IOrderedEnumerable<Order>? sorted = null;
        foreach (var s in q.Groups.Concat(q.Sorts))
        {
            Func<Order, object?> key = o => typeof(Order).GetProperty(s.Field)!.GetValue(o);
            bool desc = s.Direction == SortDirection.Descending;
            sorted = sorted is null
                ? (desc ? data.OrderByDescending(key) : data.OrderBy(key))
                : (desc ? sorted.ThenByDescending(key) : sorted.ThenBy(key));
        }
        var list = (sorted ?? data).ToList();
        return new PageResult(list.Skip(q.Skip).Take(q.PageSize).ToList(), list.Count);
    }

    public static Task<IEnumerable<string>> DistinctAsync(string field) =>
        Task.FromResult(Db.Select(o => typeof(Order).GetProperty(field)!.GetValue(o)?.ToString() ?? "")
            .Distinct().OrderBy(x => x).AsEnumerable());

    private static bool Matches(Order o, ColumnFilter f)
    {
        var v = typeof(Order).GetProperty(f.Field)!.GetValue(o);
        switch (f.Kind)
        {
            case FilterKind.MultiSelect:
                return f.Values!.Contains(v?.ToString() ?? "");
            case FilterKind.Text:
            {
                var s = v?.ToString() ?? "";
                var t = f.Text ?? "";
                return f.TextOperator switch
                {
                    TextOperator.Contains => s.Contains(t, StringComparison.OrdinalIgnoreCase),
                    TextOperator.NotContains => !s.Contains(t, StringComparison.OrdinalIgnoreCase),
                    TextOperator.Equals => s.Equals(t, StringComparison.OrdinalIgnoreCase),
                    TextOperator.NotEquals => !s.Equals(t, StringComparison.OrdinalIgnoreCase),
                    TextOperator.StartsWith => s.StartsWith(t, StringComparison.OrdinalIgnoreCase),
                    TextOperator.EndsWith => s.EndsWith(t, StringComparison.OrdinalIgnoreCase),
                    TextOperator.Empty => s.Length == 0,
                    TextOperator.NotEmpty => s.Length > 0,
                    _ => true
                };
            }
            case FilterKind.Number:
            {
                if (v is null) return f.Operator == CompareOperator.Empty || f.Operator == CompareOperator.NotEquals;
                var d = Convert.ToDouble(v);
                return Compare(d, f.Operator, f.Number, f.Number2);
            }
            case FilterKind.Date:
            {
                if (v is not DateTime dt) return f.Operator == CompareOperator.Empty || f.Operator == CompareOperator.NotEquals;
                var day = dt.Date;
                return f.Operator switch
                {
                    CompareOperator.Equals => day == f.Date!.Value.Date,
                    CompareOperator.NotEquals => day != f.Date!.Value.Date,
                    CompareOperator.GreaterThan => day > f.Date!.Value.Date,
                    CompareOperator.GreaterOrEqual => day >= f.Date!.Value.Date,
                    CompareOperator.LessThan => day < f.Date!.Value.Date,
                    CompareOperator.LessOrEqual => day <= f.Date!.Value.Date,
                    CompareOperator.Between => day >= f.Date!.Value.Date && day <= f.Date2!.Value.Date,
                    CompareOperator.NotEmpty => true,
                    _ => false
                };
            }
        }
        return true;
    }

    private static bool Compare(double d, CompareOperator op, double? a, double? b) => op switch
    {
        CompareOperator.Equals => d == a,
        CompareOperator.NotEquals => d != a,
        CompareOperator.GreaterThan => d > a,
        CompareOperator.GreaterOrEqual => d >= a,
        CompareOperator.LessThan => d < a,
        CompareOperator.LessOrEqual => d <= a,
        CompareOperator.Between => d >= a && d <= b,
        CompareOperator.NotEmpty => true,
        _ => false
    };
}
