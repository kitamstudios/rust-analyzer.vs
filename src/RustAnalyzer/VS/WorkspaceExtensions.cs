using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KS.RustAnalyzer.Cargo;
using Microsoft.VisualStudio.Workspace;

namespace KS.RustAnalyzer.VS;

public static class WorkspaceExtensions
{
    public static async Task<CargoManifest> GetParentCargoManifestAsync(this IWorkspace workspace, string filePath)
    {
        var hasParentCargoFile = RustHelpers.GetParentCargoManifest(filePath, workspace.Location, out string parentCargoPath);

        if (hasParentCargoFile)
        {
            return await CargoManifestFactory.CreateAsync(parentCargoPath);
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
