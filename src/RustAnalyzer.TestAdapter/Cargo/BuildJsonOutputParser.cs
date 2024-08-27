using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using KS.RustAnalyzer.TestAdapter.Common;
using Newtonsoft.Json.Linq;
using static KS.RustAnalyzer.TestAdapter.Common.DetailedBuildMessage;

namespace KS.RustAnalyzer.TestAdapter.Cargo;

/// <summary>
/// References
/// - https://doc.rust-lang.org/cargo/reference/external-tools.html#json-messages.
/// - https://doc.rust-lang.org/rustc/json.html.
/// </summary>
public static class BuildJsonOutputParser
{
    private static readonly IReadOnlyDictionary<string, Level> LevelToMessageTypeMap =
        new Dictionary<string, Level>()
        {
            ["error"] = Level.Error,
            ["warning"] = Level.Warning,
            ["note"] = Level.None,
            ["help"] = Level.None,
            ["failure-note"] = Level.None,
            ["error: internal compiler error"] = Level.Error,
        };

    private static readonly Regex CompilerArtifactMessageCracker1 =
        new(@"^(.*) (.*) \((.*)\+(.*)\)$", RegexOptions.Compiled);

    private static readonly Regex CompilerArtifactMessageCracker2 =
        new(@"^(.*)\+(.*)@(.*)$", RegexOptions.Compiled);

    public static BuildMessage[] Parse(PathEx workspaceRoot, string jsonLine, TL tl)
    {
        dynamic obj;
        try
        {
            obj = JObject.Parse(jsonLine);
        }
        catch (Exception e)
        {
            tl.L.WriteLine("CargoJsonOutputParser failed to parse line: {0}. Exception {1}.", jsonLine, e);
            tl.T.TrackException(e, new[] { ("Id", "JObjectParse"), ("Line", jsonLine) });
            return new[] { new StringBuildMessage { Message = jsonLine } };
        }

        try
        {
            if (obj.reason == "compiler-artifact")
            {
                return ParseCompilerArtifact(obj);
            }
            else if (obj.reason == "compiler-message")
            {
                return ParseCompilerMessage(workspaceRoot, obj);
            }
        }
        catch (Exception e)
        {
            tl.L.WriteLine("CargoJsonOutputParser failed to parse line: {0}. Exception {1}.", jsonLine, e);
            tl.T.TrackException(e, new[] { ("Id", "ParseCompilerX"), ("Line", jsonLine) });
            return new[] { new StringBuildMessage { Message = jsonLine } };
        }

        return Array.Empty<BuildMessage>();
    }

    private static BuildMessage[] ParseCompilerMessage(PathEx workspaceRoot, dynamic obj)
    {
        if (obj.message.spans == null || obj.message.spans.Count == 0)
        {
            return new BuildMessage[] { CreateBuildMessage(workspaceRoot, obj) };
        }

        return (obj.message.spans as IEnumerable<dynamic>).Select(
            s =>
            {
                DetailedBuildMessage msg = CreateBuildMessage(workspaceRoot, obj, s.file_name, s.line_start, s.column_start);
                return msg;
            }).ToArray();
    }

    private static int GetIntValue(dynamic obj, int defaultValue = default)
    {
        var value = 0;
        return obj != null && obj.Value != null && int.TryParse(obj.Value.ToString(), out value)
            ? value
            : defaultValue;
    }

    private static DetailedBuildMessage CreateBuildMessage(PathEx workspaceRoot, dynamic obj, dynamic fileInfo = null, dynamic lineInfo = null, dynamic colInfo = null)
    {
        var msg = new DetailedBuildMessage
        {
            Code = GetMessageCode(obj.message),
            ColumnNumber = GetIntValue(colInfo, 1),
            File = obj.target.src_path.Value,
            HelpKeyword = GetMessageCode(obj.message),
            LineNumber = GetIntValue(lineInfo, 1),
            ProjectFile = GetProjectFile(obj),
            SubCategory = null,
            TaskText = obj.message.message.Value,
            Type = GetMessageType(obj.message.level.Value),
        };

        msg.File = fileInfo != null && fileInfo.Value != null ? Path.Combine(workspaceRoot, fileInfo.Value) : msg.File;
        msg.LogMessage = GetLogMessage(obj.message, msg);

        return msg;
    }

    private static dynamic GetProjectFile(dynamic obj)
    {
        if (obj.manifest_path != null && obj.manifest_path.Value != null)
        {
            return obj.manifest_path.Value;
        }

        return obj.package_id.Value;
    }

    private static dynamic GetMessageCode(dynamic obj)
    {
        return obj.code != null && obj.code.code != null
            ? obj.code.code.Value
            : "RS0000";
    }

    private static dynamic GetLogMessage(dynamic message, DetailedBuildMessage msg)
    {
        var logMsgText = $@"{msg.File}({msg.LineNumber},{msg.ColumnNumber}): {msg.Type} {msg.Code}: {msg.TaskText}";
        var rendered = message.rendered != null ? $"Details:\r\n{message.rendered.Value}" : string.Empty;

        return $"{logMsgText}\r\n{rendered}";
    }

    private static Level GetMessageType(string level)
    {
        return LevelToMessageTypeMap.TryGetValue(level, out Level type)
            ? type
            : Level.Error;
    }

    private static BuildMessage[] ParseCompilerArtifact(dynamic obj)
    {
        if ((bool)obj.fresh.Value)
        {
            return Array.Empty<BuildMessage>();
        }

        var matches = CompilerArtifactMessageCracker1.Matches(obj.package_id.Value as string);
        if (matches.Count != 0)
        {
            return new[] { new StringBuildMessage { Message = $"   Compiling {matches[0].Groups[1].Value} v{matches[0].Groups[2].Value} ({matches[0].Groups[4].Value})" } };
        }

        matches = CompilerArtifactMessageCracker2.Matches(obj.package_id.Value as string);
        if (matches.Count != 0)
        {
            return new[] { new StringBuildMessage { Message = $"   Compiling {matches[0].Groups[2].Value} v{matches[0].Groups[3].Value}" } };
        }

        throw new InvalidDataException($"Unable to match. Will be shown as is in the output window.");
    }
}
