namespace BlazeGrid.Internal;

/// <summary>
/// In-process engine. Columns are extracted lazily into flat arrays; every column gets a dense
/// rank (by numeric value or case-insensitive text) so multi-key sorts become a single
/// Array.Sort over packed 64-bit keys. Mirrors bzgrid.worker.js exactly.
/// </summary>
public sealed class ManagedEngine<TItem> : GridEngine<TItem>
{
    private sealed class Col
    {
        public double[]? Num;
        public string[]? Text;
        public string[]? Lower;
        public int[]? TextRank;
        public string[]? TextDistinct;
        public int[]? SortRank;
        public int SortRankCount;
    }

    private Col?[] _cols = Array.Empty<Col?>();

    public ManagedEngine(IReadOnlyList<ColumnModel<TItem>> columns) : base(columns) { }

    public override string Name => "managed";

    public override ValueTask SetDataAsync(IReadOnlyList<TItem> items)
    {
        base.SetDataAsync(items);
        _cols = new Col?[Columns.Count];
        return default;
    }

    public override void Invalidate(int column)
    {
        if (column < _cols.Length) _cols[column] = null;
    }

    private Col Get(int c)
    {
        if (c >= _cols.Length) Array.Resize(ref _cols, Columns.Count);
        return _cols[c] ??= new Col();
    }

    private double[] Num(int c)
    {
        var col = Get(c);
        if (col.Num is null)
        {
            var a = new double[N];
            var acc = Columns[c].Accessor;
            if (acc is null) Array.Fill(a, double.NaN); else acc.FillNumbers(Items, a);
            col.Num = a;
        }
        return col.Num;
    }

    private string[] Text(int c)
    {
        var col = Get(c);
        if (col.Text is null)
        {
            var model = Columns[c];
            var t = new string[N];
            for (int i = 0; i < t.Length; i++) t[i] = model.GetText(Items[i]);
            col.Text = t;
        }
        return col.Text;
    }

    private string[] Lower(int c)
    {
        var col = Get(c);
        if (col.Lower is null)
        {
            var t = Text(c);
            var l = new string[t.Length];
            for (int i = 0; i < t.Length; i++) l[i] = t[i].ToLowerInvariant();
            col.Lower = l;
        }
        return col.Lower;
    }

    private Col TextRank(int c)
    {
        var col = Get(c);
        if (col.TextRank is not null) return col;
        var t = Text(c);
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        var ids = new int[t.Length];
        var list = new List<string>();
        for (int i = 0; i < t.Length; i++)
        {
            var s = t[i];
            if (!map.TryGetValue(s, out var id)) { id = list.Count; map[s] = id; list.Add(s); }
            ids[i] = id;
        }
        int d = list.Count;
        var low = new string[d];
        var order = new int[d];
        for (int k = 0; k < d; k++) { low[k] = list[k].ToLowerInvariant(); order[k] = k; }
        Array.Sort(order, (a, b) =>
        {
            int r = string.CompareOrdinal(low[a], low[b]);
            return r != 0 ? r : string.CompareOrdinal(list[a], list[b]);
        });
        var rankOf = new int[d];
        var sorted = new string[d];
        for (int k = 0; k < d; k++) { rankOf[order[k]] = k; sorted[k] = list[order[k]]; }
        for (int i = 0; i < ids.Length; i++) ids[i] = rankOf[ids[i]];
        col.TextRank = ids;
        col.TextDistinct = sorted;
        return col;
    }

    private Col SortRank(int c, bool numeric)
    {
        var col = Get(c);
        if (col.SortRank is not null) return col;
        if (!numeric)
        {
            TextRank(c);
            col.SortRank = col.TextRank;
            col.SortRankCount = col.TextDistinct!.Length;
            return col;
        }
        var v = Num(c);
        var vals = new double[v.Length];
        int m = 0;
        for (int i = 0; i < v.Length; i++) if (!double.IsNaN(v[i])) vals[m++] = v[i];
        Array.Sort(vals, 0, m);
        int u = 0;
        for (int i = 0; i < m; i++) if (i == 0 || vals[i] != vals[i - 1]) vals[u++] = vals[i];
        var r = new int[v.Length];
        for (int i = 0; i < v.Length; i++)
        {
            double x = v[i];
            r[i] = double.IsNaN(x) ? 0 : Array.BinarySearch(vals, 0, u, x) + 1;
        }
        col.SortRank = r;
        col.SortRankCount = u + 1;
        return col;
    }

    /// <summary>Rank array for a key, honouring an explicit value order when given.</summary>
    private (int[] rank, int count) KeyRank(EngineKey k)
    {
        var sr = SortRank(k.C, k.Num);
        if (k.Ord is null || k.Ord.Length == 0) return (sr.SortRank!, sr.SortRankCount);
        var tr = TextRank(k.C);
        int L = k.Ord.Length;
        var pos = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < L; i++) pos.TryAdd(k.Ord[i], i);
        var map = new int[tr.TextDistinct!.Length];
        for (int r = 0; r < map.Length; r++) map[r] = pos.TryGetValue(tr.TextDistinct[r], out var p) ? p : -1;
        var res = new int[N];
        var t = tr.TextRank!;
        var s = sr.SortRank!;
        for (int i = 0; i < res.Length; i++)
        {
            int p = map[t[i]];
            res[i] = p >= 0 ? p : L + s[i];
        }
        return (res, L + sr.SortRankCount);
    }

    private byte[]? Mask(EngineQuery q, int skipColumn)
    {
        byte[]? mask = null;
        foreach (var p in q.P)
        {
            if (p.C == skipColumn) continue;
            if (mask is null) { mask = new byte[N]; Array.Fill(mask, (byte)1); }
            Apply(p, mask);
        }
        return mask;
    }

    private void Apply(EnginePredicate p, byte[] m)
    {
        bool neg = p.Neg;
        int n = m.Length;
        switch (p.K)
        {
            case "t":
            {
                var low = Lower(p.C);
                var v = p.V ?? "";
                switch (p.Op)
                {
                    case "contains":
                        for (int i = 0; i < n; i++) if (m[i] != 0 && low[i].Contains(v, StringComparison.Ordinal) == neg) m[i] = 0;
                        break;
                    case "eq":
                        for (int i = 0; i < n; i++) if (m[i] != 0 && string.Equals(low[i], v, StringComparison.Ordinal) == neg) m[i] = 0;
                        break;
                    case "sw":
                        for (int i = 0; i < n; i++) if (m[i] != 0 && low[i].StartsWith(v, StringComparison.Ordinal) == neg) m[i] = 0;
                        break;
                    case "ew":
                        for (int i = 0; i < n; i++) if (m[i] != 0 && low[i].EndsWith(v, StringComparison.Ordinal) == neg) m[i] = 0;
                        break;
                }
                break;
            }
            case "r":
            {
                var v = Num(p.C);
                double min = p.Min ?? double.NegativeInfinity, max = p.Max ?? double.PositiveInfinity;
                bool mi = p.MinI, ma = p.MaxI;
                for (int i = 0; i < n; i++)
                {
                    if (m[i] == 0) continue;
                    double x = v[i];
                    bool ok = !double.IsNaN(x) && (mi ? x >= min : x > min) && (ma ? x <= max : x < max);
                    if (ok == neg) m[i] = 0;
                }
                break;
            }
            case "s":
            {
                var col = TextRank(p.C);
                var set = new HashSet<string>(p.Vals ?? Array.Empty<string>(), StringComparer.Ordinal);
                var allowed = new bool[col.TextDistinct!.Length];
                for (int r = 0; r < allowed.Length; r++) allowed[r] = set.Contains(col.TextDistinct[r]);
                var tr = col.TextRank!;
                for (int i = 0; i < n; i++) if (m[i] != 0 && allowed[tr[i]] == neg) m[i] = 0;
                break;
            }
            case "e":
            {
                if (p.Num)
                {
                    var v = Num(p.C);
                    for (int i = 0; i < n; i++) if (m[i] != 0 && double.IsNaN(v[i]) == neg) m[i] = 0;
                }
                else
                {
                    var t = Text(p.C);
                    for (int i = 0; i < n; i++) if (m[i] != 0 && (t[i].Length == 0) == neg) m[i] = 0;
                }
                break;
            }
        }
    }

    public override ValueTask<EngineResult> QueryAsync(EngineQuery q)
    {
        var mask = Mask(q, -1);
        int count = N;
        int[]? view = null;
        if (mask is not null)
        {
            count = 0;
            for (int i = 0; i < mask.Length; i++) count += mask[i];
            view = new int[count];
            for (int i = 0, k = 0; i < mask.Length; i++) if (mask[i] != 0) view[k++] = i;
        }
        if (q.K.Count > 0)
        {
            if (view is null) { view = new int[N]; for (int i = 0; i < N; i++) view[i] = i; }
            Sort(view, q.K);
        }
        var starts = q.G > 0 ? GroupStarts(view, count, q) : Array.Empty<int[]>();
        return new(new EngineResult(view, count, starts));
    }

    private void Sort(int[] view, List<EngineKey> keys)
    {
        int K = keys.Count;
        var ranks = new int[K][];
        var rc = new int[K];
        var bits = new int[K];
        int idxBits = BitsFor(N), total = idxBits;
        for (int k = 0; k < K; k++)
        {
            (ranks[k], rc[k]) = KeyRank(keys[k]);
            bits[k] = BitsFor(rc[k]);
            total += bits[k];
        }

        if (total <= 63)
        {
            var packed = new long[view.Length];
            for (int j = 0; j < view.Length; j++)
            {
                int r = view[j];
                long v = 0;
                for (int k = 0; k < K; k++)
                {
                    int rk = ranks[k][r];
                    if (keys[k].D) rk = rc[k] - 1 - rk;
                    v = (v << bits[k]) | (uint)rk;
                }
                packed[j] = (v << idxBits) | (uint)r;
            }
            Array.Sort(packed);
            long mask = (1L << idxBits) - 1;
            for (int j = 0; j < view.Length; j++) view[j] = (int)(packed[j] & mask);
        }
        else
        {
            Array.Sort(view, (a, b) =>
            {
                for (int k = 0; k < K; k++)
                {
                    int d = ranks[k][a] - ranks[k][b];
                    if (d != 0) return keys[k].D ? -d : d;
                }
                return a - b;
            });
        }
    }

    private int[][] GroupStarts(int[]? view, int count, EngineQuery q)
    {
        int G = q.G;
        var tr = new int[G][];
        var lists = new List<int>[G];
        for (int g = 0; g < G; g++) { tr[g] = TextRank(q.K[g].C).TextRank!; lists[g] = new List<int>(); }
        if (count > 0) foreach (var l in lists) l.Add(0);
        for (int j = 1; j < count; j++)
        {
            int r = view is null ? j : view[j];
            int prev = view is null ? j - 1 : view[j - 1];
            int changed = G;
            for (int g = 0; g < G; g++) if (tr[g][r] != tr[g][prev]) { changed = g; break; }
            for (int L = changed; L < G; L++) lists[L].Add(j);
        }
        var res = new int[G][];
        for (int g = 0; g < G; g++) res[g] = lists[g].ToArray();
        return res;
    }

    public override ValueTask<List<DistinctValue>> DistinctAsync(int column, EngineQuery q)
    {
        var mask = Mask(q, column);
        var col = TextRank(column);
        var tr = col.TextRank!;
        int d = col.TextDistinct!.Length;
        var counts = new int[d];
        var rep = new int[d];
        Array.Fill(rep, -1);
        for (int i = 0; i < tr.Length; i++)
        {
            int r = tr[i];
            if (rep[r] < 0) rep[r] = i;
            if (mask is null || mask[i] != 0) counts[r]++;
        }
        var order = new int[d];
        for (int k = 0; k < d; k++) order[k] = k;
        if (Columns[column].IsNumeric)
        {
            var v = Num(column);
            Array.Sort(order, (a, b) =>
            {
                double x = v[rep[a]], y = v[rep[b]];
                bool xn = double.IsNaN(x), yn = double.IsNaN(y);
                if (xn || yn) return xn == yn ? a - b : (xn ? -1 : 1);
                return x < y ? -1 : x > y ? 1 : a - b;
            });
        }
        var res = new List<DistinctValue>();
        foreach (var r in order)
        {
            if (counts[r] == 0) continue;
            res.Add(new DistinctValue { T = col.TextDistinct[r], C = counts[r] });
            if (res.Count >= 10_000) break;
        }
        return new(res);
    }
}
