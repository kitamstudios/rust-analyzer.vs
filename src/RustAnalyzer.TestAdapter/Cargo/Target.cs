using System.IO;
using EnsureThat;

namespace KS.RustAnalyzer.TestAdapter.Cargo;

public class Target
{
    public Target(Manifest manifest)
    {
        Manifest = manifest;
    }

    public Manifest Manifest { get; }

    public bool IsRunnable => IsBinary;

    public string TargetFileName => $"{Manifest.GetPackageName()}{GetExtension()}";

    public bool IsLibrary => File.Exists(Path.Combine(Path.GetDirectoryName(Manifest.FullPath), @"src\lib.rs"));

    public bool IsBinary => File.Exists(Path.Combine(Path.GetDirectoryName(Manifest.FullPath), @"src\main.rs"));

    private string GetExtension()
    {
        Ensure.That(Manifest.IsPackage, nameof(Manifest.IsPackage)).IsTrue();

        if (IsBinary)
        {
            return ".exe";
        }
        else if (IsLibrary)
        {
            return ".rlib";
        }

        return "._ni_";
    }
}