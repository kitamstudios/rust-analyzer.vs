using System.Collections.Generic;
using System.Linq;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;

namespace KS.RustAnalyzer.TestAdapter;

public abstract class BaseTestExecutor
{
    public void RunTests(IEnumerable<string> sources, IRunContext runContext, IFrameworkHandle frameworkHandle)
    {
        RunTests(sources.Select(s => (PathEx)s).Where(s => s != default), runContext, frameworkHandle);
    }

    public abstract void RunTests(IEnumerable<PathEx> sources, IRunContext runContext, IFrameworkHandle frameworkHandle);
}
