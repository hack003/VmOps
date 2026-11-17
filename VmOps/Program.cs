// ============================================================================
// Program.cs - 应用入口
// ============================================================================
// 启动顺序：
//   ✅ 单实例 Mutex 检测 — 已有实例时触发还原信号并退出
//   ✅ CheckPowerShellEnvironment() — 检查 PowerShell 5.1 是否可用（失败则弹窗退出）
//   ✅ DpiUtils.EnableDpiAware() — 进程级 DPI 感知设置
//   ✅ Application.EnableVisualStyles() / SetCompatibleTextRenderingDefault(false)
//   ✅ 启动 MainForm 主窗口
//
// 单实例机制：
//   Named Mutex "VmOps.NET.SingleInstance" 保证全局唯一
//   Named EventWaitHandle "VmOps.NET.RestoreWindow" 用于通知托盘状态的已有实例还原窗口
//   已有实例窗口可见时，新实例同时用 Win32 ShowWindow/SetForegroundWindow 激活
//
// 约束：.NET Framework 4.8，单 STA 线程模型
// ============================================================================
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace VmOps
{
    internal static class Program
    {
        private const string MUTEX_NAME = "VmOps.NET.SingleInstance";
        private const string RESTORE_EVENT_NAME = "VmOps.NET.RestoreWindow";

        private static Mutex? _singleInstanceMutex;
        private static EventWaitHandle? _restoreEvent;
        private static MainForm? _mainForm;

        [STAThread]
        static void Main()
        {
            bool createdNew;
            _singleInstanceMutex = new Mutex(true, MUTEX_NAME, out createdNew);

            if (!createdNew)
            {
                ActivateExistingInstance();
                return;
            }

            Utils.DpiUtils.EnableDpiAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (!CheckPowerShellEnvironment())
            {
                MessageBox.Show(
                    "PowerShell 5.1 环境检测失败。\n\n" +
                    "本工具需要 Windows 10/11 或 Server 2016+ 系统自带的 PowerShell 5.1 环境。\n" +
                    "请确认系统 PowerShell 5.1 未被卸载或损坏。",
                    "环境检测失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                ReleaseSingleInstance();
                return;
            }

            _restoreEvent = new EventWaitHandle(false, EventResetMode.AutoReset, RESTORE_EVENT_NAME);
            ThreadPool.RegisterWaitForSingleObject(_restoreEvent, OnRestoreSignal, null, Timeout.Infinite, false);

            try
            {
                using (_mainForm = new MainForm())
                {
                    Application.Run(_mainForm);
                    _mainForm.WaitForCleanup().Wait(TimeSpan.FromSeconds(20));
                }
            }
            finally
            {
                ReleaseSingleInstance();
            }
        }

        private static void OnRestoreSignal(object? state, bool timedOut)
        {
            try
            {
                _mainForm?.BeginInvoke(new Action(() =>
                {
                    _mainForm.RestoreFromTray();
                }));
            }
            catch { }
        }

        private static void ActivateExistingInstance()
        {
            try
            {
                using (var restoreEvent = new EventWaitHandle(false, EventResetMode.AutoReset, RESTORE_EVENT_NAME))
                {
                    restoreEvent.Set();
                }
            }
            catch { }

            try
            {
                var proc = Process.GetProcessesByName("VmOps.NET").FirstOrDefault();
                if (proc != null && proc.MainWindowHandle != IntPtr.Zero)
                {
                    ShowWindow(proc.MainWindowHandle, SW_RESTORE);
                    SetForegroundWindow(proc.MainWindowHandle);
                }
            }
            catch { }
        }

        private static void ReleaseSingleInstance()
        {
            try { _restoreEvent?.Dispose(); } catch { }
            _restoreEvent = null;

            try
            {
                _singleInstanceMutex?.ReleaseMutex();
            }
            catch { }
            try { _singleInstanceMutex?.Dispose(); } catch { }
            _singleInstanceMutex = null;
        }

        private static bool CheckPowerShellEnvironment()
        {
            try
            {
                var asm = System.Reflection.Assembly.Load(
                    "System.Management.Automation, Version=3.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");

                if (asm == null) return false;

                var psType = asm.GetType("System.Management.Automation.PowerShell");
                if (psType == null) return false;

                var runspaceFactoryType = asm.GetType("System.Management.Automation.Runspaces.RunspaceFactory");
                if (runspaceFactoryType == null) return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private const int SW_RESTORE = 9;
    }
}