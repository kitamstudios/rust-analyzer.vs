using System;
using System.Collections.Generic;
using KS.RustAnalyzer.Cargo;
using Microsoft.VisualStudio.Workspace;

namespace KS.RustAnalyzer.VS;

public static class IWorkspaceExtensions
{
    public static Manifest GetParentCargoManifest(this IWorkspace workspace, string filePath)
    {
        var hasParentCargoFile = Manifest.GetParentCargoManifest(filePath, workspace.Location, out string parentCargoPath);
        if (hasParentCargoFile)
        {
            return Manifest.Create(parentCargoPath);
        }

        return null!;
    }

    public sealed class FileCollector : IProgress<string>
    {
        public List<string> FoundFiles => new ();

        public void Report(string value)
        {
            FoundFiles.Add(value);
        }
    }
}
