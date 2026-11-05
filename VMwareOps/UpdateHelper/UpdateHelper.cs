// ============================================================================
// UpdateHelper.cs - 全自动更新辅助程序
// ============================================================================
// 职责：等待指定 PID 主进程退出 → 原子替换 exe → 重启 → 自删
// 命令行参数：
//   update_helper.exe <targetExe> <newExePath> [--pid=<MainPID>]
//   targetExe   = 主程序完整路径
//   newExePath  = 下载到临时目录的新版 exe
//   --pid=xxx   = 主进程 PID（可选，有则精确等待，无则退化为进程名匹配）
// ============================================================================
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace VMwareOps.UpdateHelper
{
    class Program
    {
        private const string HelperVersion = "2.0.0";

        private static StreamWriter? _logWriter;
        private static string _logPath = "";

        static int Main(string[] args)
        {
            string targetExe = "";
            string newExe = "";
            int mainPid = -1;

            foreach (var arg in args)
            {
                if (arg.StartsWith("--pid=", StringComparison.OrdinalIgnoreCase))
                {
                    int.TryParse(arg.Substring(6), out mainPid);
                }
                else if (string.IsNullOrEmpty(targetExe))
                {
                    targetExe = arg;
                }
                else if (string.IsNullOrEmpty(newExe))
                {
                    newExe = arg;
                }
            }

            if (string.IsNullOrEmpty(targetExe) || string.IsNullOrEmpty(newExe))
                return 1;

            targetExe = Path.GetFullPath(targetExe);
            newExe = Path.GetFullPath(newExe);

            InitLog(targetExe);
            Log($"=== update_helper v{HelperVersion} 启动 ===");
            Log($"目标文件: {targetExe}");
            Log($"新文件:   {newExe}");
            Log($"主进程PID: {mainPid}");

            try
            {
                if (!File.Exists(newExe))
                {
                    Log("新文件不存在，终止更新");
                    return Fatal("新 exe 不存在");
                }

                if (!WaitForMainExit(targetExe, mainPid))
                {
                    Log("等待主进程退出超时");
                    return Fatal("主进程未能在规定时间内退出");
                }

                Log("主进程已退出，开始原子替换...");
                if (!AtomicReplace(targetExe, newExe))
                {
                    Log("原子替换失败");
                    return Fatal("文件替换失败");
                }

                Log("替换成功，写入 marker 并启动主程序...");
                WriteMarker(targetExe);

                var mainProc = LaunchMain(targetExe);
                if (mainProc == null)
                {
                    Log("主程序启动失败，尝试回滚 .bak");
                    Rollback(targetExe);
                    return Fatal("主程序启动失败");
                }

                Log($"主程序已启动 PID={mainProc.Id}，等待 5 秒做健康检查...");
                if (!WaitHealthy(mainProc, 5000))
                {
                    Log("主程序启动后立即退出，回滚 .bak");
                    Rollback(targetExe);
                    return Fatal("新版主程序启动失败，已自动回滚");
                }

                Log("主程序健康检查通过，更新完成，计划自删");
                ScheduleSelfDelete();
                return 0;
            }
            catch (Exception ex)
            {
                Log($"未预期异常: {ex}");
                return Fatal(ex.Message);
            }
            finally
            {
                FlushLog();
            }
        }

        // ---------- 等待主进程退出 ----------

        private static bool WaitForMainExit(string targetExe, int mainPid)
        {
            string targetName = Path.GetFileNameWithoutExtension(targetExe);
            int deadline = Environment.TickCount + 60_000;

            if (mainPid > 0)
            {
                try
                {
                    using var proc = Process.GetProcessById(mainPid);
                    Log($"等待 PID={mainPid} 退出...");
                    while (!proc.HasExited)
                    {
                        if (Environment.TickCount > deadline) break;
                        Thread.Sleep(300);
                        proc.Refresh();
                    }
                    Log($"PID={mainPid} {(proc.HasExited ? "已退出" : "未在超时内退出")}");
                }
                catch (ArgumentException)
                {
                    Log($"PID={mainPid} 已不存在（主程序已退出）");
                }
                catch (Exception ex)
                {
                    Log($"等待 PID 异常: {ex.Message}");
                }
            }

            Log("检查是否还有残留同名进程...");
            KillSiblingProcesses(mainPid, targetName);

            Thread.Sleep(500);
            if (!WaitByName(targetName, deadline))
            {
                Log("超时：仍有同名进程无法退出");
                return false;
            }

            return true;
        }

        private static void KillSiblingProcesses(int mainPid, string targetName)
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(targetName))
                {
                    try
                    {
                        if (p.Id == mainPid) continue;
                        if (p.Id == Process.GetCurrentProcess().Id) continue;
                        if (!p.HasExited)
                        {
                            Log($"发现残留实例 PID={p.Id}，尝试结束");
                            try { p.Kill(); } catch { }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static bool WaitByName(string targetName, int deadline)
        {
            while (Environment.TickCount <= deadline)
            {
                bool any = false;
                try
                {
                    foreach (var p in Process.GetProcessesByName(targetName))
                    {
                        try { if (!p.HasExited) { any = true; break; } }
                        catch { any = true; break; }
                    }
                }
                catch { return true; }

                if (!any) return true;
                Thread.Sleep(500);
            }
            return false;
        }

        // ---------- 原子替换 ----------

        private static bool AtomicReplace(string targetExe, string newExe)
        {
            string dir = Path.GetDirectoryName(targetExe) ?? Directory.GetCurrentDirectory();
            string bakExe = targetExe + ".bak";

            for (int attempt = 0; attempt < 30; attempt++)
            {
                try
                {
                    Log($"替换尝试 #{attempt + 1}");

                    if (File.Exists(bakExe))
                    {
                        try { File.Delete(bakExe); }
                        catch { }
                    }

                    if (File.Exists(targetExe))
                    {
                        File.Move(targetExe, bakExe);
                        Log("旧 exe 已重命名为 .bak");
                    }

                    File.Move(newExe, targetExe);
                    Log("新 exe 已就位");

                    try { File.Delete(bakExe); }
                    catch { Log("警告：无法删除 .bak（不影响运行）"); }

                    return true;
                }
                catch (IOException ex)
                {
                    Log($"IO 异常: {ex.Message}");
                    Thread.Sleep(500);
                }
                catch (Exception ex)
                {
                    Log($"未知异常: {ex.Message}");
                    Thread.Sleep(500);
                }
            }

            Log("替换达到最大重试次数，执行回滚...");
            try
            {
                if (!File.Exists(targetExe) && File.Exists(bakExe))
                {
                    File.Move(bakExe, targetExe);
                    Log("已从 .bak 回滚");
                }
            }
            catch (Exception ex) { Log($"回滚失败: {ex.Message}"); }

            try { if (File.Exists(newExe)) File.Delete(newExe); } catch { }
            return false;
        }

        // ---------- 启动主程序 ----------

        private static Process? LaunchMain(string targetExe)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo
                {
                    FileName = targetExe,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(targetExe) ?? ""
                });
                return p;
            }
            catch (Exception ex)
            {
                Log($"启动主程序异常: {ex.Message}");
                return null;
            }
        }

        private static bool WaitHealthy(Process proc, int checkMs)
        {
            try
            {
                proc.WaitForInputIdle(checkMs);
                proc.Refresh();
                if (proc.HasExited)
                {
                    Log($"主程序在 {checkMs}ms 内已退出，ExitCode={proc.ExitCode}");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Log($"健康检查异常: {ex.Message}，假设正常");
                return true;
            }
        }

        private static void Rollback(string targetExe)
        {
            string bakExe = targetExe + ".bak";
            try
            {
                if (File.Exists(targetExe)) File.Delete(targetExe);
                if (File.Exists(bakExe))
                {
                    File.Move(bakExe, targetExe);
                    Log("已从 .bak 回滚到旧版本");

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = targetExe,
                        UseShellExecute = true,
                        WorkingDirectory = Path.GetDirectoryName(targetExe) ?? ""
                    });
                }
                else
                {
                    Log("警告：没有 .bak 可回滚！");
                }
            }
            catch (Exception ex)
            {
                Log($"回滚失败: {ex.Message}");
            }
        }

        // ---------- Marker 文件（主程序可以在下次启动时读取，表示刚更新过） ----------

        private static void WriteMarker(string targetExe)
        {
            try
            {
                string markerPath = Path.ChangeExtension(targetExe, ".updated");
                File.WriteAllText(markerPath, DateTime.Now.ToString("o"));
            }
            catch { }
        }

        // ---------- 自删 ----------

        private static void ScheduleSelfDelete()
        {
            string? selfPath = null;
            try { selfPath = Process.GetCurrentProcess().MainModule?.FileName; }
            catch { }
            if (string.IsNullOrEmpty(selfPath)) return;

            try
            {
                string cmd = $"/c ping 127.0.0.1 -n 4 >nul & del /f /q \"{selfPath}\"";
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = cmd,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }
            catch { }
        }

        // ---------- 日志 ----------

        private static void InitLog(string targetExe)
        {
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "VmOps_Update");
                Directory.CreateDirectory(dir);
                _logPath = Path.Combine(dir, "update_helper.log");
                _logWriter = new StreamWriter(_logPath, false, System.Text.Encoding.UTF8) { AutoFlush = true };
                AppDomain.CurrentDomain.ProcessExit += (sender, args) => FlushLog();
            }
            catch { }
        }

        private static void Log(string msg)
        {
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
            Debug.WriteLine($"[UpdateHelper] {msg}");
            try { _logWriter?.WriteLine(line); } catch { }
        }

        private static void FlushLog()
        {
            try { _logWriter?.Flush(); _logWriter?.Close(); } catch { }
        }

        private static int Fatal(string msg)
        {
            Log($"FATAL: {msg}");
            return 1;
        }
    }
}