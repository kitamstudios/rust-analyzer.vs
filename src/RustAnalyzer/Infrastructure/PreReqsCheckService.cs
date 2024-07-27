using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Shell;
using CommunityVS = Community.VisualStudio.Toolkit.VS;
using Constants = KS.RustAnalyzer.TestAdapter.Constants;

namespace KS.RustAnalyzer.Infrastructure;

public interface IPreReqsCheckService
{
    Task<bool> SatisfySilentAsync(CancellationToken ct);

    Task SatisfyAsync(CancellationToken ct);
}

[Export(typeof(IPreReqsCheckService))]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class PreReqsCheckService : IPreReqsCheckService
{
    private readonly IToolChainService _cargoService;
    private readonly TL _tl;

    private readonly IReadOnlyDictionary<string, Func<IToolChainService, CancellationToken, Task<(bool, string)>>> _preReqChecks =
        new Dictionary<string, Func<IToolChainService, CancellationToken, Task<(bool, string)>>>
        {
            [nameof(VsVersionCheck)] = VsVersionCheck.CheckAsync,
            [nameof(CheckRustupToolchainInstallationAsync)] = CheckRustupToolchainInstallationAsync,
            [nameof(CheckRustupAsync)] = CheckRustupAsync,
            [nameof(CheckCargoAsync)] = CheckCargoAsync,
        };

    [ImportingConstructor]
    public PreReqsCheckService([Import] IToolChainService cargoService, [Import] ITelemetryService t, [Import] ILogger l)
    {
        _cargoService = cargoService;
        _tl = new TL
        {
            T = t,
            L = l,
        };
    }

    public async Task<bool> SatisfySilentAsync(CancellationToken ct)
    {
        var results = await DoChecksAsync(ct);

        return results.All(x => x.success);
    }

    public async Task SatisfyAsync(CancellationToken ct)
    {
        var results = await DoChecksAsync(ct);

        var failures = results.Where(x => !x.success);
        if (failures.Any())
        {
            var line1 = failures
                .Aggregate(
                    new StringBuilder("Prerequisite check(s) failed:"),
                    (acc, e) => acc.AppendLine().AppendFormat("- {0}", e.message))
                .ToString();
            await VsCommon.ShowMessageBoxAsync(
                line1,
                $"Pressing OK will open prerequsites install instructions and restart the IDE.");
            VsShellUtilities.OpenSystemBrowser(Constants.PrerequisitesUrl);
            await CommunityVS.Shell.RestartAsync();
        }
    }

    private async Task<IEnumerable<(bool success, string message)>> DoChecksAsync(CancellationToken ct)
    {
        var results = new List<(bool success, string message)>();
        foreach (var kv in _preReqChecks)
        {
            _tl.L.WriteLine("Running PreReqCheck: {0}...", kv.Key);
            var (success, message) = await kv.Value(_cargoService, ct);
            if (!success)
            {
                _tl.L.WriteLine("... {0} failed: {1}.", kv.Key, message);
                _tl.T.TrackException(new ArgumentOutOfRangeException(message));
                results.Add((success, message));
            }
        }

        return results;
    }

    private static async Task<(bool, string)> CheckCargoAsync(IToolChainService ts, CancellationToken ct)
    {
        try
        {
            if (ts.GetCargoExePath().FileExists())
            {
                return await (true, string.Empty).ToTask();
            }
        }
        catch
        {
        }

        return (false, $"{Constants.CargoExe} component is not found in any active toolchain.");
    }

    private static async Task<(bool, string)> CheckRustupAsync(IToolChainService ts, CancellationToken ct)
    {
        try
        {
            if (ToolChainServiceExtensions.GetRustupPath().FileExists())
            {
                return await (true, string.Empty).ToTask();
            }
        }
        catch
        {
        }

        return (false, $"{Constants.RustUpExe} not installed.");
    }

    private static async Task<(bool, string)> CheckRustupToolchainInstallationAsync(IToolChainService ts, CancellationToken ct)
    {
        try
        {
            if ((await ToolChainServiceExtensions.GetDefaultToolchainAsync((PathEx)Environment.GetEnvironmentVariable("WINDIR"), ct)).IsNullOrEmptyOrWhiteSpace())
            {
                return await (true, string.Empty).ToTask();
            }
        }
        catch
        {
        }

        return (false, $"Rust installation not found or is corrupted. Reinstall {Constants.RustUpExe} and toolchains.");
    }

    #region VsVersionCheck

    public static class VsVersionCheck
    {
        public static async Task<(bool, string)> CheckAsync(IToolChainService ts, CancellationToken ct)
        {
            var version = await CommunityVS.Shell.GetVsVersionAsync();
            if (version == null)
            {
                return (false, "GetVsVersionAsync returned null. Indicates an issue with VS installation, restarting or latest updates may help.");
            }

            if (version <= Constants.MinimumRequiredVsVersion)
            {
                return (false, $"VS Version check failed. Minimum {Constants.MinimumRequiredVsVersion}, found {version}.");
            }

            return (true, string.Empty);
        }
    }

    #endregion
}
