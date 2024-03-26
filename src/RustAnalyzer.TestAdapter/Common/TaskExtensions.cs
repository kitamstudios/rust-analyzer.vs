using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace KS.RustAnalyzer.TestAdapter.Common;

public static class TaskExtensions
{
    public static Task<T> ToTask<T>(this T @this) => Task.FromResult(@this);

    public static void Forget(this Task @this)
    {
        @this.ConfigureAwait(false);
    }

    public static async Task<IEnumerable<T>> ToTaskEnumerableAsync<T>(this IEnumerable<Task<T>> @this)
    {
        var ret = new ConcurrentBag<T>();
        foreach (var t in @this)
        {
            ret.Add(await t);
        }

        return ret;
    }
}