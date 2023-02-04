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
    public BuildFileContext(ICargoService cs, BuildTargetInfo bti, IBuildOutputSink outputPane, Func<string, Task> showMessageBox, TL tl)
        : base(bti, outputPane, showMessageBox, tl, cs.BuildAsync)
    {
    }
}

public class CleanFileContext : BuildFileContextBase
{
    public CleanFileContext(ICargoService cs, BuildTargetInfo bti, IBuildOutputSink outputPane, Func<string, Task> showMessageBox, TL tl)
        : base(bti, outputPane, showMessageBox, tl, cs.CleanAsync)
    {
    }
}

public abstract class BuildFileContextBase : IBuildFileContext
{
    private readonly Func<BuildTargetInfo, BuildOutputSinks, CancellationToken, Task<bool>> _commandFunc;
    private readonly IMapper _buildMessageMapper = new MapperConfiguration(cfg => cfg.CreateMap<DetailedBuildMessage, WorkspaceBuildMessage>()).CreateMapper();
    private readonly Func<string, Task> _showMessageBox;
    private readonly IBuildOutputSink _outputPane;
    private readonly TL _tl;

    public BuildFileContextBase(BuildTargetInfo bti, IBuildOutputSink outputPane, Func<string, Task> showMessageBox, TL tl, Func<BuildTargetInfo, BuildOutputSinks, CancellationToken, Task<bool>> commandFunc)
    {
        BuildTargetInfo = bti;
        _outputPane = outputPane;
        _showMessageBox = showMessageBox;
        _tl = tl;
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
            ShowMessageBox = _showMessageBox,
        };

        return await _commandFunc(BuildTargetInfo, bos, cancellationToken);
    }
}
