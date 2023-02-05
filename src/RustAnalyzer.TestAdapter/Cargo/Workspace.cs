using System.Runtime.Serialization;
using KS.RustAnalyzer.TestAdapter.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace KS.RustAnalyzer.TestAdapter.Cargo;

/// <summary>
/// Spec: https://doc.rust-lang.org/cargo/commands/cargo-metadata.html.
/// </summary>
public sealed class Workspace
{
    public Workspace() => Packages = new ChildCollection<Workspace, Package>(this);

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
    public ChildCollection<Workspace, Package> Packages { get; set; }

    public sealed class Package : IHasParent<Workspace>
    {
        public Package() => Targets = new ChildCollection<Package, Target>(this);

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("manifest_path")]
        public PathEx ManifestPath { get; set; }

        [JsonProperty("targets")]
        public ChildCollection<Package, Target> Targets { get; set; }

        [JsonIgnore]
        public Workspace Parent { get; private set; }

        public void OnParentChanging(Workspace newParent) => Parent = newParent;
    }

    public sealed class Target : IHasParent<Package>
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("src_path")]
        public PathEx SourcePath { get; set; }

        [JsonProperty("kind")]
        public Kind[] Kinds { get; set; }

        [JsonProperty("crate_types")]
        public CrateType[] CrateTypes { get; set; }

        [JsonIgnore]
        public Package Parent { get; private set; }

        public void OnParentChanging(Package newParent) => Parent = newParent;
    }
}
