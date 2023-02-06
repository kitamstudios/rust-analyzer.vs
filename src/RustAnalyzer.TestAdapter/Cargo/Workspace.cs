using System;
using System.Diagnostics;
using System.Runtime.Serialization;
using KS.RustAnalyzer.TestAdapter.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace KS.RustAnalyzer.TestAdapter.Cargo;

/// <summary>
/// Spec: https://doc.rust-lang.org/cargo/commands/cargo-metadata.html.
/// </summary>
[DebuggerDisplay("{WorkspaceRoot} [#packages = {Packages.Count}]")]
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

    [DebuggerDisplay("{Name} - {ManifestPath} [#targets = {Targets.Count}]")]
    public sealed class Package : IHasParent<Workspace>
    {
        public const string RootPackageName = "<root>";

        public Package() => Targets = new ChildCollection<Package, Target>(this);

        [JsonIgnore]
        public Workspace Parent { get; private set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("manifest_path")]
        public PathEx ManifestPath { get; set; }

        [JsonProperty("targets")]
        public ChildCollection<Package, Target> Targets { get; set; }

        public PathEx WorkspaceRoot => Parent.WorkspaceRoot;

        public PathEx FullPath => ManifestPath;

        public bool IsPackage => !RootPackageName.Equals(Name, StringComparison.Ordinal);

        public void OnParentChanging(Workspace newParent) => Parent = newParent;
    }

    [DebuggerDisplay("{Name} {Kinds[0]} - {SourcePath}")]
    public sealed class Target : IHasParent<Package>
    {
        [JsonIgnore]
        public Package Parent { get; private set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("src_path")]
        public PathEx SourcePath { get; set; }

        [JsonProperty("kind")]
        public Kind[] Kinds { get; set; }

        [JsonProperty("crate_types")]
        public CrateType[] CrateTypes { get; set; }

        public PathEx TargetFileName => this.CreateTargetFileName();

        public bool IsRunnable => CrateTypes[0] == CrateType.Bin;

        public string QualifiedTargetFileName => $"[{Kinds[0].ToString().ToLower()}: {(string)this.GetTargetPathRelativeToWorkspace()}] {(string)TargetFileName}";

        public string AdditionalBuildArgs => Kinds[0] == Kind.Example ? $"--example \"{Name}\"" : string.Empty;

        public void OnParentChanging(Package newParent) => Parent = newParent;
    }
}
