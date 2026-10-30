// ============================================================================
// SessionManager.cs - 常驻通用 Runspace 会话（单例）
// ============================================================================
// 职责：程序启动时初始化一个长驻 Runspace，全程复用，串行执行
//   适用场景：查询操作 / 单台手动 VM 操作 / 轻量运维操作
//
// 双通道架构（Rule 2）：
//   ✅ SessionManager       — 常驻通用通道（本文件），进程存活期间保持连接
//   ✅ TempDeploymentRunner  — 临时部署通道（批量部署时动态创建，15分钟闲置回收）
//   ✅ 两个通道各自独立 Connect-VIServer，脚本显式带 -Server 参数
//
// 核心机制：
//   ✅ SemaphoreSlim(1,1) 串行排队，避免 Runspace 并发冲突
//   ✅ 后台线程 Task.Run 执行，不阻塞 UI
//   ✅ 直接读 PSObject 属性，不强制 JSON 序列化
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VirtualizationOpsTool.Core
{
    /// <summary>
    /// PowerShell 执行异常
    /// </summary>
    public class PowerShellException : Exception
    {
        public PowerShellException(string message) : base(message) { }
    }

    /// <summary>
    /// PowerShell 会话管理器 - 单 Runspace 长驻会话
    /// 程序启动时一次性初始化 Runspace、导入 PowerCLI、配置证书策略
    /// 全程复用同一 vCenter 会话，查询串行排队，后台线程执行
    /// </summary>
    public class SessionManager : IDisposable
    {
        private static readonly Lazy<SessionManager> _instance = new Lazy<SessionManager>(() => new SessionManager());
        public static SessionManager Instance => _instance.Value;

        private Runspace? _runspace;
        private volatile bool _isConnected;
        private volatile bool _isDisposed;
        private string? _currentVc;
        private string? _currentUser;
        private string? _currentPassword;
        private string? _currentDomain;
        private bool _isInitialized;
        
        // 唯一串行化器：所有 Runspace 操作必须先获取信号量
        private readonly SemaphoreSlim _querySemaphore = new SemaphoreSlim(1, 1);
        
        private volatile bool _isTaskRunning;
        private volatile bool _cancelRequested;

        private TempDeploymentRunner? _tempRunner;
        private Timer? _tempRunnerTimer;
        private readonly object _tempRunnerLock = new object();
        private readonly SemaphoreSlim _deploymentMutex = new SemaphoreSlim(1, 1);
        private const int TempRunnerKeepAliveMinutes = 15;

        public bool IsConnected => _isConnected;
        public string? CurrentVc => _currentVc;
        public string? CurrentDomain => _currentDomain;
        public bool IsTaskRunning => _isTaskRunning;

        /// <summary>
        /// 当前连接凭据（用于外部 PowerShell 窗口复用连接参数）
        /// </summary>
        public (string Username, string Password)? CurrentCredentials
        {
            get
            {
                if (string.IsNullOrEmpty(_currentUser) || string.IsNullOrEmpty(_currentPassword))
                    return null;
                return (_currentUser!, _currentPassword!);
            }
        }

        private SessionManager()
        {
        }

        /// <summary>
        /// 确保 Runspace 已初始化（延迟初始化，程序启动阶段调用）
        /// </summary>
        public void EnsureInitialized()
        {
            if (_isInitialized) return;

            _querySemaphore.Wait();
            try
            {
                if (!_isInitialized)
                {
                    InitializeRunspace();
                    _isInitialized = true;
                }
            }
            finally
            {
                _querySemaphore.Release();
            }
        }

        /// <summary>
        /// 初始化单个 Runspace（程序启动阶段一次性执行）
        /// 导入 PowerCLI 模块、配置证书策略，保持长连接
        /// </summary>
        public event Action<string, string>? InitializationLog;

        private void InitializeRunspace()
        {
            try
            {
                InitialSessionState initialSessionState = InitialSessionState.CreateDefault();
                initialSessionState.ExecutionPolicy = Microsoft.PowerShell.ExecutionPolicy.Bypass;

                _runspace = RunspaceFactory.CreateRunspace(initialSessionState);
                _runspace.Open();

                string initScript = @"
Import-Module VMware.VimAutomation.Core -ErrorAction SilentlyContinue;
Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null;
Set-PowerCLIConfiguration -ParticipateInCEIP $false -Confirm:$false -Scope Session | Out-Null;
";
                using (var ps = PowerShell.Create())
                {
                    ps.Runspace = _runspace;
                    ps.AddScript(initScript);
                    InvokeWithTimeout(ps, 30);
                    
                    if (ps.HadErrors)
                    {
                        foreach (var err in ps.Streams.Error)
                        {
                            LogInit($"PowerCLI 初始化警告: {err}", "warn");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogInit($"PowerShell 引擎初始化失败: {ex.Message}", "error");
            }
        }

        private void LogInit(string message, string level = "info")
        {
            InitializationLog?.Invoke(message, level);
        }

        /// <summary>
        /// 连接 vCenter/ESXi（在后台线程执行，保持会话状态）
        /// </summary>
        public async Task<bool> ConnectAsync(string vc, string user, string pw, string? domain, Action<string?, string?>? logCallback = null)
        {
            return await Task.Run(() =>
            {
                _querySemaphore.Wait();
                try
                {
                    EnsureInitializedCore();

                    const int maxAttempts = 3;
                    Exception lastEx = null;
                    for (int attempt = 1; attempt <= maxAttempts; attempt++)
                    {
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
                                    var completed = Task.WhenAny(connectTask, tcs.Task).GetAwaiter().GetResult();
                                    if (completed == tcs.Task)
                                    {
                                        if (attempt < maxAttempts)
                                        {
                                            logCallback?.Invoke($"目标主机 {vc}:443 TCP 超时（第 {attempt}/{maxAttempts} 次），1 秒后重试...", "warn");
                                            Thread.Sleep(1000);
                                            continue;
                                        }
                                        logCallback?.Invoke($"目标主机 {vc}:443 网络不通（重试 {maxAttempts} 次均超时），请检查网络", "error");
                                        return false;
                                    }
                                    connectTask.GetAwaiter().GetResult();
                                }
                                lastEx = null;
                                break;
                            }
                            catch (Exception ex)
                            {
                                lastEx = ex;
                                if (attempt < maxAttempts)
                                {
                                    logCallback?.Invoke($"目标主机 {vc}:443 TCP 异常（第 {attempt}/{maxAttempts} 次）: {ex.Message}，1 秒后重试...", "warn");
                                    Thread.Sleep(1000);
                                    continue;
                                }
                                logCallback?.Invoke($"目标主机 {vc}:443 TCP 连接失败: {ex.Message}", "error");
                                return false;
                            }
                        }
                    }
                    if (lastEx != null) return false;

                    bool isEsxi = user.Equals("root", StringComparison.OrdinalIgnoreCase);
                    string userDomain = !isEsxi && !string.IsNullOrEmpty(domain) ? $"{domain}\\{user}" : user;
                    string forceParam = isEsxi ? " -Force" : "";
                    string serverType = isEsxi ? "ESXi" : "vCenter";

                    string script = $@"
$ErrorActionPreference = 'Stop';
Connect-VIServer -Server '{EscapePsString(vc)}' -User '{EscapePsString(userDomain)}' -Password '{EscapePsString(pw)}'{forceParam} -ErrorAction Stop | Out-Null;
Write-Output 'CONNECTED';
";

                    logCallback?.Invoke($"正在连接到 {serverType}: {vc}...", "info");

                    var result = ExecuteScriptCore(script, logCallback, timeoutSec: 60);

                    if (result.Output.ToString().Contains("CONNECTED"))
                    {
                        _currentVc = vc;
                        _currentUser = userDomain;
                        _currentPassword = pw;
                        _currentDomain = domain;
                        _isConnected = true;
                        return true;
                    }
                    else
                    {
                        string errorMsg = string.Join("\n", result.Error);
                        logCallback?.Invoke($"连接失败: {errorMsg}", "error");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    logCallback?.Invoke($"连接异常: {ex.Message}", "error");
                    return false;
                }
                finally
                {
                    _querySemaphore.Release();
                }
            });
        }

        /// <summary>
        /// 同步断开连接（用于程序关闭等必须等待完成的场景）
        /// </summary>
        public void Disconnect(Action<string?, string?>? logCallback = null)
        {
            bool acquired = _querySemaphore.Wait(TimeSpan.FromSeconds(5));
            if (!acquired)
            {
                logCallback?.Invoke("Disconnect: 等待 Runspace 信号量超时，跳过主动断开", "warn");
                return;
            }
            try
            {
                if (_isConnected && _runspace != null)
                {
                    try
                    {
                        string script = "Disconnect-VIServer * -Confirm:$false -ErrorAction SilentlyContinue; $global:DefaultVIServer = $null; $global:DefaultVIServers = $null;";                        ExecuteScriptCore(script, logCallback, timeoutSec: 10);
                    }
                    catch { }
                }
            }
            finally
            {
                _isConnected = false;
                _currentVc = null;
                _currentUser = null;
                _currentPassword = null;
                _currentDomain = null;
                _querySemaphore.Release();
            }
        }

        /// <summary>
        /// 异步断开连接（用于 UI 按钮点击等非阻塞场景）
        /// </summary>
        public async Task DisconnectAsync(Action<string?, string?>? logCallback = null)
        {
            await Task.Run(() => Disconnect(logCallback));
        }

        /// <summary>
        /// 执行查询脚本（后台线程 + 串行排队 + 直接返回 PSObject 列表）
        /// 所有 PowerCLI 查询通过此方法执行，避免 UI 卡死
        /// </summary>
        public async Task<List<Dictionary<string, object>>> ExecuteQueryAsync(
            string script, 
            Action<string?, string?>? logCallback = null, 
            int timeoutSec = 90)
        {
            return await Task.Run(() =>
            {
                _querySemaphore.Wait();
                try
                {
                    EnsureInitializedCore();
                    return ExecuteQueryCore(script, logCallback, timeoutSec);
                }
                finally
                {
                    _querySemaphore.Release();
                }
            });
        }

        /// <summary>
        /// ExecutePsScriptAsync - 兼容旧接口别名
        /// </summary>
        public async Task<string> ExecutePsScriptAsync(string script, Action<string?, string?>? logCallback = null, int timeoutSec = 60)
        {
            return await Task.Run(() =>
            {
                _querySemaphore.Wait();
                try
                {
                    EnsureInitializedCore();
                    var result = ExecuteScriptCore(script, logCallback, timeoutSec);
                    return result.OutputString;
                }
                finally
                {
                    _querySemaphore.Release();
                }
            });
        }

        private void EnsureInitializedCore()
        {
            if (!_isInitialized)
            {
                InitializeRunspace();
                _isInitialized = true;
            }
        }

        private PsExecutionResult ExecuteScriptCore(string script, Action<string?, string?>? logCallback, int timeoutSec)
        {
            var result = new PsExecutionResult();

            if (_runspace == null)
            {
                result.Error.Add("Runspace 未初始化");
                return result;
            }

            try
            {
                using var ps = PowerShell.Create();
                ps.Runspace = _runspace;
                ps.AddScript(script);

                ps.Streams.Error.DataAdded += (sender, e) =>
                {
                    var error = ps.Streams.Error[e.Index];
                    var errMsg = error.ToString();
                    result.Error.Add(errMsg);
                    logCallback?.Invoke(errMsg, "error");
                };

                ps.Streams.Warning.DataAdded += (sender, e) =>
                {
                    var warning = ps.Streams.Warning[e.Index];
                    var warnMsg = warning.ToString();
                    result.Warning.Add(warnMsg);
                    logCallback?.Invoke(warnMsg, "warn");
                };

                ps.Streams.Information.DataAdded += (sender, e) =>
                {
                    var info = ps.Streams.Information[e.Index];
                    var msg = info?.MessageData?.ToString();
                    if (!string.IsNullOrEmpty(msg))
                        logCallback?.Invoke(msg, "info");
                };

                var invokeResult = InvokeWithTimeout(ps, timeoutSec);

                foreach (var psObject in invokeResult.OutputObjects)
                {
                    string? line = psObject?.BaseObject?.ToString();
                    if (!string.IsNullOrEmpty(line))
                    {
                        result.Output.AppendLine(line);
                    }
                }

                if (invokeResult.TimedOut)
                {
                    result.HasErrors = true;
                    string timeoutMsg = $"PowerShell 脚本执行超时 ({timeoutSec}秒)，已强制终止";
                    result.Error.Add(timeoutMsg);
                    logCallback?.Invoke(timeoutMsg, "error");
                }
                else if (ps.HadErrors && ps.InvocationStateInfo.State != System.Management.Automation.PSInvocationState.Completed)
                {
                    result.HasErrors = true;
                    if (result.Error.Count == 0)
                    {
                        foreach (var err in ps.Streams.Error)
                        {
                            result.Error.Add(err.ToString());
                        }
                    }
                    var distinct = result.Error.Distinct().Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                    result.Error.Clear();
                    result.Error.AddRange(distinct);
                }

                return result;
            }
            catch (Exception ex)
            {
                result.Error.Add(ex.Message);
                logCallback?.Invoke(ex.Message, "error");
                throw;
            }
        }

        private List<Dictionary<string, object>> ExecuteQueryCore(
            string script, 
            Action<string?, string?>? logCallback, 
            int timeoutSec)
        {
            var result = new List<Dictionary<string, object>>();

            if (_runspace == null)
            {
                logCallback?.Invoke("Runspace 未初始化", "error");
                return result;
            }

            try
            {
                using var ps = PowerShell.Create();
                ps.Runspace = _runspace;
                ps.AddScript(script);

                var errorMessages = new List<string>();
                ps.Streams.Error.DataAdded += (sender, e) =>
                {
                    var error = ps.Streams.Error[e.Index];
                    var errMsg = error.ToString();
                    logCallback?.Invoke(errMsg, "error");
                    errorMessages.Add(errMsg);
                };

                ps.Streams.Warning.DataAdded += (sender, e) =>
                {
                    var warning = ps.Streams.Warning[e.Index];
                    logCallback?.Invoke(warning.ToString(), "warn");
                };

                ps.Streams.Information.DataAdded += (sender, e) =>
                {
                    var info = ps.Streams.Information[e.Index];
                    var msg = info?.MessageData?.ToString();
                    if (!string.IsNullOrEmpty(msg))
                        logCallback?.Invoke(msg, "info");
                };

                var invokeResult = InvokeWithTimeout(ps, timeoutSec);

                if (invokeResult.TimedOut)
                {
                    string timeoutMsg = $"PowerShell 查询执行超时 ({timeoutSec}秒)，已强制终止";
                    logCallback?.Invoke(timeoutMsg, "error");
                    throw new PowerShellException(timeoutMsg);
                }

                foreach (var psObject in invokeResult.OutputObjects)
                {
                    if (psObject == null) continue;
                    
                    if (psObject.BaseObject is string strObj)
                    {
                        logCallback?.Invoke(strObj, "info");
                        continue;
                    }

                    var dict = new Dictionary<string, object>();
                    foreach (var prop in psObject.Properties)
                    {
                        dict[prop.Name] = prop.Value;
                    }

                    if (dict.Count > 0)
                    {
                        result.Add(dict);
                    }
                }

                if (ps.HadErrors && ps.InvocationStateInfo.State != System.Management.Automation.PSInvocationState.Completed)
                {
                    var merged = new List<string>();
                    merged.AddRange(errorMessages);
                    merged.AddRange(ps.Streams.Error.Select(e => e.ToString()));
                    merged = merged.Distinct().Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                    if (merged.Count > 0)
                    {
                        throw new PowerShellException(string.Join("; ", merged));
                    }
                    throw new PowerShellException("PowerShell 执行产生错误，但无法获取详细信息");
                }

                return result;
            }
            catch (Exception ex)
            {
                string errMsg = string.IsNullOrEmpty(ex.Message) 
                    ? $"查询异常 (类型: {ex.GetType().Name})" 
                    : ex.Message;
                logCallback?.Invoke(errMsg, "error");
                throw;
            }
        }

        /// <summary>
        /// PowerShell 执行核心：BeginInvoke + AsyncWaitHandle + Stop() 实现可靠超时
        /// 防止 vCenter 无响应或网络断开导致 ps.Invoke() 永久阻塞、Runspace 死锁
        /// </summary>
        private InvokeResult InvokeWithTimeout(PowerShell ps, int timeoutSec)
        {
            IAsyncResult asyncResult;
            try
            {
                asyncResult = ps.BeginInvoke();
            }
            catch (Exception)
            {
                return new InvokeResult { OutputObjects = Array.Empty<PSObject>(), TimedOut = false };
            }

            bool completed;
            if (timeoutSec <= 0)
            {
                asyncResult.AsyncWaitHandle.WaitOne();
                completed = true;
            }
            else
            {
                completed = asyncResult.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(timeoutSec));
            }

            if (!completed)
            {
                try { ps.Stop(); } catch { }
                try { asyncResult.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(3)); } catch { }
                return new InvokeResult { OutputObjects = Array.Empty<PSObject>(), TimedOut = true };
            }

            try
            {
                var output = ps.EndInvoke(asyncResult);
                return new InvokeResult { OutputObjects = output.ToArray(), TimedOut = false };
            }
            catch (RuntimeException ex)
            {
                string errMsg = null;
                var buffered = new List<string>();
                try {
                    var partial = ps.EndInvoke(asyncResult);
                    if (partial != null) {
                        foreach (var o in partial) {
                            var s = o?.ToString();
                            if (!string.IsNullOrEmpty(s)) buffered.Add(s);
                        }
                    }
                } catch { }
                try {
                    if (ps.Streams.Error.Count > 0) { errMsg = ps.Streams.Error[0].ToString(); }
                    if (string.IsNullOrEmpty(errMsg) && ps.HadErrors) {
                        var allErrs = ps.Streams.Error.Select(e => e.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToList();
                        if (allErrs.Count > 0) { errMsg = string.Join("; ", allErrs); }
                    }
                    if (string.IsNullOrEmpty(errMsg) && ex != null) {
                        errMsg = ex.Message;
                        if (ex.InnerException != null) { errMsg += " | " + ex.InnerException.Message; }
                    }
                } catch {}
                if (buffered.Count > 0) {
                    errMsg = "[PROGRESS] " + string.Join(" | ", buffered) + " | [ERROR] " + errMsg;
                }
                if (!string.IsNullOrEmpty(errMsg)) {
                    throw new PowerShellException(errMsg);
                }
                throw;
            }
        }

        private struct InvokeResult
        {
            public PSObject[] OutputObjects;
            public bool TimedOut;
        }

        /// <summary>
        /// PowerShell 字符串转义
        /// </summary>
        internal static string EscapePsString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";

            var sb = new StringBuilder();
            foreach (char ch in s)
            {
                int o = ch;
                if (o < 0x20 || o == 0x7F || (0x80 <= o && o <= 0x9F))
                {
                    sb.Append(' ');
                }
                else
                {
                    sb.Append(ch);
                }
            }

            return sb.ToString().Replace("'", "''");
        }

        /// <summary>
        /// 请求取消当前正在执行的任务
        /// </summary>
        public void CancelCurrentTask()
        {
            if (_isTaskRunning)
            {
                _cancelRequested = true;
            }
        }

        /// <summary>
        /// 检查是否已请求取消（供长时间运行的任务调用）
        /// </summary>
        public bool IsCancelRequested => _cancelRequested;

        /// <summary>
        /// 标记任务开始
        /// </summary>
        public void MarkTaskStarted()
        {
            _isTaskRunning = true;
            _cancelRequested = false;
        }

        /// <summary>
        /// 标记任务已完成
        /// </summary>
        public void MarkTaskCompleted()
        {
            _isTaskRunning = false;
            _cancelRequested = false;
        }

        public async Task<bool> AcquireDeploymentMutexAsync()
        {
            return await _deploymentMutex.WaitAsync(TimeSpan.Zero);
        }

        public void ReleaseDeploymentMutex()
        {
            try { _deploymentMutex.Release(); } catch { }
        }

        public async Task<TempDeploymentRunner?> GetDeploymentRunnerAsync(Action<string?, string?>? logCallback = null)
        {
            lock (_tempRunnerLock)
            {
                if (_tempRunner != null && _tempRunner.IsConnected && !_tempRunner.IsDisposed)
                {
                    RenewTempRunnerTimer();
                    logCallback?.Invoke("复用通道B", "info");
                    return _tempRunner;
                }

                DisposeTempRunnerInternal();

                _tempRunner = new TempDeploymentRunner();
            }

            if (string.IsNullOrEmpty(_currentVc))
            {
                logCallback?.Invoke("通道A未连接，无法确定要连接的 vCenter", "error");
                lock (_tempRunnerLock) { DisposeTempRunnerInternal(); }
                return null;
            }

            var cred = CurrentCredentials;
            if (cred == null)
            {
                logCallback?.Invoke("通道A未持有有效凭据", "error");
                lock (_tempRunnerLock) { DisposeTempRunnerInternal(); }
                return null;
            }

            bool ok = await _tempRunner.ConnectFromSessionAsync(_currentVc, cred.Value.Username, cred.Value.Password, _currentDomain, logCallback);
            if (!ok)
            {
                lock (_tempRunnerLock) { DisposeTempRunnerInternal(); }
                return null;
            }

            RenewTempRunnerTimer();
            return _tempRunner;
        }

        public void RenewTempRunnerTimer()
        {
            lock (_tempRunnerLock)
            {
                if (_tempRunnerTimer != null)
                {
                    _tempRunnerTimer.Dispose();
                    _tempRunnerTimer = null;
                }

                if (_tempRunner == null || _tempRunner.IsDisposed) return;

                _tempRunnerTimer = new Timer(_ => DisposeTempRunnerInternal(), null,
                    TimeSpan.FromMinutes(TempRunnerKeepAliveMinutes),
                    Timeout.InfiniteTimeSpan);
            }
        }

        public void DisposeTempRunner()
        {
            lock (_tempRunnerLock)
            {
                DisposeTempRunnerInternal();
            }
        }

        private void DisposeTempRunnerInternal()
        {
            if (_tempRunnerTimer != null)
            {
                _tempRunnerTimer.Dispose();
                _tempRunnerTimer = null;
            }

            if (_tempRunner != null)
            {
                try { _tempRunner.DisconnectAsync().Wait(TimeSpan.FromSeconds(5)); } catch { }
                try { _tempRunner.Dispose(); } catch { }
                _tempRunner = null;
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            DisposeTempRunner();

            bool acquired = false;
            try
            {
                acquired = _querySemaphore.Wait(TimeSpan.FromSeconds(3));
                if (acquired)
                {
                    try
                    {
                        if (_runspace != null && _isConnected)
                        {
                            try
                            {
                                string script = "Disconnect-VIServer * -Confirm:$false -ErrorAction SilentlyContinue; $global:DefaultVIServer = $null; $global:DefaultVIServers = $null;";                                ExecuteScriptCore(script, null, timeoutSec: 10);
                            }
                            catch { }
                        }
                    }
                    catch { }

                    try
                    {
                        if (_runspace != null)
                        {
                            try { _runspace.Close(); } catch { }
                            _runspace.Dispose();
                            _runspace = null;
                        }
                    }
                    catch { }
                }
            }
            catch { }
            finally
            {
                if (acquired)
                {
                    try { _querySemaphore.Release(); } catch { }
                }
                _querySemaphore.Dispose();
                _isConnected = false;
                _currentVc = null;
                _currentUser = null;
                _currentPassword = null;
                _currentDomain = null;
            }
        }
    }

    /// <summary>
    /// PowerShell 执行结果（兼容旧接口）
    /// </summary>
    public class PsExecutionResult
    {
        public StringBuilder Output { get; } = new StringBuilder();
        public List<string> Error { get; } = new List<string>();
        public List<string> Warning { get; } = new List<string>();
        public bool HasErrors { get; set; }

        public string OutputString => Output.ToString();
    }
}