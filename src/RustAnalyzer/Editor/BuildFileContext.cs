using System;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Workspace.Build;
using WorkspaceBuildMessage = Microsoft.VisualStudio.Workspace.Build.BuildMessage;

namespace KS.RustAnalyzer.Editor;

public class BuildFileContext : BuildFileContextBase
{
    public BuildFileContext(
        IToolchainService cs,
        BuildTargetInfo bti,
        IBuildOutputSink outputPane,
        PrerequisiteAvailabilityPolicy availabilityPolicy)
        : base(
            bti,
            outputPane,
            cs.BuildAsync,
            availabilityPolicy,
            AutomaticRustPath.OpenFolderBuild,
            RlsUpdatedNotification.ShowAsync)
    {
    }
}

public class CleanFileContext : BuildFileContextBase
{
    public CleanFileContext(
        IToolchainService cs,
        BuildTargetInfo bti,
        IBuildOutputSink outputPane,
        PrerequisiteAvailabilityPolicy availabilityPolicy)
        : base(
            bti,
            outputPane,
            cs.CleanAsync,
            availabilityPolicy,
            AutomaticRustPath.OpenFolderClean,
            RlsUpdatedNotification.ShowAsync)
    {
    }
}

public abstract class BuildFileContextBase : IBuildFileContext
{
    private readonly PrerequisiteAvailabilityPolicy _availabilityPolicy;
    private readonly Func<BuildTargetInfo, BuildOutputSinks, CancellationToken, Task<bool>> _commandFunc;
    private readonly IMapper _buildMessageMapper = new MapperConfiguration(cfg => cfg.CreateMap<DetailedBuildMessage, WorkspaceBuildMessage>()).CreateMapper();
    private readonly IBuildOutputSink _outputPane;
    private readonly AutomaticRustPath _path;
    private readonly Func<Task> _showUpdateNotificationAsync;

    protected BuildFileContextBase(
        BuildTargetInfo bti,
        IBuildOutputSink outputPane,
        Func<BuildTargetInfo, BuildOutputSinks, CancellationToken, Task<bool>> commandFunc,
        PrerequisiteAvailabilityPolicy availabilityPolicy,
        AutomaticRustPath path,
        Func<Task> showUpdateNotificationAsync)
    {
        BuildTargetInfo = bti;
        _outputPane = outputPane;
        _commandFunc = commandFunc;
        _availabilityPolicy = availabilityPolicy;
        _path = path;
        _showUpdateNotificationAsync = showUpdateNotificationAsync;
    }

    public string BuildConfiguration => BuildTargetInfo.Profile;

    public BuildTargetInfo BuildTargetInfo { get; }

    public async Task<bool> ExecuteBuildAsync(IBuildActionProgress progress, CancellationToken cancellationToken)
    {
        if (!_availabilityPolicy.IsReady(_path))
        {
            return false;
        }

        var bos = new BuildOutputSinks
        {
            BuildActionProgressReporter = bm => progress.ReportAsync(_buildMessageMapper.Map<WorkspaceBuildMessage>(bm), null),
            OutputSink = _outputPane,
        };

        await _showUpdateNotificationAsync();
        if (!_availabilityPolicy.IsReady(_path))
        {
            return false;
        }

        return await _commandFunc(BuildTargetInfo, bos, cancellationToken);
    }
}
