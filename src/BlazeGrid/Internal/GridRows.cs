namespace BlazeGrid.Internal;

public sealed class GroupNode
{
    public int Id { get; init; }
    public int Level { get; init; }
    public int ParentId { get; init; } = -1;
    /// <summary>First view position (inclusive).</summary>
    public int Start { get; init; }
    /// <summary>Last view position (exclusive).</summary>
    public int End { get; init; }
    public string Field { get; init; } = "";
    public string Title { get; init; } = "";
    public string Text { get; init; } = "";
    public string Path { get; init; } = "";
    public bool Collapsed { get; internal set; }
    public int Count => End - Start;
    internal string KeyNormal => _kn ??= "g" + Id;
    internal string KeyContinued => _kc ??= "c" + Id;
    private string? _kn, _kc;
}

public readonly struct GridRow
{
    private GridRow(GroupNode? group, int itemIndex, bool continued)
    {
        Group = group; ItemIndex = itemIndex; Continued = continued;
    }

    public static GridRow ForGroup(GroupNode g, bool continued) => new(g, -1, continued);
    public static GridRow ForItem(int index) => new(null, index, false);

    public GroupNode? Group { get; }
    public int ItemIndex { get; }
    public bool Continued { get; }
    public bool IsGroup => Group is not null;
}
