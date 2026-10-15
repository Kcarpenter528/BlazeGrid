using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BlazeGrid.Internal;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace BlazeGrid;

public partial class BzGrid<TItem> : ComponentBase, IAsyncDisposable
{
    private static int s_nextId;
    private readonly int _id = Interlocked.Increment(ref s_nextId);

    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ------------------------------------------------------------------ parameters

    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>The data. Replace the list instance (or call <see cref="RefreshAsync"/>) to update.</summary>
    [Parameter] public IReadOnlyList<TItem>? Items { get; set; }

    [Parameter] public int RowHeight { get; set; } = 34;
    [Parameter] public string Height { get; set; } = "520px";
    [Parameter] public string? Class { get; set; }
    [Parameter] public string? Style { get; set; }
    /// <summary>Adds class "bz-theme-{Theme}" (built-in: "dark", "compact").</summary>
    [Parameter] public string? Theme { get; set; }
    [Parameter] public Func<TItem, string?>? RowClass { get; set; }

    /// <summary>0 = no paging (pure virtual scrolling).</summary>
    [Parameter] public int PageSize { get; set; }
    [Parameter] public int[] PageSizeOptions { get; set; } = { 25, 50, 100, 250, 1000 };

    [Parameter] public bool ShowGroupPanel { get; set; } = true;
    [Parameter] public bool GroupsCollapsedByDefault { get; set; }

    /// <summary>When set, layout / sort / filter / group state is persisted to localStorage under this key.</summary>
    [Parameter] public string? StateKey { get; set; }

    [Parameter] public bool Loading { get; set; }
    [Parameter] public string LoadingText { get; set; } = "Loading…";
    [Parameter] public string? LoadingClass { get; set; }
    [Parameter] public RenderFragment? LoadingTemplate { get; set; }
    [Parameter] public RenderFragment? EmptyTemplate { get; set; }

    [Parameter] public DataEngineMode Engine { get; set; } = DataEngineMode.Auto;
    [Parameter] public int WorkerThreshold { get; set; } = 20_000;
    [Parameter] public int Overscan { get; set; } = 10;

    /// <summary>
    /// Server mode: the grid does not filter/sort/page locally. Handle <see cref="OnQueryChanged"/>, fetch the page and
    /// set <see cref="Items"/> + <see cref="TotalCount"/>. Grouping is still applied to the received page.
    /// </summary>
    [Parameter] public bool ServerMode { get; set; }
    [Parameter] public int? TotalCount { get; set; }
    /// <summary>Server mode: supplies multi-select filter values for a field.</summary>
    [Parameter] public Func<string, Task<IEnumerable<string>>>? FilterOptionsProvider { get; set; }

    [Parameter] public EventCallback<GridQuery> OnQueryChanged { get; set; }
    [Parameter] public EventCallback<FilterChangedEventArgs> OnFilterChanged { get; set; }
    [Parameter] public EventCallback<GridState> OnStateChanged { get; set; }
    [Parameter] public EventCallback<GridRenderStats> OnRenderStats { get; set; }

    [Parameter] public TItem? SelectedItem { get; set; }
    [Parameter] public EventCallback<TItem?> SelectedItemChanged { get; set; }
    [Parameter] public EventCallback<RowEventArgs<TItem>> OnRowClick { get; set; }
    [Parameter] public EventCallback<RowEventArgs<TItem>> OnRowDoubleClick { get; set; }

    [Parameter] public EditTrigger EditTrigger { get; set; } = EditTrigger.DoubleClick;
    /// <summary>Adds a right-locked column with edit / save / cancel buttons.</summary>
    [Parameter] public bool ShowEditCommands { get; set; }
    [Parameter] public EventCallback<RowEditStartEventArgs<TItem>> OnRowEditStart { get; set; }
    /// <summary>Raised before changes are applied. Set Cancel (and Error) to keep the row in edit mode.</summary>
    [Parameter] public EventCallback<RowEditCommitEventArgs<TItem>> OnRowEditCommit { get; set; }
    /// <summary>Raised after changes were written to the item — a good place to call your API.</summary>
    [Parameter] public EventCallback<RowEditCommitEventArgs<TItem>> OnRowUpdated { get; set; }
    [Parameter] public EventCallback<TItem> OnRowEditCancel { get; set; }
    [Parameter] public EventCallback<CellValueChangedEventArgs<TItem>> OnCellValueChanged { get; set; }

    // ------------------------------------------------------------------ fields

    private ElementReference _root, _scroller;
    private GridBody<TItem>? _body;
    private IJSObjectReference? _module;
    private DotNetObjectReference<BzGrid<TItem>>? _dotnet;
    private int _handle;
    private bool _initDone, _rendered, _started, _disposed;
    private string? _pendingStateJson;

    private readonly List<ColumnModel<TItem>> _columns = new();
    private List<ColumnModel<TItem>> _visible = new();
    private ColumnModel<TItem>? _commandColumn;
    private string _rootStyle = "";

    private readonly List<SortDescriptor> _sorts = new();
    private readonly Dictionary<string, ColumnFilter> _filters = new(StringComparer.Ordinal);
    private readonly List<SortDescriptor> _groups = new();
    private readonly Dictionary<string, List<string>> _groupOrders = new(StringComparer.Ordinal);
    private readonly HashSet<string> _toggledGroups = new(StringComparer.Ordinal);
    private bool _collapseDefault;
    private int _page, _pageSize;

    private IReadOnlyList<TItem> _items = Array.Empty<TItem>();
    private IReadOnlyList<TItem>? _lastItemsParam;
    private bool _autoSizedWithData;
    private GridEngine<TItem>? _engine;
    private EngineResult _result = EngineResult.Empty;
    private int _queryVersion;
    private bool _busyVisible, _queryPending;
    private TItem? _lastSelectedParam;
    private double _lastQueryMs;

    private readonly List<GroupNode> _nodes = new();
    private int[][] _nodesByLevel = Array.Empty<int[]>();
    private int[]? _flat;
    private int _flatCount, _pageStart, _pageLen;
    private readonly List<int> _prefix = new();

    private TItem? _selected;
    private bool _hasSelected;

    private int _editIndex = -1;
    private Dictionary<string, object?> _editValues = new();
    private Dictionary<string, object?> _editOriginal = new();
    internal bool FocusEditorPending;

    private ColumnModel<TItem>? _menuColumn;
    private double _menuX, _menuY;

    private Stopwatch? _statsWatch;

    // ------------------------------------------------------------------ public surface used by internal components

    public int BodyVersion { get; private set; }
    public IReadOnlyList<ColumnModel<TItem>> VisibleColumns => _visible;
    public IReadOnlyList<ColumnModel<TItem>> Columns => _columns;
    public int RowCount => _prefix.Count + _pageLen;
    public bool IsLoading => Loading || _busyVisible;
    public string? EditError { get; private set; }
    public string EngineName => _engine?.Name ?? "none";
    /// <summary>Data rows after filtering (all pages).</summary>
    public int FilteredCount => _result.Count;
    public int PageIndex => _page;
    public int CurrentPageSize => _pageSize;
    public bool HasGroups => _groups.Count > 0;
    internal ElementReference ScrollerRef => _scroller;
    internal IJSObjectReference? Module => _module;

    public int PageCount
    {
        get
        {
            if (_pageSize <= 0) return 1;
            int total = ServerMode ? (TotalCount ?? _items.Count) : _flatCount;
            return Math.Max(1, (total + _pageSize - 1) / _pageSize);
        }
    }

    public int TotalItemCount => ServerMode ? (TotalCount ?? _items.Count) : _result.Count;

    public TItem ItemAt(int index) => _items[index];

    public GridRow GetRow(int i)
    {
        if (i < _prefix.Count) return GridRow.ForGroup(_nodes[_prefix[i]], true);
        int f = _pageStart + i - _prefix.Count;
        int e = _flat is null ? f : _flat[f];
        if (e < 0) return GridRow.ForGroup(_nodes[~e], false);
        return GridRow.ForItem(ViewToItem(e));
    }

    private int ViewToItem(int p) => _result.View is null ? p : _result.View[p];

    public string RowCss(TItem item, int displayIndex)
    {
        var css = (displayIndex & 1) == 1 ? "bz-row bz-alt" : "bz-row";
        if (_hasSelected && EqualityComparer<TItem>.Default.Equals(item, _selected)) css += " bz-selected";
        var extra = RowClass?.Invoke(item);
        return string.IsNullOrEmpty(extra) ? css : css + " " + extra;
    }

    public bool IsEditing(int itemIndex) => itemIndex == _editIndex;

    public SortDescriptor? SortFor(ColumnModel<TItem> col) => _sorts.FirstOrDefault(s => s.Field == col.Key);
    public int SortIndex(ColumnModel<TItem> col) => _sorts.FindIndex(s => s.Field == col.Key);
    public int SortCount => _sorts.Count;
    public ColumnFilter? FilterFor(ColumnModel<TItem> col) => _filters.GetValueOrDefault(col.Key);
    public bool IsGrouped(ColumnModel<TItem> col) => _groups.Any(g => g.Field == col.Key);
    public IReadOnlyList<SortDescriptor> Groups => _groups;
    public IReadOnlyList<SortDescriptor> Sorts => _sorts;
    public IReadOnlyCollection<ColumnFilter> Filters => _filters.Values;

    public ColumnModel<TItem>? FindColumn(string key)
    {
        foreach (var c in _columns) if (!c.Removed && c.Key == key) return c;
        return _commandColumn?.Key == key ? _commandColumn : null;
    }

    // ------------------------------------------------------------------ column registration

    internal void AddColumn(BzColumn<TItem> def)
    {
        var model = new ColumnModel<TItem>(def, _columns.Count);
        _columns.Add(model);
        if (_started)
        {
            if (_pendingStateJson is not null) ApplyColumnState(model, DeserializeState(_pendingStateJson));
            UpdateLayout();
            StateHasChanged();
        }
    }

    internal void RemoveColumn(BzColumn<TItem> def)
    {
        var model = _columns.FirstOrDefault(c => c.Def == def);
        if (model is null || _disposed) return;
        model.Removed = true;
        model.Visible = false;
        UpdateLayout();
        StateHasChanged();
    }

    // ------------------------------------------------------------------ lifecycle

    protected override void OnInitialized()
    {
        _pageSize = PageSize;
        _collapseDefault = GroupsCollapsedByDefault;
        if (ShowEditCommands)
        {
            var def = new BzColumn<TItem> { Title = "", Width = 72, Sortable = false, Filterable = false, Groupable = false, Reorderable = false, Resizable = false };
            _commandColumn = new ColumnModel<TItem>(def, -1, isCommand: true);
        }
    }

    protected override async Task OnInitializedAsync()
    {
        _module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/BlazeGrid/bzgrid.js");
        if (StateKey is not null) _pendingStateJson = await _module.InvokeAsync<string?>("loadState", StateKey);
        _initDone = true;
        await TryStartAsync();
    }

    protected override async Task OnParametersSetAsync()
    {
        if (!EqualityComparer<TItem>.Default.Equals(SelectedItem, _lastSelectedParam))
        {
            _lastSelectedParam = SelectedItem;
            _selected = SelectedItem;
            _hasSelected = SelectedItem is not null;
            BodyVersion++;
        }
        if (!ReferenceEquals(Items, _lastItemsParam))
        {
            _lastItemsParam = Items;
            if (_started) await ApplyDataAsync();
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _rendered = true;
            await TryStartAsync();
        }
        if (_statsWatch is not null && _module is not null && _started && !_queryPending)
        {
            var sw = _statsWatch;
            _statsWatch = null;
            double dom = sw.Elapsed.TotalMilliseconds;
            await _module.InvokeVoidAsync("afterPaint");
            double paint = sw.Elapsed.TotalMilliseconds;
            if (OnRenderStats.HasDelegate)
            {
                await OnRenderStats.InvokeAsync(new GridRenderStats
                {
                    Rows = _result.Count, VisibleRows = RowCount, Engine = EngineName,
                    QueryMs = _lastQueryMs, DomMs = dom, PaintMs = paint
                });
            }
        }
    }

    private async Task TryStartAsync()
    {
        if (_started || !_initDone || !_rendered || _module is null) return;
        _started = true;
        if (_pendingStateJson is not null)
        {
            var state = DeserializeState(_pendingStateJson);
            if (state is not null) ApplyState(state);
        }
        UpdateLayout();
        _dotnet = DotNetObjectReference.Create(this);
        _handle = await _module.InvokeAsync<int>("init", _root, _scroller, _dotnet, new { rowHeight = RowHeight, overscan = Overscan });
        _lastItemsParam = Items;
        if (Items is not null) await ApplyDataAsync();
        else
        {
            await AutoSizeAsync(_columns.Where(c => !c.WidthFixed), markFixed: false);
            StateHasChanged();
        }
        // In server mode a restored filter/sort must reach the API.
        if (ServerMode && (_filters.Count > 0 || _sorts.Count > 0 || _pageSize != PageSize)) await RaiseQueryAsync("state");
    }

    // ------------------------------------------------------------------ data pipeline

    /// <summary>Re-reads the current Items (e.g. after mutating the list in place).</summary>
    public async Task RefreshAsync()
    {
        if (_started) await ApplyDataAsync();
    }

    private async Task ApplyDataAsync()
    {
        _statsWatch = Stopwatch.StartNew();
        _items = Items ?? Array.Empty<TItem>();
        _editIndex = -1;
        EditError = null;
        await EnsureEngineAsync();
        await _engine!.SetDataAsync(_items);

        if (!_autoSizedWithData && _items.Count > 0)
        {
            _autoSizedWithData = true;
            await AutoSizeAsync(_columns.Where(c => !c.WidthFixed), markFixed: false);
        }
        await RunQueryAsync(resetScroll: false);
    }

    private async Task EnsureEngineAsync()
    {
        bool worker = Engine == DataEngineMode.Worker
                      || (Engine == DataEngineMode.Auto && !ServerMode && _items.Count >= WorkerThreshold && OperatingSystem.IsBrowser());
        if (_engine is not null && (_engine is WorkerEngine<TItem>) == worker) return;
        if (_engine is not null) await _engine.DisposeAsync();
        _engine = worker ? new WorkerEngine<TItem>(_columns, _module!, _id) : new ManagedEngine<TItem>(_columns);
    }

    private EngineQuery BuildEngineQuery() =>
        QueryBuilder.Build(FindColumn, _filters.Values, _sorts, _groups, EffectiveGroupOrderArray, includeFiltersAndSorts: !ServerMode);

    private async Task RunQueryAsync(bool resetScroll)
    {
        if (_engine is null) return;
        _statsWatch ??= Stopwatch.StartNew();
        int version = ++_queryVersion;
        var q = BuildEngineQuery();
        EngineResult r;
        var sw = Stopwatch.StartNew();
        if (q.IsTrivial)
        {
            r = new EngineResult(null, _items.Count, Array.Empty<int[]>());
        }
        else
        {
            var task = _engine.QueryAsync(q);
            if (!task.IsCompleted)
            {
                _queryPending = true;
                _ = ShowBusyLaterAsync(version);
            }
            try { r = await task; }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"BlazeGrid query failed: {ex}");
                r = new EngineResult(null, _items.Count, Array.Empty<int[]>());
            }
        }
        if (version != _queryVersion) return;
        _queryPending = false;
        _lastQueryMs = sw.Elapsed.TotalMilliseconds;
        _result = r;
        _busyVisible = false;
        BuildFlat();
        BodyVersion++;
        StateHasChanged();
        if (_module is not null && _handle != 0) await _module.InvokeVoidAsync("refresh", _handle, RowHeight, resetScroll);
    }

    private async Task ShowBusyLaterAsync(int version)
    {
        await Task.Delay(120);
        if (version == _queryVersion && _queryPending && !_busyVisible)
        {
            _busyVisible = true;
            StateHasChanged();
        }
    }

    private void BuildFlat()
    {
        _nodes.Clear();
        var starts = _result.GroupStarts;
        int G = starts.Length, count = _result.Count;
        if (G == 0)
        {
            _flat = null;
            _flatCount = count;
            _nodesByLevel = Array.Empty<int[]>();
            ComputePage();
            return;
        }

        _nodesByLevel = new int[G][];
        for (int L = 0; L < G; L++)
        {
            var col = FindColumn(_groups[L].Field)!;
            var s = starts[L];
            var ids = new int[s.Length];
            int[]? parentIds = L > 0 ? _nodesByLevel[L - 1] : null;
            int pp = 0;
            for (int k = 0; k < s.Length; k++)
            {
                int start = s[k], end = k + 1 < s.Length ? s[k + 1] : count;
                GroupNode? parent = null;
                if (parentIds is not null)
                {
                    while (pp + 1 < parentIds.Length && _nodes[parentIds[pp + 1]].Start <= start) pp++;
                    parent = _nodes[parentIds[pp]];
                }
                var text = col.GetText(_items[ViewToItem(start)]);
                var path = parent is null ? col.Key + "=" + text : parent.Path + "\u001f" + col.Key + "=" + text;
                var node = new GroupNode
                {
                    Id = _nodes.Count, Level = L, ParentId = parent?.Id ?? -1, Start = start, End = end,
                    Field = col.Key, Title = col.Title, Text = text, Path = path,
                    Collapsed = _collapseDefault ^ _toggledGroups.Contains(path)
                };
                ids[k] = node.Id;
                _nodes.Add(node);
            }
            _nodesByLevel[L] = ids;
        }

        var flat = new int[_nodes.Count + count];
        int fc = 0;
        var ptr = new int[G];
        void Flatten(int L, int to)
        {
            var ids = _nodesByLevel[L];
            while (ptr[L] < ids.Length && _nodes[ids[ptr[L]]].Start < to)
            {
                var nd = _nodes[ids[ptr[L]++]];
                flat[fc++] = ~nd.Id;
                if (nd.Collapsed)
                {
                    for (int d = L + 1; d < G; d++)
                        while (ptr[d] < _nodesByLevel[d].Length && _nodes[_nodesByLevel[d][ptr[d]]].Start < nd.End) ptr[d]++;
                    continue;
                }
                if (L == G - 1) for (int j = nd.Start; j < nd.End; j++) flat[fc++] = j;
                else Flatten(L + 1, nd.End);
            }
        }
        Flatten(0, count);
        _flat = flat;
        _flatCount = fc;
        ComputePage();
    }

    private void ComputePage()
    {
        _prefix.Clear();
        if (ServerMode || _pageSize <= 0)
        {
            _pageStart = 0;
            _pageLen = _flatCount;
            return;
        }
        int pages = Math.Max(1, (_flatCount + _pageSize - 1) / _pageSize);
        _page = Math.Clamp(_page, 0, pages - 1);
        _pageStart = _page * _pageSize;
        _pageLen = Math.Max(0, Math.Min(_pageSize, _flatCount - _pageStart));

        // A page that starts inside a group repeats its ancestor headers, marked "continued".
        if (_pageStart > 0 && _pageLen > 0 && _flat is not null)
        {
            int e = _flat[_pageStart];
            GroupNode? node = e < 0 ? (_nodes[~e].ParentId >= 0 ? _nodes[_nodes[~e].ParentId] : null) : LeafGroupAt(e);
            var chain = new List<int>();
            for (var n = node; n is not null; n = n.ParentId >= 0 ? _nodes[n.ParentId] : null) chain.Add(n.Id);
            chain.Reverse();
            _prefix.AddRange(chain);
        }
    }

    private GroupNode? LeafGroupAt(int viewPos)
    {
        if (_nodesByLevel.Length == 0) return null;
        var ids = _nodesByLevel[^1];
        int lo = 0, hi = ids.Length - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (_nodes[ids[mid]].Start <= viewPos) { found = mid; lo = mid + 1; } else hi = mid - 1;
        }
        return found < 0 ? null : _nodes[ids[found]];
    }

    // ------------------------------------------------------------------ layout

    private static int LockRank(ColumnLock l) => l == ColumnLock.Left ? 0 : l == ColumnLock.None ? 1 : 2;

    private void UpdateLayout()
    {
        var vis = _columns.Where(c => c.Visible && !c.Removed).ToList();
        if (_commandColumn is not null) vis.Add(_commandColumn);
        vis = vis.OrderBy(c => LockRank(c.Lock)).ThenBy(c => c.Order).ThenBy(c => c.EngineIndex).ToList();

        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("--bz-rh:").Append(RowHeight.ToString(inv)).Append("px;--bz-cols:");
        double total = 0;
        for (int i = 0; i < vis.Count; i++)
        {
            int w = (int)Math.Round(vis[i].Width);
            if (i > 0) sb.Append(' ');
            sb.Append(w.ToString(inv)).Append("px");
            total += w;
        }
        if (vis.Count == 0) sb.Append("1fr");
        sb.Append(";--bz-tw:").Append(total.ToString(inv)).Append("px;");

        double left = 0;
        int lastLeft = -1, firstRight = -1;
        for (int i = 0; i < vis.Count; i++)
        {
            if (vis[i].Lock != ColumnLock.Left) continue;
            sb.Append("--bz-o").Append(i.ToString(inv)).Append(':').Append(left.ToString(inv)).Append("px;");
            left += Math.Round(vis[i].Width);
            lastLeft = i;
        }
        double right = 0;
        for (int i = vis.Count - 1; i >= 0; i--)
        {
            if (vis[i].Lock != ColumnLock.Right) continue;
            sb.Append("--bz-o").Append(i.ToString(inv)).Append(':').Append(right.ToString(inv)).Append("px;");
            right += Math.Round(vis[i].Width);
            firstRight = i;
        }
        sb.Append("height:").Append(Height).Append(';');
        if (!string.IsNullOrEmpty(Style)) sb.Append(Style);
        _rootStyle = sb.ToString();

        for (int i = 0; i < vis.Count; i++)
        {
            var c = vis[i];
            c.DisplayIndex = i;
            string align = c.Def.Align switch
            {
                ColumnAlign.Right => " bz-right",
                ColumnAlign.Center => " bz-center",
                ColumnAlign.Left => "",
                _ => c.IsNumeric && !c.IsDate && !c.IsBool ? " bz-right" : c.IsCommand ? " bz-center" : ""
            };
            string lockCss = c.Lock switch
            {
                ColumnLock.Left => " bz-lock bz-lock-l" + (i == lastLeft ? " bz-lock-edge-l" : ""),
                ColumnLock.Right => " bz-lock bz-lock-r" + (i == firstRight ? " bz-lock-edge-r" : ""),
                _ => ""
            };
            string? lockStyle = c.Lock switch
            {
                ColumnLock.Left => $"left:var(--bz-o{i})",
                ColumnLock.Right => $"right:var(--bz-o{i})",
                _ => null
            };
            c.CellCss = "bz-cell" + align + lockCss + (string.IsNullOrEmpty(c.Def.CellClass) ? "" : " " + c.Def.CellClass);
            c.CellStyle = lockStyle;
            c.HeaderCss = "bz-hcell" + align + lockCss + (string.IsNullOrEmpty(c.Def.HeaderClass) ? "" : " " + c.Def.HeaderClass);
            c.HeaderStyle = lockStyle;
        }
        _visible = vis;
        BodyVersion++;
    }

    private string RootCss
    {
        get
        {
            var css = "bz-grid";
            if (!string.IsNullOrEmpty(Theme)) css += " bz-theme-" + Theme;
            if (!string.IsNullOrEmpty(Class)) css += " " + Class;
            return css;
        }
    }

    private static readonly Regex TagRegex = new("<[^>]+>", RegexOptions.Compiled);

    private async Task AutoSizeAsync(IEnumerable<ColumnModel<TItem>> columns, bool markFixed)
    {
        if (_module is null) return;
        var list = columns.Where(c => !c.IsCommand && !c.Removed && (c.HasData || c.Html is not null)).ToList();
        if (list.Count == 0) return;

        int n = _items.Count;
        var rows = new List<int>();
        for (int i = 0; i < Math.Min(n, 60); i++) rows.Add(i);
        if (n > 60)
        {
            int stride = Math.Max(1, n / 240);
            for (int i = 60; i < n && rows.Count < 300; i += stride) rows.Add(i);
        }

        var samples = new string[list.Count][];
        var headers = new string[list.Count];
        for (int k = 0; k < list.Count; k++)
        {
            var c = list[k];
            headers[k] = c.Title;
            var texts = new List<string>(rows.Count);
            foreach (var r in rows)
            {
                var item = _items[r];
                texts.Add(c.Html is not null ? TagRegex.Replace(c.RenderHtml(item), "") : c.GetText(item));
            }
            samples[k] = texts.OrderByDescending(t => t.Length).Take(8).ToArray();
        }

        double[] widths = _module is IJSInProcessObjectReference sync
            ? sync.Invoke<double[]>("measure", _root, samples, headers)
            : await _module.InvokeAsync<double[]>("measure", _root, samples, headers);

        for (int k = 0; k < list.Count; k++)
        {
            var c = list[k];
            c.Width = Math.Clamp(widths[k], c.Def.MinWidth, Math.Max(c.Def.MinWidth, c.Def.MaxAutoWidth));
            if (markFixed) c.WidthFixed = true;
        }
        UpdateLayout();
    }

    // ------------------------------------------------------------------ sorting / filtering / grouping / layout API

    private async Task HeaderClickAsync(ColumnModel<TItem> col, MouseEventArgs e)
    {
        if (!col.CanSort) return;
        var existing = _sorts.FirstOrDefault(s => s.Field == col.Key);
        SortDirection? next = existing is null ? SortDirection.Ascending
            : existing.Direction == SortDirection.Ascending ? SortDirection.Descending : null;
        if (!e.ShiftKey) _sorts.RemoveAll(s => s.Field != col.Key);
        await SetSortAsync(col, next, append: e.ShiftKey);
    }

    public async Task SetSortAsync(ColumnModel<TItem> col, SortDirection? direction, bool append = false)
    {
        var existing = _sorts.FirstOrDefault(s => s.Field == col.Key);
        if (!append) _sorts.RemoveAll(s => s.Field != col.Key);
        if (direction is null) _sorts.RemoveAll(s => s.Field == col.Key);
        else if (existing is not null) existing.Direction = direction.Value;
        else _sorts.Add(new SortDescriptor { Field = col.Key, Direction = direction.Value });
        await QueryChangedAsync("sort");
    }

    public async Task SetFilterAsync(ColumnModel<TItem> col, ColumnFilter? filter)
    {
        if (filter is null || !filter.IsActive) _filters.Remove(col.Key);
        else
        {
            filter.Field = col.Key;
            _filters[col.Key] = filter;
        }
        await QueryChangedAsync("filter", changedFilterField: col.Key);
    }

    public async Task ClearFiltersAsync()
    {
        _filters.Clear();
        await QueryChangedAsync("filter", changedFilterField: "");
    }

    public async Task GroupByAsync(ColumnModel<TItem> col, int index = -1)
    {
        if (!col.CanGroup || IsGrouped(col)) return;
        var g = new SortDescriptor { Field = col.Key };
        if (index < 0 || index > _groups.Count) _groups.Add(g); else _groups.Insert(index, g);
        await QueryChangedAsync("group");
    }

    public async Task UngroupAsync(string field)
    {
        _groups.RemoveAll(g => g.Field == field);
        await QueryChangedAsync("group");
    }

    public async Task ToggleGroupDirectionAsync(string field)
    {
        var g = _groups.FirstOrDefault(x => x.Field == field);
        if (g is null) return;
        g.Direction = g.Direction == SortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending;
        await QueryChangedAsync("group");
    }

    public async Task MoveGroupAsync(string field, int delta)
    {
        int i = _groups.FindIndex(g => g.Field == field);
        int j = i + delta;
        if (i < 0 || j < 0 || j >= _groups.Count) return;
        (_groups[i], _groups[j]) = (_groups[j], _groups[i]);
        await QueryChangedAsync("group");
    }

    public void SetAllGroupsCollapsed(bool collapsed)
    {
        _collapseDefault = collapsed;
        _toggledGroups.Clear();
        BuildFlat();
        BodyVersion++;
        StateHasChanged();
        _ = RefreshViewportAsync(false);
    }

    /// <summary>The explicit order of group values for a column: user order from the menu, else the column's GroupOrder.</summary>
    public IReadOnlyList<string>? EffectiveGroupOrder(ColumnModel<TItem> col) =>
        _groupOrders.TryGetValue(col.Key, out var o) ? o : col.Def.GroupOrder;

    public bool HasUserGroupOrder(ColumnModel<TItem> col) => _groupOrders.ContainsKey(col.Key);

    private string[]? EffectiveGroupOrderArray(ColumnModel<TItem> col) => EffectiveGroupOrder(col)?.ToArray();

    /// <summary>Sets (or with null, clears) the user-defined group value order for a column.</summary>
    public async Task SetGroupOrderAsync(ColumnModel<TItem> col, IReadOnlyList<string>? order)
    {
        if (order is null) _groupOrders.Remove(col.Key);
        else _groupOrders[col.Key] = order.ToList();
        if (IsGrouped(col)) await QueryChangedAsync("group");
        else SaveState();
    }

    public async Task SetLockAsync(ColumnModel<TItem> col, ColumnLock lockSide)
    {
        col.Lock = lockSide;
        UpdateLayout();
        SaveState();
        StateHasChanged();
        await Task.CompletedTask;
    }

    public void SetVisible(ColumnModel<TItem> col, bool visible)
    {
        col.Visible = visible;
        UpdateLayout();
        SaveState();
        StateHasChanged();
    }

    public void ShowAllColumns()
    {
        foreach (var c in _columns) if (!c.Removed) c.Visible = true;
        UpdateLayout();
        SaveState();
        StateHasChanged();
    }

    public async Task AutoFitAsync(ColumnModel<TItem>? col = null)
    {
        await AutoSizeAsync(col is null ? _columns : new[] { col }, markFixed: true);
        SaveState();
        StateHasChanged();
    }

    // ------------------------------------------------------------------ paging

    public async Task SetPageAsync(int page)
    {
        page = Math.Clamp(page, 0, PageCount - 1);
        if (page == _page) return;
        _page = page;
        if (ServerMode)
        {
            await RaiseQueryAsync("page");
            return;
        }
        ComputePage();
        BodyVersion++;
        StateHasChanged();
        await RefreshViewportAsync(true);
        await RaiseQueryAsync("page");
    }

    public async Task SetPageSizeAsync(int size)
    {
        _pageSize = Math.Max(0, size);
        _page = 0;
        SaveState();
        if (ServerMode)
        {
            await RaiseQueryAsync("pagesize");
            return;
        }
        ComputePage();
        BodyVersion++;
        StateHasChanged();
        await RefreshViewportAsync(true);
    }

    private async Task RefreshViewportAsync(bool resetScroll)
    {
        if (_module is not null && _handle != 0) await _module.InvokeVoidAsync("refresh", _handle, RowHeight, resetScroll);
    }

    // ------------------------------------------------------------------ query change plumbing

    public GridQuery CurrentQuery(string reason = "") => new()
    {
        Filters = _filters.Values.Select(f => f.Clone()).ToList(),
        Sorts = _sorts.Select(s => s.Clone()).ToList(),
        Groups = _groups.Select(s => s.Clone()).ToList(),
        Page = _page,
        PageSize = _pageSize,
        Reason = reason
    };

    private async Task QueryChangedAsync(string reason, string? changedFilterField = null)
    {
        _page = 0;
        SaveState();
        if (!ServerMode || reason == "group") await RunQueryAsync(resetScroll: true);
        else StateHasChanged();

        if (changedFilterField is not null && OnFilterChanged.HasDelegate)
        {
            await OnFilterChanged.InvokeAsync(new FilterChangedEventArgs
            {
                Field = changedFilterField,
                Filter = _filters.GetValueOrDefault(changedFilterField)?.Clone(),
                Filters = _filters.Values.Select(f => f.Clone()).ToList(),
                Query = CurrentQuery(reason)
            });
        }
        await RaiseQueryAsync(reason);
    }

    private Task RaiseQueryAsync(string reason) =>
        OnQueryChanged.HasDelegate ? OnQueryChanged.InvokeAsync(CurrentQuery(reason)) : Task.CompletedTask;

    public async Task<List<DistinctValue>> GetDistinctValuesAsync(ColumnModel<TItem> col)
    {
        if (ServerMode && FilterOptionsProvider is not null)
            return (await FilterOptionsProvider(col.Key)).Select(v => new DistinctValue { T = v }).ToList();
        if (_engine is null) return new();
        return await _engine.DistinctAsync(col.EngineIndex, BuildEngineQuery());
    }

    // ------------------------------------------------------------------ editing

    public async Task BeginEditAsync(TItem item)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (EqualityComparer<TItem>.Default.Equals(_items[i], item)) { await BeginEditAtAsync(i); return; }
        }
    }

    private bool AnyEditable => _columns.Any(c => c.Visible && c.CanEdit);

    internal async Task BeginEditAtAsync(int index)
    {
        if (_editIndex == index || !AnyEditable) return;
        if (_editIndex >= 0 && !await CommitEditAsync()) return;
        var item = _items[index];
        var args = new RowEditStartEventArgs<TItem> { Item = item };
        await OnRowEditStart.InvokeAsync(args);
        if (args.Cancel) return;
        _editIndex = index;
        _editOriginal = new();
        foreach (var c in _columns)
        {
            if (c.CanEdit) _editOriginal[c.Key] = c.GetValue(item);
        }
        _editValues = new Dictionary<string, object?>(_editOriginal);
        EditError = null;
        FocusEditorPending = true;
        BodyVersion++;
        StateHasChanged();
    }

    public async Task<bool> CommitEditAsync()
    {
        if (_editIndex < 0) return true;
        var item = _items[_editIndex];
        var changes = new Dictionary<string, CellChange>();
        foreach (var (key, v) in _editValues)
        {
            var old = _editOriginal.GetValueOrDefault(key);
            if (!Equals(old, v)) changes[key] = new CellChange(key, old, v);
        }
        if (changes.Count == 0) { EndEdit(); return true; }

        var args = new RowEditCommitEventArgs<TItem> { Item = item, Changes = changes };
        await OnRowEditCommit.InvokeAsync(args);
        if (args.Cancel)
        {
            EditError = args.Error ?? "The changes were rejected.";
            StateHasChanged();
            return false;
        }
        try
        {
            foreach (var ch in changes.Values)
            {
                var col = FindColumn(ch.Field);
                if (col?.Accessor?.CanWrite == true) col.Accessor.Set(item, ch.NewValue);
                if (col is not null) _engine?.Invalidate(col.EngineIndex);
            }
        }
        catch (Exception ex)
        {
            EditError = ex.Message;
            StateHasChanged();
            return false;
        }
        EndEdit();
        if (OnRowUpdated.HasDelegate)
        {
            await OnRowUpdated.InvokeAsync(args);
            // The handler may have changed other fields (e.g. computed totals): drop all cached columns and redraw.
            foreach (var c in _columns) _engine?.Invalidate(c.EngineIndex);
            BodyVersion++;
            StateHasChanged();
        }
        return true;
    }

    public async Task CancelEditAsync()
    {
        if (_editIndex < 0) return;
        var item = _items[_editIndex];
        EndEdit();
        await OnRowEditCancel.InvokeAsync(item);
    }

    private void EndEdit()
    {
        _editIndex = -1;
        EditError = null;
        BodyVersion++;
        StateHasChanged();
    }

    public object? GetEditValue(ColumnModel<TItem> col) => _editValues.GetValueOrDefault(col.Key);

    public void SetEditValue(ColumnModel<TItem> col, object? raw)
    {
        if (_editIndex < 0) return;
        try
        {
            var target = col.Accessor?.ValueType;
            var v = target is null ? raw : ValueConvert.To(raw, target);
            var old = _editValues.GetValueOrDefault(col.Key);
            _editValues[col.Key] = v;
            if (EditError is not null) { EditError = null; StateHasChanged(); }
            if (OnCellValueChanged.HasDelegate)
                _ = OnCellValueChanged.InvokeAsync(new CellValueChangedEventArgs<TItem>
                {
                    Item = _items[_editIndex], Field = col.Key, OldValue = old, NewValue = v
                });
        }
        catch (Exception)
        {
            EditError = $"'{raw}' is not a valid value for {col.Title}.";
            StateHasChanged();
        }
    }

    public CellEditContext<TItem> EditContextFor(ColumnModel<TItem> col) =>
        new(_items[_editIndex], col.Key, () => GetEditValue(col), v => SetEditValue(col, v));

    // ------------------------------------------------------------------ JS callbacks

    [JSInvokable]
    public void OnViewport(int start, int end) => _body?.SetViewport(start, end);

    [JSInvokable]
    public async Task OnRowEvent(string type, int row, bool ctrl, bool shift)
    {
        if (row < 0 || row >= RowCount) return;
        var r = GetRow(row);
        if (r.IsGroup) return;
        var item = _items[r.ItemIndex];
        var args = new RowEventArgs<TItem> { Item = item, Index = r.ItemIndex };
        if (type == "click")
        {
            if (_editIndex >= 0 && _editIndex != r.ItemIndex && !await CommitEditAsync()) return;
            if (!_hasSelected || !EqualityComparer<TItem>.Default.Equals(_selected, item))
            {
                _selected = item;
                _hasSelected = true;
                BodyVersion++;
                _body?.Refresh();
                await SelectedItemChanged.InvokeAsync(item);
            }
            await OnRowClick.InvokeAsync(args);
        }
        else if (type == "dblclick")
        {
            await OnRowDoubleClick.InvokeAsync(args);
            if (EditTrigger == EditTrigger.DoubleClick) await BeginEditAtAsync(r.ItemIndex);
        }
    }

    [JSInvokable]
    public async Task OnRowAction(string action, int row)
    {
        if (row < 0 || row >= RowCount) return;
        var r = GetRow(row);
        if (r.IsGroup) return;
        switch (action)
        {
            case "edit": await BeginEditAtAsync(r.ItemIndex); break;
            case "save": await CommitEditAsync(); break;
            case "cancel": await CancelEditAsync(); break;
        }
    }

    [JSInvokable]
    public async Task OnGroupToggle(int row)
    {
        if (row < 0 || row >= RowCount) return;
        var r = GetRow(row);
        if (!r.IsGroup) return;
        var path = r.Group!.Path;
        if (!_toggledGroups.Remove(path)) _toggledGroups.Add(path);
        BuildFlat();
        BodyVersion++;
        StateHasChanged();
        await RefreshViewportAsync(false);
    }

    [JSInvokable]
    public Task OnColumnResized(string key, double width)
    {
        var col = FindColumn(key);
        if (col is null) return Task.CompletedTask;
        col.Width = Math.Max(col.Def.MinWidth, width);
        col.WidthFixed = true;
        UpdateLayout();
        BodyVersion--; // widths are CSS-only; the body does not need to re-render
        SaveState();
        StateHasChanged();
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnAutoFit(string key)
    {
        var col = FindColumn(key);
        return col is null ? Task.CompletedTask : AutoFitAsync(col);
    }

    [JSInvokable]
    public Task OnColumnMoved(string key, string targetKey, bool before)
    {
        var src = FindColumn(key);
        var target = FindColumn(targetKey);
        if (src is null || target is null || src == target || src.IsCommand || target.IsCommand) return Task.CompletedTask;
        var ordered = _columns.Where(c => !c.Removed)
            .OrderBy(c => LockRank(c.Lock)).ThenBy(c => c.Order).ThenBy(c => c.EngineIndex).ToList();
        ordered.Remove(src);
        int idx = ordered.IndexOf(target) + (before ? 0 : 1);
        ordered.Insert(idx, src);
        src.Lock = target.Lock; // dropping into a locked zone locks the column
        for (int i = 0; i < ordered.Count; i++) ordered[i].Order = i;
        UpdateLayout();
        SaveState();
        StateHasChanged();
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnGroupDrop(string key)
    {
        var col = FindColumn(key);
        return col is null ? Task.CompletedTask : GroupByAsync(col);
    }

    // ------------------------------------------------------------------ column menu

    private void OpenMenu(ColumnModel<TItem> col, MouseEventArgs e)
    {
        _menuColumn = col;
        _menuX = e.ClientX - 8;
        _menuY = e.ClientY + 12;
    }

    internal void CloseMenu()
    {
        _menuColumn = null;
        StateHasChanged();
    }

    // ------------------------------------------------------------------ state

    public GridState GetState() => new()
    {
        Columns = _columns.Where(c => !c.Removed).Select(c => new ColumnState
        {
            Field = c.Key, Width = c.WidthFixed ? Math.Round(c.Width) : null, Lock = c.Lock, Order = c.Order, Visible = c.Visible
        }).ToList(),
        Sorts = _sorts.Select(s => s.Clone()).ToList(),
        Filters = _filters.Values.Select(f => f.Clone()).ToList(),
        Groups = _groups.Select(g => g.Clone()).ToList(),
        GroupOrders = _groupOrders.ToDictionary(kv => kv.Key, kv => kv.Value.ToList()),
        PageSize = _pageSize
    };

    public async Task SetStateAsync(GridState state)
    {
        ApplyState(state);
        UpdateLayout();
        SaveState();
        await RunQueryAsync(resetScroll: true);
    }

    /// <summary>Clears persisted state and restores the column definitions' defaults.</summary>
    public async Task ResetStateAsync()
    {
        foreach (var c in _columns) c.ResetLayout();
        _sorts.Clear();
        _filters.Clear();
        _groups.Clear();
        _groupOrders.Clear();
        _toggledGroups.Clear();
        _collapseDefault = GroupsCollapsedByDefault;
        _pageSize = PageSize;
        _page = 0;
        _pendingStateJson = null;
        if (StateKey is not null && _module is not null) await _module.InvokeVoidAsync("clearState", StateKey);
        await AutoSizeAsync(_columns.Where(c => !c.WidthFixed), markFixed: false);
        UpdateLayout();
        await RunQueryAsync(resetScroll: true);
        await RaiseQueryAsync("state");
    }

    private static GridState? DeserializeState(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<GridState>(json); }
        catch { return null; }
    }

    private void ApplyColumnState(ColumnModel<TItem> col, GridState? state)
    {
        var cs = state?.Columns.FirstOrDefault(x => x.Field == col.Key);
        if (cs is null) return;
        if (cs.Width.HasValue) { col.Width = cs.Width.Value; col.WidthFixed = true; }
        col.Lock = cs.Lock;
        col.Order = cs.Order;
        col.Visible = cs.Visible;
    }

    private void ApplyState(GridState s)
    {
        foreach (var c in _columns) ApplyColumnState(c, s);
        _sorts.Clear();
        _sorts.AddRange(s.Sorts.Where(x => FindColumn(x.Field) is not null));
        _filters.Clear();
        foreach (var f in s.Filters) if (FindColumn(f.Field) is not null) _filters[f.Field] = f;
        _groups.Clear();
        _groups.AddRange(s.Groups.Where(x => FindColumn(x.Field) is not null));
        _groupOrders.Clear();
        foreach (var (k, v) in s.GroupOrders) _groupOrders[k] = v;
        if (PageSize > 0 && s.PageSize > 0) _pageSize = s.PageSize;
    }

    private void SaveState()
    {
        if (!_started) return;
        var state = GetState();
        if (StateKey is not null && _module is not null)
        {
            var json = JsonSerializer.Serialize(state);
            _pendingStateJson = json;
            _ = _module.InvokeVoidAsync("saveState", StateKey, json).AsTask();
        }
        if (OnStateChanged.HasDelegate) _ = OnStateChanged.InvokeAsync(state);
    }

    // ------------------------------------------------------------------ dispose

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        try
        {
            if (_engine is not null) await _engine.DisposeAsync();
            if (_module is not null)
            {
                if (_handle != 0) await _module.InvokeVoidAsync("dispose", _handle);
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException) { }
        _dotnet?.Dispose();
        GC.SuppressFinalize(this);
    }
}
