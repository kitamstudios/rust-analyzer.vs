using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace KS.RustAnalyzer.TestAdapter.Common;

/// <summary>
/// A utility class to determine a process parent.
/// Modified from: https://stackoverflow.com/a/3346055/6196679.
/// </summary>
public static class ProcessExtensions
{
    /// <summary>
    /// Get processes that match the following:
    /// - No parent or parent started after.
    ///
    /// NOTE:
    /// - Will get such processes for all users.
    /// </summary>
    public static Process[] GetOrphanedProcesses(this string process)
    {
        return process
            .GetProcessesByName()
            .Where(p =>
            {
                var parent = p.GetParentProcessId().GetProcessByIdSafe();
                return parent?.HasExited ?? true
                    || parent?.StartTime > p.StartTime;
            })
            .ToArray();
    }

    public static void KillSafe(this Process proc)
    {
        try
        {
            proc.Kill();
        }
        catch (InvalidOperationException)
        {
            // NOTE: Already exited. Do nothing.
        }
    }

    public static Process GetProcessByIdSafe(this int id)
    {
        try
        {
            return id.GetProcessById();
        }
        catch
        {
            return null;
        }
    }

    public static Process GetProcessById(this int id)
    {
        return Process.GetProcessById(id);
    }

    public static Process[] GetProcessesByName(this string name)
    {
        return Process.GetProcessesByName(name);
    }

    /// <summary>
    /// Gets the parent process of the current process.
    /// </summary>
    /// <returns>An instance of the Process class.</returns>
    public static int GetParentProcessId(this Process p)
    {
        return GetProcessBasicInformation(p.Handle)
            .InheritedFromUniqueProcessId.ToInt32();
    }

    /// <summary>
    /// Gets the parent process of specified process.
    /// </summary>
    /// <param name="pid">The process id.</param>
    /// <returns>An instance of the Process class.</returns>
    public static Process GetParentProcess(this int pid)
    {
        var process = Process.GetProcessById(pid);
        return GetParentProcess(process.Handle);
    }

    /// <summary>
    /// Gets the parent process of a specified process.
    /// </summary>
    /// <param name="handle">The process handle.</param>
    /// <returns>An instance of the Process class.</returns>
    public static Process GetParentProcess(this IntPtr handle)
    {
        var pbi = GetProcessBasicInformation(handle);
        var ppid = pbi.InheritedFromUniqueProcessId.ToInt32();
        try
        {
            return Process.GetProcessById(ppid);
        }
        catch (ArgumentException)
        {
            // not found
            return null;
        }
    }

    public static string GetProcessOwnerUser(this Process process)
    {
        IntPtr processHandle = IntPtr.Zero;
        try
        {
            OpenProcessToken(process.Handle, 8, out processHandle);
            var wi = new WindowsIdentity(processHandle);
            return wi.Name;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (processHandle != IntPtr.Zero)
            {
                CloseHandle(processHandle);
            }
        }
    }

    private static PROCESS_BASIC_INFORMATION GetProcessBasicInformation(IntPtr handle)
    {
        var pbi = default(PROCESS_BASIC_INFORMATION);
        int returnLength;
        int status = NtQueryInformationProcess(handle, 0, ref pbi, Marshal.SizeOf(pbi), out returnLength);
        if (status != 0)
        {
            throw new Win32Exception(status);
        }

        return pbi;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass, ref PROCESS_BASIC_INFORMATION processInformation, int processInformationLength, out int returnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}

/// <summary>
/// These members must match PROCESS_BASIC_INFORMATION.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct PROCESS_BASIC_INFORMATION
{
    public IntPtr Reserved1;
    public IntPtr PebBaseAddress;
#pragma warning disable SA1310 // Field names should not contain underscore
    public IntPtr Reserved2_0;
    public IntPtr Reserved2_1;
#pragma warning restore SA1310 // Field names should not contain underscore
    public IntPtr UniqueProcessId;
    public IntPtr InheritedFromUniqueProcessId;
}