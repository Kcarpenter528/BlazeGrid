using Microsoft.AspNetCore.Components;

namespace BlazeGrid;

/// <summary>Declares a column of a <see cref="BzGrid{TItem}"/>. Renders nothing itself.</summary>
public sealed class BzColumn<TItem> : ComponentBase, IDisposable
{
    [CascadingParameter] public BzGrid<TItem>? Grid { get; set; }

    /// <summary>Property name (nested paths like "Customer.Name" are supported).</summary>
    [Parameter] public string? Field { get; set; }
    [Parameter] public string? Title { get; set; }

    /// <summary>Initial width in px. Null = measured automatically from the data.</summary>
    [Parameter] public double? Width { get; set; }
    [Parameter] public double MinWidth { get; set; } = 48;
    [Parameter] public double MaxAutoWidth { get; set; } = 420;

    [Parameter] public ColumnLock Locked { get; set; }
    [Parameter] public bool Visible { get; set; } = true;
    [Parameter] public ColumnAlign Align { get; set; }

    [Parameter] public FilterKind Filter { get; set; } = FilterKind.Auto;
    [Parameter] public bool Sortable { get; set; } = true;
    [Parameter] public bool Filterable { get; set; } = true;
    [Parameter] public bool Groupable { get; set; } = true;
    [Parameter] public bool Resizable { get; set; } = true;
    [Parameter] public bool Reorderable { get; set; } = true;
    /// <summary>Null = editable when the property has a public setter.</summary>
    [Parameter] public bool? Editable { get; set; }

    /// <summary>.NET format string applied to the value (e.g. "C2", "yyyy-MM-dd").</summary>
    [Parameter] public string? Format { get; set; }
    [Parameter] public IFormatProvider? FormatProvider { get; set; }
    /// <summary>Custom display text. Also used for text filtering, multi-select values and grouping.</summary>
    [Parameter] public Func<TItem, string?>? DisplayText { get; set; }

    /// <summary>
    /// Fixed order for group values (display text). Values not listed follow in natural order.
    /// A user-defined order from the column menu overrides this.
    /// </summary>
    [Parameter] public IReadOnlyList<string>? GroupOrder { get; set; }

    [Parameter] public string? CellClass { get; set; }
    [Parameter] public Func<TItem, string?>? CellClassSelector { get; set; }
    [Parameter] public string? HeaderClass { get; set; }

    /// <summary>Component template for the cell.</summary>
    [Parameter] public RenderFragment<TItem>? Template { get; set; }
    /// <summary>HTML template with {{Field}} or {{Field:format}} placeholders (values are HTML-encoded).</summary>
    [Parameter] public string? HtmlTemplate { get; set; }
    /// <summary>Custom editor used while the row is in edit mode.</summary>
    [Parameter] public RenderFragment<CellEditContext<TItem>>? EditTemplate { get; set; }

    protected override void OnInitialized()
    {
        if (Grid is null) throw new InvalidOperationException($"{nameof(BzColumn<TItem>)} must be placed inside a BzGrid.");
        Grid.AddColumn(this);
    }

    protected override bool ShouldRender() => false;

    public void Dispose() => Grid?.RemoveColumn(this);
}
