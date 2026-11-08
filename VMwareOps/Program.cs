using System;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Owin.Hosting;
using VMwareOps.Core;

namespace VMwareOps
{
    internal static class Program
    {
        private static IDisposable _webServer;
        public const string WebBaseUrl = "http://localhost:5036";
        public const string WebBaseUrlAlt = "http://127.0.0.1:5036";

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

            StartWebServer();

            try
            {
                using (var form = new MainForm())
                {
                    Application.Run(form);
                    form.WaitForCleanup().Wait(TimeSpan.FromSeconds(20));
                }
            }
            finally
            {
                StopWebServer();
            }
        }

        private static void StartWebServer()
        {
            try
            {
                ExtensionManager.Instance.Initialize();

                var options = new StartOptions();
                options.Urls.Add(WebBaseUrl);
                options.Urls.Add(WebBaseUrlAlt);
                _webServer = WebApp.Start<WebApi.Startup>(options);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Web Server start failed: {ex.Message}");
            }
        }

        private static void StopWebServer()
        {
            _webServer?.Dispose();
            _webServer = null;
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
    }
}