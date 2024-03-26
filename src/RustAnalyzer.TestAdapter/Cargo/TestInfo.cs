using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.Serialization;
using KS.RustAnalyzer.TestAdapter.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace KS.RustAnalyzer.TestAdapter.Cargo;

[JsonConverter(typeof(StringEnumConverter))]
public enum TestType
{
    [EnumMember(Value = "test")]
    Test,
    [EnumMember(Value = "benchmark")]
    Benchmark,
}

[DebuggerDisplay("{Source} {Exe} [#tests = {Tests.Count}]")]
public sealed class TestSuiteInfo
{
    [JsonProperty("container")]
    public TestContainer Container { get; set; }

    [JsonProperty("exe")]
    public PathEx Exe { get; set; }

    [JsonProperty("tests")]
    public Collection<TestInfo> Tests { get; set; } = new Collection<TestInfo>();

    [DebuggerDisplay("[{Type}] {FQN}")]
    public sealed class TestInfo
    {
        [JsonProperty("type")]
        public TestType Type { get; set; }

        [JsonProperty("name")]
        public string FQN { get; set; }

        [JsonProperty("ignore")]
        public bool Skipped { get; set; }

        [JsonProperty("ignore_message")]
        public string SkippedReason { get; set; }

        [JsonProperty("source_path")]
        public PathEx SourcePath { get; set; }

        [JsonProperty("start_line")]
        public int StartLine { get; set; }

        [JsonProperty("start_col")]
        public int StartColumn { get; set; }

        [JsonProperty("end_line")]
        public int EndLine { get; set; }

        [JsonProperty("end_col")]
        public int EndColumn { get; set; }
    }
}

[DebuggerDisplay("[{Type}] {FQN}")]
public sealed class TestRunInfo
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum EventType
    {
        [EnumMember(Value = "started")]
        Started,
        [EnumMember(Value = "ok")]
        Ok,
        [EnumMember(Value = "ignored")]
        Ignored,
        [EnumMember(Value = "failed")]
        Failed,
    }

    [JsonProperty("type")]
    public TestType Type { get; set; }

    [JsonProperty("event")]
    public EventType Event { get; set; }

    [JsonProperty("name")]
    public string FQN { get; set; }

    [JsonProperty("stdout")]
    public string StdOut { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; }

    [JsonProperty("exec_time")]
    public double ExecutionTime { get; set; }
}