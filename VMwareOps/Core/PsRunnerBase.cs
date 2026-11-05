// ============================================================================
// PsRunnerBase.cs - PowerShell Runspace 执行基类（抽象）
// ============================================================================
// 职责：封装 Runspace 创建、PowerShell 脚本异步执行、超时控制、结果收集
//
// 核心机制：
//   ✅ InvokeWithTimeout() — BeginInvoke + WaitOne 异步执行 + 超时 Stop
//   ✅ InvokeResult — 统一返回结构（OutputObjects / TimedOut）
//   ✅ 子类继承 — SessionManager（常驻通道）/ TempDeploymentRunner（临时部署通道）
//
// 约束：
//   不在此文件写 Connect-VIServer 业务逻辑，只做纯执行层封装
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VMwareOps.Core
{
    public abstract class PsRunnerBase
    {
        protected Runspace? Runspace;
        public volatile bool IsDisposed;

        protected InvokeResult InvokeWithTimeout(PowerShell ps, int timeoutSec)
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
                } catch { }
                if (buffered.Count > 0) {
                    errMsg = "[PROGRESS] " + string.Join(" | ", buffered) + " | [ERROR] " + errMsg;
                }
                if (!string.IsNullOrEmpty(errMsg)) {
                    throw new PowerShellException(errMsg);
                }
                throw;
            }
        }

        protected PsExecutionResult ExecuteScriptCore(string script, Action<string?, string?>? logCallback, int timeoutSec)
        {
            var result = new PsExecutionResult();

            if (Runspace == null)
            {
                result.Error.Add("Runspace 未初始化");
                return result;
            }

            try
            {
                using var ps = PowerShell.Create();
                ps.Runspace = Runspace;
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
                else if (ps.HadErrors)
                {
                    result.HasErrors = true;
                    if (result.Error.Count == 0)
                    {
                        foreach (var err in ps.Streams.Error)
                        {
                            result.Error.Add(err.ToString());
                        }
                    }
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

        protected List<Dictionary<string, object>> ExecuteQueryCore(
            string script,
            Action<string?, string?>? logCallback,
            int timeoutSec)
        {
            var result = new List<Dictionary<string, object>>();

            if (Runspace == null)
            {
                logCallback?.Invoke("Runspace 未初始化", "error");
                return result;
            }

            try
            {
                using var ps = PowerShell.Create();
                ps.Runspace = Runspace;
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

                if (ps.HadErrors)
                {
                    if (errorMessages.Count > 0)
                    {
                        throw new PowerShellException(string.Join("; ", errorMessages));
                    }
                    if (ps.Streams.Error.Count > 0)
                    {
                        var allErrors = ps.Streams.Error.Select(e => e.ToString()).ToList();
                        throw new PowerShellException(string.Join("; ", allErrors));
                    }
                    string dollarErrorMsg = null;
                    try {
                        using var probe = PowerShell.Create();
                        probe.Runspace = Runspace;
                        probe.AddScript("$Error | ForEach-Object { $_.ToString() } | Select-Object -First 5");
                        var errOut = probe.Invoke();
                        if (errOut != null && errOut.Count > 0) {
                            dollarErrorMsg = string.Join(" | ", errOut.Select(o => o?.ToString()).Where(s => !string.IsNullOrEmpty(s)));
                        }
                    } catch { }
                    if (!string.IsNullOrEmpty(dollarErrorMsg)) {
                        throw new PowerShellException("$Error: " + dollarErrorMsg);
                    }
                    throw new PowerShellException("PowerShell 执行产生错误（ps.HadErrors=true），但无法获取详细信息");
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

        protected void InitRunspaceAndPowerCLI(Action<string, string>? logAction = null)
        {
            logAction?.Invoke("创建 Runspace...", "info");

            var initialSessionState = InitialSessionState.CreateDefault();
            initialSessionState.ExecutionPolicy = Microsoft.PowerShell.ExecutionPolicy.Bypass;

            Runspace = RunspaceFactory.CreateRunspace(initialSessionState);
            Runspace.Open();

            string initScript = @"
Import-Module VMware.VimAutomation.Core -ErrorAction SilentlyContinue;
Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null;
Set-PowerCLIConfiguration -ParticipateInCEIP $false -Confirm:$false -Scope Session | Out-Null;
";
            using (var ps = PowerShell.Create())
            {
                ps.Runspace = Runspace;
                ps.AddScript(initScript);
                InvokeWithTimeout(ps, 30);

                if (ps.HadErrors)
                {
                    foreach (var err in ps.Streams.Error)
                    {
                        logAction?.Invoke(err.ToString(), "warn");
                    }
                }
            }

            logAction?.Invoke("PowerCLI 模块已加载，证书策略已配置", "info");
        }

        protected struct InvokeResult
        {
            public PSObject[] OutputObjects;
            public bool TimedOut;
        }
    }
}