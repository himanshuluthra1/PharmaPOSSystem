using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PharmaPOS.WPF.Services;

/// <summary>Ensures only one PharmaPOS process runs; re-activates an existing window on repeat launch.</summary>
internal static class SingleInstanceService
{
    private const string MutexName = @"Global\PharmaPOS.SingleInstance.v1";
    private static Mutex? _mutex;

    public static bool TryAcquire()
    {
        _mutex = new Mutex(true, MutexName, out var createdNew);
        return createdNew;
    }

    public static void Release()
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        _mutex = null;
    }

    public static void ActivateOtherInstance()
    {
        var current = Process.GetCurrentProcess();
        foreach (var proc in Process.GetProcessesByName(current.ProcessName))
        {
            if (proc.Id == current.Id)
                continue;

            var handle = proc.MainWindowHandle;
            if (handle == IntPtr.Zero)
                continue;

            ShowWindow(handle, SwRestore);
            SetForegroundWindow(handle);
            return;
        }
    }

    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
