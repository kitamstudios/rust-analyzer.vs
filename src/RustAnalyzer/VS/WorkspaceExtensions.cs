using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using KS.RustAnalyzer.Cargo;
using Microsoft.VisualStudio.Workspace;

namespace KS.RustAnalyzer.VS;

public static class WorkspaceExtensions
{
    public static async Task<CargoManifest> GetParentCargoManifestAsync(this IWorkspace workspace, string filePath)
    {
        var fileService = workspace.GetFindFilesService();
        var collector = new FileCollector();
        await fileService.FindFilesAsync(RustConstants.CargoFileName, collector);

        foreach (var file in collector.FoundFiles)
        {
            if (RustHelpers.IsCargoFile(file))
            {
                var directory = Path.GetDirectoryName(file);
                if (filePath.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
                {
                    return await CargoManifestFactory.CreateAsync(file);
                }
            }
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
