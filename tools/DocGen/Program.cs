// Builds docs/data/api.json, the data behind the API reference site in /docs.
//
//   dotnet run --project tools/DocGen [-- <repoRoot> [<outputFile>]]
//
// Sources of truth:
//   * the BlazeGrid assembly (reflection): members, exact C# types, nullability, defaults (read from a new instance);
//   * BlazeGrid.xml (compiler doc file): summary / remarks / example / param / returns;
//   * #region names in src/BlazeGrid/*.cs: the category of each parameter and property;
//   * src/BlazeGrid/wwwroot/bzgrid.css: the --bz-* variables with their trailing /* description */ comments.
// Members marked [EditorBrowsable(Never)] are plumbing and are left out.

using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using BlazeGrid;
using BlazeGrid.Internal;
using Microsoft.AspNetCore.Components;

var root = args.Length > 0 ? Path.GetFullPath(args[0]) : FindRepoRoot();
var output = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(root, "docs", "data", "api.json");
var srcDir = Path.Combine(root, "src", "BlazeGrid");

var asm = typeof(BzGrid<>).Assembly;
var xmlPath = Path.ChangeExtension(asm.Location, ".xml");
if (!File.Exists(xmlPath)) Fail($"XML doc file not found: {xmlPath} (is GenerateDocumentationFile on?)");

// The navigation tree: every documented type, in reading order.
var groups = new (string Name, Type[] Types)[]
{
    ("Components", new[] { typeof(BzGrid<>), typeof(BzColumn<>) }),
    ("Columns at runtime", new[] { typeof(ColumnModel<>) }),
    ("Query & state", new[] { typeof(ColumnFilter), typeof(SortDescriptor), typeof(GridQuery), typeof(GridState), typeof(ColumnState) }),
    ("Event arguments", new[]
    {
        typeof(RowEventArgs<>), typeof(RowEditStartEventArgs<>), typeof(RowEditCommitEventArgs<>), typeof(CellChange),
        typeof(CellValueChangedEventArgs<>), typeof(FilterChangedEventArgs), typeof(GridRenderStats)
    }),
    ("Template contexts", new[] { typeof(CellEditContext<>), typeof(GridFooterContext), typeof(GroupHeaderContext) }),
    ("Enums", new[]
    {
        typeof(ColumnLock), typeof(ColumnAlign), typeof(FilterKind), typeof(TextOperator), typeof(CompareOperator),
        typeof(SortDirection), typeof(DataEngineMode), typeof(EditTrigger)
    }),
};

// Methods have no #region (they sit among private helpers), so they are grouped here by name.
var methodCategories = new Dictionary<string, string>
{
    ["RefreshAsync"] = "Data",
    ["FindColumn"] = "Columns", ["SetLockAsync"] = "Columns", ["SetVisible"] = "Columns", ["ShowAllColumns"] = "Columns", ["AutoFitAsync"] = "Columns",
    ["SetSortAsync"] = "Sorting & filtering", ["SetFilterAsync"] = "Sorting & filtering", ["ClearFiltersAsync"] = "Sorting & filtering",
    ["CurrentQuery"] = "Sorting & filtering",
    ["GroupByAsync"] = "Grouping", ["UngroupAsync"] = "Grouping", ["ToggleGroupDirectionAsync"] = "Grouping", ["MoveGroupAsync"] = "Grouping",
    ["SetAllGroupsCollapsed"] = "Grouping", ["EffectiveGroupOrder"] = "Grouping", ["SetGroupOrderAsync"] = "Grouping",
    ["SetPageAsync"] = "Paging", ["SetPageSizeAsync"] = "Paging",
    ["BeginEditAsync"] = "Editing", ["CommitEditAsync"] = "Editing", ["CancelEditAsync"] = "Editing",
    ["GetState"] = "State", ["SetStateAsync"] = "State", ["ResetStateAsync"] = "State",
};

var documented = groups.SelectMany(g => g.Types).ToList();
var warnings = new List<string>();

// Any public type in the BlazeGrid namespace that the tree doesn't list is probably new and undocumented.
foreach (var t in asm.GetExportedTypes().Where(t => t.Namespace == "BlazeGrid" && t.Name != "_Imports" && !documented.Contains(t)))
    warnings.Add($"public type {t.Name} is not in the docs navigation");

var xml = XDocument.Load(xmlPath).Descendants("member")
    .ToDictionary(m => (string)m.Attribute("name")!, m => m);
var regions = ScanRegions(srcDir);
var nullability = new NullabilityInfoContext();

// Pass 1: what exists, so cross-references can be resolved to links (or left as code when they point at plumbing).
var known = new HashSet<string>();
var plan = new List<(Type Type, List<MemberInfo> Members)>();
foreach (var t in documented)
{
    var members = PublicMembers(t).ToList();
    plan.Add((t, members));
    known.Add(ShortName(t));
    foreach (var m in members) known.Add($"{ShortName(t)}/{m.Name}");
}

var types = new Dictionary<string, object>();
foreach (var (t, members) in plan)
{
    var typeId = XmlTypeId(t);
    var tdoc = xml.GetValueOrDefault("T:" + typeId);
    if (tdoc is null) warnings.Add($"{ShortName(t)}: no summary");
    object? instance = TryCreate(t);
    var kind = KindOf(t);

    var mlist = new List<object>();
    foreach (var m in members)
    {
        var entry = DescribeMember(t, typeId, m, instance, kind);
        if (entry is not null) mlist.Add(entry);
    }

    types[ShortName(t)] = new
    {
        name = ShortName(t),
        display = DisplayName(t),
        kind,
        @namespace = t.Namespace,
        @base = t.BaseType is { } b && b != typeof(object) && b != typeof(Enum) && b != typeof(ValueType) ? DisplayName(b) : null,
        summary = Render(tdoc?.Element("summary"), t),
        remarks = Render(tdoc?.Element("remarks"), t),
        example = Render(tdoc?.Element("example"), t),
        members = mlist,
    };
}

var css = ScanCssVariables(Path.Combine(srcDir, "wwwroot", "bzgrid.css"));
var version = Regex.Match(File.ReadAllText(Path.Combine(srcDir, "BlazeGrid.csproj")), @"<Version>([^<]+)</Version>").Groups[1].Value;

var api = new
{
    product = "BlazeGrid",
    version,
    generated = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
    groups = groups.Select(g => new { name = g.Name, types = g.Types.Select(ShortName).ToArray() }),
    types,
    cssVariables = css,
};

Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(api, new JsonSerializerOptions
{
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
}));

foreach (var w in warnings) Console.WriteLine("warning: " + w);
Console.WriteLine($"Wrote {output}: {types.Count} types, {plan.Sum(p => p.Members.Count)} members, {css.Count} CSS variables.");
return 0;

// ======================================================================== members

object? DescribeMember(Type t, string typeId, MemberInfo m, object? instance, string typeKind)
{
    switch (m)
    {
        case PropertyInfo p:
        {
            // Positional record properties are documented as <param> tags on the record itself.
            var doc = xml.GetValueOrDefault($"P:{typeId}.{p.Name}");
            if (doc is null &&
                xml.GetValueOrDefault("T:" + typeId)?.Elements("param").FirstOrDefault(e => (string?)e.Attribute("name") == p.Name) is { } rp)
                doc = new XElement("member", new XElement("summary", rp.Nodes()));
            bool isParam = p.GetCustomAttribute<ParameterAttribute>() is not null;
            bool isEvent = isParam && (p.PropertyType == typeof(EventCallback) ||
                                       (p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(EventCallback<>)));
            string kind = isEvent ? "event" : isParam ? "param" : "property";
            if (doc is null) warnings.Add($"{ShortName(t)}.{p.Name}: no summary");
            var setter = p.SetMethod is { IsPublic: true } s ? s : null;
            bool init = setter?.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)) == true;
            return new
            {
                name = p.Name,
                kind,
                category = isEvent ? "Events" : regions.GetValueOrDefault((t.Name.Split('`')[0], p.Name)) ?? (isParam ? "General" : "Properties"),
                type = TypeName(p.PropertyType, nullability.Create(p), AnnotatedTypeParameter(p.PropertyType, p.CustomAttributes, null, t)),
                @default = isEvent ? null : FormatDefault(instance, p),
                access = isParam ? null : setter is null ? "get" : init ? "get; init" : "get; set",
                required = p.GetCustomAttribute<RequiredMemberAttribute>() is not null ? true : (bool?)null,
                summary = Render(doc?.Element("summary"), t),
                remarks = Render(doc?.Element("remarks"), t),
                example = Render(doc?.Element("example"), t),
            };
        }
        case MethodInfo mi:
        {
            var doc = FindMethodDoc(typeId, mi.Name);
            if (doc is null) warnings.Add($"{ShortName(t)}.{mi.Name}(): no summary");
            var pars = mi.GetParameters();
            var ret = TypeName(mi.ReturnType, nullability.Create(mi.ReturnParameter), AnnotatedTypeParameter(mi.ReturnType, mi.ReturnParameter.CustomAttributes, mi, t));
            string ParamType(ParameterInfo pp) => TypeName(pp.ParameterType, nullability.Create(pp), AnnotatedTypeParameter(pp.ParameterType, pp.CustomAttributes, mi, t));
            var sig = $"{ret} {mi.Name}(" +
                      string.Join(", ", pars.Select(pp => $"{ParamType(pp)} {pp.Name}" +
                                                         (pp.HasDefaultValue ? " = " + Literal(pp.DefaultValue) : ""))) + ")";
            return new
            {
                name = mi.Name,
                kind = "method",
                category = methodCategories.GetValueOrDefault(mi.Name) ?? "Methods",
                type = ret,
                signature = sig,
                summary = Render(doc?.Element("summary"), t),
                remarks = Render(doc?.Element("remarks"), t),
                example = Render(doc?.Element("example"), t),
                @params = pars.Length == 0 ? null : pars.Select(pp => new
                {
                    name = pp.Name,
                    type = ParamType(pp),
                    @default = pp.HasDefaultValue ? Literal(pp.DefaultValue) : null,
                    summary = Render(doc?.Elements("param").FirstOrDefault(e => (string?)e.Attribute("name") == pp.Name), t),
                }).ToArray(),
                returns = Render(doc?.Element("returns"), t),
            };
        }
        case FieldInfo f:
        {
            var doc = xml.GetValueOrDefault($"F:{typeId}.{f.Name}");
            if (doc is null) warnings.Add($"{ShortName(t)}.{f.Name}: no summary");
            return new
            {
                name = f.Name,
                kind = "value",
                category = "Values",
                type = ShortName(t),
                @default = Convert.ToInt64(f.GetRawConstantValue(), CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
                summary = Render(doc?.Element("summary"), t),
            };
        }
    }
    return null;
}

IEnumerable<MemberInfo> PublicMembers(Type t)
{
    const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
    static bool Hidden(MemberInfo m) =>
        m.GetCustomAttribute<EditorBrowsableAttribute>()?.State == EditorBrowsableState.Never ||
        m.GetCustomAttribute<CompilerGeneratedAttribute>() is not null;

    if (t.IsEnum)
    {
        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static)) yield return f;
        yield break;
    }

    // Declaration order (MetadataToken) keeps the docs in the same order as the source.
    foreach (var p in t.GetProperties(flags).Where(p => !Hidden(p) && p.Name != "EqualityContract").OrderBy(p => p.MetadataToken))
        yield return p;

    var skip = new HashSet<string> { "Dispose", "DisposeAsync", "ToString", "Equals", "GetHashCode", "Deconstruct", "SetParametersAsync" };
    foreach (var mi in t.GetMethods(flags).Where(mi => !mi.IsSpecialName && !Hidden(mi) && !skip.Contains(mi.Name) && !mi.Name.StartsWith('<'))
                 .OrderBy(mi => mi.MetadataToken))
        yield return mi;
}

XElement? FindMethodDoc(string typeId, string name) =>
    xml.GetValueOrDefault($"M:{typeId}.{name}") ??
    xml.FirstOrDefault(kv => kv.Key.StartsWith($"M:{typeId}.{name}(", StringComparison.Ordinal)).Value;

string KindOf(Type t) =>
    t.IsEnum ? "enum"
    : typeof(ComponentBase).IsAssignableFrom(t) ? "component"
    : t.GetMethod("<Clone>$") is not null ? "record"
    : "class";

object? TryCreate(Type t)
{
    if (t.IsEnum || t.IsAbstract) return null;
    var closed = t.IsGenericTypeDefinition ? t.MakeGenericType(t.GetGenericArguments().Select(_ => typeof(object)).ToArray()) : t;
    if (closed.GetConstructor(Type.EmptyTypes) is null) return null;
    try { return Activator.CreateInstance(closed); } catch { return null; }
}

string? FormatDefault(object? instance, PropertyInfo p)
{
    if (instance is null || p.GetIndexParameters().Length > 0) return null;
    var prop = instance.GetType().GetProperty(p.Name);
    object? v;
    try { v = prop?.GetValue(instance); } catch { return null; }
    var core = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
    bool simple = core.IsPrimitive || core.IsEnum || core == typeof(string) || core == typeof(decimal) || core.IsArray;
    if (!simple) return null;               // collections, delegates, fragments: "new()" or null says nothing useful
    if (v is null) return "null";
    return Literal(v);
}

static string Literal(object? v) => v switch
{
    null => "null",
    string s => "\"" + s + "\"",
    bool b => b ? "true" : "false",
    Enum e => $"{e.GetType().Name}.{e}",
    Array a => "{ " + string.Join(", ", a.Cast<object?>().Select(Literal)) + " }",
    IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
    _ => v.ToString() ?? ""
};

// ======================================================================== type names

static string ShortName(Type t) => t.Name.Split('`')[0];

static string XmlTypeId(Type t) => (t.IsGenericType ? t.GetGenericTypeDefinition() : t).FullName!.Replace('+', '.');

static string DisplayName(Type t) =>
    t.IsGenericType ? $"{ShortName(t)}<{string.Join(", ", t.GetGenericArguments().Select(a => a.Name))}>" : t.Name;

// True when a member whose type is a bare type parameter is declared "T?": its own [Nullable] byte is 2, or it has
// none and the nearest [NullableContext] (method, then type) is 2.
static bool AnnotatedTypeParameter(Type type, IEnumerable<CustomAttributeData> own, MemberInfo? method, Type declaring)
{
    if (!type.IsGenericParameter) return false;
    static byte? Flag(IEnumerable<CustomAttributeData> attrs, string name) =>
        attrs.FirstOrDefault(a => a.AttributeType.Name == name)?.ConstructorArguments.FirstOrDefault().Value switch
        {
            byte b => b,
            IReadOnlyCollection<CustomAttributeTypedArgument> arr => (byte?)arr.First().Value,
            _ => null
        };
    var flag = Flag(own, "NullableAttribute")
               ?? (method is null ? null : Flag(method.CustomAttributes, "NullableContextAttribute"))
               ?? Flag(declaring.CustomAttributes, "NullableContextAttribute");
    return flag == 2;
}

static string Alias(Type t) => t switch
{
    _ when t == typeof(int) => "int",
    _ when t == typeof(long) => "long",
    _ when t == typeof(double) => "double",
    _ when t == typeof(decimal) => "decimal",
    _ when t == typeof(bool) => "bool",
    _ when t == typeof(string) => "string",
    _ when t == typeof(object) => "object",
    _ when t == typeof(void) => "void",
    _ => ShortName(t)
};

// NullabilityInfoContext reports unconstrained type parameters (TItem) as nullable everywhere, so a type parameter
// only gets "?" when it is the member's whole type and its raw [Nullable] flag says annotated (TItem? SelectedItem).
static string TypeName(Type t, NullabilityInfo? n, bool annotatedTypeParameter = false)
{
    if (Nullable.GetUnderlyingType(t) is { } u) return TypeName(u, null) + "?";
    if (t.IsGenericParameter) return annotatedTypeParameter ? t.Name + "?" : t.Name;
    string name;
    if (t.IsArray) name = TypeName(t.GetElementType()!, n?.ElementType) + "[]";
    else if (t.IsGenericType)
    {
        var args = t.GetGenericArguments();
        name = $"{ShortName(t)}<{string.Join(", ", args.Select((a, i) => TypeName(a, n?.GenericTypeArguments.ElementAtOrDefault(i))))}>";
    }
    else name = Alias(t);
    bool nullableRef = !t.IsValueType && n?.ReadState == NullabilityState.Nullable;
    return nullableRef ? name + "?" : name;
}

// ======================================================================== XML doc → HTML

string? Render(XElement? e, Type context)
{
    if (e is null) return null;
    var sb = new StringBuilder();
    RenderNodes(e.Nodes(), sb, context);
    var html = sb.ToString().Trim();
    return html.Length == 0 ? null : html;
}

void RenderNodes(IEnumerable<XNode> nodes, StringBuilder sb, Type context)
{
    foreach (var node in nodes)
    {
        switch (node)
        {
            case XText text:
                sb.Append(Html(Regex.Replace(text.Value, @"\s+", " ")));
                break;
            case XElement el:
                switch (el.Name.LocalName)
                {
                    case "c":
                        sb.Append("<code>").Append(Html(el.Value)).Append("</code>");
                        break;
                    case "code":
                        var code = Dedent(el.Value);
                        var lang = Regex.IsMatch(code, @"^\s*<|=\s*""@|&lt;") ? "razor" : "csharp";
                        sb.Append($"<pre><code class=\"language-{lang}\">").Append(Html(code)).Append("</code></pre>");
                        break;
                    case "para":
                        sb.Append("<p>");
                        RenderNodes(el.Nodes(), sb, context);
                        sb.Append("</p>");
                        break;
                    case "paramref":
                    case "typeparamref":
                        sb.Append("<code>").Append(Html((string?)el.Attribute("name") ?? "")).Append("</code>");
                        break;
                    case "see":
                        if (el.Attribute("langword") is { } lw) sb.Append("<code>").Append(Html(lw.Value)).Append("</code>");
                        else sb.Append(CrefLink((string?)el.Attribute("cref") ?? "", el.Value, context));
                        break;
                    default:
                        RenderNodes(el.Nodes(), sb, context);
                        break;
                }
                break;
        }
    }
}

// "P:BlazeGrid.BzGrid`1.Items" → <a href="#/api/BzGrid/Items"><code>Items</code></a>
string CrefLink(string cref, string text, Type context)
{
    var m = Regex.Match(cref, @"^(?<k>[TPMFE]):(?<path>[^(]+)");
    if (!m.Success) return $"<code>{Html(text.Length > 0 ? text : cref)}</code>";
    var parts = m.Groups["path"].Value.Split('.');
    string type, member = "";
    if (m.Groups["k"].Value == "T") type = parts[^1].Split('`')[0];
    else { type = parts[^2].Split('`')[0]; member = parts[^1]; }
    var label = text.Length > 0 ? text
        : member.Length == 0 ? type
        : type == ShortName(context) ? member : $"{type}.{member}";
    var key = member.Length == 0 ? type : $"{type}/{member}";
    return known.Contains(key)
        ? $"<a href=\"#/api/{key}\"><code>{Html(label)}</code></a>"
        : $"<code>{Html(label)}</code>";
}

static string Dedent(string code)
{
    var lines = code.Replace("\r", "").Split('\n').ToList();
    while (lines.Count > 0 && lines[0].Trim().Length == 0) lines.RemoveAt(0);
    while (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
    int indent = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
    return string.Join("\n", lines.Select(l => l.Length >= indent ? l[indent..] : l.TrimStart()));
}

static string Html(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

// ======================================================================== source scanning

// (class, member) → enclosing #region name.
static Dictionary<(string, string), string> ScanRegions(string dir)
{
    var map = new Dictionary<(string, string), string>();
    var typeDecl = new Regex(@"\b(?:class|record|struct|enum)\s+(\w+)");
    var memberDecl = new Regex(@"^\s*(?:\[[^\]]*\]\s*)*public\s+[^=(]*?\b(\w+)\s*(?:\{|=>|\()");
    foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
                 .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                             !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
    {
        string currentType = "";
        var stack = new Stack<string>();
        foreach (var line in File.ReadLines(file))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("#region")) { stack.Push(trimmed["#region".Length..].Trim()); continue; }
            if (trimmed.StartsWith("#endregion")) { if (stack.Count > 0) stack.Pop(); continue; }
            if (trimmed.StartsWith("//")) continue;
            var td = typeDecl.Match(line);
            if (td.Success && line.TrimStart().StartsWith("public")) currentType = td.Groups[1].Value;
            if (stack.Count == 0) continue;
            var md = memberDecl.Match(line);
            if (md.Success) map[(currentType, md.Groups[1].Value)] = stack.Peek();
        }
    }
    return map;
}

// The --bz-* declarations of the .bz-grid block, with their /* trailing comments */.
static List<object> ScanCssVariables(string path)
{
    var css = File.ReadAllText(path);
    var block = Regex.Match(css, @"\.bz-grid\s*\{(?<body>.*?)\n\s*\n", RegexOptions.Singleline).Groups["body"].Value;
    var dark = Regex.Match(css, @"\.bz-theme-dark\s*\{(?<body>.*?)\}", RegexOptions.Singleline).Groups["body"].Value;
    var darkValues = Regex.Matches(dark, @"(--bz-[\w-]+):\s*([^;]+);").ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim());
    return Regex.Matches(block, @"(--bz-[\w-]+):\s*([^;]+);\s*(?:/\*\s*(.*?)\s*\*/)?")
        .Select(m => (object)new
        {
            name = m.Groups[1].Value,
            value = m.Groups[2].Value.Trim(),
            dark = darkValues.GetValueOrDefault(m.Groups[1].Value),
            description = m.Groups[3].Success ? m.Groups[3].Value : null,
        }).ToList();
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "BlazeGrid"))) dir = dir.Parent;
    return dir?.FullName ?? throw new InvalidOperationException("Run from inside the repository or pass the repo root.");
}

static void Fail(string message)
{
    Console.Error.WriteLine(message);
    Environment.Exit(1);
}
