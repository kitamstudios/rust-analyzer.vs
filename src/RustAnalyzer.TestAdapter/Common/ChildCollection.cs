using System.Collections.ObjectModel;

namespace KS.RustAnalyzer.TestAdapter.Common;

/// <summary>
/// From https://stackoverflow.com/a/46837877/6196679.
/// </summary>
/// <typeparam name="TParent">Type of the parent class.</typeparam>
public interface IHasParent<TParent>
    where TParent : class
{
    TParent Parent { get; }

    void OnParentChanging(TParent newParent);
}

public class ChildCollection<TParent, TChild> : Collection<TChild>
    where TChild : IHasParent<TParent>
    where TParent : class
{
    private readonly TParent _parent;

    public ChildCollection(TParent parent)
    {
        _parent = parent;
    }

    protected override void ClearItems()
    {
        foreach (var item in this)
        {
            item?.OnParentChanging(null);
        }

        base.ClearItems();
    }

    protected override void InsertItem(int index, TChild item)
    {
        item?.OnParentChanging(_parent);

        base.InsertItem(index, item);
    }

    protected override void RemoveItem(int index)
    {
        var item = this[index];

        item?.OnParentChanging(null);

        base.RemoveItem(index);
    }

    protected override void SetItem(int index, TChild item)
    {
        var oldItem = this[index];
        oldItem?.OnParentChanging(null);
        item?.OnParentChanging(_parent);
        base.SetItem(index, item);
    }
}