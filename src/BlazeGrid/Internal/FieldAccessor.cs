using System.Globalization;
using System.Reflection;

namespace BlazeGrid.Internal;

/// <summary>
/// Reads/writes one field of TItem. Direct properties use open-instance delegates
/// (Delegate.CreateDelegate) because Expression.Compile is interpreted on WebAssembly.
/// </summary>
public abstract class FieldAccessor<TItem>
{
    public Type ValueType { get; protected init; } = typeof(object);
    /// <summary>ValueType with Nullable&lt;&gt; stripped.</summary>
    public Type CoreType { get; protected init; } = typeof(object);
    public bool CanWrite { get; protected init; }

    public bool IsNumeric => NumberConv.IsNumeric(CoreType);
    public bool IsDate => CoreType == typeof(DateTime) || CoreType == typeof(DateTimeOffset) || CoreType == typeof(DateOnly);

    public abstract object? Get(TItem item);
    public abstract void Set(TItem item, object? value);
    public abstract double GetNumber(TItem item);

    public virtual void FillNumbers(IReadOnlyList<TItem> items, double[] dest)
    {
        for (int i = 0; i < dest.Length; i++) dest[i] = GetNumber(items[i]);
    }

    public static FieldAccessor<TItem>? Create(string field)
    {
        var parts = field.Split('.');
        var props = new PropertyInfo[parts.Length];
        var t = typeof(TItem);
        for (int i = 0; i < parts.Length; i++)
        {
            var p = t.GetProperty(parts[i], BindingFlags.Public | BindingFlags.Instance);
            if (p is null || p.GetGetMethod() is null) return null;
            props[i] = p;
            t = p.PropertyType;
        }

        if (props.Length == 1 && !typeof(TItem).IsValueType)
        {
            var type = typeof(TypedAccessor<,>).MakeGenericType(typeof(TItem), props[0].PropertyType);
            return (FieldAccessor<TItem>)Activator.CreateInstance(type, props[0])!;
        }
        return new ReflectionAccessor<TItem>(props);
    }
}

internal sealed class TypedAccessor<TItem, TValue> : FieldAccessor<TItem>
{
    private readonly Func<TItem, TValue> _get;
    private readonly Action<TItem, TValue>? _set;
    private readonly Func<TValue, double>? _num;

    public TypedAccessor(PropertyInfo p)
    {
        _get = (Func<TItem, TValue>)Delegate.CreateDelegate(typeof(Func<TItem, TValue>), p.GetGetMethod()!);
        var sm = p.GetSetMethod();
        if (sm is not null) _set = (Action<TItem, TValue>)Delegate.CreateDelegate(typeof(Action<TItem, TValue>), sm);
        ValueType = typeof(TValue);
        CoreType = Nullable.GetUnderlyingType(typeof(TValue)) ?? typeof(TValue);
        CanWrite = _set is not null;
        _num = NumberConv.For<TValue>();
    }

    public override object? Get(TItem item) => _get(item);

    public override void Set(TItem item, object? value) => _set!(item, (TValue)ValueConvert.To(value, typeof(TValue))!);

    public override double GetNumber(TItem item) => _num is null ? double.NaN : _num(_get(item));

    public override void FillNumbers(IReadOnlyList<TItem> items, double[] dest)
    {
        var get = _get;
        var num = _num;
        if (num is null) { Array.Fill(dest, double.NaN); return; }
        if (items is TItem[] arr)
        {
            for (int i = 0; i < dest.Length; i++) dest[i] = num(get(arr[i]));
        }
        else if (items is List<TItem> list)
        {
            for (int i = 0; i < dest.Length; i++) dest[i] = num(get(list[i]));
        }
        else
        {
            for (int i = 0; i < dest.Length; i++) dest[i] = num(get(items[i]));
        }
    }
}

internal sealed class ReflectionAccessor<TItem> : FieldAccessor<TItem>
{
    private readonly PropertyInfo[] _path;

    public ReflectionAccessor(PropertyInfo[] path)
    {
        _path = path;
        var last = path[^1];
        ValueType = last.PropertyType;
        CoreType = Nullable.GetUnderlyingType(ValueType) ?? ValueType;
        CanWrite = last.GetSetMethod() is not null && (path.Length == 1 || !path[^2].PropertyType.IsValueType);
    }

    public override object? Get(TItem item)
    {
        object? cur = item;
        foreach (var p in _path)
        {
            if (cur is null) return null;
            cur = p.GetValue(cur);
        }
        return cur;
    }

    public override void Set(TItem item, object? value)
    {
        object? target = item;
        for (int i = 0; i < _path.Length - 1 && target is not null; i++) target = _path[i].GetValue(target);
        if (target is null) return;
        _path[^1].SetValue(target, ValueConvert.To(value, ValueType));
    }

    public override double GetNumber(TItem item) => NumberConv.FromObject(Get(item));
}

/// <summary>Maps numeric / date / bool values onto doubles (dates as milliseconds since 0001-01-01).</summary>
public static class NumberConv
{
    private static readonly Dictionary<Type, Delegate> Map = new();

    static NumberConv()
    {
        Add<int>(x => x); Add<long>(x => x); Add<short>(x => x); Add<byte>(x => x); Add<sbyte>(x => x);
        Add<uint>(x => x); Add<ulong>(x => x); Add<ushort>(x => x);
        Add<float>(x => x); Add<double>(x => x); Add<decimal>(x => (double)x);
        Add<DateTime>(DateToNumber);
        Add<DateTimeOffset>(x => DateToNumber(x.DateTime));
        Add<DateOnly>(x => x.DayNumber * 86_400_000d);
        Add<bool>(x => x ? 1 : 0);
    }

    private static void Add<T>(Func<T, double> f) where T : struct
    {
        Map[typeof(T)] = f;
        Map[typeof(T?)] = (Func<T?, double>)(x => x.HasValue ? f(x.Value) : double.NaN);
    }

    public static double DateToNumber(DateTime d) => d.Ticks / 10_000d;

    public static bool IsNumeric(Type core) => Map.ContainsKey(core);

    public static Func<T, double>? For<T>() => Map.TryGetValue(typeof(T), out var d) ? (Func<T, double>)d : null;

    public static double FromObject(object? v) => v switch
    {
        null => double.NaN,
        double d => d,
        int i => i,
        long l => l,
        float f => f,
        decimal m => (double)m,
        short s => s,
        byte b => b,
        DateTime dt => DateToNumber(dt),
        DateTimeOffset dto => DateToNumber(dto.DateTime),
        DateOnly d => d.DayNumber * 86_400_000d,
        bool b => b ? 1 : 0,
        IConvertible c when IsNumeric(v.GetType()) => c.ToDouble(CultureInfo.InvariantCulture),
        _ => double.NaN
    };
}

public static class ValueConvert
{
    public static object? To(object? v, Type target)
    {
        var core = Nullable.GetUnderlyingType(target);
        bool nullable = core is not null || !target.IsValueType;
        core ??= target;

        if (v is null || (v is string s0 && s0.Length == 0 && core != typeof(string)))
            return nullable ? null : Activator.CreateInstance(target);

        if (core.IsInstanceOfType(v)) return v;

        if (v is string s)
        {
            var inv = CultureInfo.InvariantCulture;
            if (core.IsEnum) return Enum.Parse(core, s, ignoreCase: true);
            if (core == typeof(DateOnly)) return DateOnly.Parse(s, inv);
            if (core == typeof(DateTime)) return DateTime.Parse(s, inv);
            if (core == typeof(DateTimeOffset)) return DateTimeOffset.Parse(s, inv);
            if (core == typeof(Guid)) return Guid.Parse(s);
            if (core == typeof(bool)) return bool.Parse(s);
            if (NumberConv.IsNumeric(core))
                return Convert.ChangeType(decimal.Parse(s, NumberStyles.Float, inv), core, inv);
            return Convert.ChangeType(s, core, inv);
        }

        if (core == typeof(DateOnly) && v is DateTime dt) return DateOnly.FromDateTime(dt);
        if (core == typeof(DateTime) && v is DateOnly d) return d.ToDateTime(TimeOnly.MinValue);
        if (core == typeof(DateTimeOffset) && v is DateTime dt2) return new DateTimeOffset(dt2);
        if (core.IsEnum) return Enum.ToObject(core, v);
        if (core == typeof(string)) return Convert.ToString(v, CultureInfo.CurrentCulture);
        return Convert.ChangeType(v, core, CultureInfo.InvariantCulture);
    }
}

public static class TextFormat
{
    public static string Format(object? v, string? format, IFormatProvider? provider)
    {
        provider ??= CultureInfo.CurrentCulture;
        switch (v)
        {
            case null: return "";
            case string s: return s;
            case DateTime d when format is null:
                return d.TimeOfDay == TimeSpan.Zero ? d.ToString("d", provider) : d.ToString("g", provider);
            case DateTimeOffset dto when format is null:
                return dto.TimeOfDay == TimeSpan.Zero ? dto.ToString("d", provider) : dto.ToString("g", provider);
            case IFormattable f: return f.ToString(format, provider);
            default: return v.ToString() ?? "";
        }
    }
}
