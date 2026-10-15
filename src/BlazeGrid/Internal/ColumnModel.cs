using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace BlazeGrid.Internal;

/// <summary>Runtime state of a column (definition + layout + render caches).</summary>
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

    public BzColumn<TItem> Def { get; }
    public string Key { get; }
    public int EngineIndex { get; }
    public bool IsCommand { get; }
    public bool Removed { get; internal set; }
    public FieldAccessor<TItem>? Accessor { get; }
    public FilterKind Kind { get; }
    public bool IsNumeric { get; }
    public bool IsDate { get; }
    public bool IsBool { get; }
    public bool IsEnum { get; }
    public Type? EnumType { get; }

    // layout state
    public double Width { get; internal set; }
    /// <summary>True when width came from the column definition, persisted state or the user.</summary>
    public bool WidthFixed { get; internal set; }
    public ColumnLock Lock { get; internal set; }
    public int Order { get; internal set; }
    public bool Visible { get; internal set; }

    // render caches (rebuilt by the grid on layout change)
    public int DisplayIndex { get; internal set; }
    public string CellCss { get; internal set; } = "bz-cell";
    public string? CellStyle { get; internal set; }
    public string HeaderCss { get; internal set; } = "bz-hcell";
    public string? HeaderStyle { get; internal set; }
    internal TemplateSegment<TItem>[]? Html { get; }

    public string Title => Def.Title ?? Def.Field ?? "";
    public bool HasData => Accessor is not null || Def.DisplayText is not null;
    public bool CanSort => !IsCommand && Def.Sortable && HasData;
    public bool CanFilter => !IsCommand && Def.Filterable && HasData && Kind != FilterKind.None;
    public bool CanGroup => !IsCommand && Def.Groupable && HasData;
    public bool CanReorder => !IsCommand && Def.Reorderable;
    public bool CanResize => Def.Resizable;
    public bool CanEdit => !IsCommand && Def.Editable != false
                           && ((Accessor?.CanWrite ?? false) || (Def.Editable == true && Def.EditTemplate is not null));
    public bool HasMenu => !IsCommand;

    internal void ResetLayout()
    {
        Width = Def.Width ?? (IsCommand ? 72 : 120);
        WidthFixed = Def.Width.HasValue || IsCommand;
        Lock = IsCommand ? ColumnLock.Right : Def.Locked;
        Order = IsCommand ? int.MaxValue : EngineIndex;
        Visible = Def.Visible;
    }

    public string GetText(TItem item)
    {
        if (Def.DisplayText is not null) return Def.DisplayText(item) ?? "";
        return Accessor is null ? "" : TextFormat.Format(Accessor.Get(item), Def.Format, Def.FormatProvider);
    }

    public object? GetValue(TItem item) => Accessor?.Get(item);

    public string RenderHtml(TItem item)
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
