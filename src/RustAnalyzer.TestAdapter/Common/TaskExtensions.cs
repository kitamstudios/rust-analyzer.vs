using System.Threading.Tasks;

namespace KS.RustAnalyzer.TestAdapter.Common;

public static class TaskExtensions
{
    public static Task<T> ToTask<T>(this T @this) => Task.FromResult(@this);

    public static void Forget(this Task @this)
    {
        @this.ConfigureAwait(false);
    }
}