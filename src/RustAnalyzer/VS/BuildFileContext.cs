using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Workspace.Build;
using WorkspaceBuildMessage = Microsoft.VisualStudio.Workspace.Build.BuildMessage;

namespace KS.RustAnalyzer.VS;

public class BuildFileContext : BuildFileContextBase
{
    public BuildFileContext(BuildTargetInfo bti, IBuildOutputSink outputPane, Func<string, Task> showMessageBox, TL tl)
        : base(BuildContextTypes.BuildContextTypeGuid, bti, outputPane, showMessageBox, tl)
    {
    }
}

public class CleanFileContext : BuildFileContextBase
{
    public CleanFileContext(BuildTargetInfo bti, IBuildOutputSink outputPane, Func<string, Task> showMessageBox, TL tl)
        : base(BuildContextTypes.CleanContextTypeGuid, bti, outputPane, showMessageBox, tl)
    {
    }
}

public abstract class BuildFileContextBase : IBuildFileContext
{
    private static readonly IReadOnlyDictionary<Guid, Func<BuildTargetInfo, BuildOutputSinks, TL, CancellationToken, Task<bool>>> FileContextActionInfo =
        new Dictionary<Guid, Func<BuildTargetInfo, BuildOutputSinks, TL, CancellationToken, Task<bool>>>
        {
            [BuildContextTypes.BuildContextTypeGuid] = ExeRunner.BuildAsync,
            [BuildContextTypes.CleanContextTypeGuid] = ExeRunner.CleanAsync,
        };

    private readonly IMapper _buildMessageMapper = new MapperConfiguration(cfg => cfg.CreateMap<DetailedBuildMessage, WorkspaceBuildMessage>()).CreateMapper();
    private readonly Func<string, Task> _showMessageBox;
    private readonly Func<BuildTargetInfo, BuildOutputSinks, TL, CancellationToken, Task<bool>> _commandFunc;
    private readonly IBuildOutputSink _outputPane;
    private readonly TL _tl;

    public BuildFileContextBase(Guid contextTypeGuid, BuildTargetInfo bti, IBuildOutputSink outputPane, Func<string, Task> showMessageBox, TL tl)
    {
        BuildTargetInfo = bti;
        _outputPane = outputPane;
        _showMessageBox = showMessageBox;
        _tl = tl;
        _commandFunc = FileContextActionInfo[contextTypeGuid];
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

        return await _commandFunc(BuildTargetInfo, bos, _tl, cancellationToken);
    }
}
