using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using KS.RustAnalyzer.TestAdapter.Common;

namespace KS.RustAnalyzer.Infrastructure;

public interface IPreReqsCheckService
{
    Task<PrerequisiteResult> EvaluateAsync(CancellationToken ct);
}

[Export(typeof(IPreReqsCheckService))]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class PreReqsCheckService : IPreReqsCheckService
{
    private readonly IPrerequisiteProbe _probe;
    private readonly ILogger _logger;

    [ImportingConstructor]
    public PreReqsCheckService([Import] ILogger logger)
        : this(new VisualStudioPrerequisiteProbe(), logger)
    {
    }

    public PreReqsCheckService(IPrerequisiteProbe probe, ILogger logger)
    {
        _probe = EnsureArg.IsNotNull(
            probe,
            nameof(probe),
            options => options.WithException(new ArgumentNullException(nameof(probe))));
        _logger = logger;
    }

    public async Task<PrerequisiteResult> EvaluateAsync(CancellationToken ct)
    {
        try
        {
            var diagnosticProbe = new DiagnosticPrerequisiteProbe(_probe);
            var result = await new PrerequisiteEvaluator(diagnosticProbe).EvaluateAsync(ct);
            diagnosticProbe.WriteDiagnostics(result, _logger);
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            _logger.WriteError("Prerequisite evaluation failed unexpectedly. Ex: {0}", e);
            return PrerequisiteResult.Failed(
                new[]
                {
                    new PrerequisiteFailure(
                        PrerequisiteFailureKind.PrerequisiteEvaluationFailed,
                        "Prerequisite evaluation could not complete. Review Output > rust-analyzer.vs for diagnostics, repair the reported environment issue, then restart Visual Studio."),
                });
        }
    }

    private sealed class DiagnosticPrerequisiteProbe : IPrerequisiteProbe
    {
        private const string CargoVersionOperation = "Prerequisite.CargoVersion";
        private const string RustupDefaultOperation = "Prerequisite.RustupDefault";
        private const string RustupVersionOperation = "Prerequisite.RustupVersion";
        private const string TruncationMarker = "...[truncated]...";
        private const int StartErrorLimit = 2 * 1024;
        private const int StreamPartLimit = 4 * 1024;
        private readonly List<(string Operation, PrerequisiteCommandResult Result)> _commands = new();
        private readonly IPrerequisiteProbe _inner;

        public DiagnosticPrerequisiteProbe(IPrerequisiteProbe inner)
        {
            _inner = inner;
        }

        Task<Version> IPrerequisiteProbe.GetVisualStudioVersionAsync(
            CancellationToken cancellationToken)
        {
            return _inner.GetVisualStudioVersionAsync(cancellationToken);
        }

        string IPrerequisiteProbe.FindExecutable(string fileName)
        {
            return _inner.FindExecutable(fileName);
        }

        async Task<PrerequisiteCommandResult> IPrerequisiteProbe.RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            var result = await _inner.RunAsync(executablePath, arguments, cancellationToken);
            var operation = GetOperation(arguments);
            if (operation != null)
            {
                _commands.Add((operation, result));
            }

            return result;
        }

        public void WriteDiagnostics(PrerequisiteResult result, ILogger logger)
        {
            var defaultToolchainFailed = result.Failures.Any(
                failure => failure.Kind == PrerequisiteFailureKind.DefaultToolchainNotConfigured);
            foreach (var command in _commands)
            {
                if (!command.Result.IsSuccess ||
                    (command.Operation == RustupDefaultOperation && defaultToolchainFailed))
                {
                    logger.WriteError("{0}", FormatDiagnostic(command.Operation, command.Result));
                }
            }
        }

        private static string FormatDiagnostic(string operation, PrerequisiteCommandResult result)
        {
            var status = result.WasStarted
                ? $"Exit code: {result.ExitCode}"
                : "Start status: failed";
            var startError = result.WasStarted
                ? string.Empty
                : $"\nStart error:\n{SanitizeStartError(result.StartError)}";
            return
                $"Prerequisite probe operation: {operation}\n" +
                $"{status}{startError}\n" +
                $"stdout:\n{SanitizeStream(result.StandardOutput)}\n" +
                $"stderr:\n{SanitizeStream(result.StandardError)}";
        }

        private static string GetOperation(IReadOnlyList<string> arguments)
        {
            if (arguments.Count == 1 && arguments[0] == "--version")
            {
                return RustupVersionOperation;
            }

            if (arguments.Count == 1 && arguments[0] == "default")
            {
                return RustupDefaultOperation;
            }

            return arguments.Count == 2 && arguments[1] == "--version"
                ? CargoVersionOperation
                : null;
        }

        private static string SanitizeStartError(string value)
        {
            var sanitized = Sanitize(value);
            if (sanitized.Length == 0)
            {
                return "<empty>";
            }

            return sanitized.Length <= StartErrorLimit
                ? sanitized
                : sanitized.Substring(0, StartErrorLimit - TruncationMarker.Length) +
                    TruncationMarker;
        }

        private static string SanitizeStream(string value)
        {
            var sanitized = Sanitize(value);
            if (sanitized.Length == 0)
            {
                return "<empty>";
            }

            return sanitized.Length <= StreamPartLimit * 2
                ? sanitized
                : sanitized.Substring(0, StreamPartLimit) +
                    "\n" + TruncationMarker + "\n" +
                    sanitized.Substring(sanitized.Length - StreamPartLimit);
        }

        private static string Sanitize(string value)
        {
            var normalized = (value ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');
            var sanitized = new StringBuilder(normalized.Length);
            foreach (var character in normalized)
            {
                if (character == '\n' || !char.IsControl(character))
                {
                    sanitized.Append(character);
                }
            }

            return sanitized.ToString();
        }
    }
}
