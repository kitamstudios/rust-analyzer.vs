namespace KS.RustAnalyzer.Common;

public static class ArrayExtensions
{
    public static T[] SingleToArray<T>(this T obj)
    {
        return new[] { obj };
    }
}
