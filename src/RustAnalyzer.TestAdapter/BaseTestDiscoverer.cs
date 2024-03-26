using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace KS.RustAnalyzer.TestAdapter;

public abstract class BaseTestDiscoverer
{
    /// <summary>
    /// Signature requried by ITestDiscoverer.
    /// </summary>
    public void DiscoverTests(IEnumerable<string> sources, IDiscoveryContext discoveryContext, IMessageLogger logger, ITestCaseDiscoverySink discoverySink)
    {
        DiscoverTests(sources.Select(s => (PathEx)s).Where(s => s != default), discoveryContext, logger, discoverySink);
    }

    public void DiscoverTests(PathEx source, IDiscoveryContext discoveryContext, IMessageLogger logger, ITestCaseDiscoverySink discoverySink)
    {
        DiscoverTests(new[] { source }, discoveryContext, logger, discoverySink);
    }

    public abstract void DiscoverTests(IEnumerable<PathEx> sources, IDiscoveryContext discoveryContext, IMessageLogger logger, ITestCaseDiscoverySink discoverySink);

    protected void SignupForAssemblyResolution(ILogger logger)
    {
        AppDomain.CurrentDomain.AssemblyResolve +=
            new ResolveEventHandler(CurrentDomain_AssemblyResolve(logger));
    }

    protected Func<object, ResolveEventArgs, Assembly> CurrentDomain_AssemblyResolve(ILogger logger)
    {
        return (object sender, ResolveEventArgs args) =>
        {
            logger.WriteLine(@"This is a sign of impending doom. Have been asked by '{0}' to resolve '{1}'.", args.RequestingAssembly.FullName, args.Name);

            var name = new AssemblyName(args.Name);
            if (name.Name == "System.Diagnostics.DiagnosticSource")
            {
                return typeof(System.Diagnostics.DiagnosticSource).Assembly;
            }
            else if (name.Name == "System.Runtime.CompilerServices.Unsafe")
            {
                return typeof(System.Runtime.CompilerServices.Unsafe).Assembly;
            }

            logger.WriteError(@"Unable to resolve resolve assembly '{0}'. Behavior unknow from this point on. Please file a bug at https://github.com/kitamstudios/rust-analyzer.vs.", args.Name);
            return null;
        };
    }
}
