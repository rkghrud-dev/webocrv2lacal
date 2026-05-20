using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KeywordOcr.App.Services;

public enum CompletionPowerAction
{
    None,
    CloseApp,
    Sleep,
    Shutdown,
}

public static class PowerManagementService
{
    [Flags]
    private enum ExecutionState : uint
    {
        EsContinuous = 0x80000000,
        EsSystemRequired = 0x00000001,
        EsDisplayRequired = 0x00000002,
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);

    public static void PreventSleep()
    {
        var previous = SetThreadExecutionState(
            ExecutionState.EsContinuous
            | ExecutionState.EsSystemRequired
            | ExecutionState.EsDisplayRequired);
        if (previous == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "작업 중 절전 방지 설정에 실패했습니다.");
    }

    public static void AllowSleep()
    {
        SetThreadExecutionState(ExecutionState.EsContinuous);
    }

    public static void RequestSleep()
    {
        StartHidden("rundll32.exe", "powrprof.dll,SetSuspendState 0,0,0");
    }

    public static void RequestShutdown(int delaySeconds = 60)
    {
        StartHidden("shutdown.exe", $"/s /t {Math.Max(0, delaySeconds)} /c \"KeywordOCR 작업 완료 후 자동 종료\"");
    }

    public static void CancelShutdown()
    {
        StartHidden("shutdown.exe", "/a");
    }

    private static void StartHidden(string fileName, string arguments)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }
}
