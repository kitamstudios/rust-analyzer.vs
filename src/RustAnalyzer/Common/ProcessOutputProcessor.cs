using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;

namespace KS.RustAnalyzer.Common;

/// <summary>
/// Stolen from https://github.com/microsoft/nodejstools/blob/main/Common/Product/SharedProject/ProcessOutput.cs.
/// </summary>
public sealed class ProcessOutputProcessor : IDisposable
{
    private readonly ProcessOutputRedirector _redirector;
    private readonly CancellationToken _cancellationToken;
    private readonly object _seenNullLock = new object();
    private readonly object _killLock = new object();

    private readonly string _arguments;
    private readonly List<string> _output = new List<string>();
    private readonly List<string> _error = new List<string>();
    private ManualResetEvent _waitHandleEvent;
    private bool _isDisposed;
    private bool _seenNullInOutput;
    private bool _seenNullInError;
    private bool _haveRaisedExitedEvent;
    private Task<int> _awaiter;

    private static readonly char[] EolChars = new[] { '\r', '\n' };
    private static readonly char[] NeedToBeQuoted = new[] { ' ', '"' };

    private ProcessOutputProcessor(Process process, ProcessOutputRedirector redirector, CancellationToken cancellationToken)
    {
        EnsureArg.IsNotNull(process, nameof(process));

        _arguments = QuoteSingleArgument(process.StartInfo.FileName) + " " + process.StartInfo.Arguments;
        _redirector = redirector;
        _cancellationToken = cancellationToken;
        Process = process;
        if (Process.StartInfo.RedirectStandardOutput)
        {
            Process.OutputDataReceived += OnOutputDataReceived;
        }

        if (Process.StartInfo.RedirectStandardError)
        {
            Process.ErrorDataReceived += OnErrorDataReceived;
        }

        if (!Process.StartInfo.RedirectStandardOutput && !Process.StartInfo.RedirectStandardError)
        {
            // If we are receiving output events, we signal that the process
            // has exited when one of them receives null. Otherwise, we have
            // to listen for the Exited event.
            // If we just listen for the Exited event, we may receive it
            // before all the output has arrived.
            Process.Exited += OnExited;
        }

        Process.EnableRaisingEvents = true;

        try
        {
            Process.Start();
        }
        catch (Exception ex)
        {
            if (IsCriticalException(ex))
            {
                throw;
            }

            if (_redirector != null)
            {
                foreach (var line in SplitLines(ex.ToString()))
                {
                    _redirector.WriteErrorLine(line);
                }
            }
            else if (_error != null)
            {
                _error.AddRange(SplitLines(ex.ToString()));
            }

            Process = null!;
        }

        if (Process != null)
        {
            if (Process.StartInfo.RedirectStandardOutput)
            {
                Process.BeginOutputReadLine();
            }

            if (Process.StartInfo.RedirectStandardError)
            {
                Process.BeginErrorReadLine();
            }

            if (Process.StartInfo.RedirectStandardInput)
            {
                // Close standard input so that we don't get stuck trying to read input from the user.
                if (_redirector == null || (_redirector != null && _redirector.CloseStandardInput()))
                {
                    try
                    {
                        Process.StandardInput.Close();
                    }
                    catch (InvalidOperationException)
                    {
                        // StandardInput not available
                    }
                }
            }
        }
    }

    /// <summary>
    /// Raised when the process exits.
    /// </summary>
    public event EventHandler Exited;

    public int? ProcessId => IsStarted ? Process.Id : (int?)null;

    public Process Process { get; }

    public string Arguments => _arguments;

    public bool IsStarted => Process != null;

    public int? ExitCode
    {
        get
        {
            if (Process == null || !Process.HasExited)
            {
                return null;
            }

            return Process.ExitCode;
        }
    }

    /// <summary>
    /// Gets or sets the priority class of the process.
    /// </summary>
    public ProcessPriorityClass PriorityClass
    {
        get
        {
            if (Process != null && !Process.HasExited)
            {
                try
                {
                    return Process.PriorityClass;
                }
                catch (Win32Exception)
                {
                }
                catch (InvalidOperationException)
                {
                    // Return Normal if we've raced with the process
                    // exiting.
                }
            }

            return ProcessPriorityClass.Normal;
        }

        set
        {
            if (Process != null && !Process.HasExited)
            {
                try
                {
                    Process.PriorityClass = value;
                }
                catch (Win32Exception)
                {
                }
                catch (InvalidOperationException)
                {
                    // Silently fail if we've raced with the process
                    // exiting.
                }
            }
        }
    }

    public ProcessOutputRedirector Redirector => _redirector;

    /// <summary>
    /// Gets the lines of text sent to standard output. These do not include
    /// newline characters.
    /// </summary>
    public IEnumerable<string> StandardOutputLines => _output;

    /// <summary>
    /// Gets the lines of text sent to standard error. These do not include
    /// newline characters.
    /// </summary>
    public IEnumerable<string> StandardErrorLines => _error;

    /// <summary>
    /// Gets a handle that can be waited on. It triggers when the process exits.
    /// </summary>
    public WaitHandle WaitHandle
    {
        get
        {
            if (Process == null)
            {
                return null!;
            }

            if (_waitHandleEvent == null)
            {
                _waitHandleEvent = new ManualResetEvent(_haveRaisedExitedEvent);
            }

            return _waitHandleEvent;
        }
    }

    public bool IsDisposed { get => _isDisposed; set => _isDisposed = value; }

    /// <summary>
    /// Waits until the process exits.
    /// </summary>
    public void Wait()
    {
        if (Process != null)
        {
            Process.WaitForExit();

            // Should have already been called, in which case this is a no-op
            OnExited(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Waits until the process exits or the timeout expires.
    /// </summary>
    /// <param name="timeout">The maximum time to wait.</param>
    /// <returns>
    /// True if the process exited before the timeout expired.
    /// </returns>
    public bool Wait(TimeSpan timeout)
    {
        if (Process != null)
        {
            var exited = Process.WaitForExit((int)timeout.TotalMilliseconds);
            if (exited)
            {
                // Should have already been called, in which case this is a no-op
                OnExited(this, EventArgs.Empty);
            }

            return exited;
        }

        return true;
    }

    public TaskAwaiter<int> GetAwaiter()
    {
        if (_awaiter == null)
        {
            if (Process == null)
            {
                var tcs = new TaskCompletionSource<int>();
                tcs.SetCanceled();
                _awaiter = tcs.Task;
            }
            else if (Process.HasExited)
            {
                // Should have already been called, in which case this is a no-op
                OnExited(this, EventArgs.Empty);
                var tcs = new TaskCompletionSource<int>();
                tcs.SetResult(Process.ExitCode);
                _awaiter = tcs.Task;
            }
            else
            {
                _awaiter = Task.Run(() =>
                {
                    try
                    {
                        Wait();
                    }
                    catch (Win32Exception)
                    {
                        throw new OperationCanceledException();
                    }

                    return Process.ExitCode;
                });
            }
        }

        return _awaiter.GetAwaiter();
    }

    /// <summary>
    /// Immediately stops the process.
    /// </summary>
    public void Kill()
    {
        if (Process != null && !Process.HasExited)
        {
            lock (_killLock)
            {
                if (Process != null && !Process.HasExited)
                {
                    Process.Kill();

                    // Should have already been called, in which case this is a no-op
                    OnExited(this, EventArgs.Empty);
                }
            }
        }
    }

    public static ProcessOutputProcessor RunVisible(string filename, string[] arguments, CancellationToken cancellationToken)
    {
        return Run(filename, arguments, null!, null!, true, null!, cancellationToken: cancellationToken);
    }

    public static ProcessOutputProcessor RunHiddenAndCapture(string filename, string[] arguments, CancellationToken cancellationToken)
    {
        return Run(filename, arguments, null!, null!, false, null!, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Runs the file with the provided settings.
    /// </summary>
    /// <param name="filename">Executable file to run.</param>
    /// <param name="arguments">Arguments to pass.</param>
    /// <param name="workingDirectory">Starting directory.</param>
    /// <param name="env">Environment variables to set.</param>
    /// <param name="visible">
    /// False to hide the window and redirect output to
    /// <see cref="StandardOutputLines"/> and
    /// <see cref="StandardErrorLines"/>.
    /// </param>
    /// <param name="redirector">
    /// An object to receive redirected output.
    /// </param>
    /// <param name="quoteArgs">
    /// True to ensure each argument is correctly quoted.
    /// </param>
    /// <param name="outputEncoding">
    /// Encoding for output stream.
    /// </param>
    /// <param name="errorEncoding">
    /// Encoding for error stream.
    /// </param>
    /// <param name="cancellationToken">
    /// Request cancellation.
    /// </param>
    /// <returns>A <see cref="ProcessOutputProcessor"/> object.</returns>
    public static ProcessOutputProcessor Run(
        string filename,
        IEnumerable<string> arguments,
        string workingDirectory,
        IEnumerable<KeyValuePair<string, string>> env,
        bool visible,
        ProcessOutputRedirector redirector,
        bool quoteArgs = true,
        Encoding outputEncoding = null!,
        Encoding errorEncoding = null!,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(filename))
        {
            throw new ArgumentException("Filename required", nameof(filename));
        }

        var psi = new ProcessStartInfo(filename)
        {
            Arguments = GetArguments(arguments, quoteArgs),
            CreateNoWindow = !visible,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory
        };

        if (!visible || (redirector != null))
        {
            psi.RedirectStandardError = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardInput = true;

            // only set the encoding when we're redirecting the output
            psi.StandardOutputEncoding = outputEncoding ?? psi.StandardOutputEncoding;
            psi.StandardErrorEncoding = errorEncoding ?? outputEncoding ?? psi.StandardErrorEncoding;
        }

        if (env != null)
        {
            foreach (var kv in env)
            {
                psi.EnvironmentVariables[kv.Key] = kv.Value;
            }
        }

        var process = new Process { StartInfo = psi };
        return new ProcessOutputProcessor(process, redirector, cancellationToken);
    }

    public static string GetArguments(IEnumerable<string> arguments, bool quoteArgs)
    {
        if (quoteArgs)
        {
            return string.Join(" ", arguments.Where(a => a != null).Select(QuoteSingleArgument));
        }
        else
        {
            return string.Join(" ", arguments.Where(a => a != null));
        }
    }

    public static string QuoteSingleArgument(string arg)
    {
        if (string.IsNullOrEmpty(arg))
        {
            return "\"\"";
        }

        if (arg.IndexOfAny(NeedToBeQuoted) < 0)
        {
            return arg;
        }

        if (arg.StartsWith("\"") && arg.EndsWith("\""))
        {
            var inQuote = false;
            var consecutiveBackslashes = 0;
            foreach (var c in arg)
            {
                if (c == '"')
                {
                    if (consecutiveBackslashes % 2 == 0)
                    {
                        inQuote = !inQuote;
                    }
                }

                if (c == '\\')
                {
                    consecutiveBackslashes += 1;
                }
                else
                {
                    consecutiveBackslashes = 0;
                }
            }

            if (!inQuote)
            {
                return arg;
            }
        }

        var newArg = arg.Replace("\"", "\\\"");
        if (newArg.EndsWith("\\"))
        {
            newArg += "\\";
        }

        return "\"" + newArg + "\"";
    }

    public void WriteInputLine(string line)
    {
        if (IsStarted && _redirector != null && !_redirector.CloseStandardInput())
        {
            Process.StandardInput.WriteLine(line);
            Process.StandardInput.Flush();
        }
    }

    /// <summary>
    /// Called to dispose of unmanaged resources.
    /// </summary>
    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            if (Process != null)
            {
                if (Process.StartInfo.RedirectStandardOutput)
                {
                    Process.OutputDataReceived -= OnOutputDataReceived;
                }

                if (Process.StartInfo.RedirectStandardError)
                {
                    Process.ErrorDataReceived -= OnErrorDataReceived;
                }

                Process.Dispose();
            }

            if (_redirector is IDisposable disp)
            {
                disp.Dispose();
            }

            if (_waitHandleEvent != null)
            {
                _waitHandleEvent.Set();
                _waitHandleEvent.Dispose();
            }
        }
    }

    private static IEnumerable<string> SplitLines(string source)
    {
        var start = 0;
        var end = source.IndexOfAny(EolChars);
        while (end >= start)
        {
            yield return source.Substring(start, end - start);
            start = end + 1;
            if (source[start - 1] == '\r' && start < source.Length && source[start] == '\n')
            {
                start += 1;
            }

            if (start < source.Length)
            {
                end = source.IndexOfAny(EolChars, start);
            }
            else
            {
                end = -1;
            }
        }

        if (start <= 0)
        {
            yield return source;
        }
        else if (start < source.Length)
        {
            yield return source.Substring(start);
        }
    }

    private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        if (_cancellationToken.IsCancellationRequested)
        {
            Kill();
        }

        if (e.Data == null)
        {
            bool shouldExit;
            lock (_seenNullLock)
            {
                _seenNullInOutput = true;
                shouldExit = _seenNullInError || !Process.StartInfo.RedirectStandardError;
            }

            if (shouldExit)
            {
                OnExited(Process, EventArgs.Empty);
            }
        }
        else if (!string.IsNullOrEmpty(e.Data))
        {
            foreach (var line in SplitLines(e.Data))
            {
                if (_output != null)
                {
                    _output.Add(line);
                }

                if (_redirector != null)
                {
                    _redirector.WriteLine(line);
                }
            }
        }
    }

    private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        if (_cancellationToken.IsCancellationRequested)
        {
            Kill();
        }

        if (e.Data == null)
        {
            bool shouldExit;
            lock (_seenNullLock)
            {
                _seenNullInError = true;
                shouldExit = _seenNullInOutput || !Process.StartInfo.RedirectStandardOutput;
            }

            if (shouldExit)
            {
                OnExited(Process, EventArgs.Empty);
            }
        }
        else if (!string.IsNullOrEmpty(e.Data))
        {
            foreach (var line in SplitLines(e.Data))
            {
                if (_error != null)
                {
                    _error.Add(line);
                }

                if (_redirector != null)
                {
                    _redirector.WriteErrorLine(line);
                }
            }
        }
    }

    private void FlushAndCloseOutput()
    {
        if (Process == null)
        {
            return;
        }

        if (Process.StartInfo.RedirectStandardOutput)
        {
            try
            {
                Process.CancelOutputRead();
            }
            catch (InvalidOperationException)
            {
                // Reader has already been cancelled
            }
        }

        if (Process.StartInfo.RedirectStandardError)
        {
            try
            {
                Process.CancelErrorRead();
            }
            catch (InvalidOperationException)
            {
                // Reader has already been cancelled
            }
        }

        if (_waitHandleEvent != null)
        {
            try
            {
                _waitHandleEvent.Set();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private void OnExited(object sender, EventArgs e)
    {
        if (_isDisposed || _haveRaisedExitedEvent)
        {
            return;
        }

        _haveRaisedExitedEvent = true;
        FlushAndCloseOutput();
        Exited?.Invoke(this, e);
    }

    private static bool IsCriticalException(Exception ex)
    {
        return ex is StackOverflowException ||
            ex is OutOfMemoryException ||
            ex is ThreadAbortException ||
            ex is AccessViolationException;
    }
}
