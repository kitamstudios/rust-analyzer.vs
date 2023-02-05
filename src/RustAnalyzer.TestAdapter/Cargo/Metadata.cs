using System.Runtime.Serialization;
using KS.RustAnalyzer.TestAdapter.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace KS.RustAnalyzer.TestAdapter.Cargo;

/// <summary>
/// Spec: https://doc.rust-lang.org/cargo/commands/cargo-metadata.html.
/// </summary>
public sealed class Metadata
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum Kind
    {
        [EnumMember(Value = "lib")]
        Lib,
        [EnumMember(Value = "rlib")]
        RLib,
        [EnumMember(Value = "dylib")]
        DyLib,
        [EnumMember(Value = "cdylib")]
        CdyLib,
        [EnumMember(Value = "staticlib")]
        StaticLib,
        [EnumMember(Value = "proc-macro")]
        ProcMacro,
        [EnumMember(Value = "bin")]
        Bin,
        [EnumMember(Value = "example")]
        Example,
        [EnumMember(Value = "test")]
        Test,
        [EnumMember(Value = "bench")]
        BenchMark,
        [EnumMember(Value = "custom-build")]
        CustomBuild,
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum CrateType
    {
        [EnumMember(Value = "lib")]
        Lib,
        [EnumMember(Value = "rlib")]
        RLib,
        [EnumMember(Value = "dylib")]
        DyLib,
        [EnumMember(Value = "cdylib")]
        CdyLib,
        [EnumMember(Value = "staticlib")]
        StaticLib,
        [EnumMember(Value = "proc-macro")]
        ProcMacro,
        [EnumMember(Value = "bin")]
        Bin,
    }

    [JsonProperty("version")]
    public int Version { get; set; }

    [JsonProperty("target_directory")]
    public PathEx TargetDirectory { get; set; }

    [JsonProperty("workspace_root")]
    public PathEx WorkspaceRoot { get; set; }

    [JsonProperty("packages")]
    public Package[] Packages { get; set; }

    public sealed class Package
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("manifest_path")]
        public PathEx ManifestPath { get; set; }

        [JsonProperty("targets")]
        public Target[] Targets { get; set; }
    }

    public sealed class Target
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("src_path")]
        public PathEx SourcePath { get; set; }

        [JsonProperty("kind")]
        public Kind[] Kinds { get; set; }

        [JsonProperty("crate_types")]
        public CrateType[] CrateTypes { get; set; }
    }
}
