using System.Diagnostics;
using System.Runtime.Serialization;
using KS.RustAnalyzer.TestAdapter.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace KS.RustAnalyzer.TestAdapter.Cargo;

[DebuggerDisplay("[{Type}] {FQN}")]
public sealed class Test
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum TestType
    {
        [EnumMember(Value = "test")]
        Test,
        [EnumMember(Value = "bench")]
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
