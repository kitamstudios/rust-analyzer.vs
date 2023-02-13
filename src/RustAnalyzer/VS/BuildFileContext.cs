using System;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Workspace.Build;
using WorkspaceBuildMessage = Microsoft.VisualStudio.Workspace.Build.BuildMessage;

namespace KS.RustAnalyzer.VS;

public class BuildFileContext : BuildFileContextBase
{
    public BuildFileContext(ICargoService cs, BuildTargetInfo bti, IBuildOutputSink outputPane)
        : base(bti, outputPane, cs.BuildAsync)
    {
    }
}

public class CleanFileContext : BuildFileContextBase
{
    public CleanFileContext(ICargoService cs, BuildTargetInfo bti, IBuildOutputSink outputPane)
        : base(bti, outputPane, cs.CleanAsync)
    {
    }
}

public abstract class BuildFileContextBase : IBuildFileContext
{
    private readonly Func<BuildTargetInfo, BuildOutputSinks, CancellationToken, Task<bool>> _commandFunc;
    private readonly IMapper _buildMessageMapper = new MapperConfiguration(cfg => cfg.CreateMap<DetailedBuildMessage, WorkspaceBuildMessage>()).CreateMapper();
    private readonly IBuildOutputSink _outputPane;

    public BuildFileContextBase(BuildTargetInfo bti, IBuildOutputSink outputPane, Func<BuildTargetInfo, BuildOutputSinks, CancellationToken, Task<bool>> commandFunc)
    {
        BuildTargetInfo = bti;
        _outputPane = outputPane;
        _commandFunc = commandFunc;
    }

    public string BuildConfiguration => BuildTargetInfo.Profile;

    public BuildTargetInfo BuildTargetInfo { get; }

    public async Task<bool> ExecuteBuildAsync(IBuildActionProgress progress, CancellationToken cancellationToken)
    {
        var bos = new BuildOutputSinks
        {
            BuildActionProgressReporter = bm => progress.ReportAsync(_buildMessageMapper.Map<WorkspaceBuildMessage>(bm), null),
            OutputSink = _outputPane,
        };

        return await _commandFunc(BuildTargetInfo, bos, cancellationToken);
    }
}
