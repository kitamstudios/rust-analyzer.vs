using System.ComponentModel.Composition;
using Microsoft.Extensions.Logging;

namespace KS.RustAnalyzer.Infrastructure;

[Export(typeof(ILoggerFactory))]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class VsixLoggerFactory : LoggerFactory
{
    [ImportingConstructor]
    public VsixLoggerFactory([Import] OutputWindowLoggerProvider provider)
        : base(
            new[] { provider, },
            new LoggerFilterOptions { MinLevel = LogLevel.Information, })
    {
    }
}
