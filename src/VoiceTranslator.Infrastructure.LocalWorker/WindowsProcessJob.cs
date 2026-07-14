using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace VoiceTranslator.Infrastructure.LocalWorker;

internal sealed class WindowsProcessJob : IDisposable
{
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private readonly SafeFileHandle handle;

    private WindowsProcessJob(SafeFileHandle handle)
    {
        this.handle = handle;
    }

    public static WindowsProcessJob CreateAndAssign(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Worker process jobs require Windows.");
        }

        var jobHandle = new SafeFileHandle(
            NativeMethods.CreateJobObject(IntPtr.Zero, null),
            ownsHandle: true);
        if (jobHandle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            SetKillOnClose(jobHandle);
            if (!NativeMethods.AssignProcessToJobObject(
                    jobHandle,
                    process.Handle))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return new WindowsProcessJob(jobHandle);
        }
        catch
        {
            jobHandle.Dispose();
            throw;
        }
    }

    public void Dispose() => handle.Dispose();

    private static void SetKillOnClose(SafeFileHandle jobHandle)
    {
        var information = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation
            {
                LimitFlags = JobObjectLimitKillOnJobClose,
            },
        };
        int size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
        IntPtr pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(information, pointer, fDeleteOld: false);
            if (!NativeMethods.SetInformationJobObject(
                    jobHandle,
                    JobObjectInfoType.ExtendedLimitInformation,
                    pointer,
                    (uint)size))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private enum JobObjectInfoType
    {
        ExtendedLimitInformation = 9,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
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
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    private static class NativeMethods
    {
        [DllImport(
            "kernel32.dll",
            EntryPoint = "CreateJobObjectW",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        public static extern IntPtr CreateJobObject(
            IntPtr jobAttributes,
            string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetInformationJobObject(
            SafeFileHandle job,
            JobObjectInfoType infoType,
            IntPtr jobObjectInfo,
            uint jobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AssignProcessToJobObject(
            SafeFileHandle job,
            IntPtr process);
    }
}
