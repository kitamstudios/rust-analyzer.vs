using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;

namespace KS.RustAnalyzer.Tests.Common;

public static class TestHelpers
{
    public static readonly PathEx ThisTestRoot =
        (PathEx)Path.Combine(
            Path.GetDirectoryName(Uri.UnescapeDataString(new Uri(Assembly.GetExecutingAssembly().CodeBase).AbsolutePath)),
            @"Cargo\TestData").ToLowerInvariant();

    public static readonly TL TL =
        new()
        {
            L = Mock.Of<KS.RustAnalyzer.TestAdapter.Common.ILogger>(),
            T = Mock.Of<IFeatureUsageTelemetry>(),
        };

    private static readonly ConcurrentDictionary<PathEx, IMetadataService> MetadataServices = new ConcurrentDictionary<PathEx, IMetadataService>();

    public static PathEx RemoveMachineSpecificPaths(this PathEx @this)
        => (PathEx)((string)@this).ToLowerInvariant().Replace(ThisTestRoot, "<TestRoot>");

    public static IMetadataService MS(this PathEx @this)
    {
        // NOTE: This simulates the case when a folder with multiple workspaces is opened.
        var root = @this.GetDirectoryName();
        return MetadataServices.GetOrAdd(root, (wr) => new MetadataService(new ToolchainService(TL.T, TL.L), wr, TL));
    }

    public static string Replace(this string str, string old, string @new, StringComparison comparison)
    {
        @new = @new ?? string.Empty;
        if (string.IsNullOrEmpty(str) || string.IsNullOrEmpty(old) || old.Equals(@new, comparison))
        {
            return str;
        }

        int foundAt = 0;
        while ((foundAt = str.IndexOf(old, foundAt, comparison)) != -1)
        {
            str = str.Remove(foundAt, old.Length).Insert(foundAt, @new);
            foundAt += @new.Length;
        }

        return str;
    }

    public static Task<bool> DoBuildAsync(
        this IToolchainService @this,
        PathEx workspacePath,
        PathEx manifestPath,
        string profile,
        string additionalBuildArgs = "",
        string additionalTestDiscoveryArguments = "",
        string additionalTestExecutionArguments = "",
        string testExecutionEnvironment = "")
    {
        return @this.BuildAsync(
                    new BuildTargetInfo
                    {
                        WorkspaceRoot = workspacePath,
                        ManifestPath = manifestPath,
                        Profile = profile,
                        AdditionalBuildArgs = additionalBuildArgs,
                        AdditionalTestDiscoveryArguments = additionalTestDiscoveryArguments,
                        AdditionalTestExecutionArguments = additionalTestExecutionArguments,
                        TestExecutionEnvironment = testExecutionEnvironment,
                    },
                    new BuildOutputSinks { OutputSink = Mock.Of<IBuildOutputSink>(), BuildActionProgressReporter = bm => Task.CompletedTask },
                    default);
    }

    public static string SerializeAndNormalizeObject(this object @this)
    {
        return @this
            .SerializeObject(Formatting.Indented, new PathExJsonConverter())
            .Replace(((string)ThisTestRoot).Replace("\\", "\\\\"), "<TestRoot>", StringComparison.OrdinalIgnoreCase)
            .RegexReplace(@"^\s+""(StartTime|EndTime)"": ""[^""]*"",?\r?\n", "\r\n", RegexOptions.Multiline)
            .RegexReplace(@"(""Duration"": "")[^""]+("")", @"${1}00:00:00${2}")
            .RegexReplace(@"thread '([^']+)' \(\d+\) panicked", @"thread '$1' panicked")
            .RegexReplace(@"([\\/])[\da-f]{16}([\\/])", "$1*$2", RegexOptions.IgnoreCase)
            .RegexReplace(@"\-[\da-f]{16}\.", "-*.", RegexOptions.IgnoreCase)
            .Replace("note: run with `RUST_BACKTRACE=1` environment variable to display a backtrace\\n", string.Empty);
    }

    public static (PathEx WorkspacePath, PathEx ManifestPath, PathEx TargetPath) GetTestPaths(this string @this, string profile)
    {
        var workspacePath = ThisTestRoot + (PathEx)@this;
        var manifestPath = workspacePath + Constants.ManifestFileName2;
        var targetPath = (workspacePath + (PathEx)@"target").MakeProfilePath(profile);

        return (WorkspacePath: workspacePath, ManifestPath: manifestPath, TargetPath: targetPath);
    }
}

public sealed class RecordingFeatureUsageTelemetry : IFeatureUsageTelemetry
{
    public ConcurrentQueue<(UsageOperation Operation, UsageOutcome Outcome, TimeSpan Duration)> Events { get; } = new();

    public void Track(UsageOperation operation, UsageOutcome outcome, TimeSpan duration)
    {
        Events.Enqueue((operation, outcome, duration));
    }
}

public sealed class RecordingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<RecordingLogEntry> Entries { get; } = new();

    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName)
    {
        return new RecordingLogger(categoryName, Entries);
    }

    public void Dispose()
    {
    }

    private sealed class RecordingLogger : Microsoft.Extensions.Logging.ILogger
    {
        private readonly string _categoryName;
        private readonly ConcurrentQueue<RecordingLogEntry> _entries;

        public RecordingLogger(
            string categoryName,
            ConcurrentQueue<RecordingLogEntry> entries)
        {
            _categoryName = categoryName;
            _entries = entries;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return EmptyScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                _entries.Enqueue(
                    new RecordingLogEntry(
                        _categoryName,
                        logLevel,
                        eventId,
                        state,
                        exception,
                        formatter(state, exception)));
            }
        }
    }

    private sealed class EmptyScope : IDisposable
    {
        public static readonly EmptyScope Instance = new();

        private EmptyScope()
        {
        }

        public void Dispose()
        {
        }
    }
}

public sealed class RecordingLogEntry
{
    public RecordingLogEntry(
        string category,
        LogLevel level,
        EventId eventId,
        object state,
        Exception exception,
        string message)
    {
        Category = category;
        Level = level;
        EventId = eventId;
        Exception = exception;
        Message = message;
        Properties = (state as IEnumerable<KeyValuePair<string, object>>)
            ?.ToDictionary(pair => pair.Key, pair => pair.Value)
            ?? new Dictionary<string, object>();
    }

    public string Category { get; }

    public LogLevel Level { get; }

    public EventId EventId { get; }

    public Exception Exception { get; }

    public string Message { get; }

    public IReadOnlyDictionary<string, object> Properties { get; }

    public string Template => Properties.TryGetValue(
        "{OriginalFormat}",
        out var template)
        ? (string)template
        : null;
}
