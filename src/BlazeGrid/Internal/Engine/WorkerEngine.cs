using System.Runtime.InteropServices;
using System.Text;
using Microsoft.JSInterop;

namespace BlazeGrid.Internal;

/// <summary>
/// Runs filter / sort / group in a Web Worker. Columns are shipped lazily as raw bytes
/// (Float64 buffers or \u001F-joined UTF-8 text) only when a query first needs them, so
/// data with no active query reaches the screen without touching the worker at all.
/// Results come back as one Int32 buffer.
/// </summary>
public sealed class WorkerEngine<TItem> : GridEngine<TItem>
{
    private readonly IJSObjectReference _module;
    private readonly int _id;
    private bool[] _num = Array.Empty<bool>();
    private bool[] _text = Array.Empty<bool>();

    public WorkerEngine(IReadOnlyList<ColumnModel<TItem>> columns, IJSObjectReference module, int id) : base(columns)
    {
        _module = module;
        _id = id;
    }

    public override string Name => "worker";

    public override async ValueTask SetDataAsync(IReadOnlyList<TItem> items)
    {
        await base.SetDataAsync(items);
        _num = new bool[Columns.Count];
        _text = new bool[Columns.Count];
        await _module.InvokeVoidAsync("wReset", _id, N);
    }

    public override void Invalidate(int column)
    {
        if (column < _num.Length) { _num[column] = false; _text[column] = false; }
    }

    private async ValueTask EnsureAsync(int c, bool num, bool text)
    {
        if (c >= _num.Length)
        {
            Array.Resize(ref _num, Columns.Count);
            Array.Resize(ref _text, Columns.Count);
        }
        var model = Columns[c];
        if (num && !_num[c])
        {
            var arr = new double[N];
            if (model.Accessor is null) Array.Fill(arr, double.NaN); else model.Accessor.FillNumbers(Items, arr);
            var bytes = MemoryMarshal.AsBytes(arr.AsSpan()).ToArray();
            await _module.InvokeVoidAsync("wLoad", _id, c, true, model.IsNumeric, bytes);
            _num[c] = true;
        }
        if (text && !_text[c])
        {
            var sb = new StringBuilder(N * 12);
            for (int i = 0; i < N; i++)
            {
                if (i > 0) sb.Append('\u001f');
                var s = model.GetText(Items[i]);
                if (s.Contains('\u001f')) s = s.Replace('\u001f', ' ');
                sb.Append(s);
            }
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            await _module.InvokeVoidAsync("wLoad", _id, c, false, model.IsNumeric, bytes);
            _text[c] = true;
        }
    }

    private async ValueTask EnsureForQueryAsync(EngineQuery q, int skipColumn)
    {
        foreach (var p in q.P)
        {
            if (p.C == skipColumn) continue;
            bool num = p.K == "r" || (p.K == "e" && p.Num);
            await EnsureAsync(p.C, num, !num);
        }
        for (int k = 0; k < q.K.Count; k++)
        {
            var key = q.K[k];
            bool needText = !key.Num || k < q.G || key.Ord is { Length: > 0 };
            await EnsureAsync(key.C, key.Num, needText);
        }
    }

    public override async ValueTask<EngineResult> QueryAsync(EngineQuery q)
    {
        await EnsureForQueryAsync(q, -1);
        var bytes = await _module.InvokeAsync<byte[]>("wQuery", _id, q);
        return Parse(bytes);
    }

    private static EngineResult Parse(byte[] bytes)
    {
        var ints = MemoryMarshal.Cast<byte, int>(bytes.AsSpan());
        int o = 0;
        int count = ints[o++];
        bool hasView = ints[o++] != 0;
        int[]? view = null;
        if (hasView) { view = ints.Slice(o, count).ToArray(); o += count; }
        int G = ints[o++];
        var starts = new int[G][];
        for (int g = 0; g < G; g++)
        {
            int len = ints[o++];
            starts[g] = ints.Slice(o, len).ToArray();
            o += len;
        }
        return new EngineResult(view, count, starts);
    }

    public override async ValueTask<List<DistinctValue>> DistinctAsync(int column, EngineQuery q)
    {
        await EnsureForQueryAsync(q, column);
        await EnsureAsync(column, Columns[column].IsNumeric, true);
        return await _module.InvokeAsync<List<DistinctValue>>("wDistinct", _id, column, Columns[column].IsNumeric, q);
    }

    public override async ValueTask DisposeAsync()
    {
        try { await _module.InvokeVoidAsync("wDispose", _id); }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
    }
}
