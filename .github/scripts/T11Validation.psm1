Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$script:T11Artifacts = @(
    [pscustomobject]@{
        Name = "MainVsix"
        RelativePath = "projects/RustAnalyzer/RustAnalyzer.vsix"
    },
    [pscustomobject]@{
        Name = "TestAdapter"
        RelativePath = "projects/RustAnalyzer.TestAdapter/KS.RustAnalyzer.TestAdapter.zip"
    })
$script:T11ManifestRelativePath = "t11/canonical-artifacts.json"
$script:T11VsixNamespace =
    "http://schemas.microsoft.com/developer/vsx-schema/2011"

if (-not ("RustAnalyzerVs.T11Private.JobProcess" -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace RustAnalyzerVs.T11Private
{
    public sealed class JobProcessResult
    {
        public string FilePath { get; set; }
        public string[] Arguments { get; set; }
        public long RootProcessId { get; set; }
        public long? RootExitCode { get; set; }
        public bool AssignedBeforeResume { get; set; }
        public bool JobZeroConfirmed { get; set; }
        public bool ProcessTreeQuiescent { get; set; }
        public bool TimedOut { get; set; }
        public bool TerminationRequested { get; set; }
        public bool CleanupFailed { get; set; }
        public long ElapsedMilliseconds { get; set; }
        public int TerminationReserveMilliseconds { get; set; }
        public string StartedUtc { get; set; }
        public string FinishedUtc { get; set; }
        public string Error { get; set; }
    }

    public static class JobProcess
    {
        private const uint CreateSuspended = 0x00000004;
        private const uint CreateUnicodeEnvironment = 0x00000400;
        private const uint ExtendedStartupInfoPresent = 0x00080000;
        private const uint CreateNoWindow = 0x08000000;
        private const uint StartfUseStdHandles = 0x00000100;
        private const uint JobObjectLimitKillOnJobClose = 0x00002000;
        private const int JobObjectBasicAccountingInformationClass = 1;
        private const int JobObjectAssociateCompletionPortInformationClass = 7;
        private const int JobObjectExtendedLimitInformationClass = 9;
        private const uint JobObjectMsgActiveProcessZero = 4;
        private const uint ProcThreadAttributeHandleList = 0x00020002;
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint FileShareRead = 0x00000001;
        private const uint FileShareWrite = 0x00000002;
        private const uint CreateNew = 1;
        private const uint OpenExisting = 3;
        private const uint FileAttributeNormal = 0x00000080;
        private const uint WaitObject0 = 0;
        private const uint WaitTimeout = 258;
        private const uint ErrorTimeout = 1460;
        private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);
        private static readonly IntPtr CompletionKey = new IntPtr(0x543131);

        [StructLayout(LayoutKind.Sequential)]
        private struct SecurityAttributes
        {
            public uint Length;
            public IntPtr SecurityDescriptor;
            public int InheritHandle;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct StartupInfo
        {
            public uint Size;
            public IntPtr Reserved;
            public IntPtr Desktop;
            public IntPtr Title;
            public uint X;
            public uint Y;
            public uint XSize;
            public uint YSize;
            public uint XCountChars;
            public uint YCountChars;
            public uint FillAttribute;
            public uint Flags;
            public ushort ShowWindow;
            public ushort Reserved2Size;
            public IntPtr Reserved2;
            public IntPtr StandardInput;
            public IntPtr StandardOutput;
            public IntPtr StandardError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct StartupInfoEx
        {
            public StartupInfo StartupInfo;
            public IntPtr AttributeList;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation
        {
            public IntPtr Process;
            public IntPtr Thread;
            public uint ProcessId;
            public uint ThreadId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectBasicLimitInformation
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectExtendedLimitInformation
        {
            public JobObjectBasicLimitInformation BasicLimitInformation;
            public IoCounters IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectAssociateCompletionPort
        {
            public IntPtr CompletionKey;
            public IntPtr CompletionPort;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectBasicAccountingInformation
        {
            public long TotalUserTime;
            public long TotalKernelTime;
            public long ThisPeriodTotalUserTime;
            public long ThisPeriodTotalKernelTime;
            public uint TotalPageFaultCount;
            public uint TotalProcesses;
            public uint ActiveProcesses;
            public uint TotalTerminatedProcesses;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObjectW(
            IntPtr jobAttributes,
            string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(
            IntPtr job,
            int informationClass,
            IntPtr information,
            uint informationLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool QueryInformationJobObject(
            IntPtr job,
            int informationClass,
            IntPtr information,
            uint informationLength,
            out uint returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(
            IntPtr job,
            IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool IsProcessInJob(
            IntPtr process,
            IntPtr job,
            out bool result);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateJobObject(
            IntPtr job,
            uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateIoCompletionPort(
            IntPtr fileHandle,
            IntPtr existingCompletionPort,
            IntPtr completionKey,
            uint concurrentThreads);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetQueuedCompletionStatus(
            IntPtr completionPort,
            out uint completionCode,
            out IntPtr completionKey,
            out IntPtr overlapped,
            uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool InitializeProcThreadAttributeList(
            IntPtr attributeList,
            int attributeCount,
            int flags,
            ref IntPtr size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool UpdateProcThreadAttribute(
            IntPtr attributeList,
            uint flags,
            IntPtr attribute,
            IntPtr value,
            IntPtr size,
            IntPtr previousValue,
            IntPtr returnSize);

        [DllImport("kernel32.dll")]
        private static extern void DeleteProcThreadAttributeList(
            IntPtr attributeList);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileW(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            ref SecurityAttributes securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcessW(
            string applicationName,
            StringBuilder commandLine,
            IntPtr processAttributes,
            IntPtr threadAttributes,
            bool inheritHandles,
            uint creationFlags,
            IntPtr environment,
            string currentDirectory,
            ref StartupInfoEx startupInfo,
            out ProcessInformation processInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint ResumeThread(IntPtr thread);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeProcess(
            IntPtr process,
            out uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateProcess(
            IntPtr process,
            uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(
            IntPtr handle,
            uint milliseconds);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        public static JobProcessResult Run(
            string filePath,
            string[] arguments,
            string standardOutputPath,
            string standardErrorPath,
            int timeoutSeconds,
            string workingDirectory,
            IDictionary environmentOverrides,
            string failurePoint)
        {
            if (timeoutSeconds < 1)
            {
                throw new ArgumentOutOfRangeException("timeoutSeconds");
            }

            string startedUtc = DateTime.UtcNow.ToString("O");
            long started = Stopwatch.GetTimestamp();
            long timeoutMilliseconds = checked((long)timeoutSeconds * 1000L);
            int reserveMilliseconds = (int)Math.Min(
                2000L,
                Math.Max(250L, timeoutMilliseconds / 10L));
            long hardDeadline = AddMilliseconds(started, timeoutMilliseconds);
            long executionDeadline = AddMilliseconds(
                started,
                timeoutMilliseconds - reserveMilliseconds);

            IntPtr job = IntPtr.Zero;
            IntPtr completionPort = IntPtr.Zero;
            IntPtr process = IntPtr.Zero;
            IntPtr thread = IntPtr.Zero;
            IntPtr standardInput = IntPtr.Zero;
            IntPtr standardOutput = IntPtr.Zero;
            IntPtr standardError = IntPtr.Zero;
            IntPtr attributeList = IntPtr.Zero;
            IntPtr inheritedHandles = IntPtr.Zero;
            IntPtr environment = IntPtr.Zero;
            int environmentLength = 0;
            bool assigned = false;
            bool resumed = false;
            bool activeZeroSeen = false;

            try
            {
                job = CreateJobObjectW(IntPtr.Zero, null);
                ThrowIfInvalid(job, "CreateJobObjectW");
                SetKillOnClose(job);

                completionPort = CreateIoCompletionPort(
                    InvalidHandleValue,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    1);
                ThrowIfInvalid(completionPort, "CreateIoCompletionPort");
                AssociateCompletionPort(job, completionPort);

                SecurityAttributes inheritable = new SecurityAttributes
                {
                    Length = (uint)Marshal.SizeOf(typeof(SecurityAttributes)),
                    InheritHandle = 1
                };
                standardInput = CreateFileW(
                    "NUL",
                    GenericRead,
                    FileShareRead | FileShareWrite,
                    ref inheritable,
                    OpenExisting,
                    FileAttributeNormal,
                    IntPtr.Zero);
                ThrowIfInvalidFile(standardInput, "opening NUL");
                standardOutput = CreateFileW(
                    standardOutputPath,
                    GenericWrite,
                    FileShareRead | FileShareWrite,
                    ref inheritable,
                    CreateNew,
                    FileAttributeNormal,
                    IntPtr.Zero);
                ThrowIfInvalidFile(standardOutput, "creating stdout");
                standardError = CreateFileW(
                    standardErrorPath,
                    GenericWrite,
                    FileShareRead | FileShareWrite,
                    ref inheritable,
                    CreateNew,
                    FileAttributeNormal,
                    IntPtr.Zero);
                ThrowIfInvalidFile(standardError, "creating stderr");

                StartupInfoEx startup = new StartupInfoEx();
                startup.StartupInfo.Size =
                    (uint)Marshal.SizeOf(typeof(StartupInfoEx));
                startup.StartupInfo.Flags = StartfUseStdHandles;
                startup.StartupInfo.StandardInput = standardInput;
                startup.StartupInfo.StandardOutput = standardOutput;
                startup.StartupInfo.StandardError = standardError;
                PrepareHandleList(
                    new[] { standardInput, standardOutput, standardError },
                    ref attributeList,
                    ref inheritedHandles);
                startup.AttributeList = attributeList;

                environment = BuildEnvironmentBlock(
                    environmentOverrides,
                    out environmentLength);
                if (string.Equals(
                    failurePoint,
                    "Create",
                    StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Synthetic CreateProcessW failure.");
                }

                ProcessInformation created;
                uint flags =
                    CreateSuspended |
                    CreateUnicodeEnvironment |
                    ExtendedStartupInfoPresent |
                    CreateNoWindow;
                StringBuilder commandLine = new StringBuilder(
                    BuildCommandLine(filePath, arguments));
                if (!CreateProcessW(
                    filePath,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    true,
                    flags,
                    environment,
                    string.IsNullOrWhiteSpace(workingDirectory)
                        ? null
                        : workingDirectory,
                    ref startup,
                    out created))
                {
                    throw LastError("CreateProcessW");
                }
                process = created.Process;
                thread = created.Thread;

                Close(ref standardInput);
                Close(ref standardOutput);
                Close(ref standardError);
                ReleaseAttributeList(ref attributeList, ref inheritedHandles);
                ReleaseEnvironment(ref environment, ref environmentLength);

                if (string.Equals(
                    failurePoint,
                    "Assign",
                    StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Synthetic AssignProcessToJobObject failure.");
                }
                if (!AssignProcessToJobObject(job, process))
                {
                    throw LastError("AssignProcessToJobObject");
                }
                bool isInJob;
                if (!IsProcessInJob(process, job, out isInJob))
                {
                    throw LastError("IsProcessInJob");
                }
                if (!isInJob)
                {
                    throw new InvalidOperationException(
                        "The suspended root process was not assigned to its job.");
                }
                assigned = true;

                if (string.Equals(
                    failurePoint,
                    "Resume",
                    StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Synthetic ResumeThread failure.");
                }
                if (ResumeThread(thread) == UInt32.MaxValue)
                {
                    throw LastError("ResumeThread");
                }
                resumed = true;

                bool timedOut = false;
                bool terminationRequested = false;
                bool cleanupFailed = false;
                string error = null;
                bool jobZeroConfirmed;
                try
                {
                    jobZeroConfirmed = WaitForActiveProcessZero(
                        job,
                        completionPort,
                        executionDeadline,
                        ref activeZeroSeen);
                }
                catch (Exception ex)
                {
                    jobZeroConfirmed = false;
                    error = ex.Message;
                }

                if (!jobZeroConfirmed)
                {
                    timedOut = error == null;
                    terminationRequested = true;
                    if (!TerminateJobObject(job, ErrorTimeout))
                    {
                        cleanupFailed = true;
                        error = AppendError(
                            error,
                            LastError("TerminateJobObject").Message);
                    }
                    else
                    {
                        try
                        {
                            jobZeroConfirmed = WaitForActiveProcessZero(
                                job,
                                completionPort,
                                hardDeadline,
                                ref activeZeroSeen);
                        }
                        catch (Exception ex)
                        {
                            error = AppendError(error, ex.Message);
                        }
                    }
                }

                if (!jobZeroConfirmed)
                {
                    cleanupFailed = true;
                    error = AppendError(
                        error,
                        "The Windows Job Object did not report active-process zero before the hard deadline.");
                }

                long? rootExitCode = null;
                if (jobZeroConfirmed)
                {
                    uint exitCode;
                    if (!GetExitCodeProcess(process, out exitCode))
                    {
                        cleanupFailed = true;
                        error = AppendError(
                            error,
                            LastError("GetExitCodeProcess").Message);
                    }
                    else
                    {
                        rootExitCode = exitCode;
                    }
                }

                return new JobProcessResult
                {
                    FilePath = filePath,
                    Arguments = arguments ?? new string[0],
                    RootProcessId = created.ProcessId,
                    RootExitCode = rootExitCode,
                    AssignedBeforeResume = assigned && resumed,
                    JobZeroConfirmed = jobZeroConfirmed,
                    ProcessTreeQuiescent = jobZeroConfirmed,
                    TimedOut = timedOut,
                    TerminationRequested = terminationRequested,
                    CleanupFailed = cleanupFailed,
                    ElapsedMilliseconds = ElapsedMilliseconds(started),
                    TerminationReserveMilliseconds = reserveMilliseconds,
                    StartedUtc = startedUtc,
                    FinishedUtc = DateTime.UtcNow.ToString("O"),
                    Error = error
                };
            }
            catch
            {
                TerminateFailedLaunch(
                    job,
                    completionPort,
                    process,
                    assigned,
                    hardDeadline,
                    ref activeZeroSeen);
                throw;
            }
            finally
            {
                Close(ref standardInput);
                Close(ref standardOutput);
                Close(ref standardError);
                ReleaseAttributeList(ref attributeList, ref inheritedHandles);
                ReleaseEnvironment(ref environment, ref environmentLength);
                Close(ref thread);
                Close(ref process);
                Close(ref completionPort);
                Close(ref job);
            }
        }

        public static void TerminateCurrentProcessAfterDelayForTest(
            int milliseconds,
            uint exitCode)
        {
            Thread thread = new Thread(delegate()
            {
                Thread.Sleep(milliseconds);
                TerminateProcess(GetCurrentProcess(), exitCode);
            });
            thread.IsBackground = true;
            thread.Start();
        }

        private static void SetKillOnClose(IntPtr job)
        {
            JobObjectExtendedLimitInformation information =
                new JobObjectExtendedLimitInformation();
            information.BasicLimitInformation.LimitFlags =
                JobObjectLimitKillOnJobClose;
            SetJobInformation(
                job,
                JobObjectExtendedLimitInformationClass,
                information);
        }

        private static void AssociateCompletionPort(
            IntPtr job,
            IntPtr completionPort)
        {
            JobObjectAssociateCompletionPort information =
                new JobObjectAssociateCompletionPort
                {
                    CompletionKey = CompletionKey,
                    CompletionPort = completionPort
                };
            SetJobInformation(
                job,
                JobObjectAssociateCompletionPortInformationClass,
                information);
        }

        private static void SetJobInformation<T>(
            IntPtr job,
            int informationClass,
            T information)
            where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(information, buffer, false);
                if (!SetInformationJobObject(
                    job,
                    informationClass,
                    buffer,
                    (uint)size))
                {
                    throw LastError("SetInformationJobObject");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static uint QueryActiveProcesses(IntPtr job)
        {
            int size = Marshal.SizeOf(
                typeof(JobObjectBasicAccountingInformation));
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                uint returned;
                if (!QueryInformationJobObject(
                    job,
                    JobObjectBasicAccountingInformationClass,
                    buffer,
                    (uint)size,
                    out returned))
                {
                    throw LastError("QueryInformationJobObject");
                }
                JobObjectBasicAccountingInformation information =
                    (JobObjectBasicAccountingInformation)
                    Marshal.PtrToStructure(
                        buffer,
                        typeof(JobObjectBasicAccountingInformation));
                return information.ActiveProcesses;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static bool WaitForActiveProcessZero(
            IntPtr job,
            IntPtr completionPort,
            long deadline,
            ref bool activeZeroSeen)
        {
            while (true)
            {
                if (activeZeroSeen && QueryActiveProcesses(job) == 0)
                {
                    return true;
                }

                int remaining = RemainingMilliseconds(deadline);
                if (remaining <= 0)
                {
                    return activeZeroSeen &&
                        QueryActiveProcesses(job) == 0;
                }

                uint completionCode;
                IntPtr key;
                IntPtr overlapped;
                bool completed = GetQueuedCompletionStatus(
                    completionPort,
                    out completionCode,
                    out key,
                    out overlapped,
                    (uint)Math.Min(remaining, 250));
                if (completed)
                {
                    if (key == CompletionKey &&
                        completionCode == JobObjectMsgActiveProcessZero)
                    {
                        activeZeroSeen = true;
                    }
                    continue;
                }

                int error = Marshal.GetLastWin32Error();
                if (error != WaitTimeout)
                {
                    throw new Win32Exception(
                        error,
                        "GetQueuedCompletionStatus failed.");
                }
            }
        }

        private static void TerminateFailedLaunch(
            IntPtr job,
            IntPtr completionPort,
            IntPtr process,
            bool assigned,
            long deadline,
            ref bool activeZeroSeen)
        {
            if (process == IntPtr.Zero)
            {
                return;
            }

            if (assigned)
            {
                TerminateJobObject(job, 1);
                if (completionPort != IntPtr.Zero)
                {
                    try
                    {
                        WaitForActiveProcessZero(
                            job,
                            completionPort,
                            deadline,
                            ref activeZeroSeen);
                    }
                    catch
                    {
                    }
                }
                return;
            }

            TerminateProcess(process, 1);
            int remaining = RemainingMilliseconds(deadline);
            if (remaining > 0)
            {
                WaitForSingleObject(process, (uint)remaining);
            }
        }

        private static void PrepareHandleList(
            IntPtr[] handles,
            ref IntPtr attributeList,
            ref IntPtr inheritedHandles)
        {
            IntPtr size = IntPtr.Zero;
            InitializeProcThreadAttributeList(
                IntPtr.Zero,
                1,
                0,
                ref size);
            IntPtr preparedList = Marshal.AllocHGlobal(size);
            if (!InitializeProcThreadAttributeList(
                preparedList,
                1,
                0,
                ref size))
            {
                int error = Marshal.GetLastWin32Error();
                Marshal.FreeHGlobal(preparedList);
                throw new Win32Exception(
                    error,
                    "InitializeProcThreadAttributeList failed.");
            }
            attributeList = preparedList;

            inheritedHandles = Marshal.AllocHGlobal(
                checked(IntPtr.Size * handles.Length));
            for (int index = 0; index < handles.Length; index++)
            {
                Marshal.WriteIntPtr(
                    inheritedHandles,
                    index * IntPtr.Size,
                    handles[index]);
            }
            if (!UpdateProcThreadAttribute(
                attributeList,
                0,
                new IntPtr(ProcThreadAttributeHandleList),
                inheritedHandles,
                new IntPtr(IntPtr.Size * handles.Length),
                IntPtr.Zero,
                IntPtr.Zero))
            {
                throw LastError("UpdateProcThreadAttribute");
            }
        }

        private static IntPtr BuildEnvironmentBlock(
            IDictionary overrides,
            out int characterLength)
        {
            SortedDictionary<string, string> environment =
                new SortedDictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in
                Environment.GetEnvironmentVariables())
            {
                environment[(string)entry.Key] =
                    Convert.ToString(entry.Value);
            }
            if (overrides != null)
            {
                foreach (DictionaryEntry entry in overrides)
                {
                    string name = Convert.ToString(entry.Key);
                    string value = Convert.ToString(entry.Value);
                    ValidateEnvironmentEntry(name, value);
                    environment[name] = value;
                }
            }

            StringBuilder block = new StringBuilder();
            foreach (KeyValuePair<string, string> entry in environment)
            {
                ValidateEnvironmentEntry(entry.Key, entry.Value);
                block.Append(entry.Key);
                block.Append('=');
                block.Append(entry.Value);
                block.Append('\0');
            }
            block.Append('\0');
            if (block.Length == 1)
            {
                block.Append('\0');
            }

            char[] characters = block.ToString().ToCharArray();
            characterLength = characters.Length;
            IntPtr result = Marshal.AllocHGlobal(
                checked(characterLength * sizeof(char)));
            Marshal.Copy(characters, 0, result, characterLength);
            return result;
        }

        private static void ValidateEnvironmentEntry(
            string name,
            string value)
        {
            if (string.IsNullOrEmpty(name) ||
                name.IndexOf('\0') >= 0 ||
                (name[0] != '=' && name.IndexOf('=') >= 0) ||
                value == null ||
                value.IndexOf('\0') >= 0)
            {
                throw new ArgumentException(
                    "A child-process environment entry is invalid.");
            }
        }

        private static string BuildCommandLine(
            string filePath,
            string[] arguments)
        {
            StringBuilder command = new StringBuilder();
            command.Append(QuoteArgument(filePath));
            if (arguments != null)
            {
                foreach (string argument in arguments)
                {
                    command.Append(' ');
                    command.Append(QuoteArgument(argument ?? string.Empty));
                }
            }
            return command.ToString();
        }

        private static string QuoteArgument(string argument)
        {
            if (argument.Length > 0 &&
                argument.IndexOf('"') < 0 &&
                argument.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }) < 0)
            {
                return argument;
            }

            StringBuilder quoted = new StringBuilder();
            quoted.Append('"');
            int backslashes = 0;
            foreach (char character in argument)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (character == '"')
                {
                    quoted.Append('\\', backslashes * 2 + 1);
                    quoted.Append('"');
                    backslashes = 0;
                    continue;
                }
                quoted.Append('\\', backslashes);
                backslashes = 0;
                quoted.Append(character);
            }
            quoted.Append('\\', backslashes * 2);
            quoted.Append('"');
            return quoted.ToString();
        }

        private static string AppendError(
            string current,
            string additional)
        {
            return string.IsNullOrEmpty(current)
                ? additional
                : current + " " + additional;
        }

        private static long AddMilliseconds(
            long timestamp,
            long milliseconds)
        {
            return checked(
                timestamp +
                milliseconds * Stopwatch.Frequency / 1000L);
        }

        private static int RemainingMilliseconds(long deadline)
        {
            long remaining = deadline - Stopwatch.GetTimestamp();
            if (remaining <= 0)
            {
                return 0;
            }
            long milliseconds =
                (remaining * 1000L + Stopwatch.Frequency - 1L) /
                Stopwatch.Frequency;
            return (int)Math.Min(Int32.MaxValue, milliseconds);
        }

        private static long ElapsedMilliseconds(long started)
        {
            return (Stopwatch.GetTimestamp() - started) * 1000L /
                Stopwatch.Frequency;
        }

        private static Win32Exception LastError(string operation)
        {
            return new Win32Exception(
                Marshal.GetLastWin32Error(),
                operation + " failed.");
        }

        private static void ThrowIfInvalid(
            IntPtr handle,
            string operation)
        {
            if (handle == IntPtr.Zero)
            {
                throw LastError(operation);
            }
        }

        private static void ThrowIfInvalidFile(
            IntPtr handle,
            string operation)
        {
            if (handle == IntPtr.Zero || handle == InvalidHandleValue)
            {
                throw LastError(operation);
            }
        }

        private static void ReleaseAttributeList(
            ref IntPtr attributeList,
            ref IntPtr inheritedHandles)
        {
            if (attributeList != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
                attributeList = IntPtr.Zero;
            }
            if (inheritedHandles != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(inheritedHandles);
                inheritedHandles = IntPtr.Zero;
            }
        }

        private static void ReleaseEnvironment(
            ref IntPtr environment,
            ref int characterLength)
        {
            if (environment == IntPtr.Zero)
            {
                return;
            }
            for (int index = 0; index < characterLength; index++)
            {
                Marshal.WriteInt16(environment, index * sizeof(char), 0);
            }
            Marshal.FreeHGlobal(environment);
            environment = IntPtr.Zero;
            characterLength = 0;
        }

        private static void Close(ref IntPtr handle)
        {
            if (handle == IntPtr.Zero || handle == InvalidHandleValue)
            {
                handle = IntPtr.Zero;
                return;
            }
            CloseHandle(handle);
            handle = IntPtr.Zero;
        }
    }
}
'@
}

function Write-T11Json {
    param (
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [object] $Value
    )

    $directory = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        [void](New-Item -ItemType Directory -Path $directory -Force)
    }

    $json = $Value | ConvertTo-Json -Depth 12
    [IO.File]::WriteAllText(
        $Path,
        $json + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false))
}

function Get-T11ArtifactDefinitions {
    return @($script:T11Artifacts)
}

function Get-T11Sha256 {
    param (
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [byte[]] $Bytes
    )

    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        return [Convert]::ToHexString($sha256.ComputeHash($Bytes))
    }
    finally {
        $sha256.Dispose()
    }
}

function Get-T11CanonicalLexicalPath {
    param (
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Description
    )

    if ([string]::IsNullOrWhiteSpace($Path) -or
        $Path.Contains("/") -or
        $Path -notmatch "^[A-Za-z]:\\") {
        throw "$Description must be an absolute canonical Windows path."
    }

    $root = [IO.Path]::GetPathRoot($Path)
    if ($Path.Length -gt $root.Length -and $Path.EndsWith("\")) {
        throw "$Description must not end in a directory separator."
    }

    $relative = $Path.Substring($root.Length)
    $segments = if ($relative.Length -eq 0) {
        @()
    }
    else {
        @($relative.Split("\"))
    }
    if (@($segments | Where-Object {
                [string]::IsNullOrEmpty($_) -or
                $_ -eq "." -or
                $_ -eq ".." -or
                $_.EndsWith(".") -or
                $_.EndsWith(" ") -or
                $_.Contains(":")
            }).Count -gt 0) {
        throw "$Description contains a noncanonical path segment."
    }

    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $Path.Equals(
            $fullPath,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Description is not in canonical lexical form."
    }
    return $fullPath
}

function Test-T11ReparsePoint {
    param (
        [Parameter(Mandatory)]
        [IO.FileSystemInfo] $Item
    )

    return ($Item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
}

function Get-T11DirectEntries {
    param (
        [Parameter(Mandatory)]
        [string] $Directory
    )

    return @(Get-ChildItem `
            -LiteralPath $Directory `
            -Force `
            -ErrorAction Stop)
}

function Assert-T11NoReparsePath {
    param (
        [Parameter(Mandatory)]
        [string] $AnchorPath,

        [Parameter(Mandatory)]
        [string] $Path
    )

    $anchorPath = Get-T11CanonicalLexicalPath `
        -Path $AnchorPath `
        -Description "T11 path anchor"
    $path = Get-T11CanonicalLexicalPath `
        -Path $Path `
        -Description "T11 owned path"
    if (-not $path.Equals(
            $anchorPath,
            [StringComparison]::OrdinalIgnoreCase) -and
        -not $path.StartsWith(
            "$anchorPath\",
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "The T11 owned path is outside its trusted anchor."
    }

    $anchor = Get-Item `
        -LiteralPath $anchorPath `
        -Force `
        -ErrorAction Stop
    if (-not $anchor.PSIsContainer -or (Test-T11ReparsePoint -Item $anchor)) {
        throw "The T11 path anchor must be a regular non-reparse directory."
    }

    $relative = $path.Substring($anchorPath.Length).TrimStart("\")
    if ($relative.Length -eq 0) {
        return $true
    }

    $current = $anchor
    foreach ($segment in $relative.Split("\")) {
        if (-not $current.PSIsContainer) {
            throw "A T11 path ancestor is not a directory."
        }

        $matches = @(Get-T11DirectEntries -Directory $current.FullName |
                Where-Object {
                    $_.Name.Equals(
                        $segment,
                        [StringComparison]::OrdinalIgnoreCase)
                })
        if ($matches.Count -eq 0) {
            return $false
        }
        if ($matches.Count -ne 1) {
            throw "A T11 path component is ambiguous."
        }

        $current = $matches[0]
        if (Test-T11ReparsePoint -Item $current) {
            throw "A T11 path component is a reparse point: '$($current.FullName)'."
        }
    }

    return $true
}

function Get-T11SafeSubtreeEntries {
    param (
        [Parameter(Mandatory)]
        [string] $RootPath
    )

    $root = Get-Item -LiteralPath $RootPath -Force -ErrorAction Stop
    if (-not $root.PSIsContainer -or (Test-T11ReparsePoint -Item $root)) {
        throw "The T11 subtree root must be a regular non-reparse directory."
    }

    $queue = [Collections.Generic.Queue[IO.DirectoryInfo]]::new()
    $queue.Enqueue($root)
    $entries = [Collections.Generic.List[IO.FileSystemInfo]]::new()
    while ($queue.Count -gt 0) {
        $directory = $queue.Dequeue()
        foreach ($entry in Get-T11DirectEntries -Directory $directory.FullName) {
            if (-not [IO.Path]::GetDirectoryName($entry.FullName).Equals(
                    $directory.FullName,
                    [StringComparison]::OrdinalIgnoreCase)) {
                throw "The T11 subtree contains an ambiguous child path."
            }
            if (Test-T11ReparsePoint -Item $entry) {
                throw "The T11 subtree contains a reparse point: '$($entry.FullName)'."
            }

            $entries.Add($entry)
            if ($entry.PSIsContainer) {
                $queue.Enqueue($entry)
            }
        }
    }

    return @($entries)
}

function Assert-T11OwnedDirectory {
    param (
        [Parameter(Mandatory)]
        [object] $Ownership
    )

    $requiredProperties = @(
        "AnchorPath",
        "Path",
        "ParentPath",
        "LeafName",
        "Reserved")
    foreach ($property in $requiredProperties) {
        if ($Ownership.PSObject.Properties.Name -notcontains $property) {
            throw "The T11 directory ownership record is incomplete."
        }
    }
    if (-not $Ownership.Reserved) {
        throw "The T11 directory was not reserved by this run."
    }

    $anchorPath = Get-T11CanonicalLexicalPath `
        -Path ([string]$Ownership.AnchorPath) `
        -Description "T11 directory ownership anchor"
    $path = Get-T11CanonicalLexicalPath `
        -Path ([string]$Ownership.Path) `
        -Description "T11 directory ownership path"
    $parentPath = Get-T11CanonicalLexicalPath `
        -Path ([string]$Ownership.ParentPath) `
        -Description "T11 directory ownership parent"
    if (-not [IO.Path]::GetDirectoryName($path).Equals(
            $parentPath,
            [StringComparison]::OrdinalIgnoreCase) -or
        -not [IO.Path]::GetFileName($path).Equals(
            [string]$Ownership.LeafName,
            [StringComparison]::Ordinal)) {
        throw "The T11 directory ownership record changed after reservation."
    }

    [void](Assert-T11NoReparsePath `
            -AnchorPath $anchorPath `
            -Path $parentPath)
    return $path
}

function New-T11OwnedDirectory {
    param (
        [Parameter(Mandatory)]
        [string] $AnchorPath,

        [Parameter(Mandatory)]
        [string] $Path
    )

    $anchorPath = Get-T11CanonicalLexicalPath `
        -Path $AnchorPath `
        -Description "T11 directory anchor"
    $path = Get-T11CanonicalLexicalPath `
        -Path $Path `
        -Description "T11 directory path"
    $parentPath = [IO.Path]::GetDirectoryName($path)
    [void](Assert-T11NoReparsePath `
            -AnchorPath $anchorPath `
            -Path $parentPath)
    $parent = Get-Item `
        -LiteralPath $parentPath `
        -Force `
        -ErrorAction Stop
    if (-not $parent.PSIsContainer) {
        throw "The T11 directory parent is not a directory."
    }

    $leafName = [IO.Path]::GetFileName($path)
    $existing = @(Get-T11DirectEntries -Directory $parentPath |
            Where-Object {
                $_.Name.Equals(
                    $leafName,
                    [StringComparison]::OrdinalIgnoreCase)
            })
    if ($existing.Count -gt 0) {
        throw "The T11 directory path already exists: '$path'."
    }

    $ownership = [pscustomobject]@{
        AnchorPath = $anchorPath
        Path = $path
        ParentPath = $parentPath
        LeafName = $leafName
        Reserved = $true
        Created = $false
        Removed = $false
    }
    return $ownership
}

function Initialize-T11OwnedDirectory {
    param (
        [Parameter(Mandatory)]
        [object] $Ownership
    )

    $path = Assert-T11OwnedDirectory -Ownership $Ownership
    $existing = @(Get-T11DirectEntries -Directory $Ownership.ParentPath |
            Where-Object {
                $_.Name.Equals(
                    [string]$Ownership.LeafName,
                    [StringComparison]::OrdinalIgnoreCase)
            })
    if ($existing.Count -gt 0) {
        throw "The reserved T11 directory path already exists: '$path'."
    }

    [void](New-Item -ItemType Directory -Path $path)
    if (-not (Assert-T11NoReparsePath `
            -AnchorPath $Ownership.AnchorPath `
            -Path $path)) {
        throw "The reserved T11 directory was not created."
    }
    $created = Get-Item -LiteralPath $path -Force
    if (-not $created.PSIsContainer) {
        throw "The reserved T11 path is not a directory."
    }

    $Ownership.Created = $true
    return $path
}

function Remove-T11OwnedDirectory {
    param (
        [Parameter(Mandatory)]
        [object] $Ownership
    )

    $path = Assert-T11OwnedDirectory -Ownership $Ownership
    $leafName = [IO.Path]::GetFileName($path)
    $matches = @(Get-T11DirectEntries -Directory $Ownership.ParentPath |
            Where-Object {
                $_.Name.Equals(
                    $leafName,
                    [StringComparison]::OrdinalIgnoreCase)
            })
    if ($matches.Count -gt 1) {
        throw "The run-owned T11 directory path is ambiguous."
    }

    $wasPresent = $matches.Count -eq 1
    if ($wasPresent) {
        $item = $matches[0]
        if (Test-T11ReparsePoint -Item $item) {
            throw "The run-owned T11 directory is a reparse point."
        }
        if ($item.PSIsContainer) {
            [void](Get-T11SafeSubtreeEntries -RootPath $item.FullName)
        }
        Remove-Item -LiteralPath $path -Recurse -Force
    }

    if (Test-Path -LiteralPath $path) {
        throw "The run-owned T11 directory remains after cleanup."
    }
    $remaining = @(Get-T11DirectEntries -Directory $Ownership.ParentPath |
            Where-Object {
                $_.Name.Equals(
                    $leafName,
                    [StringComparison]::OrdinalIgnoreCase)
            })
    if ($remaining.Count -gt 0) {
        throw "A filesystem object remains at the run-owned T11 path."
    }

    $Ownership.Removed = $true
    return $wasPresent
}

function Save-T11InstallerLogs {
    param (
        [Parameter(Mandatory)]
        [object] $SourceOwnership,

        [Parameter(Mandatory)]
        [object] $RawLogOwnership,

        [Parameter(Mandatory)]
        [string] $ReportPath
    )

    $report = [ordered]@{
        Status = "Failed"
        SourceDirectory = $null
        Logs = @()
        Error = $null
    }

    try {
        $sourceDirectory = Assert-T11OwnedDirectory `
            -Ownership $SourceOwnership
        $report.SourceDirectory = $sourceDirectory
        if (-not (Assert-T11NoReparsePath `
                -AnchorPath $SourceOwnership.AnchorPath `
                -Path $sourceDirectory)) {
            throw "The isolated installer log directory was not found."
        }

        $entries = @(Get-T11SafeSubtreeEntries -RootPath $sourceDirectory)
        $matching = @($entries | Where-Object {
                $_.Name -like "dd_VSIXInstaller_*.log"
            })
        $nested = @($matching | Where-Object {
                -not [IO.Path]::GetDirectoryName($_.FullName).Equals(
                    $sourceDirectory,
                    [StringComparison]::OrdinalIgnoreCase)
            })
        if ($nested.Count -gt 0) {
            throw "Native VSIXInstaller logs must be direct children of the isolated directory."
        }

        $logs = @($matching | Where-Object {
                -not $_.PSIsContainer
            })
        if ($logs.Count -ne $matching.Count) {
            throw "Every native VSIXInstaller log must be a regular file."
        }
        if ($logs.Count -eq 0) {
            throw "VSIXInstaller produced no native logs in its isolated directory."
        }

        $logsByName =
            [Collections.Generic.Dictionary[string, IO.FileInfo]]::new(
                [StringComparer]::Ordinal)
        foreach ($log in $logs) {
            $logsByName.Add($log.Name, $log)
        }
        $orderedNames = [string[]]@($logsByName.Keys)
        [Array]::Sort($orderedNames, [StringComparer]::Ordinal)
        $logs = @($orderedNames | ForEach-Object { $logsByName[$_] })

        $rawLogDirectory = Assert-T11OwnedDirectory `
            -Ownership $RawLogOwnership
        if ($rawLogDirectory.Equals(
                $sourceDirectory,
                [StringComparison]::OrdinalIgnoreCase) -or
            $rawLogDirectory.StartsWith(
                "$sourceDirectory\",
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Raw installer diagnostics must be outside the isolated source directory."
        }
        if (-not (Assert-T11NoReparsePath `
                -AnchorPath $RawLogOwnership.AnchorPath `
                -Path $rawLogDirectory)) {
            throw "The installer diagnostic directory was not created."
        }
        $rawDirectory = Get-Item -LiteralPath $rawLogDirectory -Force
        if (-not $rawDirectory.PSIsContainer -or
            (Test-T11ReparsePoint -Item $rawDirectory)) {
            throw "The installer diagnostic directory is not a regular directory."
        }
        [void](Get-T11SafeSubtreeEntries -RootPath $rawLogDirectory)

        $records = [Collections.Generic.List[object]]::new()
        $emptyLogs = [Collections.Generic.List[string]]::new()
        for ($index = 0; $index -lt $logs.Count; $index++) {
            $log = $logs[$index]
            $bytes = [IO.File]::ReadAllBytes($log.FullName)
            $evidencePath = Join-Path $rawLogDirectory (
                "{0:D3}-{1}" -f ($index + 1), $log.Name)
            [IO.File]::WriteAllBytes($evidencePath, $bytes)
            $record = [ordered]@{
                OriginalPath = $log.FullName
                OriginalName = $log.Name
                CreationTimeUtc = $log.CreationTimeUtc.ToString("O")
                LastWriteTimeUtc = $log.LastWriteTimeUtc.ToString("O")
                ByteLength = $bytes.LongLength
                Sha256 = Get-T11Sha256 -Bytes $bytes
                EvidencePath = $evidencePath
            }
            $records.Add($record)
            if ($bytes.LongLength -eq 0) {
                $emptyLogs.Add($log.FullName)
            }
        }
        $report.Logs = @($records)
        if ($emptyLogs.Count -gt 0) {
            throw "VSIXInstaller produced empty native logs: $($emptyLogs -join ', ')."
        }

        $expectedEvidence = @($records.EvidencePath)
        $unexpectedEvidence = @(Get-T11DirectEntries `
                -Directory $rawLogDirectory |
                Where-Object {
                    $expectedEvidence -notcontains $_.FullName
                })
        if ($unexpectedEvidence.Count -gt 0) {
            throw "The installer diagnostic directory contains stale raw logs."
        }

        $report.Status = "Passed"
        return [pscustomobject]$report
    }
    catch {
        $report.Error = $_.Exception.Message
        throw
    }
    finally {
        Write-T11Json -Path $ReportPath -Value $report
    }
}

function New-T11ArtifactManifest {
    param (
        [Parameter(Mandatory)]
        [string] $ArtifactRoot,

        [Parameter(Mandatory)]
        [string] $ManifestPath
    )

    $artifactRoot = [IO.Path]::GetFullPath($ArtifactRoot)
    $expectedManifestPath = [IO.Path]::GetFullPath(
        (Join-Path $artifactRoot $script:T11ManifestRelativePath))
    if ([IO.Path]::GetFullPath($ManifestPath) -ne $expectedManifestPath) {
        throw "The T11 artifact manifest must be written to '$expectedManifestPath'."
    }

    $records = foreach ($artifact in $script:T11Artifacts) {
        $path = Join-Path $artifactRoot $artifact.RelativePath
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "The canonical T11 artifact '$path' was not found."
        }

        $file = Get-Item -LiteralPath $path
        if ($file.Length -le 0) {
            throw "The canonical T11 artifact '$path' is empty."
        }

        [ordered]@{
            Name = $artifact.Name
            RelativePath = $artifact.RelativePath
            Sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
            ByteLength = $file.Length
        }
    }

    $manifest = [ordered]@{
        SchemaVersion = 1
        Artifacts = @($records)
    }
    Write-T11Json -Path $expectedManifestPath -Value $manifest
    return $manifest
}

function Test-T11ArtifactTransport {
    param (
        [Parameter(Mandatory)]
        [string] $ArtifactRoot,

        [Parameter(Mandatory)]
        [string] $ManifestPath,

        [Parameter(Mandatory)]
        [string] $ReportPath
    )

    $artifactRoot = [IO.Path]::GetFullPath($ArtifactRoot)
    $manifestPath = [IO.Path]::GetFullPath($ManifestPath)
    $report = [ordered]@{
        Status = "Failed"
        ArtifactRoot = $artifactRoot
        ManifestPath = $manifestPath
        Artifacts = @()
        UnexpectedFiles = @()
        Error = $null
    }

    try {
        if (-not (Test-Path -LiteralPath $artifactRoot -PathType Container)) {
            throw "The downloaded T11 artifact root '$artifactRoot' was not found."
        }

        $expectedManifestPath = [IO.Path]::GetFullPath(
            (Join-Path $artifactRoot $script:T11ManifestRelativePath))
        if ($manifestPath -ne $expectedManifestPath) {
            throw "The T11 artifact manifest must be '$expectedManifestPath'."
        }

        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
            throw "The downloaded T11 artifact manifest '$manifestPath' was not found."
        }

        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ($manifest.SchemaVersion -ne 1) {
            throw "The T11 artifact manifest schema version is not supported."
        }

        $records = @($manifest.Artifacts)
        if ($records.Count -ne $script:T11Artifacts.Count) {
            throw "The T11 artifact manifest must contain exactly $($script:T11Artifacts.Count) records."
        }

        $evidence = [Collections.Generic.List[object]]::new()
        foreach ($artifact in $script:T11Artifacts) {
            $record = @($records | Where-Object {
                    $_.Name -ceq $artifact.Name -and
                    $_.RelativePath -ceq $artifact.RelativePath
                })
            if ($record.Count -ne 1) {
                throw "The T11 artifact manifest must contain exactly one '$($artifact.Name)' record."
            }

            $record = $record[0]
            if ([string]$record.Sha256 -cnotmatch "^[0-9A-F]{64}$") {
                throw "The '$($artifact.Name)' SHA-256 record is invalid."
            }

            $path = Join-Path $artifactRoot $artifact.RelativePath
            $exists = Test-Path -LiteralPath $path -PathType Leaf
            $actualHash = $null
            $actualLength = $null
            if ($exists) {
                $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
                $actualLength = (Get-Item -LiteralPath $path).Length
            }

            $hashMatches = $exists -and $actualHash -ceq [string]$record.Sha256
            $lengthMatches = $exists -and $actualLength -eq [long]$record.ByteLength
            $evidence.Add([ordered]@{
                    Name = $artifact.Name
                    RelativePath = $artifact.RelativePath
                    ExpectedSha256 = [string]$record.Sha256
                    ActualSha256 = $actualHash
                    ExpectedByteLength = [long]$record.ByteLength
                    ActualByteLength = $actualLength
                    HashMatches = $hashMatches
                    ByteLengthMatches = $lengthMatches
                })
        }
        $report.Artifacts = @($evidence)

        $expectedFiles = [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
        [void]$expectedFiles.Add($script:T11ManifestRelativePath)
        foreach ($artifact in $script:T11Artifacts) {
            [void]$expectedFiles.Add($artifact.RelativePath)
        }

        $unexpected = [Collections.Generic.List[string]]::new()
        foreach ($file in Get-ChildItem -LiteralPath $artifactRoot -File -Recurse) {
            $relativePath = [IO.Path]::GetRelativePath($artifactRoot, $file.FullName).
                Replace("\", "/")
            if (-not $expectedFiles.Contains($relativePath)) {
                $unexpected.Add($relativePath)
            }
        }
        $report.UnexpectedFiles = @($unexpected)

        $failedEvidence = @($evidence | Where-Object {
                -not $_.HashMatches -or -not $_.ByteLengthMatches
            })
        if ($failedEvidence.Count -gt 0) {
            throw "One or more downloaded T11 artifacts do not match the producer records."
        }

        if ($unexpected.Count -gt 0) {
            throw "The downloaded T11 transport contains unexpected files."
        }

        $report.Status = "Passed"
        return [pscustomobject]@{
            MainVsixPath = Join-Path $artifactRoot $script:T11Artifacts[0].RelativePath
            TestAdapterPath = Join-Path $artifactRoot $script:T11Artifacts[1].RelativePath
        }
    }
    catch {
        $report.Error = $_.Exception.Message
        throw
    }
    finally {
        Write-T11Json -Path $ReportPath -Value $report
    }
}

function Get-T11VsixIdentity {
    param (
        [Parameter(Mandatory)]
        [string] $Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "VSIX '$Path' was not found."
    }

    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $manifestEntries = @($archive.Entries | Where-Object {
                $_.FullName -ceq "extension.vsixmanifest"
            })
        if ($manifestEntries.Count -ne 1) {
            throw "VSIX '$Path' must contain one root extension.vsixmanifest."
        }

        $stream = $manifestEntries[0].Open()
        $reader = [IO.StreamReader]::new($stream)
        try {
            [xml]$manifest = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
            $stream.Dispose()
        }

        $identity = $manifest.SelectSingleNode(
            "/*[local-name()='PackageManifest']/*[local-name()='Metadata']/*[local-name()='Identity']")
        $displayName = $manifest.SelectSingleNode(
            "/*[local-name()='PackageManifest']/*[local-name()='Metadata']/*[local-name()='DisplayName']")
        if (-not $identity -or
            [string]::IsNullOrWhiteSpace($identity.Id) -or
            [string]::IsNullOrWhiteSpace($identity.Version) -or
            -not $displayName) {
            throw "VSIX '$Path' has incomplete identity metadata."
        }

        return [pscustomobject]@{
            Id = [string]$identity.Id
            Version = [string]$identity.Version
            DisplayName = [string]$displayName.InnerText
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Get-T11AdapterPackageEvidence {
    param (
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string[]] $ExpectedNames,

        [Parameter(Mandatory)]
        [string] $ReportPath
    )

    $expected = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $actual = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $expectedMembers = [Collections.Generic.List[string]]::new()
    $actualMembers = [Collections.Generic.List[string]]::new()
    $invalidMembers = [Collections.Generic.List[string]]::new()
    $listing = [Collections.Generic.List[object]]::new()
    foreach ($name in $ExpectedNames) {
        $displayName = if ([string]::IsNullOrWhiteSpace($name)) {
            "<empty>"
        }
        else {
            $name
        }
        $expectedMembers.Add($displayName)
        if ([string]::IsNullOrWhiteSpace($name)) {
            $invalidMembers.Add("invalid expected: $displayName")
        }
        elseif (-not $expected.Add($name)) {
            $invalidMembers.Add("duplicate expected: $name")
        }
    }

    $archive = $null
    $entryCount = 0
    $errorMessage = $null
    try {
        if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
            throw "TestAdapter archive '$Path' was not found."
        }

        $archive = [IO.Compression.ZipFile]::OpenRead($Path)
        foreach ($entry in $archive.Entries) {
            $entryCount++
            $fullName = [string]$entry.FullName
            $displayName = if ([string]::IsNullOrWhiteSpace($fullName)) {
                "<empty>"
            }
            else {
                $fullName
            }
            $actualMembers.Add(
                "$displayName`tByteLength=$($entry.Length)`tCompressedByteLength=$($entry.CompressedLength)")
            $listing.Add([ordered]@{
                    Name = $entry.Name
                    ByteLength = $entry.Length
                    CompressedByteLength = $entry.CompressedLength
                })

            if ([string]::IsNullOrWhiteSpace($entry.Name) -or
                $entry.FullName -cne $entry.Name) {
                $invalidMembers.Add("invalid entry: $displayName")
            }
            if (-not $actual.Add($fullName)) {
                $invalidMembers.Add("duplicate entry: $displayName")
            }
            if ($entry.Length -le 0) {
                $invalidMembers.Add("empty entry: $displayName")
            }
        }
    }
    catch {
        $errorMessage = $_.Exception.Message
    }
    finally {
        if ($archive) {
            $archive.Dispose()
        }
    }

    $missing = [Collections.Generic.List[string]]::new()
    foreach ($name in $expected) {
        if (-not $actual.Contains($name)) {
            $missing.Add($name)
        }
    }
    $extra = [Collections.Generic.List[string]]::new()
    foreach ($name in $actual) {
        if (-not $expected.Contains($name)) {
            $extra.Add($name)
        }
    }

    $expectedMembers.Sort([StringComparer]::Ordinal)
    $actualMembers.Sort([StringComparer]::Ordinal)
    $missing.Sort([StringComparer]::Ordinal)
    $extra.Sort([StringComparer]::Ordinal)
    $invalidMembers.Sort([StringComparer]::Ordinal)
    if (-not $errorMessage -and
        ($missing.Count -gt 0 -or
        $extra.Count -gt 0 -or
        $invalidMembers.Count -gt 0 -or
        $entryCount -ne $expected.Count)) {
        $errorMessage =
            "TestAdapter archive membership does not match the canonical package list."
    }

    $status = if ($errorMessage) { "Failed" } else { "Passed" }
    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add("Status: $status")
    $lines.Add("Error: $(if ($errorMessage) { $errorMessage } else { '<none>' })")
    foreach ($section in @(
            [pscustomobject]@{ Name = "Expected"; Values = $expectedMembers },
            [pscustomobject]@{ Name = "Actual"; Values = $actualMembers },
            [pscustomobject]@{ Name = "Missing"; Values = $missing },
            [pscustomobject]@{ Name = "Extra"; Values = $extra },
            [pscustomobject]@{
                Name = "DuplicateOrInvalid"
                Values = $invalidMembers
            }
        )) {
        $lines.Add("$($section.Name):")
        if ($section.Values.Count -eq 0) {
            $lines.Add("<none>")
        }
        else {
            foreach ($value in $section.Values) {
                $lines.Add([string]$value)
            }
        }
    }
    $reportDirectory = Split-Path -Parent $ReportPath
    if (-not (Test-Path -LiteralPath $reportDirectory -PathType Container)) {
        [void](New-Item -ItemType Directory -Path $reportDirectory -Force)
    }
    [IO.File]::WriteAllLines(
        $ReportPath,
        $lines,
        [Text.UTF8Encoding]::new($false))

    if ($errorMessage) {
        throw $errorMessage
    }
    return @($listing)
}

function Get-T11HostSelection {
    param (
        [Parameter(Mandatory)]
        [object[]] $Instances,

        [Parameter(Mandatory)]
        [string[]] $CoreEditorInstanceIds,

        [Parameter(Mandatory)]
        [ValidateSet(17, 18)]
        [int] $VisualStudioMajorVersion,

        [Parameter(Mandatory)]
        [version] $MinimumVersion,

        [Parameter(Mandatory)]
        [version] $MaximumVersion
    )

    $allowedProducts = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    @(
        "Microsoft.VisualStudio.Product.Community",
        "Microsoft.VisualStudio.Product.Professional",
        "Microsoft.VisualStudio.Product.Enterprise"
    ) | ForEach-Object { [void]$allowedProducts.Add($_) }

    $coreIds = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($id in $CoreEditorInstanceIds) {
        if (-not [string]::IsNullOrWhiteSpace($id)) {
            [void]$coreIds.Add($id)
        }
    }

    $instanceIdCounts = @{}
    foreach ($instance in $Instances) {
        $id = [string]$instance.instanceId
        if (-not [string]::IsNullOrWhiteSpace($id)) {
            $key = $id.ToUpperInvariant()
            if (-not $instanceIdCounts.ContainsKey($key)) {
                $instanceIdCounts[$key] = 0
            }
            $instanceIdCounts[$key]++
        }
    }

    $decisions = [Collections.Generic.List[object]]::new()
    $candidates = [Collections.Generic.List[object]]::new()
    foreach ($instance in $Instances) {
        $reasons = [Collections.Generic.List[string]]::new()
        $instanceId = [string]$instance.instanceId
        $installationPath = [string]$instance.installationPath
        $installationVersion = [string]$instance.installationVersion
        $productId = [string]$instance.productId
        $productPath = [string]$instance.productPath
        $version = $null

        if ([string]::IsNullOrWhiteSpace($instanceId)) {
            $reasons.Add("Missing instance ID.")
        }
        elseif ($instanceIdCounts[$instanceId.ToUpperInvariant()] -ne 1) {
            $reasons.Add("Duplicate instance ID.")
        }

        if (-not $coreIds.Contains($instanceId)) {
            $reasons.Add("Core Editor component is missing.")
        }
        if ($instance.isComplete -isnot [bool] -or -not $instance.isComplete) {
            $reasons.Add("Installation is incomplete.")
        }
        if ($instance.isLaunchable -isnot [bool] -or -not $instance.isLaunchable) {
            $reasons.Add("Installation is not launchable.")
        }
        if (-not $allowedProducts.Contains($productId)) {
            $reasons.Add("Product is not Community, Professional, or Enterprise.")
        }

        if (-not [version]::TryParse($installationVersion, [ref]$version)) {
            $reasons.Add("Installation version is invalid.")
        }
        else {
            if ($version.Major -ne $VisualStudioMajorVersion) {
                $reasons.Add("Installation major is not $VisualStudioMajorVersion.")
            }
            if ($version -lt $MinimumVersion) {
                $reasons.Add("Installation version is below $MinimumVersion.")
            }
            if ($version -ge $MaximumVersion) {
                $reasons.Add("Installation version is not below $MaximumVersion.")
            }
        }

        $devenvPath = $null
        $vsixInstallerPath = $null
        $vstestPath = $null
        if ([string]::IsNullOrWhiteSpace($installationPath)) {
            $reasons.Add("Installation path is missing.")
        }
        else {
            $installationPath = [IO.Path]::GetFullPath($installationPath)
            $devenvPath = Join-Path $installationPath "Common7\IDE\devenv.exe"
            $vsixInstallerPath = Join-Path $installationPath "Common7\IDE\VSIXInstaller.exe"
            $vstestPath = Join-Path $installationPath `
                "Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe"

            if ([string]::IsNullOrWhiteSpace($productPath) -or
                [IO.Path]::GetFullPath($productPath) -ne
                    [IO.Path]::GetFullPath($devenvPath)) {
                $reasons.Add("Product path does not identify the selected devenv.exe.")
            }
            foreach ($tool in @($devenvPath, $vsixInstallerPath, $vstestPath)) {
                if (-not (Test-Path -LiteralPath $tool -PathType Leaf)) {
                    $reasons.Add("Required selected-host tool is missing: $tool")
                }
            }
        }

        $accepted = $reasons.Count -eq 0
        $decision = [ordered]@{
            InstanceId = $instanceId
            InstallationPath = $installationPath
            InstallationVersion = $installationVersion
            ProductId = $productId
            ProductPath = $productPath
            IsComplete = $instance.isComplete
            IsLaunchable = $instance.isLaunchable
            HasCoreEditor = $coreIds.Contains($instanceId)
            Accepted = $accepted
            RejectionReasons = @($reasons)
            DevenvPath = $devenvPath
            VsixInstallerPath = $vsixInstallerPath
            VSTestPath = $vstestPath
        }
        $decisions.Add($decision)
        if ($accepted) {
            $candidates.Add([pscustomobject]$decision)
        }
    }

    $knownIds = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($instance in $Instances) {
        if (-not [string]::IsNullOrWhiteSpace([string]$instance.instanceId)) {
            [void]$knownIds.Add([string]$instance.instanceId)
        }
    }
    $unknownCoreIds = @($coreIds | Where-Object { -not $knownIds.Contains($_) })

    return [pscustomobject]@{
        Decisions = @($decisions)
        Candidates = @($candidates)
        UnknownCoreEditorInstanceIds = $unknownCoreIds
    }
}

function Invoke-T11BoundedProcess {
    param (
        [Parameter(Mandatory)]
        [string] $FilePath,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [AllowEmptyString()]
        [string[]] $ArgumentList,

        [Parameter(Mandatory)]
        [string] $StandardOutputPath,

        [Parameter(Mandatory)]
        [string] $StandardErrorPath,

        [Parameter(Mandatory)]
        [ValidateRange(1, 1800)]
        [int] $TimeoutSeconds,

        [string] $WorkingDirectory,

        [Collections.IDictionary] $EnvironmentVariables,

        [ValidateSet("None", "Create", "Assign", "Resume")]
        [string] $TestFailurePoint = "None"
    )

    if (-not (Test-Path -LiteralPath $FilePath -PathType Leaf)) {
        throw "Process executable '$FilePath' was not found."
    }

    foreach ($path in @($StandardOutputPath, $StandardErrorPath)) {
        $directory = Split-Path -Parent $path
        if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
            [void](New-Item -ItemType Directory -Path $directory -Force)
        }
        if (Test-Path -LiteralPath $path) {
            throw "Process output path already exists: '$path'."
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory) -and
        -not (Test-Path -LiteralPath $WorkingDirectory -PathType Container)) {
        throw "Process working directory '$WorkingDirectory' was not found."
    }

    $nativeResult =
        [RustAnalyzerVs.T11Private.JobProcess]::Run(
            $FilePath,
            [string[]]$ArgumentList,
            $StandardOutputPath,
            $StandardErrorPath,
            $TimeoutSeconds,
            $WorkingDirectory,
            $EnvironmentVariables,
            $TestFailurePoint)
    $standardOutput = Get-Item `
        -LiteralPath $StandardOutputPath `
        -Force
    $standardError = Get-Item `
        -LiteralPath $StandardErrorPath `
        -Force
    return [pscustomobject][ordered]@{
        FilePath = $nativeResult.FilePath
        Arguments = @($nativeResult.Arguments)
        RootProcessId = $nativeResult.RootProcessId
        RootExitCode = $nativeResult.RootExitCode
        ExitCode = $nativeResult.RootExitCode
        AssignedBeforeResume = $nativeResult.AssignedBeforeResume
        JobZeroConfirmed = $nativeResult.JobZeroConfirmed
        ProcessTreeQuiescent = $nativeResult.ProcessTreeQuiescent
        TimedOut = $nativeResult.TimedOut
        TerminationRequested = $nativeResult.TerminationRequested
        CleanupFailed = $nativeResult.CleanupFailed
        ElapsedMilliseconds = $nativeResult.ElapsedMilliseconds
        TerminationReserveMilliseconds =
            $nativeResult.TerminationReserveMilliseconds
        StartedUtc = $nativeResult.StartedUtc
        FinishedUtc = $nativeResult.FinishedUtc
        TimeoutSeconds = $TimeoutSeconds
        StandardOutputPath = $StandardOutputPath
        StandardOutputByteLength = $standardOutput.Length
        StandardErrorPath = $StandardErrorPath
        StandardErrorByteLength = $standardError.Length
        Error = $nativeResult.Error
    }
}

function Assert-T11CleanupProcessSafety {
    param (
        [Parameter(Mandatory)]
        [ValidateRange(0, 100)]
        [int] $RequiredInvocationCount,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [object[]] $InvocationResults
    )

    if ($InvocationResults.Count -ne $RequiredInvocationCount) {
        throw "Not every required T11 invocation returned job-zero evidence."
    }
    foreach ($result in $InvocationResults) {
        foreach ($property in @(
                "AssignedBeforeResume",
                "JobZeroConfirmed",
                "ProcessTreeQuiescent",
                "CleanupFailed"
            )) {
            if ($result.PSObject.Properties.Name -notcontains $property) {
                throw "A required T11 invocation has incomplete job evidence."
            }
        }
        if (-not $result.AssignedBeforeResume -or
            -not $result.JobZeroConfirmed -or
            -not $result.ProcessTreeQuiescent -or
            $result.CleanupFailed) {
            throw "A required T11 invocation lacks confirmed job-zero evidence."
        }
    }
    return $true
}

function Resolve-T11VisualStudioHost {
    param (
        [Parameter(Mandatory)]
        [ValidateSet(17, 18)]
        [int] $VisualStudioMajorVersion,

        [Parameter(Mandatory)]
        [version] $MinimumVersion,

        [Parameter(Mandatory)]
        [version] $MaximumVersion,

        [Parameter(Mandatory)]
        [string] $DiagnosticsDirectory,

        [Parameter(Mandatory)]
        [string] $ReportPath,

        [string] $VsWherePath = (Join-Path ${env:ProgramFiles(x86)} `
                "Microsoft Visual Studio\Installer\vswhere.exe")
    )

    $report = [ordered]@{
        Status = "Failed"
        VisualStudioMajorVersion = $VisualStudioMajorVersion
        MinimumVersion = $MinimumVersion.ToString()
        MaximumVersion = $MaximumVersion.ToString()
        VsWherePath = $VsWherePath
        Queries = @()
        DiscoveredInstances = @()
        UnknownCoreEditorInstanceIds = @()
        SelectedInstance = $null
        Error = $null
    }

    try {
        if (-not (Test-Path -LiteralPath $VsWherePath -PathType Leaf)) {
            throw "vswhere was not found at '$VsWherePath'."
        }

        $queries = @(
            [pscustomobject]@{
                Name = "all"
                Arguments = @(
                    "-all",
                    "-prerelease",
                    "-products",
                    "*",
                    "-format",
                    "json",
                    "-utf8")
            },
            [pscustomobject]@{
                Name = "core-editor"
                Arguments = @(
                    "-all",
                    "-prerelease",
                    "-products",
                    "*",
                    "-requires",
                    "Microsoft.VisualStudio.Component.CoreEditor",
                    "-format",
                    "json",
                    "-utf8")
            })

        $outputs = @{}
        $queryEvidence = [Collections.Generic.List[object]]::new()
        foreach ($query in $queries) {
            $stdoutPath = Join-Path $DiagnosticsDirectory "vswhere-$($query.Name).json"
            $stderrPath = Join-Path $DiagnosticsDirectory "vswhere-$($query.Name).stderr.log"
            $result = Invoke-T11BoundedProcess `
                -FilePath $VsWherePath `
                -ArgumentList $query.Arguments `
                -StandardOutputPath $stdoutPath `
                -StandardErrorPath $stderrPath `
                -TimeoutSeconds 30
            $queryEvidence.Add($result)
            $report.Queries = @($queryEvidence)
            if (-not $result.AssignedBeforeResume -or
                -not $result.JobZeroConfirmed -or
                -not $result.ProcessTreeQuiescent -or
                $result.CleanupFailed) {
                throw "The vswhere '$($query.Name)' query did not complete with confirmed job-zero evidence."
            }
            if ($result.TimedOut) {
                throw "The vswhere '$($query.Name)' query timed out."
            }
            if ($result.ExitCode -ne 0) {
                throw "The vswhere '$($query.Name)' query exited with code $($result.ExitCode)."
            }

            $raw = Get-Content -LiteralPath $stdoutPath -Raw
            if ([string]::IsNullOrWhiteSpace($raw)) {
                throw "The vswhere '$($query.Name)' query returned no JSON."
            }
            $outputs[$query.Name] = @($raw | ConvertFrom-Json)
        }
        $coreIds = @($outputs["core-editor"] | ForEach-Object {
                [string]$_.instanceId
            })
        $selection = Get-T11HostSelection `
            -Instances @($outputs["all"]) `
            -CoreEditorInstanceIds $coreIds `
            -VisualStudioMajorVersion $VisualStudioMajorVersion `
            -MinimumVersion $MinimumVersion `
            -MaximumVersion $MaximumVersion
        $report.DiscoveredInstances = $selection.Decisions
        $report.UnknownCoreEditorInstanceIds =
            $selection.UnknownCoreEditorInstanceIds

        if ($selection.UnknownCoreEditorInstanceIds.Count -gt 0) {
            throw "The Core Editor query returned unknown Visual Studio instances."
        }
        if ($selection.Candidates.Count -ne 1) {
            throw "Expected exactly one complete Visual Studio $VisualStudioMajorVersion Core Editor installation in [$MinimumVersion,$MaximumVersion); found $($selection.Candidates.Count)."
        }

        $report.Status = "Passed"
        $report.SelectedInstance = $selection.Candidates[0]
        return $selection.Candidates[0]
    }
    catch {
        $report.Error = $_.Exception.Message
        throw
    }
    finally {
        Write-T11Json -Path $ReportPath -Value $report
    }
}

function Get-T11ProfilePaths {
    param (
        [Parameter(Mandatory)]
        [string] $LocalAppData,

        [Parameter(Mandatory)]
        [ValidateSet(17, 18)]
        [int] $VisualStudioMajorVersion,

        [Parameter(Mandatory)]
        [string] $InstanceId,

        [Parameter(Mandatory)]
        [ValidatePattern("^[A-Za-z][A-Za-z0-9]{5,63}$")]
        [string] $RootSuffix
    )

    if ($InstanceId -notmatch "^[A-Za-z0-9]+$") {
        throw "The selected Visual Studio instance ID is not path-safe."
    }

    $localAppData = Get-T11CanonicalLexicalPath `
        -Path $LocalAppData `
        -Description "LOCALAPPDATA"
    $profileParent = Join-Path $localAppData "Microsoft\VisualStudio"
    $profileName =
        "$VisualStudioMajorVersion.0_$InstanceId$RootSuffix"
    return [pscustomobject]@{
        LocalAppData = $localAppData
        ProfileParent = $profileParent
        ProfileName = $profileName
        ProfilePath = Join-Path $profileParent $profileName
    }
}

function Assert-T11ProfileOwnership {
    param (
        [Parameter(Mandatory)]
        [object] $Ownership
    )

    $requiredProperties = @(
        "LocalAppData",
        "VisualStudioMajorVersion",
        "InstanceId",
        "RootSuffix",
        "ProfileParent",
        "ProfileName",
        "OwnedProfilePath",
        "Reserved")
    foreach ($property in $requiredProperties) {
        if ($Ownership.PSObject.Properties.Name -notcontains $property) {
            throw "The Visual Studio profile ownership record is incomplete."
        }
    }
    if (-not $Ownership.Reserved) {
        throw "The Visual Studio profile was not reserved by this run."
    }

    $paths = Get-T11ProfilePaths `
        -LocalAppData ([string]$Ownership.LocalAppData) `
        -VisualStudioMajorVersion ([int]$Ownership.VisualStudioMajorVersion) `
        -InstanceId ([string]$Ownership.InstanceId) `
        -RootSuffix ([string]$Ownership.RootSuffix)
    [void](Get-T11CanonicalLexicalPath `
            -Path ([string]$Ownership.ProfileParent) `
            -Description "Reserved profile parent")
    [void](Get-T11CanonicalLexicalPath `
            -Path ([string]$Ownership.OwnedProfilePath) `
            -Description "Reserved profile path")
    if (-not $paths.ProfileParent.Equals(
            [string]$Ownership.ProfileParent,
            [StringComparison]::OrdinalIgnoreCase) -or
        -not $paths.ProfileName.Equals(
            [string]$Ownership.ProfileName,
            [StringComparison]::Ordinal) -or
        -not $paths.ProfilePath.Equals(
            [string]$Ownership.OwnedProfilePath,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "The Visual Studio profile ownership record changed after reservation."
    }

    [void](Assert-T11NoReparsePath `
            -AnchorPath $paths.LocalAppData `
            -Path $paths.ProfileParent)
    return $paths
}

function Get-T11ProfileSuffixEntries {
    param (
        [Parameter(Mandatory)]
        [object] $Ownership
    )

    $paths = Assert-T11ProfileOwnership -Ownership $Ownership
    if (-not (Assert-T11NoReparsePath `
            -AnchorPath $paths.LocalAppData `
            -Path $paths.ProfileParent)) {
        return @()
    }

    return @(Get-T11DirectEntries -Directory $paths.ProfileParent |
            Where-Object {
                $_.Name.EndsWith(
                    [string]$Ownership.RootSuffix,
                    [StringComparison]::OrdinalIgnoreCase)
            })
}

function Read-T11InstalledManifest {
    param (
        [Parameter(Mandatory)]
        [IO.FileInfo] $Manifest,

        [Parameter(Mandatory)]
        [int] $MaximumByteLength
    )

    $fileStream = [IO.File]::Open(
        $Manifest.FullName,
        [IO.FileMode]::Open,
        [IO.FileAccess]::Read,
        [IO.FileShare]::Read)
    try {
        $byteLength = $fileStream.Length
        if ($byteLength -le 0 -or
            $byteLength -gt $MaximumByteLength) {
            throw "The installed extension manifest has an invalid byte length."
        }

        $bytes = [byte[]]::new([int]$byteLength)
        $offset = 0
        while ($offset -lt $bytes.Length) {
            $read = $fileStream.Read(
                $bytes,
                $offset,
                $bytes.Length - $offset)
            if ($read -eq 0) {
                break
            }
            $offset += $read
        }
        if ($offset -ne $bytes.Length -or $fileStream.ReadByte() -ne -1) {
            throw "The installed extension manifest changed while being read."
        }
    }
    finally {
        $fileStream.Dispose()
    }

    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $settings.MaxCharactersInDocument = $MaximumByteLength
    $stream = [IO.MemoryStream]::new($bytes, $false)
    $reader = $null
    try {
        $reader = [Xml.XmlReader]::Create($stream, $settings)
        $document = [Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($reader)
    }
    finally {
        if ($reader) {
            $reader.Dispose()
        }
        $stream.Dispose()
    }

    $root = $document.DocumentElement
    if (-not $root -or $root.LocalName -cne "PackageManifest") {
        throw "The installed extension manifest has no PackageManifest root."
    }
    if ($root.NamespaceURI -cne $script:T11VsixNamespace) {
        throw "PackageManifest must use the exact VSIX 2011 namespace."
    }
    $metadata = @($root.ChildNodes | Where-Object {
            $_.NodeType -eq [Xml.XmlNodeType]::Element -and
            $_.LocalName -ceq "Metadata"
        })
    if ($metadata.Count -ne 1) {
        throw "The installed extension manifest must contain exactly one Metadata element."
    }
    if ($metadata[0].NamespaceURI -cne $script:T11VsixNamespace) {
        throw "Metadata must use the exact VSIX 2011 namespace."
    }
    $identities = @($metadata[0].ChildNodes | Where-Object {
            $_.NodeType -eq [Xml.XmlNodeType]::Element -and
            $_.LocalName -ceq "Identity"
        })
    if ($identities.Count -ne 1) {
        throw "The installed extension manifest must contain exactly one Identity element."
    }
    if ($identities[0].NamespaceURI -cne $script:T11VsixNamespace) {
        throw "Identity must use the exact VSIX 2011 namespace."
    }

    return [pscustomobject]@{
        Document = $document
        Identity = $identities[0]
        Bytes = $bytes
    }
}

function Get-T11InstalledExtensionEvidence {
    param (
        [Parameter(Mandatory)]
        [object] $Ownership,

        [Parameter(Mandatory)]
        [string] $ExtensionId,

        [Parameter(Mandatory)]
        [string] $ExtensionVersion,

        [Parameter(Mandatory)]
        [string] $ReportPath,

        [Parameter(Mandatory)]
        [ValidateRange(1, 120)]
        [int] $TimeoutSeconds,

        [ValidateRange(1, 10485760)]
        [int] $MaximumManifestByteLength = 1048576
    )

    $report = [ordered]@{
        Status = "Failed"
        ProfilePath = $null
        ExpectedId = $ExtensionId
        ExpectedVersion = $ExtensionVersion
        ExtensionDirectories = @()
        ExtensionDirectory = $null
        InstalledManifest = $null
        Error = $null
    }

    try {
        $paths = Assert-T11ProfileOwnership -Ownership $Ownership
        $report.ProfilePath = $paths.ProfilePath
        $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
        do {
            $suffixEntries = @(Get-T11ProfileSuffixEntries `
                    -Ownership $Ownership)
            $unexpectedProfiles = @($suffixEntries | Where-Object {
                    $_.Name -cne $paths.ProfileName -or
                    $_.FullName -cne $paths.ProfilePath
                })
            if ($unexpectedProfiles.Count -gt 0 -or
                $suffixEntries.Count -gt 1) {
                throw "The reserved root suffix became ambiguous."
            }
            if ($suffixEntries.Count -eq 0) {
                Start-Sleep -Milliseconds 250
                continue
            }

            $profile = $suffixEntries[0]
            if (Test-T11ReparsePoint -Item $profile) {
                throw "The exact reserved profile is a reparse point."
            }
            if (-not $profile.PSIsContainer) {
                throw "The exact reserved profile is not a regular directory."
            }

            $extensionsPath = Join-Path $paths.ProfilePath "Extensions"
            if (-not (Assert-T11NoReparsePath `
                    -AnchorPath $paths.LocalAppData `
                    -Path $extensionsPath)) {
                Start-Sleep -Milliseconds 250
                continue
            }
            $extensions = Get-Item -LiteralPath $extensionsPath -Force
            if (-not $extensions.PSIsContainer) {
                throw "The profile Extensions path is not a directory."
            }

            $extensionEntries = @(Get-T11DirectEntries `
                    -Directory $extensionsPath)
            $reparseEntries = @($extensionEntries | Where-Object {
                    Test-T11ReparsePoint -Item $_
                })
            if ($reparseEntries.Count -gt 0) {
                throw "The profile Extensions directory contains a reparse point."
            }
            $extensionDirectories = @($extensionEntries | Where-Object {
                    $_.PSIsContainer
                })
            $report.ExtensionDirectories = @($extensionDirectories |
                    ForEach-Object { $_.FullName })
            if ($extensionDirectories.Count -eq 0) {
                Start-Sleep -Milliseconds 250
                continue
            }
            if ($extensionDirectories.Count -ne 1) {
                throw "The isolated profile must contain exactly one immediate extension directory."
            }

            $extensionDirectory = $extensionDirectories[0]
            $expectedExtensionDirectory = Join-Path `
                $extensionsPath `
                $extensionDirectory.Name
            if ([string]::IsNullOrWhiteSpace($extensionDirectory.Name) -or
                $extensionDirectory.FullName -cne
                    $expectedExtensionDirectory) {
                throw "The installed extension directory is not an exact direct child of Extensions."
            }
            $report.ExtensionDirectory = $extensionDirectory.FullName
            [void](Get-T11CanonicalLexicalPath `
                    -Path $extensionDirectory.FullName `
                    -Description "Installed extension directory")
            $manifestEntries = @(Get-T11DirectEntries `
                    -Directory $extensionDirectory.FullName |
                    Where-Object {
                        $_.Name.EndsWith(
                            ".vsixmanifest",
                            [StringComparison]::OrdinalIgnoreCase)
                    })
            if ($manifestEntries.Count -eq 0) {
                Start-Sleep -Milliseconds 250
                continue
            }
            if ($manifestEntries.Count -ne 1 -or
                $manifestEntries[0].PSIsContainer -or
                $manifestEntries[0].Name -cne "extension.vsixmanifest" -or
                $manifestEntries[0].FullName -cne
                    (Join-Path `
                        $extensionDirectory.FullName `
                        "extension.vsixmanifest") -or
                (Test-T11ReparsePoint -Item $manifestEntries[0])) {
                throw "The extension directory must contain exactly one direct regular extension.vsixmanifest."
            }

            $manifest = $manifestEntries[0]
            [void](Assert-T11NoReparsePath `
                    -AnchorPath $paths.LocalAppData `
                    -Path $manifest.FullName)
            $parsed = Read-T11InstalledManifest `
                -Manifest $manifest `
                -MaximumByteLength $MaximumManifestByteLength
            $installedId = [string]$parsed.Identity.GetAttribute("Id")
            $installedVersion =
                [string]$parsed.Identity.GetAttribute("Version")
            $report.InstalledManifest = [ordered]@{
                Path = $manifest.FullName
                CreationTimeUtc = $manifest.CreationTimeUtc.ToString("O")
                LastWriteTimeUtc = $manifest.LastWriteTimeUtc.ToString("O")
                ByteLength = $parsed.Bytes.LongLength
                Sha256 = Get-T11Sha256 -Bytes $parsed.Bytes
                Id = $installedId
                Version = $installedVersion
                Namespace = $parsed.Identity.NamespaceURI
            }
            if ($installedId -cne $ExtensionId) {
                throw "Installed extension identity '$installedId' does not match '$ExtensionId'."
            }
            if ($installedVersion -cne $ExtensionVersion) {
                throw "Installed extension version '$installedVersion' does not match '$ExtensionVersion'."
            }

            $report.Status = "Passed"
            return [pscustomobject]@{
                ProfilePath = $paths.ProfilePath
                ExtensionDirectory = $extensionDirectory.FullName
                ManifestPath = $manifest.FullName
                Id = $installedId
                Version = $installedVersion
                ByteLength = $parsed.Bytes.LongLength
                Sha256 = $report.InstalledManifest.Sha256
            }
        } while ([DateTime]::UtcNow -lt $deadline)

        throw "Installed extension '$ExtensionId' was not found in the exact reserved profile."
    }
    catch {
        $report.Error = $_.Exception.Message
        throw
    }
    finally {
        Write-T11Json -Path $ReportPath -Value $report
    }
}

function New-T11ProfileOwnership {
    param (
        [Parameter(Mandatory)]
        [string] $LocalAppData,

        [Parameter(Mandatory)]
        [ValidateSet(17, 18)]
        [int] $VisualStudioMajorVersion,

        [Parameter(Mandatory)]
        [string] $InstanceId,

        [Parameter(Mandatory)]
        [ValidatePattern("^[A-Za-z][A-Za-z0-9]{5,63}$")]
        [string] $RootSuffix
    )

    $paths = Get-T11ProfilePaths `
        -LocalAppData $LocalAppData `
        -VisualStudioMajorVersion $VisualStudioMajorVersion `
        -InstanceId $InstanceId `
        -RootSuffix $RootSuffix
    $profileParentExists = Assert-T11NoReparsePath `
        -AnchorPath $paths.LocalAppData `
        -Path $paths.ProfileParent
    $existingProfiles = @()
    if ($profileParentExists) {
        $profileParent = Get-Item `
            -LiteralPath $paths.ProfileParent `
            -Force
        if (-not $profileParent.PSIsContainer) {
            throw "The Visual Studio profile parent is not a directory."
        }
        $existingProfiles = @(Get-T11DirectEntries `
                -Directory $paths.ProfileParent |
                Where-Object {
                    $_.Name.EndsWith(
                        $RootSuffix,
                        [StringComparison]::OrdinalIgnoreCase)
                })
    }
    if ($existingProfiles.Count -gt 0) {
        throw "The supposedly unique root suffix '$RootSuffix' already exists."
    }

    return [pscustomobject]@{
        LocalAppData = $paths.LocalAppData
        VisualStudioMajorVersion = $VisualStudioMajorVersion
        InstanceId = $InstanceId
        RootSuffix = $RootSuffix
        ProfileParent = $paths.ProfileParent
        ProfileName = $paths.ProfileName
        OwnedProfilePath = $paths.ProfilePath
        Reserved = $true
        Removed = $false
    }
}

function Remove-T11OwnedProfile {
    param (
        [Parameter(Mandatory)]
        [object] $Ownership
    )

    $paths = Assert-T11ProfileOwnership -Ownership $Ownership
    $profileParentExists = Assert-T11NoReparsePath `
        -AnchorPath $paths.LocalAppData `
        -Path $paths.ProfileParent
    $profileEntries = @()
    if ($profileParentExists) {
        $profileParent = Get-Item `
            -LiteralPath $paths.ProfileParent `
            -Force
        if (-not $profileParent.PSIsContainer) {
            throw "The Visual Studio profile parent is not a directory."
        }
        $profileEntries = @(Get-T11DirectEntries `
                -Directory $paths.ProfileParent |
                Where-Object {
                    $_.Name.Equals(
                        $paths.ProfileName,
                        [StringComparison]::OrdinalIgnoreCase)
                })
    }
    if ($profileEntries.Count -gt 1) {
        throw "The exact run-owned Visual Studio profile path is ambiguous."
    }

    $wasPresent = $profileEntries.Count -eq 1
    if ($wasPresent) {
        $profile = $profileEntries[0]
        if ($profile.Name -cne $paths.ProfileName -or
            $profile.FullName -cne $paths.ProfilePath) {
            throw "The exact run-owned Visual Studio profile path is ambiguous."
        }
        if (Test-T11ReparsePoint -Item $profile) {
            throw "The run-owned Visual Studio profile is a reparse point."
        }
        if ($profile.PSIsContainer) {
            [void](Get-T11SafeSubtreeEntries -RootPath $profile.FullName)
        }
        Remove-Item -LiteralPath $paths.ProfilePath -Recurse -Force
    }
    if (Test-Path -LiteralPath $paths.ProfilePath) {
        throw "The run-owned Visual Studio profile remains after cleanup."
    }
    $remaining = @()
    if (Test-Path `
        -LiteralPath $paths.ProfileParent `
        -PathType Container) {
        $remaining = @(Get-T11DirectEntries `
                -Directory $paths.ProfileParent |
                Where-Object {
                    $_.Name.Equals(
                        $paths.ProfileName,
                        [StringComparison]::OrdinalIgnoreCase)
                })
    }
    if ($remaining.Count -gt 0) {
        throw "A filesystem object remains at the run-owned Visual Studio profile path."
    }

    $Ownership.Removed = $true
    return $wasPresent
}

function Test-T11PackageLoadFault {
    param (
        [Parameter(Mandatory)]
        [string] $Text,

        [Parameter(Mandatory)]
        [string[]] $MatchedScopeTokens
    )

    if ($Text -match
        "(?i)\bPackage Load (?:Failure|Failed|Error)\b|\bSetSite (?:failed|failure) for package\b|\bCreateInstance failed for package\b") {
        return $true
    }

    $failure = "(?:failed to load|did not load(?: correctly)?|could not be loaded|cannot be loaded)"
    foreach ($token in $MatchedScopeTokens) {
        $scope = [regex]::Escape($token)
        if ($Text -match
            "(?i)(?:the\s+)?['""]?$scope['""]?\s+package\s+$failure[.!]?\s*$|\bpackage\s+['""]?$scope['""]?\s+$failure[.!]?\s*$") {
            return $true
        }
    }

    return $false
}

function Get-T11ActivityLogAnalysis {
    param (
        [Parameter(Mandatory)]
        [string] $ActivityLogPath,

        [Parameter(Mandatory)]
        [string[]] $ScopeTokens,

        [Parameter(Mandatory)]
        [string] $ReportPath
    )

    $report = [ordered]@{
        Status = "Failed"
        ActivityLogPath = $ActivityLogPath
        EntryCount = 0
        ErrorCount = 0
        ScopedErrors = @()
        BlockingErrorCount = 0
        BlockingErrors = @()
        Error = $null
    }

    try {
        if (-not (Test-Path -LiteralPath $ActivityLogPath -PathType Leaf) -or
            (Get-Item -LiteralPath $ActivityLogPath).Length -le 0) {
            throw "Visual Studio did not produce a non-empty ActivityLog.xml."
        }

        [xml]$activityLog = Get-Content -LiteralPath $ActivityLogPath -Raw
        $entries = @($activityLog.SelectNodes("//*[local-name()='entry']"))
        $report.EntryCount = $entries.Count
        $errors = @($entries | Where-Object {
                $type = $_.SelectSingleNode("*[local-name()='type']")
                $type -and $type.InnerText.Equals(
                    "Error",
                    [StringComparison]::OrdinalIgnoreCase)
            })
        $report.ErrorCount = $errors.Count

        $scoped = [Collections.Generic.List[object]]::new()
        $blocking = [Collections.Generic.List[object]]::new()
        foreach ($entry in $errors) {
            $text = $entry.InnerText
            $matchedTokens = @($ScopeTokens | Where-Object {
                    -not [string]::IsNullOrWhiteSpace($_) -and
                    $text.IndexOf($_, [StringComparison]::OrdinalIgnoreCase) -ge 0
                })
            if ($matchedTokens.Count -eq 0) {
                continue
            }

            $getValue = {
                param ([string] $Name)
                $node = $entry.SelectSingleNode("*[local-name()='$Name']")
                if ($node) { return [string]$node.InnerText }
                return $null
            }
            $description = & $getValue "description"
            $packageFaultText = [string]$description
            $category = if ($text -match "(?i)\bregistration\b|\bpkgdef\b|(?:failed|failure|error|exception).{0,80}\bregister(?:ed|ing)?\b|\bregister(?:ed|ing)?\b.{0,80}(?:failed|failure|error|exception)") {
                "Registration"
            }
            elseif ($text -match "(?i)\bcomposition\b|\bMEF\b|CompositionException|ComposablePart") {
                "Composition"
            }
            elseif ($text -match "(?i)\bassembly binding\b|(?:could not|cannot|failed to|unable to) load (?:file or )?assembly|\bassembly load (?:failed|failure|error)\b|FileLoadException|FileNotFoundException|BadImageFormatException|\bfusion log\b") {
                "Binding"
            }
            elseif (Test-T11PackageLoadFault `
                    -Text $packageFaultText `
                    -MatchedScopeTokens @($matchedTokens | Where-Object {
                            $packageFaultText.IndexOf(
                                $_,
                                [StringComparison]::OrdinalIgnoreCase) -ge 0
                        })) {
                "PackageLoad"
            }
            else {
                $null
            }

            $scopedError = [ordered]@{
                    Record = & $getValue "record"
                    Time = & $getValue "time"
                    Source = & $getValue "source"
                    Description = $description
                    Guid = & $getValue "guid"
                    Category = $category
                    BlocksValidation = $null -ne $category
                    MatchedTokens = $matchedTokens
                }
            $scoped.Add($scopedError)
            if ($category) {
                $blocking.Add($scopedError)
            }
        }
        $report.ScopedErrors = @($scoped)
        $report.BlockingErrorCount = $blocking.Count
        $report.BlockingErrors = @($blocking)
        if ($blocking.Count -gt 0) {
            throw "ActivityLog.xml contains $($blocking.Count) approved main-extension fault(s)."
        }

        $report.Status = "Passed"
        return [pscustomobject]$report
    }
    catch {
        $report.Error = $_.Exception.Message
        throw
    }
    finally {
        Write-T11Json -Path $ReportPath -Value $report
    }
}

Export-ModuleMember -Function `
    Get-T11ArtifactDefinitions, `
    New-T11OwnedDirectory, `
    Initialize-T11OwnedDirectory, `
    Remove-T11OwnedDirectory, `
    Save-T11InstallerLogs, `
    New-T11ArtifactManifest, `
    Test-T11ArtifactTransport, `
    Get-T11VsixIdentity, `
    Get-T11AdapterPackageEvidence, `
    Get-T11HostSelection, `
    Invoke-T11BoundedProcess, `
    Assert-T11CleanupProcessSafety, `
    Resolve-T11VisualStudioHost, `
    Get-T11InstalledExtensionEvidence, `
    New-T11ProfileOwnership, `
    Remove-T11OwnedProfile, `
    Get-T11ActivityLogAnalysis
