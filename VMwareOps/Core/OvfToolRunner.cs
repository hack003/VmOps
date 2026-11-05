// ============================================================================
// OvfToolRunner.cs - VMware OVF Tool 进程执行器
// ============================================================================
// 职责：封装外部 ovftool.exe 子进程，支持 OVF 导出 / OVA 导出 / OVF 导入
//
// 核心类：
//   ✅ OvfExportInput — 导出参数模型（VC IP / VM 名 / 导出路径 / SSL 设置等）
//   ✅ OvfRunResult — 执行结果模型（IsSuccess / ExitCode / ErrorMessage / FullLog）
//   ✅ OvfToolRunner — 进程执行器，构建命令行参数 + 启动 Process + 实时日志回调
//   ✅ LogReceivedHandler — 日志行接收委托
//
// 特点：
//   独立于 PowerCLI 体系，直接 spawn ovftool.exe 子进程
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VMwareOps.Core
{
    /// <summary>
    /// OVF 导出输入参数
    /// </summary>
    public class OvfExportInput
    {
        public string VcIp { get; set; } = "";
        public string Domain { get; set; } = "";
        public string VmName { get; set; } = "";
        public string OvaName { get; set; } = "";
        public string ExportSavePath { get; set; } = "";
        public bool Overwrite { get; set; }
        public bool IgnoreNvram { get; set; }
        public bool SslUnsecure { get; set; }
        public bool IgnoreImageFiles { get; set; }
        public string OvfToolExePath { get; set; } = "";
        public int TimeoutMs { get; set; } = 300000;
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }

    /// <summary>
    /// OVF 执行结果
    /// </summary>
    public class OvfRunResult
    {
        public bool IsSuccess { get; set; }
        public int ExitCode { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string FullLog { get; set; } = "";
    }

    /// <summary>
    /// 日志接收委托
    /// </summary>
    public delegate void LogReceivedHandler(string logLine);

    /// <summary>
    /// OVF Tool 进程执行器（独立类，负责所有 ovftool 进程执行逻辑）
    /// </summary>
    public class OvfToolRunner
    {
        private readonly StringBuilder _logBuilder = new StringBuilder();
        private readonly object _logLock = new object();
        private Process? _currentProcess;
        private CancellationTokenSource _cts = new CancellationTokenSource();

        public event LogReceivedHandler? OnLogReceived;

        /// <summary>
        /// 执行 OVF 导出
        /// </summary>
        public async Task<OvfRunResult> RunExport(OvfExportInput input)
        {
            var result = new OvfRunResult();
            _logBuilder.Clear();
            _cts.Dispose();
            _cts = new CancellationTokenSource();

            string ovfToolPath = ResolveOvfToolPath(input.OvfToolExePath);
            if (string.IsNullOrEmpty(ovfToolPath) || !File.Exists(ovfToolPath))
            {
                result.ErrorMessage = "找不到 ovftool.exe，请确认 VMware OVF Tool 已安装。";
                result.ExitCode = -1;
                AppendLog(result.ErrorMessage);
                result.FullLog = _logBuilder.ToString();
                return result;
            }
            AppendLog($"ovftool 路径: {ovfToolPath}");

            string fullExportPath = Path.GetFullPath(input.ExportSavePath);
            string outputDir = Path.GetDirectoryName(fullExportPath) ?? "";
            if (string.IsNullOrEmpty(outputDir))
            {
                outputDir = Path.GetPathRoot(fullExportPath) ?? "";
            }
            if (!Directory.Exists(outputDir))
            {
                result.ErrorMessage = $"输出目录不存在: {outputDir}";
                result.ExitCode = -1;
                AppendLog(result.ErrorMessage);
                result.FullLog = _logBuilder.ToString();
                return result;
            }
            AppendLog($"输出目录: {outputDir}");

            if (string.IsNullOrEmpty(input.Username) || string.IsNullOrEmpty(input.Password))
            {
                result.ErrorMessage = "用户名或密码为空，无法执行导出。";
                result.ExitCode = -1;
                AppendLog(result.ErrorMessage);
                result.FullLog = _logBuilder.ToString();
                return result;
            }

            string arguments = BuildExportArguments(input);
            AppendLog($"执行参数: {arguments}");

            try
            {
                result.ExitCode = await RunProcessAsync(ovfToolPath, arguments, input.TimeoutMs);
                result.IsSuccess = result.ExitCode == 0;
                if (!result.IsSuccess && string.IsNullOrEmpty(result.ErrorMessage))
                {
                    result.ErrorMessage = $"ovftool 退出码非零: {result.ExitCode}";
                }
            }
            catch (OperationCanceledException)
            {
                result.IsSuccess = false;
                result.ExitCode = -98;
                result.ErrorMessage = $"执行超时 ({input.TimeoutMs / 1000}s)";
                AppendLog(result.ErrorMessage);
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.ExitCode = -99;
                result.ErrorMessage = $"执行异常: {ex.Message}";
                AppendLog(result.ErrorMessage);
            }

            result.FullLog = _logBuilder.ToString();
            return result;
        }

        /// <summary>
        /// 执行任意 ovftool 命令（直接传入完整参数字符串）
        /// </summary>
        public async Task<OvfRunResult> RunCommand(string arguments, int timeoutMs = 300000)
        {
            var result = new OvfRunResult();
            _logBuilder.Clear();
            _cts.Dispose();
            _cts = new CancellationTokenSource();

            string ovfToolPath = ResolveOvfToolPath("");
            if (string.IsNullOrEmpty(ovfToolPath) || !File.Exists(ovfToolPath))
            {
                result.ErrorMessage = "找不到 ovftool.exe，请确认 VMware OVF Tool 已安装。";
                result.ExitCode = -1;
                AppendLog(result.ErrorMessage);
                result.FullLog = _logBuilder.ToString();
                return result;
            }
            AppendLog($"ovftool 路径: {ovfToolPath}");
            AppendLog($"执行参数: {arguments}");

            try
            {
                result.ExitCode = await RunProcessAsync(ovfToolPath, arguments, timeoutMs);
                result.IsSuccess = result.ExitCode == 0;
                if (!result.IsSuccess && string.IsNullOrEmpty(result.ErrorMessage))
                {
                    result.ErrorMessage = $"ovftool 退出码非零: {result.ExitCode}";
                }
            }
            catch (OperationCanceledException)
            {
                result.IsSuccess = false;
                result.ExitCode = -98;
                result.ErrorMessage = $"执行超时 ({timeoutMs / 1000}s)";
                AppendLog(result.ErrorMessage);
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.ExitCode = -99;
                result.ErrorMessage = $"执行异常: {ex.Message}";
                AppendLog(result.ErrorMessage);
            }

            result.FullLog = _logBuilder.ToString();
            return result;
        }

        /// <summary>
        /// 取消正在执行的任务
        /// </summary>
        public void Cancel()
        {
            _cts.Cancel();
            try
            {
                _currentProcess?.Kill();
            }
            catch { }
        }

        /// <summary>
        /// 组装导出参数
        /// </summary>
        private string BuildExportArguments(OvfExportInput input)
        {
            var parts = new List<string>();

            if (input.Overwrite) parts.Add("-o");
            if (input.IgnoreNvram) parts.Add("--noNvramFile");
            if (input.SslUnsecure) parts.Add("--noSSLVerify");
            if (input.IgnoreImageFiles) parts.Add("--noImageFiles");

            // URI 编码用户名和密码
            string encodedUser = Uri.EscapeDataString(input.Username);
            string encodedPw = Uri.EscapeDataString(input.Password);

            // 拼接 vi locator
            string locator = $"vi://{encodedUser}:{encodedPw}@{input.VcIp}/Datacenter/vm/{input.VmName}";
            parts.Add($"\"{locator}\"");

            // 输出路径
            string cleanPath = input.ExportSavePath.TrimEnd('/', '\\');
            if (cleanPath.Length == 2 && cleanPath[1] == ':')
                cleanPath += '\\';
            string ovaName = string.IsNullOrEmpty(input.OvaName) ? input.VmName : input.OvaName;
            string fullPath = Path.Combine(cleanPath, $"{ovaName}.ova");
            parts.Add($"\"{fullPath}\"");

            return string.Join(" ", parts);
        }

        /// <summary>
        /// 解析 ovftool 路径
        /// </summary>
        private string ResolveOvfToolPath(string explicitPath)
        {
            if (!string.IsNullOrEmpty(explicitPath) && File.Exists(explicitPath))
                return explicitPath;

            // 常见安装路径
            var commonPaths = new[]
            {
                @"C:\Program Files\VMware\VMware OVF Tool\ovftool.exe",
                @"C:\Program Files (x86)\VMware\VMware OVF Tool\ovftool.exe",
                @"C:\Program Files (x86)\VMware\VMware Workstation\OVFTool\ovftool.exe",
                @"C:\Program Files\VMware\VMware Workstation\OVFTool\ovftool.exe",
            };

            foreach (var path in commonPaths)
            {
                if (File.Exists(path))
                    return path;
            }

            // 尝试从 PATH 查找
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (var dir in pathEnv.Split(';'))
                {
                    string candidate = Path.Combine(dir.Trim(), "ovftool.exe");
                    if (File.Exists(candidate))
                        return candidate;
                }
            }

            return "";
        }

        /// <summary>
        /// 启动进程并捕获输出
        /// </summary>
        private async Task<int> RunProcessAsync(string fileName, string arguments, int timeoutMs)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            _cts.CancelAfter(timeoutMs);

            using (var process = new Process { StartInfo = psi })
            {
                _currentProcess = process;

                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        AppendLog(e.Data);
                    }
                };

                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        AppendLog($"[ERR] {e.Data}");
                    }
                };

                AppendLog($"启动进程: {fileName}");
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var tcs = new TaskCompletionSource<bool>();
                process.EnableRaisingEvents = true;
                process.Exited += (s, e) => tcs.TrySetResult(true);

                using (var reg = _cts.Token.Register(() =>
                {
                    try { process.Kill(); } catch { }
                    tcs.TrySetCanceled();
                }))
                {
                    try
                    {
                        await tcs.Task;
                    }
                    catch (TaskCanceledException)
                    {
                        AppendLog($"进程被终止（超时或用户取消）");
                        throw new OperationCanceledException(_cts.Token);
                    }
                }

                process.WaitForExit();

                int exitCode = process.ExitCode;
                AppendLog($"进程退出码: {exitCode}");
                _currentProcess = null;
                return exitCode;
            }
        }

        /// <summary>
        /// 追加日志（线程安全）
        /// </summary>
        private void AppendLog(string line)
        {
            lock (_logLock)
            {
                _logBuilder.AppendLine(line);
            }
            OnLogReceived?.Invoke(line);
        }
    }
}