using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace BlazeGrid.Internal;

/// <summary>
/// A column at runtime: its <see cref="BzColumn{TItem}"/> definition plus the current layout. Get one from
/// <c>BzGrid.FindColumn</c>, <c>Columns</c> or <c>VisibleColumns</c> and pass it to the grid's column methods.
/// </summary>
public sealed class ColumnModel<TItem>
{
    internal ColumnModel(BzColumn<TItem> def, int engineIndex, bool isCommand = false)
    {
        Def = def;
        EngineIndex = engineIndex;
        IsCommand = isCommand;
        Key = isCommand ? "__cmd" : def.Field ?? def.Title ?? $"col{engineIndex}";
        if (!isCommand && def.Field is not null) Accessor = FieldAccessor<TItem>.Create(def.Field);

        var core = Accessor?.CoreType;
        IsNumeric = Accessor?.IsNumeric ?? false;
        IsDate = Accessor?.IsDate ?? false;
        IsBool = core == typeof(bool);
        IsEnum = core?.IsEnum ?? false;
        EnumType = IsEnum ? core : null;

        Kind = def.Filter != FilterKind.Auto ? def.Filter
            : IsDate ? FilterKind.Date
            : IsBool || IsEnum ? FilterKind.MultiSelect
            : IsNumeric ? FilterKind.Number
            : FilterKind.Text;

        if (!string.IsNullOrEmpty(def.HtmlTemplate)) Html = HtmlTemplateCompiler.Compile<TItem>(def.HtmlTemplate);
        ResetLayout();
    }

    /// <summary>The column's declaration (its parameters).</summary>
    public BzColumn<TItem> Def { get; }
    /// <summary>The key used in state, sorts, filters and groups: the Field, else the Title.</summary>
    public string Key { get; }
    [EditorBrowsable(EditorBrowsableState.Never)] public int EngineIndex { get; }
    [EditorBrowsable(EditorBrowsableState.Never)] public bool IsCommand { get; }
    [EditorBrowsable(EditorBrowsableState.Never)] public bool Removed { get; internal set; }
    [EditorBrowsable(EditorBrowsableState.Never)] public FieldAccessor<TItem>? Accessor { get; }
    /// <summary>The filter kind in effect (Auto resolved from the property type).</summary>
    public FilterKind Kind { get; }
    /// <summary>True for numeric, date and bool properties (they sort and filter as numbers).</summary>
    public bool IsNumeric { get; }
    /// <summary>True for DateTime, DateTimeOffset and DateOnly properties.</summary>
    public bool IsDate { get; }
    /// <summary>True for bool properties.</summary>
    public bool IsBool { get; }
    /// <summary>True for enum properties.</summary>
    public bool IsEnum { get; }
    [EditorBrowsable(EditorBrowsableState.Never)] public Type? EnumType { get; }

    // layout state
    /// <summary>Current width in pixels.</summary>
    public double Width { get; internal set; }
    /// <summary>True when width came from the column definition, persisted state or the user.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)] public bool WidthFixed { get; internal set; }
    /// <summary>Current lock side.</summary>
    public ColumnLock Lock { get; internal set; }
    /// <summary>Current display position.</summary>
    public int Order { get; internal set; }
    /// <summary>Whether the column is currently shown.</summary>
    public bool Visible { get; internal set; }

    // render caches (rebuilt by the grid on layout change)
    [EditorBrowsable(EditorBrowsableState.Never)] public int DisplayIndex { get; internal set; }
    [EditorBrowsable(EditorBrowsableState.Never)] public string CellCss { get; internal set; } = "bz-cell";
    [EditorBrowsable(EditorBrowsableState.Never)] public string? CellStyle { get; internal set; }
    [EditorBrowsable(EditorBrowsableState.Never)] public string HeaderCss { get; internal set; } = "bz-hcell";
    [EditorBrowsable(EditorBrowsableState.Never)] public string? HeaderStyle { get; internal set; }
    internal TemplateSegment<TItem>[]? Html { get; }

    /// <summary>Header text.</summary>
    public string Title => Def.Title ?? Def.Field ?? "";
    [EditorBrowsable(EditorBrowsableState.Never)] public bool HasData => Accessor is not null || Def.DisplayText is not null;
    /// <summary>The column can be sorted (Sortable and bound to data).</summary>
    public bool CanSort => !IsCommand && Def.Sortable && HasData;
    /// <summary>The column can be filtered.</summary>
    public bool CanFilter => !IsCommand && Def.Filterable && HasData && Kind != FilterKind.None;
    /// <summary>The column can be grouped by.</summary>
    public bool CanGroup => !IsCommand && Def.Groupable && HasData;
    [EditorBrowsable(EditorBrowsableState.Never)] public bool CanReorder => !IsCommand && Def.Reorderable;
    [EditorBrowsable(EditorBrowsableState.Never)] public bool CanResize => Def.Resizable;
    /// <summary>The column gets an editor in edit mode.</summary>
    public bool CanEdit => !IsCommand && Def.Editable != false
                           && ((Accessor?.CanWrite ?? false) || (Def.Editable == true && Def.EditTemplate is not null));
    [EditorBrowsable(EditorBrowsableState.Never)] public bool HasMenu => !IsCommand;

    internal void ResetLayout()
    {
        Width = Def.Width ?? (IsCommand ? 72 : 120);
        WidthFixed = Def.Width.HasValue || IsCommand;
        Lock = IsCommand ? ColumnLock.Right : Def.Locked;
        Order = IsCommand ? int.MaxValue : EngineIndex;
        Visible = Def.Visible;
    }

    /// <summary>The cell's display text for an item (DisplayText or the formatted value).</summary>
    public string GetText(TItem item)
    {
        if (Def.DisplayText is not null) return Def.DisplayText(item) ?? "";
        return Accessor is null ? "" : TextFormat.Format(Accessor.Get(item), Def.Format, Def.FormatProvider);
    }

    /// <summary>The raw property value for an item.</summary>
    public object? GetValue(TItem item) => Accessor?.Get(item);

    [EditorBrowsable(EditorBrowsableState.Never)] public string RenderHtml(TItem item)
    {
        var sb = new StringBuilder();
        foreach (var seg in Html!)
        {
            if (seg.Literal is not null) { sb.Append(seg.Literal); continue; }
            var v = seg.Accessor?.Get(item);
            sb.Append(WebUtility.HtmlEncode(TextFormat.Format(v, seg.Format, Def.FormatProvider)));
        }
        return sb.ToString();
    }
}

internal sealed record TemplateSegment<TItem>(string? Literal, FieldAccessor<TItem>? Accessor, string? Format);

internal static partial class HtmlTemplateCompiler
{
    [GeneratedRegex(@"\{\{\s*([\w\.]+)\s*(?::([^}]*))?\}\}")]
    private static partial Regex Placeholder();

    public static TemplateSegment<TItem>[] Compile<TItem>(string template)
    {
        var list = new List<TemplateSegment<TItem>>();
        int pos = 0;
        foreach (Match m in Placeholder().Matches(template))
        {
            if (m.Index > pos) list.Add(new(template[pos..m.Index], null, null));
            var fmt = m.Groups[2].Success ? m.Groups[2].Value : null;
            list.Add(new(null, FieldAccessor<TItem>.Create(m.Groups[1].Value), fmt));
            pos = m.Index + m.Length;
        }
        if (pos < template.Length) list.Add(new(template[pos..], null, null));
        return list.ToArray();
    }
}
