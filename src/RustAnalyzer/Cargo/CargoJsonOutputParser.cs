using System;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KS.RustAnalyzer.Cargo;

public static class CargoJsonOutputParser
{
    private static readonly Regex CompilerArtifactMessageCracker = new Regex(@"^(.*) (.*) \((.*)\+(.*)\)$", RegexOptions.Compiled);

    public static string[] Parse(string jsonLine)
    {
        dynamic obj;
        try
        {
            obj = JObject.Parse(jsonLine);
        }
        catch (JsonReaderException)
        {
            return Array.Empty<string>();
        }

        try
        {
            if (obj.reason == "compiler-artifact")
            {
                return ParseCompilerArtifact(obj);
            }
            else if (obj.reason == "compiler-message")
            {
                return ParseCompilerMessage(obj);
            }
        }
        catch
        {
            // TODO: Log to the output window.
            return new[] { jsonLine };
        }

        return Array.Empty<string>();
    }

    private static string[] ParseCompilerMessage(dynamic obj)
    {
        var filePath = obj.target.src_path.Value;
        var level = obj.message.level.Value;
        var message = obj.message.message.Value;
        var lineCol = string.Empty;

        if (obj.message.spans != null && obj.message.spans.Count > 0)
        {
            var span0 = obj.message.spans[0];
            lineCol = $"({span0.line_start},{span0.column_start})";
        }

        return new[]
        {
            $@"{filePath}{lineCol}: {level} RS0000: {message}",
            obj.message.rendered.Value as string,
        };
    }

    private static string[] ParseCompilerArtifact(dynamic obj)
    {
        if ((bool)obj.fresh.Value)
        {
            return Array.Empty<string>();
        }

        var matches = CompilerArtifactMessageCracker.Matches(obj.package_id.Value as string);
        var path = string.Empty;
        if (matches[0].Groups[3].Value == "path")
        {
            path = $" ({new Uri(matches[0].Groups[4].Value).LocalPath})";
        }

        return new[] { $"   Compiling {matches[0].Groups[1].Value} v{matches[0].Groups[2].Value}{path}" };
    }
}
