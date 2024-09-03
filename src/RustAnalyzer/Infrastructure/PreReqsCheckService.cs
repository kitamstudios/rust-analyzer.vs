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
    Task SatisfyAsync(CancellationToken ct);
}

[Export(typeof(IPreReqsCheckService))]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class PreReqsCheckService : IPreReqsCheckService
{
    private readonly IToolchainService _cargoService;
    private readonly TL _tl;

    private readonly IReadOnlyDictionary<string, Func<IToolchainService, CancellationToken, Task<(bool, string)>>> _preReqChecks =
        new Dictionary<string, Func<IToolchainService, CancellationToken, Task<(bool, string)>>>
        {
            [nameof(VsVersionCheck)] = VsVersionCheck.CheckAsync,

            // TODO: https://github.com/kitamstudios/rust-analyzer.vs/issues/54
            // [nameof(CheckRustupToolchainInstallationAsync)] = CheckRustupToolchainInstallationAsync,
            [nameof(CheckRustupAsync)] = CheckRustupAsync,
            [nameof(CheckCargoAsync)] = CheckCargoAsync,
        };

    [ImportingConstructor]
    public PreReqsCheckService([Import] IToolchainService cargoService, [Import] ITelemetryService t, [Import] ILogger l)
    {
        _cargoService = cargoService;
        _tl = new TL
        {
            T = t,
            L = l,
        };
    }

    public async Task SatisfyAsync(CancellationToken ct)
    {
        var results = await DoChecksAsync(ct);

        var failures = results.Where(x => !x.Success);
        if (failures.Any())
        {
            var line1 = failures
                .Aggregate(
                    new StringBuilder("Prerequisite check(s) failed:"),
                    (acc, e) => acc.AppendLine().AppendFormat("- {0}", e.Message))
                .ToString();
            await VsCommon.ShowMessageBoxAsync(
                line1,
                $"Pressing OK will open prerequsites install instructions and restart the IDE.");
            VsShellUtilities.OpenSystemBrowser(Constants.PrerequisitesUrl);
            await CommunityVS.Shell.RestartAsync();
        }
    }

    private async Task<IEnumerable<(bool Success, string Message)>> DoChecksAsync(CancellationToken ct)
    {
        var results = new List<(bool Success, string Message)>();
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

    private static async Task<(bool Success, string Message)> CheckCargoAsync(IToolchainService ts, CancellationToken ct)
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

    private static async Task<(bool Success, string Message)> CheckRustupAsync(IToolchainService ts, CancellationToken ct)
    {
        try
        {
            if (ToolchainServiceExtensions.GetRustupPath().FileExists())
            {
                return await (true, string.Empty).ToTask();
            }
        }
        catch
        {
        }

        return (false, $"{Constants.RustUpExe} not installed.");
    }

    private static async Task<(bool Success, string Message)> CheckRustupToolchainInstallationAsync(IToolchainService ts, CancellationToken ct)
    {
        try
        {
            if (!(await ToolchainServiceExtensions.GetDefaultToolchainAsync((PathEx)Environment.GetEnvironmentVariable("WINDIR"), ct)).IsNullOrEmptyOrWhiteSpace())
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
        public static async Task<(bool Success, string Message)> CheckAsync(IToolchainService ts, CancellationToken ct)
        {
            var version = await CommunityVS.Shell.GetVsVersionAsync();
            if (version == null)
            {
                return (false, "GetVsVersionAsync returned null. Indicates an issue with VS installation, restarting or latest updates may help.");
            }

            if (version <= Constants.MinimumRequiredVsVersion)
            {
                return (false, $"VS Version check failed. Minimum {Constants.MinimumRequiredVsVersion}, found {version}.\n\nInstall the latest VS update.\n\nThis is a one time thing. Unfortunately it is required as VS {Constants.MinimumRequiredVsVersion} introduced breaking changes. Sorry about that!");
            }

            return (true, string.Empty);
        }
    }

    #endregion
}
