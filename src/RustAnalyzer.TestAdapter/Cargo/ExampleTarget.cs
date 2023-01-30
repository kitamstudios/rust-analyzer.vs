using System;
using System.Collections.Generic;
using System.IO;

namespace KS.RustAnalyzer.TestAdapter.Cargo;

/// <summary>
/// See:
/// - https://github.com/rust-lang/cargo/blob/master/src/doc/src/reference/cargo-targets.md#examples.
/// - https://github.com/rust-lang/cargo/blob/master/src/doc/src/guide/project-layout.md.
/// </summary>
public sealed class ExampleTarget : Target
{
    private ExampleTarget(Manifest manifest, string name, string source)
        : base(manifest, name, TargetType.Example)
    {
        Source = source;
        AdditionalBuildArgs = $"--example {name}";
    }

    // TODO: BUG: add message box to file not found for the silent failure while debugging.
    public static IEnumerable<Target> GetAll(Manifest manifest)
    {
        var examplesFolder = Path.Combine(Path.GetDirectoryName(manifest.FullPath), "examples");
        if (manifest.IsWorkspace || !Directory.Exists(examplesFolder))
        {
            yield break;
        }

        // As per https://github.com/rust-lang/cargo/blob/master/src/doc/src/guide/project-layout.md:
        // all directories with main.rs & all files with .rs extension.
        foreach (var fse in Directory.EnumerateFileSystemEntries(examplesFolder))
        {
            var source = Path.Combine(fse, "main.rs");
            if (File.Exists(source))
            {
                yield return new ExampleTarget(manifest, Path.GetFileName(fse), source);
            }

            if (File.Exists(fse)
                && Path.GetExtension(fse).Equals(Constants.RustFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                yield return new ExampleTarget(manifest, Path.GetFileNameWithoutExtension(fse), fse);
            }
        }
    }

    public override string GetPath(string profile) => Path.Combine(GetTargetDirectory(profile), "examples", TargetFileName);
}