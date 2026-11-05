// ============================================================================
// Program.cs - 应用入口
// ============================================================================
// 启动顺序：
//   ✅ CheckPowerShellEnvironment() — 检查 PowerShell 5.1 是否可用（失败则弹窗退出）
//   ✅ DpiUtils.EnableDpiAware() — 进程级 DPI 感知设置
//   ✅ Application.EnableVisualStyles() / SetCompatibleTextRenderingDefault(false)
//   ✅ 启动 MainForm 主窗口
//
// 约束：.NET Framework 4.8，单 STA 线程模型
// ============================================================================
using System;
using System.Windows.Forms;

namespace VMwareOps
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
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
                return;
            }

            using (var form = new MainForm())
            {
                Application.Run(form);
                form.WaitForCleanup().Wait(TimeSpan.FromSeconds(20));
            }
        }

        /// <summary>
        /// 检测 PowerShell 5.1 GAC 环境是否可用
        /// </summary>
        private static bool CheckPowerShellEnvironment()
        {
            try
            {
                // 检测 System.Management.Automation 程序集是否可从 GAC 加载
                var asm = System.Reflection.Assembly.Load(
                    "System.Management.Automation, Version=3.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
                
                if (asm == null)
                {
                    return false;
                }

                // 检测 PowerShell 核心类型是否可用
                var psType = asm.GetType("System.Management.Automation.PowerShell");
                if (psType == null)
                {
                    return false;
                }

                // 检测 RunspaceFactory 是否可用
                var runspaceFactoryType = asm.GetType("System.Management.Automation.Runspaces.RunspaceFactory");
                if (runspaceFactoryType == null)
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}