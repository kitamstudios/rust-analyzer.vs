using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;

namespace KS.RustAnalyzer.TestAdapter.Common;

public static class MelLogEntryFormatter
{
    private const int MaximumScopeCount = 8;
    private const string OriginalFormat = "{OriginalFormat}";

    public static string Format<TState>(
        string categoryName,
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception exception,
        Func<TState, Exception, string> formatter,
        IExternalScopeProvider scopeProvider)
    {
        var message = formatter(state, exception);
        var builder = new StringBuilder()
            .Append('[')
            .Append(logLevel)
            .Append("] ")
            .Append(categoryName)
            .Append(" EventId=")
            .Append(eventId.Id);
        if (!string.IsNullOrEmpty(eventId.Name))
        {
            builder.Append('(').Append(eventId.Name).Append(')');
        }

        if (!string.IsNullOrEmpty(message))
        {
            builder.Append(" - ").Append(message);
        }

        AppendStructuredState(builder, state);
        AppendScopes(builder, scopeProvider);
        if (exception != null)
        {
            builder.Append(" | Exception: ").Append(exception);
        }

        return builder.ToString();
    }

    private static void AppendStructuredState<TState>(
        StringBuilder builder,
        TState state)
    {
        if (!(state is IEnumerable<KeyValuePair<string, object>> values))
        {
            return;
        }

        var structuredValues = values.ToArray();
        var template = structuredValues
            .FirstOrDefault(value => value.Key == OriginalFormat)
            .Value as string;
        if (template != null)
        {
            builder.Append(" | Template: ").Append(template);
        }

        var properties = structuredValues
            .Where(value => value.Key != OriginalFormat)
            .ToArray();
        if (properties.Length != 0)
        {
            builder.Append(" | Properties: ");
            AppendProperties(builder, properties);
        }
    }

    private static void AppendScopes(
        StringBuilder builder,
        IExternalScopeProvider scopeProvider)
    {
        if (scopeProvider == null)
        {
            return;
        }

        var scopes = new List<string>();
        scopeProvider.ForEachScope(
            (scope, values) =>
            {
                if (values.Count < MaximumScopeCount)
                {
                    values.Add(FormatScope(scope));
                }
                else if (values.Count == MaximumScopeCount)
                {
                    values.Add("...");
                }
            },
            scopes);
        if (scopes.Count != 0)
        {
            builder.Append(" | Scopes: ").Append(string.Join(" => ", scopes));
        }
    }

    private static string FormatScope(object scope)
    {
        if (!(scope is IEnumerable<KeyValuePair<string, object>> values))
        {
            return FormatValue(scope);
        }

        var structuredValues = values.ToArray();
        var builder = new StringBuilder(FormatValue(scope));
        var template = structuredValues
            .FirstOrDefault(value => value.Key == OriginalFormat)
            .Value as string;
        var properties = structuredValues
            .Where(value => value.Key != OriginalFormat)
            .OrderBy(value => value.Key, StringComparer.Ordinal)
            .ToArray();
        if (template != null || properties.Length != 0)
        {
            builder.Append(" [");
            if (template != null)
            {
                builder.Append("Template=").Append(template);
            }

            if (properties.Length != 0)
            {
                if (template != null)
                {
                    builder.Append("; ");
                }

                AppendProperties(builder, properties);
            }

            builder.Append(']');
        }

        return builder.ToString();
    }

    private static void AppendProperties(
        StringBuilder builder,
        IEnumerable<KeyValuePair<string, object>> properties)
    {
        var separator = string.Empty;
        foreach (var property in properties)
        {
            builder
                .Append(separator)
                .Append(property.Key)
                .Append('=')
                .Append(FormatValue(property.Value));
            separator = ", ";
        }
    }

    private static string FormatValue(object value)
    {
        if (value == null)
        {
            return "(null)";
        }

        if (value is IFormattable formattable)
        {
            return formattable.ToString(null, CultureInfo.InvariantCulture);
        }

        return value.ToString();
    }
}
