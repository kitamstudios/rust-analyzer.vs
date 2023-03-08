using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.Serialization;
using KS.RustAnalyzer.TestAdapter.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace KS.RustAnalyzer.TestAdapter.Cargo;

[DebuggerDisplay("{Container} [#tests = {Tests.Count}]")]
public sealed class TestSuiteInfo
{
    [JsonProperty("container")]
    public PathEx Container { get; set; }

    [JsonProperty("tests")]
    public Collection<TestInfo> Tests { get; set; } = new Collection<TestInfo>();

    [DebuggerDisplay("[{Type}] {FQN}")]
    public sealed class TestInfo
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public enum TestType
        {
            [EnumMember(Value = "test")]
            Test,
            [EnumMember(Value = "benchmark")]
            Benchmark,
        }

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