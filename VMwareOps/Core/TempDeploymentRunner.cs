// ============================================================================
// TempDeploymentRunner.cs - 临时批量部署 Runspace 通道
// ============================================================================
// 职责：批量部署任务时动态创建的独立 Runspace 通道，与 SessionManager 互不干扰
//
// 生命周期规则（Rule 2）：
//   ✅ 批量部署启动时创建，部署完成后进入 15 分钟闲置保活窗口
//   ✅ 窗口内可复用执行新部署，闲置超时自动 Disconnect-VIServer + Dispose
//   ✅ 与 SessionManager 各自独立 Connect-VIServer，脚本显式带 -Server 参数
//   ✅ 全局限制：Runspace 总数 ≤ 2（常驻 + 临时），禁止 RunspacePool
//
// 继承 PsRunnerBase，实现 IPsRunner
// ============================================================================
using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace VmOps.Core
{
    public class TempDeploymentRunner : PsRunnerBase, IPsRunner
    {
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private volatile bool _isConnected;
        private string? _currentVc;
        private string? _currentUser;
        private string? _currentDomain;

        public bool IsConnected => _isConnected;
        public string? CurrentVc => _currentVc;

        public async Task<bool> ConnectFromSessionAsync(string vc, string user, string password, string? domain, Action<string?, string?>? logCallback = null)
        {
            return await Task.Run(async () =>
            {
                await _semaphore.WaitAsync();
                try
                {
                    if (Runspace == null)
                    {
                        InitRunspaceAndPowerCLI((msg, lvl) => logCallback?.Invoke(msg, lvl));
                    }

                    if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(password))
                    {
                        logCallback?.Invoke($"凭证不可用（用户或密码为空）", "error");
                        return false;
                    }

                    bool isEsxi = user.Equals("root", StringComparison.OrdinalIgnoreCase);
                    bool alreadyQualified = user.IndexOf('\\') >= 0 || user.IndexOf('@') >= 0;
                    string userDomain = (!isEsxi && !alreadyQualified && !string.IsNullOrEmpty(domain)) ? $"{domain}\\{user}" : user;
                    string forceParam = isEsxi ? " -Force" : "";

                    using (var tcp = new TcpClient())
                    {
                        try
                        {
                            var connectTask = tcp.ConnectAsync(vc, 443);
                            using (var cts = new CancellationTokenSource(8000))
                            {
                                var tcs = new TaskCompletionSource<bool>();
                                Action onTimeout = () => { tcs.TrySetResult(false); try { tcp.Close(); } catch { } };
                                cts.Token.Register(onTimeout);
                                var completed = await Task.WhenAny(connectTask, tcs.Task);
                                if (completed == tcs.Task)
                                {
                                    logCallback?.Invoke($"目标主机 {vc}:443 网络不通（TCP 超时），请检查网络", "error");
                                    return false;
                                }
                                await connectTask;
                            }
                        }
                        catch (Exception ex)
                        {
                            logCallback?.Invoke($"目标主机 {vc}:443 TCP 连接失败: {ex.Message}", "error");
                            return false;
                        }
                    }

                    string escapedVc = SessionManager.EscapePsString(vc);
                    string escapedUser = SessionManager.EscapePsString(userDomain);
                    string escapedPw = SessionManager.EscapePsString(password);

                    string script = $@"
$ErrorActionPreference = 'Stop';
Connect-VIServer -Server '{escapedVc}' -User '{escapedUser}' -Password '{escapedPw}'{forceParam} -ErrorAction Stop | Out-Null;
Write-Output 'CONNECTED';
";
                    logCallback?.Invoke($"通道B连接到 {vc}...", "info");

                    var result = ExecuteScriptCore(script, logCallback, 60);

                    if (result.OutputString.Contains("CONNECTED"))
                    {
                        _currentVc = vc;
                        _currentUser = userDomain;
                        _currentDomain = domain;
                        _isConnected = true;
                        logCallback?.Invoke($"✓ 通道B已连接到 {vc}", "success");
                        return true;
                    }

                    string errorMsg = string.Join("\n", result.Error);
                    logCallback?.Invoke($"通道B连接失败: {errorMsg}", "error");
                    return false;
                }
                catch (Exception ex)
                {
                    logCallback?.Invoke($"通道B连接异常: {ex.Message}", "error");
                    return false;
                }
                finally
                {
                    _semaphore.Release();
                }
            });
        }

        public async Task<string> ExecuteScriptAsync(string script, Action<string?, string?>? logCallback = null, int timeoutSec = 60)
        {
            return await Task.Run(async () =>
            {
                await _semaphore.WaitAsync();
                try
                {
                    if (Runspace == null) throw new PowerShellException("通道B未初始化");
                    var result = ExecuteScriptCore(script, logCallback, timeoutSec);
                    return result.OutputString;
                }
                finally
                {
                    _semaphore.Release();
                }
            });
        }

        public async Task<List<Dictionary<string, object>>> ExecuteQueryAsync(string script, Action<string?, string?>? logCallback = null, int timeoutSec = 90)
        {
            return await Task.Run(async () =>
            {
                await _semaphore.WaitAsync();
                try
                {
                    if (Runspace == null) throw new PowerShellException("通道B未初始化");
                    return ExecuteQueryCore(script, logCallback, timeoutSec);
                }
                finally
                {
                    _semaphore.Release();
                }
            });
        }

        public async Task DisconnectAsync(Action<string?, string?>? logCallback = null)
        {
            await Task.Run(async () =>
            {
                await _semaphore.WaitAsync();
                try
                {
                    if (_isConnected && Runspace != null)
                    {
                        try
                        {
                            string script = "Disconnect-VIServer -Confirm:$false -ErrorAction SilentlyContinue;";
                            ExecuteScriptCore(script, logCallback, 10);
                        }
                        catch { }
                    }
                }
                finally
                {
                    _isConnected = false;
                    _currentVc = null;
                    _currentUser = null;
                    _currentDomain = null;
                    _semaphore.Release();
                }
            });
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;

            bool acquired = false;
            try
            {
                acquired = _semaphore.Wait(TimeSpan.FromSeconds(3));
                if (acquired)
                {
                    try
                    {
                        if (Runspace != null && _isConnected)
                        {
                            try
                            {
                                string script = "Disconnect-VIServer -Confirm:$false -ErrorAction SilentlyContinue;";
                                ExecuteScriptCore(script, null, timeoutSec: 10);
                            }
                            catch { }
                        }
                    }
                    catch { }

                    try
                    {
                        if (Runspace != null)
                        {
                            try { Runspace.Close(); } catch { }
                            Runspace.Dispose();
                            Runspace = null;
                        }
                    }
                    catch { }
                }
            }
            catch { }
            finally
            {
                try
                {
                    if (acquired) { _semaphore.Release(); }
                    _semaphore.Dispose();
                }
                catch { }
                _isConnected = false;
                _currentVc = null;
                _currentUser = null;
                _currentDomain = null;
            }
        }
    }
}