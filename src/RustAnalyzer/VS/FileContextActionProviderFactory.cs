using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.Design;
using KS.RustAnalyzer.Common;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Build;
using Microsoft.VisualStudio.Workspace.Extensions.VS;

namespace KS.RustAnalyzer.VS;

[ExportFileContextActionProvider(
    type: ProviderType,
    supportedContextTypeGuids: new[] { BuildContextTypes.BuildContextType, BuildContextTypes.CleanContextType, })]
public class FileContextActionProviderFactory : IWorkspaceProviderFactory<IFileContextActionProvider>, IVsCommandActionProvider
{
    public const string ProviderType = "F8C470E5-0000-498C-80B8-DA2674A82B88";

    [Import]
    public IOutputWindowPane OutputPane { get; set; }

    [Import]
    public ILogger L { get; set; }

    [Import]
    public ITelemetryService T { get; set; }

    public IFileContextActionProvider CreateProvider(IWorkspace workspaceContext)
    {
        T.TrackEvent(
            "Create Context Action Provider",
            new[] { ("Location", workspaceContext.Location) });
        L.WriteLine("Creating {0}.", GetType().Name);

        return new FileContextActionProvider(workspaceContext, OutputPane, T, L);
    }

    public IReadOnlyCollection<CommandID> GetSupportedVsCommands()
    {
        return new CommandID[]
        {
            // For additional menu items like restore, clippy, fmt, etc.
        };
    }
}
