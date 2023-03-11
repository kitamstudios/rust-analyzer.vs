using System;

namespace KS.RustAnalyzer.TestAdapter.Common;

public static class AttributeExtensions
{
    public static string GetAttributeValue<T, TAttribute>(
        this T source,
        Func<TAttribute, string> valueSelector)
        where TAttribute : Attribute
    {
        var fieldName = source?.ToString();
        if (fieldName == null)
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        var field = source?.GetType().GetField(fieldName);

        var attributes = field?.GetCustomAttributes(typeof(TAttribute), false) as TAttribute[];
        if (attributes != null && attributes.Length > 0)
        {
            return valueSelector(attributes[0]);
        }
        else
        {
            return source?.ToString();
        }
    }
}
