using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.RegularExpressions;

namespace KS.RustAnalyzer.TestAdapter.Common;

public static class EnumExtensions
{
    public static string GetName<T>(this T source)
    {
        return source.GetAttributeValue<T, DisplayAttribute>(a => a.Name);
    }

    public static string GetShortName<T>(this T source)
    {
        return source.GetAttributeValue<T, DisplayAttribute>(a => a.ShortName);
    }

    public static IEnumerable<T> GetEnumValues<T>()
         where T : Enum
    {
        return Enum.GetValues(typeof(T)).Cast<T>().OrderBy(it => it);
    }

    public static IEnumerable<Enum> GetEnumValues(Type type)
    {
        return Enum.GetValues(type).Cast<Enum>().OrderBy(it => it);
    }

    public static IEnumerable<T> GetRandomValues<T>()
        where T : Enum
    {
        var random = new Random();
        var values = GetEnumValues<T>().OrderBy(it => random.Next());

        var take = random.Next(1, values.Count());
        return values.Take(take).ToList();
    }

    public static T GetRandomValue<T>()
        where T : Enum
    {
        return GetRandomValues<T>().First();
    }

    public static IEnumerable<T> GetUniqueEnumListFromAnyString<T>(
        this string str,
        IReadOnlyDictionary<T, IEnumerable<string>> extraAliases = null)
        where T : Enum
    {
        return GetEnumValues<T>().Where(enumValue => IsMatching(str, enumValue, extraAliases));
    }

    public static string GetShortNameStringFromEnumList<T>(
        this IEnumerable<T> enumList)
        where T : Enum
    {
        return string.Join(", ", enumList.Select(e => e.GetShortName()));
    }

    public static bool TryGetEnumFromEnumString<T>(
        this string enumStr,
        out T value,
        IReadOnlyDictionary<T, IEnumerable<string>> extraAliases = null)
        where T : Enum
    {
        var matchResult = GetUniqueEnumListFromAnyString(enumStr, extraAliases);
        if (matchResult.Any())
        {
            value = matchResult.First();
            return true;
        }

        value = GetEnumValues<T>().First();
        return false;
    }

    public static IEnumerable<T> GetEnumListFromEnumString<T>(
        this string enumString,
        string separator,
        IReadOnlyDictionary<T, IEnumerable<string>> extraAliases = null)
        where T : Enum
    {
        var enumStrList = enumString.Split(new[] { string.IsNullOrWhiteSpace(separator) ? separator : separator.Trim() }, StringSplitOptions.None);
        foreach (var enumValue in enumStrList)
        {
            var enumStr = enumValue.Trim();
            var matchResult = GetUniqueEnumListFromAnyString(enumStr, extraAliases);
            if (matchResult.Any())
            {
                yield return matchResult.First();
            }
        }
    }

    public static IEnumerable<string> GetAllEnumAliases<T>(
        this T enumType,
        IReadOnlyDictionary<T, IEnumerable<string>> extraAliases = null)
        where T : Enum
    {
        if (extraAliases == null || !extraAliases.TryGetValue(enumType, out var seedAliases))
        {
            seedAliases = Enumerable.Empty<string>();
        }

        var aliases = new HashSet<string>(seedAliases)
        {
            enumType.ToString(),
            enumType.GetName(),
            enumType.GetShortName()
        };

        return aliases;
    }

    public static bool IsMatching<T>(
        this string str,
        T enumValue,
        IReadOnlyDictionary<T, IEnumerable<string>> extraAliases)
        where T : Enum
    {
        var listOfAliases = GetAllEnumAliases(enumValue, extraAliases);

        foreach (var alias in listOfAliases)
        {
            var isMatching = Regex.Match(
                str ?? string.Empty,
                $"([\\W]+|^){alias}(\\W+|$)",
                RegexOptions.IgnoreCase).Success;
            if (isMatching)
            {
                return true;
            }
        }

        return false;
    }
}
