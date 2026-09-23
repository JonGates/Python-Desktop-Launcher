using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ProjectLauncher.Core;

namespace ProjectLauncher.Windows;

public sealed class JobObject : IProcessLifetime
{
    private readonly SafeKernelHandle _handle;
    public JobObject()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(ProjectLauncher.Core.Localization.TextCatalog.Format(ProjectLauncher.Core.Localization.TextCatalog.Language, "Runtime.11"));
        _handle = NativeMethods.CreateJobObject(IntPtr.Zero, null);
        if (_handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var limits = new NativeMethods.ExtendedLimitInformation
        { BasicLimitInformation = new NativeMethods.BasicLimitInformation { LimitFlags = NativeMethods.KillOnJobClose } };
        if (!NativeMethods.SetInformationJobObject(_handle, 9, ref limits, (uint)Marshal.SizeOf<NativeMethods.ExtendedLimitInformation>()))
        { var error = Marshal.GetLastWin32Error(); _handle.Dispose(); throw new Win32Exception(error); }
    }
    public void Attach(Process process) => AttachHandle(process.Handle);
    internal void AttachHandle(IntPtr process)
    {
        if (!NativeMethods.AssignProcessToJobObject(_handle, process)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public void Dispose() => _handle.Dispose();
}
