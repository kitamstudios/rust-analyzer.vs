using System;
using System.Collections.Generic;

namespace KS.RustAnalyzer.TestAdapter.Common;

public static class IEnumerableExtensions
{
    public static void ForEach<T>(this IEnumerable<T> @this, Action<T> action)
    {
        foreach (var item in @this)
        {
            action(item);
        }
    }
}