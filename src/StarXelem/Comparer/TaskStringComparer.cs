using System.Collections;
using StarXelem.Models;

namespace StarXelem.Comparer;

public class TaskStringComparer : IComparer<Task<string?>>, IComparer
{
    public int Compare(Task<string?>? x, Task<string?>? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (y is null) return 1;
        if (x is null) return -1;
        return string.Compare(x.Result, y.Result, StringComparison.CurrentCultureIgnoreCase);
    }

    public int Compare(object? x, object? y)
    {
        // Si pas le bon type, on retourne 0
        if (!(x is ItemViewModel a) || !(y is ItemViewModel b))
            return 0;
        return Compare(a.Location, b.Location);
    }
}

public class ItemViewModelName : IComparer<Task<string?>>, IComparer
{
    public int Compare(Task<string?>? x, Task<string?>? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (y is null) return 1;
        if (x is null) return -1;
        return string.Compare(x.Result, y.Result, StringComparison.CurrentCultureIgnoreCase);
    }

    public int Compare(object? x, object? y)
    {
        // Si pas le bon type, on retourne 0
        if (!(x is ItemViewModel a) || !(y is ItemViewModel b))
            return 0;
        return Compare(a.Name, b.Name);
    }
}

public class ItemViewModelOwnerComparer : IComparer<Task<string>>, IComparer
{
    public int Compare(Task<string>? x, Task<string>? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (y is null) return 1;
        if (x is null) return -1;
        return string.Compare(x.Result, y.Result, StringComparison.CurrentCultureIgnoreCase);
    }

    public int Compare(object? x, object? y)
    {
        // Si pas le bon type, on retourne 0
        if (!(x is ItemViewModel a) || !(y is ItemViewModel b))
            return 0;
        return Compare(a.Owner, b.Owner);
    }
}