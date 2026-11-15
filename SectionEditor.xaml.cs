using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TDU2SaveEditor.Core;
using TDU2SaveEditor.Services;

namespace TDU2SaveEditor;

/// <summary>
/// Renders one or more savegame sections as clean, labeled forms instead of a
/// raw tree/grid. Primitives become labelled editors, nested objects become
/// group boxes and nested arrays become an entry selector plus the form of the
/// currently selected entry.
/// </summary>
public partial class SectionEditor : UserControl
{
    private const double LabelWidth = 190;
    private const double FieldWidth = 400;
    private const int InlineArrayLimit = 128;

    private Action _onChanged = () => { };

    public SectionEditor()
    {
        InitializeComponent();
    }

    public void Load(Action onChanged, params (string label, XValue? root)[] roots)
    {
        _onChanged = onChanged ?? (() => { });
        Host.Children.Clear();

        int shown = 0;
        foreach (var (label, root) in roots)
        {
            if (root == null) continue;
            Host.Children.Add(BuildSection(label, root));
            shown++;
        }

        if (shown == 0)
            Host.Children.Add(Hint(Loc.T("none_loaded")));
    }

    public void Clear() => Load(_onChanged);

    // =====================================================================
    // Section / node rendering
    // =====================================================================

    private FrameworkElement BuildSection(string label, XValue node) => node switch
    {
        XPrim prim => Group(label, Wrap(FieldBox(label, prim))),
        XObject obj => Grouped(label, obj),
        XArray arr => Arrayed(label, arr),
        _ => Hint(label)
    };

    private FrameworkElement Grouped(string label, XObject obj)
    {
        var stack = new StackPanel();
        var wrap = new WrapPanel();
        foreach (var kv in obj.Items)
            if (kv.Value is XPrim prim)
                wrap.Children.Add(FieldBox(kv.Key, prim));
        if (wrap.Children.Count > 0)
            stack.Children.Add(wrap);

        foreach (var kv in obj.Items)
        {
            if (kv.Value is XObject o)
                stack.Children.Add(Grouped(kv.Key, o));
            else if (kv.Value is XArray a)
                stack.Children.Add(Arrayed(kv.Key, a));
        }

        if (stack.Children.Count == 0)
            stack.Children.Add(Hint(Loc.T("array.empty")));
        return Group(label, stack);
    }

    private FrameworkElement Arrayed(string label, XArray arr)
    {
        if (arr.Count == 0)
            return Group(label, Hint(Loc.T("array.empty")));

        if (arr.ElementType == XType.Object || arr.Items.Any(v => v is XObject))
            return ObjectArray(label, arr);

        if (arr.Count <= InlineArrayLimit)
            return PrimitiveArrayInline(label, arr);

        return PrimitiveArrayText(label, arr);
    }

    private FrameworkElement ObjectArray(string label, XArray arr)
    {
        var combo = new ComboBox { MinWidth = 320, HorizontalAlignment = HorizontalAlignment.Left };
        var host = new ContentControl { Margin = new Thickness(0, 6, 0, 0) };

        for (int i = 0; i < arr.Count; i++)
            combo.Items.Add(EntryTitle(arr.Items[i], i));

        void Show(int index)
        {
            if (index < 0 || index >= arr.Count) { host.Content = null; return; }
            host.Content = arr.Items[index] switch
            {
                XObject o => Grouped($"[{index}]", o),
                XArray a => Arrayed($"[{index}]", a),
                XPrim p => Wrap(FieldBox($"[{index}]", p)),
                _ => null
            };
        }

        combo.SelectionChanged += (_, _) => Show(combo.SelectedIndex);
        combo.SelectedIndex = 0;

        var stack = new StackPanel();
        stack.Children.Add(combo);
        stack.Children.Add(host);
        return Group($"{label}  ({arr.Count})", stack);
    }

    private FrameworkElement PrimitiveArrayInline(string label, XArray arr)
    {
        var wrap = new WrapPanel();
        for (int i = 0; i < arr.Count; i++)
            if (arr.Items[i] is XPrim p)
                wrap.Children.Add(FieldBox($"[{i}]", p));
        return Group($"{label}  ({arr.Count})", wrap);
    }

    private FrameworkElement PrimitiveArrayText(string label, XArray arr)
    {
        var box = new TextBox
        {
            Text = string.Join(", ", arr.Items.Select(v => v.Display)),
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            MinHeight = 60,
            MaxHeight = 220,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        box.LostFocus += (_, _) =>
        {
            ApplyPrimitiveArray(arr, box.Text);
            box.Text = string.Join(", ", arr.Items.Select(v => v.Display));
            _onChanged();
        };

        var stack = new StackPanel();
        stack.Children.Add(Hint(Loc.T("array.hint")));
        box.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(box);
        return Group($"{label}  ({arr.Count})", stack);
    }

    private static void ApplyPrimitiveArray(XArray arr, string text)
    {
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        int count = Math.Min(arr.Count, parts.Length);
        for (int i = 0; i < count; i++)
        {
            if (arr.Items[i] is not XPrim p) continue;
            TrySetPrim(p, parts[i]);
        }
    }

    // =====================================================================
    // Field editors
    // =====================================================================

    private FrameworkElement FieldBox(string label, XPrim prim)
    {
        var grid = new Grid { Width = FieldWidth, Margin = new Thickness(0, 2, 14, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var lbl = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = label,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(lbl, 0);
        grid.Children.Add(lbl);

        FrameworkElement editor;
        if (prim.Type == XType.Bool)
        {
            var cb = new CheckBox
            {
                IsChecked = prim.Value is bool b && b,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            cb.Checked += (_, _) => { prim.Value = true; _onChanged(); };
            cb.Unchecked += (_, _) => { prim.Value = false; _onChanged(); };
            editor = cb;
        }
        else
        {
            var tb = new TextBox { Text = prim.Display };
            tb.LostFocus += (_, _) =>
            {
                if (TrySetPrim(prim, tb.Text))
                    _onChanged();
                tb.Text = prim.Display;
            };
            editor = tb;
        }

        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        return grid;
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private static bool TrySetPrim(XPrim prim, string text)
    {
        try
        {
            switch (prim.Type)
            {
                case XType.Bool:
                    if (bool.TryParse(text.Trim(), out var b)) { prim.Value = b; return true; }
                    return false;
                case XType.Float:
                    if (float.TryParse(text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f)) { prim.Value = f; return true; }
                    return false;
                case XType.Double:
                    if (double.TryParse(text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) { prim.Value = d; return true; }
                    return false;
                case XType.String:
                    prim.Value = text;
                    return true;
                case XType.UInt8 or XType.UInt16 or XType.UInt32 or XType.UInt64:
                    if (ulong.TryParse(text.Trim(), out var u)) { prim.Value = u; return true; }
                    return false;
                default:
                    if (long.TryParse(text.Trim(), out var v)) { prim.Value = v; return true; }
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static string EntryTitle(XValue item, int index)
    {
        if (item is XObject o)
        {
            foreach (var key in new[] { "Name", "Title", "Hashcode", "ID", "Id", "UId" })
                if (o[key] is XPrim p && !string.IsNullOrWhiteSpace(p.Display) && p.Display != "0" && p.Display != "##")
                    return $"[{index}]  {p.Display}";
        }
        return $"[{index}]  {item.TypeName}";
    }

    private static WrapPanel Wrap(FrameworkElement child)
    {
        var w = new WrapPanel();
        w.Children.Add(child);
        return w;
    }

    private GroupBox Group(string label, UIElement content) => new()
    {
        Header = label,
        Content = content,
        Margin = new Thickness(0, 2, 0, 6)
    };

    private TextBlock Hint(string text) => new()
    {
        Text = text,
        Foreground = (Brush)FindResource("FgDim"),
        Margin = new Thickness(0, 2, 0, 2)
    };
}
