using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using TDU2SaveEditor.Core;

namespace TDU2SaveEditor.ViewModels;

public abstract class ObservableBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>A row representing one field of an XObject.</summary>
public sealed class FieldRow : ObservableBase
{
    private readonly Action _onChanged;
    private readonly XValue _node;

    public FieldRow(string name, XValue node, Action onChanged)
    {
        Name = name;
        _node = node;
        _onChanged = onChanged;
    }

    public string Name { get; }
    public XValue Node => _node;
    public string TypeName => _node.TypeName;
    public bool IsEditable => _node is XPrim && _node.IsEditable;
    public bool IsContainer => _node is XObject || _node is XArray;

    public string ValueText
    {
        get => _node.Display;
        set
        {
            if (_node is not XPrim prim) return;
            if (TrySetPrim(prim, value))
            {
                Raise();
                _onChanged();
            }
            else
            {
                Raise();
            }
        }
    }

    internal static bool TrySetPrim(XPrim prim, string text)
    {
        try
        {
            switch (prim.Type)
            {
                case XType.Bool:
                    text = text.Trim().ToLowerInvariant();
                    if (text is "true" or "1" or "yes" or "ja") { prim.Value = true; return true; }
                    if (text is "false" or "0" or "no" or "nein" or "") { prim.Value = false; return true; }
                    return false;
                case XType.SInt8:
                case XType.SInt16:
                case XType.SInt32:
                case XType.SInt64:
                    prim.Value = long.Parse(text.Trim(), CultureInfo.InvariantCulture);
                    return true;
                case XType.UInt8:
                case XType.UInt16:
                case XType.UInt32:
                case XType.UInt64:
                    prim.Value = ulong.Parse(text.Trim(), CultureInfo.InvariantCulture);
                    return true;
                case XType.Float:
                    prim.Value = float.Parse(text.Trim(), CultureInfo.InvariantCulture);
                    return true;
                case XType.Double:
                    prim.Value = double.Parse(text.Trim(), CultureInfo.InvariantCulture);
                    return true;
                case XType.String:
                    prim.Value = text;
                    return true;
                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>A row representing one element of an XArray.</summary>
public sealed class ItemRow : ObservableBase
{
    private readonly Action _onChanged;
    private readonly XValue _node;

    public ItemRow(int index, XValue node, Action onChanged)
    {
        Index = index;
        _node = node;
        _onChanged = onChanged;
    }

    public int Index { get; }
    public string Name => $"[{Index}]";
    public XValue Node => _node;
    public string TypeName => _node.TypeName;
    public bool IsEditable => _node is XPrim && _node.IsEditable;
    public bool IsContainer => _node is XObject || _node is XArray;

    public string ValueText
    {
        get => _node.Display;
        set
        {
            if (_node is not XPrim prim) return;
            if (FieldRow.TrySetPrim(prim, value))
            {
                Raise();
                _onChanged();
            }
            else
            {
                Raise();
            }
        }
    }
}

public sealed class TreeItem : ObservableBase
{
    private bool _isExpanded;

    public TreeItem(string name, XValue node)
    {
        Name = name;
        Node = node;
        Children = new ObservableCollection<TreeItem>();
        Build();
    }

    public string Name { get; }
    public XValue Node { get; }
    public ObservableCollection<TreeItem> Children { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded != value) { _isExpanded = value; Raise(); } }
    }

    public string Label => $"{Name}   {Node.TypeName}";

    private const int MaxArrayChildren = 1024;

    private void Build()
    {
        if (Node is XObject obj)
        {
            foreach (var kv in obj.Items)
            {
                if (kv.Value is XObject || kv.Value is XArray)
                    Children.Add(new TreeItem(kv.Key, kv.Value));
            }
        }
        else if (Node is XArray arr && arr.ElementType == XType.Object && arr.Count <= MaxArrayChildren)
        {
            for (int i = 0; i < arr.Count; i++)
                Children.Add(new TreeItem($"[{i}]", arr.Items[i]));
        }
    }
}
