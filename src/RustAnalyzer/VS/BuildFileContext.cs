using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Workspace.Build;
using BuildMessage = KS.RustAnalyzer.TestAdapter.Common.BuildMessage;
using WorkspaceBuildMessage = Microsoft.VisualStudio.Workspace.Build.BuildMessage;

namespace KS.RustAnalyzer.VS;

public class BuildFileContext : BuildFileContextBase
{
    public BuildFileContext(string buildConfiguration, Manifest parentManifest, string filePath, string additionalBuildArgs, IBuildOutputSink outputPane, ITelemetryService telemetryService, Func<string, Task> showMessageBox, ILogger logger)
        : base(BuildContextTypes.BuildContextTypeGuid, buildConfiguration, parentManifest, filePath, additionalBuildArgs, outputPane, telemetryService, showMessageBox, logger)
    {
    }
}

public class CleanFileContext : BuildFileContextBase
{
    public CleanFileContext(string buildConfiguration, Manifest parentManifest, string filePath, IBuildOutputSink outputPane, ITelemetryService telemetryService, Func<string, Task> showMessageBox, ILogger logger)
        : base(BuildContextTypes.CleanContextTypeGuid, buildConfiguration, parentManifest, filePath, string.Empty, outputPane, telemetryService, showMessageBox, logger)
    {
    }
}

public abstract class BuildFileContextBase : IBuildFileContext
{
    private static readonly IReadOnlyDictionary<Guid, Func<string, string, string, string, IBuildOutputSink, Func<BuildMessage, Task>, ITelemetryService, Func<string, Task>, ILogger, CancellationToken, Task<bool>>> FileContextActionInfo =
        new Dictionary<Guid, Func<string, string, string, string, IBuildOutputSink, Func<BuildMessage, Task>, ITelemetryService, Func<string, Task>, ILogger, CancellationToken, Task<bool>>>
        {
            [BuildContextTypes.BuildContextTypeGuid] = ExeRunner.BuildAsync,
            [BuildContextTypes.CleanContextTypeGuid] = ExeRunner.CleanAsync,
        };

    private readonly IMapper _buildMessageMapper = new MapperConfiguration(cfg => cfg.CreateMap<DetailedBuildMessage, WorkspaceBuildMessage>()).CreateMapper();
    private readonly Manifest _parentManifest;
    private readonly ITelemetryService _t;
    private readonly Func<string, Task> _showMessageBox;
    private readonly ILogger _l;

    public BuildFileContextBase(Guid contextTypeGuid, string buildConfiguration, Manifest parentManifest, string filePath, string additionalBuildArgs, IBuildOutputSink outputPane, ITelemetryService telemetryService, Func<string, Task> showMessageBox, ILogger logger)
    {
        BuildConfiguration = buildConfiguration;
        _parentManifest = parentManifest;
        FilePath = filePath;
        AdditionalBuildArgs = additionalBuildArgs;
        OutputPane = outputPane;
        _t = telemetryService;
        _showMessageBox = showMessageBox;
        _l = logger;
        CommandFunc = FileContextActionInfo[contextTypeGuid];
    }

    public string BuildConfiguration { get; }

    // TODO: Too many arguments.
    public Func<string, string, string, string, IBuildOutputSink, Func<BuildMessage, Task>, ITelemetryService, Func<string, Task>, ILogger, CancellationToken, Task<bool>> CommandFunc { get; }

    public string FilePath { get; }

    public string AdditionalBuildArgs { get; }

    public IBuildOutputSink OutputPane { get; }

    public async Task<bool> ExecuteBuildAsync(IBuildActionProgress progress, CancellationToken cancellationToken)
    {
        Func<BuildMessage, Task> taskReporter = bm => progress.ReportAsync(_buildMessageMapper.Map<WorkspaceBuildMessage>(bm), null);
        return await CommandFunc(_parentManifest.WorkspaceRoot, FilePath, BuildConfiguration, AdditionalBuildArgs, OutputPane, taskReporter, _t, _showMessageBox, _l, cancellationToken);
    }
}
