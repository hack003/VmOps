// ============================================================================
// DeployManager.cs - 批量部署编排器
// ============================================================================
// 职责：读取 CSV 部署列表 → 创建临时部署通道 → 逐台执行 New-VM + Guest 网络配置
//
// 核心流程：
//   ✅ DeployFromCsvAsync() — 入口，解析 CSV/内存列表 → 校验 → 创建 TempDeploymentRunner
//   ✅ 单台部署流水线 — 创建 VM → 开机等待 → GuestOS 网络配置 → 资源分配
//   ✅ DictionaryExtensions — .NET Framework 4.8 兼容扩展方法（GetValueOrDefault）
//
// 依赖：
//   SessionManager（凭证复用）/ TempDeploymentRunner（临时通道）/ UnifiedCache（缓存）
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using VirtualizationOpsTool.Utils;

namespace VirtualizationOpsTool.Core
{
    /// <summary>
    /// .NET Framework 4.8 兼容扩展方法
    /// </summary>
    internal static class DictionaryExtensions
    {
        public static TValue GetValueOrDefault<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, TValue defaultValue = default)
        {
            if (dict.TryGetValue(key, out var value))
                return value;
            return defaultValue;
        }
    }

    public class DeployManager
    {
        private static readonly HashSet<char> _safeNamePattern = new HashSet<char>(
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.");

        private const string DEFAULT_GUEST_PASSWORD = "Admin@9000";
        private const string DEFAULT_DNS_LINUX = "223.5.5.5 223.6.6.6";
        private const string DEFAULT_DNS_WINDOWS = "223.5.5.5,223.6.6.6";

        private readonly Control _uiControl;
        private readonly Action<string, string> _logCallback;
        private TempDeploymentRunner? _deploymentRunner;

        public DeployManager(Control uiControl, Action<string, string> logCallback)
        {
            _uiControl = uiControl;
            _logCallback = logCallback;
        }

        private string GetVmField(Dictionary<string, string> vmData, string key, string defaultValue = "")
        {
            string value;
            if (vmData.TryGetValue(key, out value))
            {
                return value ?? defaultValue;
            }
            return defaultValue;
        }

        public async Task DeployFromCsvAsync(string csvPath = null, List<Dictionary<string, string>> vmListData = null)
        {
            if (!SessionManager.Instance.IsConnected)
            {
                _uiControl.Invoke(new Action(() =>
                {
                    _logCallback("错误", "尚未连接到 vCenter Server，请先连接成功后再执行批量部署。");
                    MessageBox.Show("尚未连接到 vCenter Server\n请先点击「开始连接」，连接成功后再执行批量部署。", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }));
                return;
            }

            List<Dictionary<string, string>> vmList;

            if (vmListData != null && vmListData.Count > 0)
            {
                vmList = vmListData;
            }
            else
            {
                string pathToUse = csvPath ?? Path.Combine(CoreUtils.GetUserDataDir(), "deploy_vm_list.csv");
                if (!File.Exists(pathToUse))
                {
                    _uiControl.Invoke(new Action(() =>
                    {
                        _logCallback("错误", "CSV 文件不存在: " + pathToUse);
                        MessageBox.Show("CSV 部署文件不存在，请先在「编辑列表」中创建或保存部署文件。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                    return;
                }

                try
                {
                    vmList = LoadCsv(pathToUse);
                }
                catch (Exception ex)
                {
                    _uiControl.Invoke(new Action(() =>
                    {
                        _logCallback("错误", "CSV 文件读取失败: " + ex.Message);
                    }));
                    return;
                }
            }

            if (vmList.Count == 0)
            {
                _uiControl.Invoke(new Action(() =>
                {
                    _logCallback("警告", "CSV 文件中没有可部署的虚拟机");
                }));
                return;
            }

            int totalCount = vmList.Count;
            _uiControl.Invoke(new Action(() =>
            {
                LogStageHeader($"批量部署虚拟机 (共 {totalCount} 台)");
                LogVmListPreview(vmList);
            }));

            // 标记任务开始
            SessionManager.Instance.MarkTaskStarted();

            bool mutexAcquired = await SessionManager.Instance.AcquireDeploymentMutexAsync();
            if (!mutexAcquired)
            {
                _uiControl.Invoke(new Action(() =>
                {
                    _logCallback("错误", "已有批量部署任务正在运行，请等待完成后再试。");
                }));
                SessionManager.Instance.MarkTaskCompleted();
                return;
            }

            try
            {
                _deploymentRunner = await SessionManager.Instance.GetDeploymentRunnerAsync(_logCallback);
                if (_deploymentRunner == null)
                {
                    _uiControl.Invoke(new Action(() =>
                    {
                        _logCallback("错误", "临时部署通道创建失败，请检查 vCenter 连接或凭证缓存。");
                    }));
                    SessionManager.Instance.ReleaseDeploymentMutex();
                    SessionManager.Instance.MarkTaskCompleted();
                    return;
                }

                var results = new List<DeployResult>();
            DateTime deployStart = DateTime.Now;
            bool wasCancelled = false;

            _uiControl.Invoke(new Action(() => LogStageHeader("阶段 1/3：创建 & 开机")));

            for (int i = 0; i < vmList.Count; i++)
            {
                if (SessionManager.Instance.IsCancelRequested)
                {
                    _uiControl.Invoke(new Action(() => _logCallback("警告", "用户请求终止部署任务")));
                    wasCancelled = true;
                    break;
                }

                var row = vmList[i];
                var result = await DeployVmAsync(row, i + 1, totalCount);
                results.Add(result);
            }

            if (!wasCancelled && results.Any(r => r.Success && !string.IsNullOrEmpty(r.VmMoRef)))
            {
                var stage1Results = results.Where(r => r.Success && !string.IsNullOrEmpty(r.VmMoRef)).ToList();
                _uiControl.Invoke(new Action(() => LogStageHeader($"阶段 2/3：等待 VMware Tools 就绪 ({stage1Results.Count} 台并行)")));

                var waitTasks = stage1Results.Select(r => WaitToolsReadyAsync(r.VmMoRef));
                var waitResults = await Task.WhenAll(waitTasks);

                int toolsOkCount = waitResults.Count(b => b);
                _uiControl.Invoke(new Action(() => _logCallback("信息", $"阶段 2 完成：{toolsOkCount}/{stage1Results.Count} 台 Tools 就绪")));

                _uiControl.Invoke(new Action(() => LogStageHeader($"阶段 3/3：Guest Operations 网络配置")));

                for (int i = 0; i < results.Count; i++)
                {
                    if (SessionManager.Instance.IsCancelRequested)
                    {
                        _uiControl.Invoke(new Action(() => _logCallback("警告", "用户请求终止部署任务")));
                        wasCancelled = true;
                        break;
                    }

                    var result = results[i];
                    if (!result.Success || string.IsNullOrEmpty(result.VmMoRef)) continue;

                    int vmIndex = i + 1;
                    bool toolsReady = waitResults[stage1Results.FindIndex(s => s.Label == result.Label)];

                    if (!toolsReady)
                    {
                        _uiControl.Invoke(new Action(() => _logCallback("错误", $"[{vmIndex}/{totalCount}] {result.Label} Tools 未就绪，跳过 GuestOps")));
                        result.Success = false;
                        result.Status = (result.Status ?? "") + "(Tools 超时)";
                        continue;
                    }

                    try
                    {
                        _uiControl.Invoke(new Action(() => _logCallback("信息", $"[{vmIndex}/{totalCount}] GuestOps 网络配置: {result.Label}...")));
                        string psScript = BuildGuestOpsScript(vmList[i], result.VmMoRef, DEFAULT_GUEST_PASSWORD);
                        await ExecuteInSessionAsync(psScript, "guestops");
                        _uiControl.Invoke(new Action(() => _logCallback("成功", $"[{vmIndex}/{totalCount}] GuestOps 完成: {result.Label} ✓")));
                    }
                    catch (Exception ex)
                    {
                        _uiControl.Invoke(new Action(() => _logCallback("错误", $"[{vmIndex}/{totalCount}] GuestOps 失败 {result.Label}: {ex.Message}")));
                        result.Success = false;
                        result.Status = (result.Status ?? "") + "(GuestOps失败)";
                    }
                }
            }

            TimeSpan deployElapsed = DateTime.Now - deployStart;
            int successCount = results.Count(r => r.Success);
            int failCount = totalCount - successCount;

            if (!wasCancelled)
            {
                _uiControl.Invoke(new Action(() =>
                {
                    LogSummaryTable(results, deployElapsed);
                }));
            }
            else
            {
                _uiControl.Invoke(new Action(() =>
                {
                    LogSummaryTable(results, deployElapsed, cancelled: true);
                }));
            }

            // 标记任务完成
            }
            finally
            {
                if (_deploymentRunner != null)
                {
                    SessionManager.Instance.RenewTempRunnerTimer();
                    _deploymentRunner = null;
                }
                SessionManager.Instance.ReleaseDeploymentMutex();
                SessionManager.Instance.MarkTaskCompleted();
            }
        }

        private async Task<DeployResult> DeployVmAsync(Dictionary<string, string> vmData, int vmIndex, int vmTotal)
        {
            string vmname = ComputeVmname(vmData);
            string vmIp = vmData.GetValueOrDefault("IP", "");
            string label = vmname;
            DateTime startTime = DateTime.Now;
            string statusText = "失败";
            string vmMoRef = null;

            try
            {
                LogVmHeader(vmIndex, vmTotal, label);

                var (isValid, err) = ValidateVmData(vmData, forNewVm: false);
                if (!isValid)
                {
                    throw new Exception($"CSV 数据校验失败: {err}");
                }

                LogVmStep("检查存在...");
                bool vmExists = await CheckVmExistsAsync(vmData);
                if (vmExists)
                {
                    LogVmStep("  → 已存在, 将重新配置");
                }
                else
                {
                    LogVmStep("  → 不存在, 开始新建");
                }

                if (vmExists)
                {
                    vmMoRef = await ResolveVmMoRefByNameAsync(vmData);
                    LogVmStep("重新配置 (CPU, 内存, 网卡, 磁盘)...");
                    string psScript = BuildDeployScriptReconfigure(vmData, vmMoRef);
                    await ExecuteInSessionAsync(psScript, "reconfigure");
                    LogVmStep("  → 完成");

                    statusText = "重新配置";
                }
                else
                {
                    string template = (vmData.GetValueOrDefault("Template", "") ?? "").Trim();
                    if (string.IsNullOrEmpty(template))
                    {
                        throw new Exception("CSV 数据校验失败: Template（模板名称）不能为空");
                    }

                    LogVmStep("从模板创建虚拟机...");
                    string psScript = BuildDeployScriptStep1(vmData);
                    await ExecuteInSessionAsyncReturnOutput(psScript, "step1");
                    LogVmStep("  → 完成");

                    vmMoRef = await ResolveVmMoRefByNameAsync(vmData);

                    LogVmStep("配置 (CPU, 内存, 网卡)...");
                    psScript = BuildDeployScriptStep2(vmData, vmMoRef);
                    await ExecuteInSessionAsync(psScript, "step2");
                    LogVmStep("  → 完成");

                    LogVmStep("启动虚拟机...");
                    psScript = BuildDeployScriptStep3(vmData, vmMoRef);
                    await ExecuteInSessionAsync(psScript, "step3");
                    LogVmStep("  → 完成");

                    statusText = "新建";
                }

                TimeSpan elapsed = DateTime.Now - startTime;
                LogVmResult($"状态: {statusText}成功 ✓  (耗时 {FormatDuration((int)elapsed.TotalSeconds)})");
                return new DeployResult { Label = label, Ip = vmIp, Status = statusText, Elapsed = (int)elapsed.TotalSeconds, Success = true, VmMoRef = vmMoRef };
            }
            catch (Exception ex)
            {
                TimeSpan elapsed = DateTime.Now - startTime;
                string errMsg = ex.Message.Length > 80 ? ex.Message.Substring(0, 80) : ex.Message;
                LogVmResult($"状态: 部署失败 ✗  ({errMsg})");
                _logCallback("错误", $"虚拟机 {label} 部署失败: {ex.Message}");
                return new DeployResult { Label = label, Ip = vmIp, Status = "失败", Elapsed = (int)elapsed.TotalSeconds, Success = false, VmMoRef = vmMoRef };
            }
        }

        private async Task<bool> CheckVmExistsAsync(Dictionary<string, string> vmData)
        {
            string vmname = ComputeVmname(vmData);
            string checkScript = $@"
$ErrorActionPreference = 'Stop';
try {{
$vm = Get-View -ViewType VirtualMachine -Property Name,Config.Template -ErrorAction SilentlyContinue | Where-Object {{ $_.Name -eq '{EscapePsString(vmname)}' -and $_.Config.Template -eq $false }};
if ($vm -ne $null) {{ Write-Output 'EXISTS' }} else {{ Write-Output 'NOT_EXISTS' }}
}} catch {{ Write-Output 'NOT_EXISTS' }}
";

            try
            {
                string output = await _deploymentRunner!.ExecuteScriptAsync(checkScript, _logCallback, 30);
                
                // 按行分割，精确匹配，避免 NOT_EXISTS 被 Contains("EXISTS") 误判
                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    string trimmed = line.Trim();
                    if (trimmed == "NOT_EXISTS") return false;
                    if (trimmed == "EXISTS") return true;
                }
                
                // 如果都没匹配到，默认认为不存在（安全起见）
                return false;
            }
            catch (Exception ex)
            {
                string lowerErr = ex.Message.ToLower();
                if (lowerErr.Contains("not found") || lowerErr.Contains("was not found") || lowerErr.Contains("不存在"))
                {
                    return false;
                }
                throw new Exception($"检查虚拟机失败: {ex.Message.Substring(0, Math.Min(200, ex.Message.Length))}");
            }
        }

        private async Task<bool> WaitToolsReadyAsync(string vmMoRef)
        {
            string script = $@"
$ErrorActionPreference = 'Stop';
try {{
$vmView = Get-View -Id '{EscapePsString(vmMoRef)}' -Property Guest.ToolsStatus -ErrorAction SilentlyContinue;
if ($vmView -and $vmView.Guest -and $vmView.Guest.ToolsStatus) {{ Write-Output ('TOOLS=' + $vmView.Guest.ToolsStatus) }} else {{ Write-Output 'TOOLS=unknown' }}
}} catch {{ Write-Output 'TOOLS=error' }}
";

            var deadline = DateTime.Now.AddMinutes(3);
            while (DateTime.Now < deadline)
            {
                if (SessionManager.Instance.IsCancelRequested) return false;
                try
                {
                    string output = await _deploymentRunner!.ExecuteScriptAsync(script, _logCallback, 15);
                    var lines = output?.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries) ?? new string[0];
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("TOOLS="))
                        {
                            string status = trimmed.Substring("TOOLS=".Length);
                            if (status == "toolsOk" || status == "toolsOld") return true;
                            break;
                        }
                    }
                }
                catch { }
                await Task.Delay(5000);
            }
            return false;
        }

        private async Task ExecuteInSessionAsync(string psScript, string? marker = null)
        {
            await ExecuteInSessionAsyncReturnOutput(psScript, marker);
        }

        private async Task<string> ExecuteInSessionAsyncReturnOutput(string psScript, string? marker = null)
        {
            string wrappedScript = psScript;
            if (!string.IsNullOrEmpty(marker))
            {
                string markerVar = $"$__marker_{marker}";
                wrappedScript = $@"
try {{
{psScript}
Write-Output '{markerVar}_OK'
}} catch {{
$errMsg = 'Deploy Error: ';
if ($_.Exception.Message) {{ $errMsg += $_.Exception.Message; }}
elseif ($_.ToString()) {{ $errMsg += $_.ToString(); }}
else {{ $errMsg += '未知错误'; }}
Write-Error $errMsg;
throw $errMsg;
}}
";
            }

            return await _deploymentRunner!.ExecuteScriptAsync(wrappedScript, _logCallback, 300);
        }

        private string ParseVmMoRef(string output)
        {
            if (string.IsNullOrEmpty(output)) return null;
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("VM_MOREF="))
                {
                    return trimmed.Substring("VM_MOREF=".Length).Trim();
                }
            }
            return null;
        }

        private async Task<string> ResolveVmMoRefByNameAsync(Dictionary<string, string> vmData)
        {
            string vmname = ComputeVmname(vmData);
            string escaped = EscapePsString(vmname);
            string script = $@"
$ErrorActionPreference = 'Stop';
$vm = Get-View -ViewType VirtualMachine -Property Name,Config.Template -ErrorAction SilentlyContinue | Where-Object {{ $_.Name -eq '{escaped}' -and $_.Config.Template -eq $false }} | Select-Object -First 1;
if ($vm) {{ Write-Output $vm.MoRef }} else {{ Write-Output 'NOT_FOUND' }}
";
            string output = await _deploymentRunner!.ExecuteScriptAsync(script, _logCallback, 30);
            var lines = output?.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines != null)
            {
                foreach (var line in lines)
                {
                    var t = line.Trim();
                    if (t == "NOT_FOUND") continue;
                    if (!string.IsNullOrEmpty(t) && t.Contains("-")) return t;
                }
            }
            throw new Exception("无法定位已存在虚拟机的 MoRef: " + vmname);
        }

        private string BuildDeployScriptStep1(Dictionary<string, string> vmData)
        {
            string hostValRaw = (vmData.GetValueOrDefault("Host") ?? "").Trim();
            string hostValEsc = EscapePsString(hostValRaw);
            string template = EscapePsString(vmData.GetValueOrDefault("Template") ?? "");
            string datastore = EscapePsString(vmData.GetValueOrDefault("Datastore") ?? "");
            string network = EscapePsString(vmData.GetValueOrDefault("Vlan") ?? "");
            string vmname = ComputeVmname(vmData);

            string chooseHost;
            if (!string.IsNullOrEmpty(hostValRaw))
            {
                chooseHost = $@"
$hostView = Get-View -ViewType HostSystem -ErrorAction SilentlyContinue | Where-Object {{ $_.Name -eq '{hostValEsc}' }} | Select-Object -First 1;
if ($null -eq $hostView) {{
    Write-Host ('主机 {0} 不存在，正在从 vCenter 自动选择一台已连接的主机...' -f '{hostValEsc}');
    $hostView = Get-View -ViewType HostSystem -ErrorAction Stop | Where-Object {{ [string]$_.Runtime.ConnectionState -eq 'connected' }} | Select-Object -First 1;
    if ($null -eq $hostView) {{ throw 'vCenter 中没有可用（Connected 状态）的主机' }};
    Write-Host ('已自动选择主机: ' + $hostView.Name);
}}";
            }
            else
            {
                chooseHost = $@"
Write-Host 'CSV 未指定主机，正在从 vCenter 自动选择一台已连接的主机...';
$hostView = Get-View -ViewType HostSystem -ErrorAction Stop | Where-Object {{ [string]$_.Runtime.ConnectionState -eq 'connected' }} | Select-Object -First 1;
if ($null -eq $hostView) {{ throw 'vCenter 中没有可用（Connected 状态）的主机' }};
Write-Host ('已自动选择主机: ' + $hostView.Name);";
            }

            string script = $@"
$ErrorActionPreference = 'Stop';
try {{
Get-View -ViewType Datacenter -Property Name -ErrorAction SilentlyContinue | Out-Null;
Write-Output (""[CFG] Template='{template}' Host='{hostValRaw}' Datastore='{datastore}' Network='{network}' VmName='{EscapePsString(vmname)}'"");
$tpl = Get-View -ViewType VirtualMachine -ErrorAction Stop | Where-Object {{ $_.Name -eq '{template}' -and $_.Config.Template -eq $true }} | Select-Object -First 1;
if ($null -eq $tpl) {{ throw '未找到模板：{template}' }};
Write-Output (""[STEP] 模板找到: $($tpl.Name) ParentType=$($tpl.Parent.Type) ParentValue=$($tpl.Parent.Value)"");
{chooseHost}
Write-Output (""[STEP] 主机找到: $($hostView.Name) MoRef=$($hostView.MoRef.Value)"");
$ds = Get-View -ViewType Datastore -Property Name -ErrorAction Stop | Where-Object {{ $_.Name -eq '{datastore}' }} | Select-Object -First 1;
if ($null -eq $ds) {{ throw '未找到存储：{datastore}' }};
Write-Output (""[STEP] 存储找到: $($ds.Name) MoRef=$($ds.MoRef.Value)"");
$netView = Get-View -ViewType Network -Property Name -ErrorAction SilentlyContinue | Where-Object {{ $_.Name -eq '{network}' }} | Select-Object -First 1;
if ($null -eq $netView) {{
  $netView = Get-View -ViewType DistributedVirtualPortgroup -Property Name -ErrorAction Stop | Where-Object {{ $_.Name -eq '{network}' }} | Select-Object -First 1;
}};
if ($null -eq $netView) {{ throw '未找到网络：{network}' }};
Write-Output (""[STEP] 网络找到: $($netView.Name) Type=$($netView.MoRef.Type) MoRef=$($netView.MoRef.Value)"");
Write-Output (""[STEP] 准备 Get-View -Id tpl.Parent (值=$($tpl.Parent.Type)-$($tpl.Parent.Value))"");
$parentObj = Get-View -Id $tpl.Parent -ErrorAction Stop;
Write-Output (""[STEP] Parent 对象拿到: Type=$($parentObj.MoRef.Type) Name=$($parentObj.Name)"");
$folderMoRef = $null;
if ($parentObj.MoRef.Type -eq 'Folder') {{
  $folderMoRef = $parentObj.MoRef;
  Write-Output (""[STEP] FolderMoRef (直接Folder): $($folderMoRef.Value)"");
}} elseif ($parentObj.MoRef.Type -eq 'ResourcePool' -and $parentObj.Owner) {{
  Write-Output (""[STEP] Parent是ResourcePool, 查 Owner..."");
  $rpOwner = Get-View -Id $parentObj.Owner -ErrorAction Stop;
  Write-Output (""[STEP] RP Owner: Type=$($rpOwner.MoRef.Type) Name=$($rpOwner.Name)"");
  if ($rpOwner.MoRef.Type -eq 'Folder') {{ $folderMoRef = $rpOwner.MoRef; Write-Output (""[STEP] FolderMoRef (从Owner): $($folderMoRef.Value)"") }};
}};
if (-not $folderMoRef) {{ throw '无法解析目标 Folder，请检查模板所在位置' }};
Write-Output (""[STEP] FolderMoRef最终: Type=$($folderMoRef.Type) Value=$($folderMoRef.Value)"");
$cloneSpec = New-Object VMware.Vim.VirtualMachineCloneSpec;
$cloneSpec.PowerOn = $false;
$cloneSpec.Template = $false;
$relocate = New-Object VMware.Vim.VirtualMachineRelocateSpec;
$relocate.Host = $hostView.MoRef;
$relocate.Datastore = $ds.MoRef;
$parent = Get-View -Id $hostView.Parent -ErrorAction Stop;
$relocatePool = $parent.ResourcePool;
$relocate.Pool = $relocatePool;
Write-Output (""[STEP] ParentCluster=$($parent.Name) Pool=$($relocate.Pool.Type)-$($relocate.Pool.Value)"");
$cloneSpec.Location = $relocate;
Write-Output (""[STEP] 准备 CloneVM_Task(folder=$($folderMoRef.Value), name='{EscapePsString(vmname)}', host=$($hostView.MoRef.Value), ds=$($ds.MoRef.Value), pool=$($relocate.Pool.Value))"");
$cloneTask = $tpl.CloneVM_Task($folderMoRef, '{EscapePsString(vmname)}', $cloneSpec);
Write-Host ('CloneVM_Task ' + $cloneTask.Value);
while ($true) {{
  Start-Sleep -Seconds 3;
  $t = Get-View -Id $cloneTask -Property Info.State,Info.Error,Info.Result -ErrorAction SilentlyContinue;
  if ($null -ne $t -and $t.Info.State -eq 'success') {{
    $newVmMoRef = $t.Info.Result;
    Write-Host ('克隆成功: ' + $newVmMoRef.Value);
    break;
  }}
  if ($null -ne $t -and $t.Info.State -eq 'error') {{ throw ('CloneVM 失败: ' + $t.Info.Error.LocalizedMessage + ' | folder=' + $folderMoRef.Type + '-' + $folderMoRef.Value + ' host=' + $hostView.MoRef.Type + '-' + $hostView.MoRef.Value + ' ds=' + $ds.MoRef.Type + '-' + $ds.MoRef.Value + ' pool=' + $relocate.Pool.Type + '-' + $relocate.Pool.Value + ' tpl=' + $tpl.MoRef.Type + '-' + $tpl.MoRef.Value + ' parentCR=' + $parent.Name) }}
}};
Write-Output (""VM_MOREF=$($newVmMoRef.Value)"");
Write-Output '[STEP] Step1 全部完成';
}} catch {{ Write-Error ('Step1 失败: ' + $_.Exception.Message); throw }}
";
            return script;
        }

        private string BuildDeployScriptStep2(Dictionary<string, string> vmData, string vmMoRef)
        {
            string vmname = ComputeVmname(vmData);
            int diskCap = SafeInt(vmData.GetValueOrDefault("DiskCapacityGB", "0"), 0, 0, 65535);
            string scriptBody = BuildReconfigScript(vmData, vmMoRef);

            if (diskCap > 0)
            {
                string diskScript = $@"
$vmView = Get-View -Id '{EscapePsString(vmMoRef)}' -Property Config.Hardware.Device -ErrorAction Stop;
if (-not $vmView) {{ throw '未找到虚拟机 MoRef: {EscapePsString(vmMoRef)}' }};
$allDisks = @($vmView.Config.Hardware.Device | Where-Object {{ $_ -is [VMware.Vim.VirtualDisk] }});
$allScsi = @($vmView.Config.Hardware.Device | Where-Object {{ $_ -is [VMware.Vim.VirtualSCSIController] }});
if ($allScsi.Count -eq 0) {{ throw '未找到任何 SCSI 控制器（VirtualSCSIController 子类），请检查模板硬件' }};
$scsiCtrl = $allScsi | Sort-Object BusNumber, Key | Select-Object -First 1;
$systemDisk = $allDisks | Where-Object {{ $_.ControllerKey -eq $scsiCtrl.Key }} | Select-Object -First 1;
if (-not $systemDisk) {{ throw ('选中的 SCSI 控制器 (Key=' + $scsiCtrl.Key + ') 上无磁盘') }};
$targetCapBytes = [int64]{diskCap} * 1GB;
$configSpec = New-Object VMware.Vim.VirtualMachineConfigSpec;
$scsiDisks = @($allDisks | Where-Object {{ $_.ControllerKey -eq $scsiCtrl.Key }});
$dataDisk = if ($scsiDisks.Count -ge 2) {{ $scsiDisks[1] }} else {{ $null }};
if (-not $dataDisk) {{
    Write-Host ('数据盘不存在，新建第2块硬盘（容量: ' + {diskCap} + ' GB，挂 SCSI 控制器 Key=' + $scsiCtrl.Key + '）');
    $newDisk = New-Object VMware.Vim.VirtualDisk;
    $newDisk.Key = -1;
    $newDisk.ControllerKey = $scsiCtrl.Key;
    $usedUnits = @($scsiDisks | ForEach-Object {{ [int]$_.UnitNumber }});
    $nextUnit = 0; foreach ($u in $usedUnits) {{ if ($u -ge $nextUnit) {{ $nextUnit = $u + 1 }} }};
    $newDisk.UnitNumber = $nextUnit;
    $newDisk.CapacityInBytes = $targetCapBytes;
    $backing = New-Object VMware.Vim.VirtualDiskFlatVer2BackingInfo;
    $backing.DiskMode = [VMware.Vim.VirtualDiskMode]::persistent;
    $backing.ThinProvisioned = $true;
    $backing.Datastore = $systemDisk.Backing.Datastore;
    $backing.FileName = '';
    $newDisk.Backing = $backing;
    $diskSpec = New-Object VMware.Vim.VirtualDeviceConfigSpec;
    $diskSpec.Operation = [VMware.Vim.VirtualDeviceConfigSpecOperation]::add;
    $diskSpec.FileOperation = [VMware.Vim.VirtualDeviceConfigSpecFileOperation]::create;
    $diskSpec.Device = $newDisk;
    $configSpec.DeviceChange += $diskSpec;
}} else {{
    Write-Host ('数据盘已存在，扩容到 ' + {diskCap} + ' GB');
    $dataDisk.CapacityInBytes = $targetCapBytes;
    $diskSpec = New-Object VMware.Vim.VirtualDeviceConfigSpec;
    $diskSpec.Operation = [VMware.Vim.VirtualDeviceConfigSpecOperation]::edit;
    $diskSpec.Device = $dataDisk;
    $configSpec.DeviceChange += $diskSpec;
}}
$task = $vmView.ReconfigVM_Task($configSpec);
while ($true) {{ Start-Sleep -Seconds 3; $t = Get-View -Id $task -Property Info.State,Info.Error -ErrorAction SilentlyContinue; if ($null -ne $t -and $t.Info.State -eq 'success') {{ Write-Host '数据盘配置完成'; break }}; if ($null -ne $t -and $t.Info.State -eq 'error') {{ throw ('磁盘操作失败: ' + $t.Info.Error.LocalizedMessage) }} }}
";
                return scriptBody + "; " + diskScript;
            }

            return scriptBody;
        }

        private string BuildDeployScriptStep3(Dictionary<string, string> vmData, string vmMoRef)
        {
            string vmname = ComputeVmname(vmData);
            return $@"
$ErrorActionPreference = 'Stop';
Write-Output '[STEP3] 开始 Step3 (开机)';
Write-Output '[STEP3] 等待 15 秒让 VSAN 磁盘元数据同步...';
Start-Sleep -Seconds 15;
$vm = Get-View -Id '{EscapePsString(vmMoRef)}' -Property Runtime.PowerState,Config.Hardware.Device -ErrorAction Stop;
if (-not $vm) {{ throw '未找到虚拟机 MoRef: {EscapePsString(vmMoRef)}' }};
Write-Output ('[STEP3] PowerState=' + $vm.Runtime.PowerState);
if ($vm.Runtime.PowerState -ne 'poweredOn') {{
  Write-Output '[STEP3] 检查磁盘就绪状态...';
  $ready = $false;
  for ($w = 0; $w -lt 60; $w++) {{
    Start-Sleep -Seconds 1;
    $chk = Get-View -Id '{EscapePsString(vmMoRef)}' -Property Config.Hardware.Device -ErrorAction SilentlyContinue;
    if ($null -ne $chk) {{
      $diskCount = @($chk.Config.Hardware.Device | Where-Object {{ $_ -is [VMware.Vim.VirtualDisk] }}).Count;
      $badDisk = @($chk.Config.Hardware.Device | Where-Object {{ $_ -is [VMware.Vim.VirtualDisk] -and -not $_.Backing }});
      if ($badDisk.Count -eq 0 -and $diskCount -gt 0) {{ 
        Write-Output ('[STEP3] 磁盘就绪: 共 ' + $diskCount + ' 块（等待 ' + ($w+1) + 's）'); 
        $ready = $true; 
        break 
      }}
      if (($w+1) % 10 -eq 0) {{ Write-Output ('[STEP3] 等待磁盘就绪... (' + ($w+1) + 's) 磁盘总数: ' + $diskCount + ' 坏盘: ' + $badDisk.Count) }}
    }}
  }};
  if (-not $ready) {{ Write-Output '[STEP3] 等待超时，尝试直接开机' }};
  $bootRetry = 0;
  while ($bootRetry -lt 3) {{
    Write-Output ('[STEP3] 尝试开机 (第 ' + ($bootRetry+1) + ' 次)...');
    $task = $vm.PowerOnVM_Task($null);
    Write-Output ('[STEP3] PowerOnVM_Task=' + $task.Value);
    $bootOk = $false;
    for ($bi = 0; $bi -lt 20; $bi++) {{
      Start-Sleep -Seconds 3;
      $t = Get-View -Id $task -Property Info.State,Info.Error -ErrorAction SilentlyContinue;
      if ($null -ne $t -and $t.Info.State -eq 'success') {{ Write-Output '[STEP3] 开机成功'; $bootOk = $true; break }}
      if ($null -ne $t -and $t.Info.State -eq 'error') {{ 
        $errLocal = $t.Info.Error.LocalizedMessage;
        Write-Output ('[STEP3] 开机返回 error: ' + $errLocal);
        if ($errLocal -match 'Unable to enumerate all disks|disk is not ready|not ready') {{
          $bootRetry++;
          Write-Output ('[STEP3] 磁盘未就绪类错误，等待 10 秒后重试...');
          Start-Sleep -Seconds 10;
          continue 2;
        }}
        throw ('开机失败: ' + $errLocal)
      }}
    }}
    if ($bootOk) {{ break }};
    if ($bootRetry -ge 3) {{ throw '开机失败: 重试 3 次后仍然无法开机' }};
  }}
  Write-Output '[STEP3] 开机后强制 Reconnect 网卡...';
  $netVms = Get-View -Id '{EscapePsString(vmMoRef)}' -Property Config.Hardware.Device -ErrorAction SilentlyContinue;
  if ($netVms) {{
    $rcSpec = New-Object VMware.Vim.VirtualMachineConfigSpec;
    $reconnectCount = 0;
    foreach ($dev in $netVms.Config.Hardware.Device) {{
      if ($dev -is [VMware.Vim.VirtualEthernetCard]) {{
        $ed = New-Object VMware.Vim.VirtualDeviceConfigSpec;
        $ed.Operation = [VMware.Vim.VirtualDeviceConfigSpecOperation]::edit;
        $ed.Device = $dev;
        $ed.Device.Connectable = New-Object VMware.Vim.VirtualDeviceConnectInfo;
        $ed.Device.Connectable.Connected = $true;
        $ed.Device.Connectable.StartConnected = $true;
        $ed.Device.Connectable.AllowGuestControl = $true;
        $rcSpec.DeviceChange += $ed;
        $reconnectCount++;
      }}
    }}
    if ($reconnectCount -gt 0) {{
      $rcTask = $netVms.ReconfigVM_Task($rcSpec);
      Write-Output ('[STEP3] Reconnect Task=' + $rcTask.Value + ' 网卡数=' + $reconnectCount);
      for ($ri=0; $ri -lt 15; $ri++) {{
        Start-Sleep -Seconds 2;
        $rt = Get-View -Id $rcTask -Property Info.State,Info.Error -ErrorAction SilentlyContinue;
        if ($rt -and $rt.Info.State -eq 'success') {{ Write-Output '[STEP3] Reconnect 成功'; break }}
        if ($rt -and $rt.Info.State -eq 'error') {{ Write-Output ('[STEP3] Reconnect 失败: ' + $rt.Info.Error.LocalizedMessage); break }}
      }}
    }}
  }}
}} else {{
  Write-Output ('[STEP3] 虚拟机已处于开机状态: {EscapePsString(vmname)}');
  Write-Output '[STEP3] 强制 Reconnect 网卡...';
  $netVms2 = Get-View -Id '{EscapePsString(vmMoRef)}' -Property Config.Hardware.Device -ErrorAction SilentlyContinue;
  if ($netVms2) {{
    $rcSpec2 = New-Object VMware.Vim.VirtualMachineConfigSpec;
    foreach ($dev in $netVms2.Config.Hardware.Device) {{
      if ($dev -is [VMware.Vim.VirtualEthernetCard]) {{
        $ed2 = New-Object VMware.Vim.VirtualDeviceConfigSpec;
        $ed2.Operation = [VMware.Vim.VirtualDeviceConfigSpecOperation]::edit;
        $ed2.Device = $dev;
        $ed2.Device.Connectable = New-Object VMware.Vim.VirtualDeviceConnectInfo;
        $ed2.Device.Connectable.Connected = $true;
        $ed2.Device.Connectable.StartConnected = $true;
        $ed2.Device.Connectable.AllowGuestControl = $true;
        $rcSpec2.DeviceChange += $ed2;
      }}
    }}
    $rcTask2 = $netVms2.ReconfigVM_Task($rcSpec2);
    Write-Output ('[STEP3] Reconnect Task=' + $rcTask2.Value);
    for ($ri=0; $ri -lt 15; $ri++) {{
      Start-Sleep -Seconds 2;
      $rt2 = Get-View -Id $rcTask2 -Property Info.State,Info.Error -ErrorAction SilentlyContinue;
      if ($rt2 -and $rt2.Info.State -eq 'success') {{ Write-Output '[STEP3] Reconnect 成功'; break }}
      if ($rt2 -and $rt2.Info.State -eq 'error') {{ Write-Output ('[STEP3] Reconnect 失败: ' + $rt2.Info.Error.LocalizedMessage); break }}
    }}
  }}
}}
";
        }

        private string BuildDeployScriptReconfigure(Dictionary<string, string> vmData, string vmMoRef)
        {
            string vmname = ComputeVmname(vmData);
            int diskCap = SafeInt(vmData.GetValueOrDefault("DiskCapacityGB", "0"), 0, 0, 65535);
            string scriptBody = BuildReconfigScript(vmData, vmMoRef);

            if (diskCap > 0)
            {
                string diskScript = $@"
$vmView = Get-View -Id '{EscapePsString(vmMoRef)}' -Property Config.Hardware.Device -ErrorAction Stop;
if (-not $vmView) {{ throw '未找到虚拟机 MoRef: {EscapePsString(vmMoRef)}' }};
$allDisks = @($vmView.Config.Hardware.Device | Where-Object {{ $_ -is [VMware.Vim.VirtualDisk] }});
$allScsi = @($vmView.Config.Hardware.Device | Where-Object {{ $_ -is [VMware.Vim.VirtualSCSIController] }});
if ($allScsi.Count -eq 0) {{ throw '未找到任何 SCSI 控制器（VirtualSCSIController 子类），请检查模板硬件' }};
$scsiCtrl = $allScsi | Sort-Object BusNumber, Key | Select-Object -First 1;
$systemDisk = $allDisks | Where-Object {{ $_.ControllerKey -eq $scsiCtrl.Key }} | Select-Object -First 1;
if (-not $systemDisk) {{ throw ('选中的 SCSI 控制器 (Key=' + $scsiCtrl.Key + ') 上无磁盘') }};
$targetCapBytes = [int64]{diskCap} * 1GB;
$configSpec = New-Object VMware.Vim.VirtualMachineConfigSpec;
$scsiDisks = @($allDisks | Where-Object {{ $_.ControllerKey -eq $scsiCtrl.Key }});
$dataDisk = if ($scsiDisks.Count -ge 2) {{ $scsiDisks[1] }} else {{ $null }};
if (-not $dataDisk) {{
    Write-Host ('数据盘不存在，新建第2块硬盘（容量: ' + {diskCap} + ' GB，挂 SCSI 控制器 Key=' + $scsiCtrl.Key + '）');
    $newDisk = New-Object VMware.Vim.VirtualDisk;
    $newDisk.Key = -1;
    $newDisk.ControllerKey = $scsiCtrl.Key;
    $usedUnits = @($scsiDisks | ForEach-Object {{ [int]$_.UnitNumber }});
    $nextUnit = 0; foreach ($u in $usedUnits) {{ if ($u -ge $nextUnit) {{ $nextUnit = $u + 1 }} }};
    $newDisk.UnitNumber = $nextUnit;
    $newDisk.CapacityInBytes = $targetCapBytes;
    $backing = New-Object VMware.Vim.VirtualDiskFlatVer2BackingInfo;
    $backing.DiskMode = [VMware.Vim.VirtualDiskMode]::persistent;
    $backing.ThinProvisioned = $true;
    $backing.Datastore = $systemDisk.Backing.Datastore;
    $backing.FileName = '';
    $newDisk.Backing = $backing;
    $diskSpec = New-Object VMware.Vim.VirtualDeviceConfigSpec;
    $diskSpec.Operation = [VMware.Vim.VirtualDeviceConfigSpecOperation]::add;
    $diskSpec.FileOperation = [VMware.Vim.VirtualDeviceConfigSpecFileOperation]::create;
    $diskSpec.Device = $newDisk;
    $configSpec.DeviceChange += $diskSpec;
    $task = $vmView.ReconfigVM_Task($configSpec);
    while ($true) {{ Start-Sleep -Seconds 3; $t = Get-View -Id $task -Property Info.State,Info.Error -ErrorAction SilentlyContinue; if ($null -ne $t -and $t.Info.State -eq 'success') {{ Write-Host '数据盘创建完成'; break }}; if ($null -ne $t -and $t.Info.State -eq 'error') {{ throw ('新建磁盘失败: ' + $t.Info.Error.LocalizedMessage) }} }}
}} else {{
    $existingCap = [math]::Round([double]$dataDisk.CapacityGB, 2);
    Write-Host ('数据盘已存在（当前容量: ' + $existingCap + ' GB）');
    if ({diskCap} -gt $existingCap) {{
        Write-Host ('扩展数据盘至: ' + {diskCap} + ' GB');
        $dataDisk.CapacityInBytes = $targetCapBytes;
        $diskSpec = New-Object VMware.Vim.VirtualDeviceConfigSpec;
        $diskSpec.Operation = [VMware.Vim.VirtualDeviceConfigSpecOperation]::edit;
        $diskSpec.Device = $dataDisk;
        $configSpec.DeviceChange += $diskSpec;
        $task = $vmView.ReconfigVM_Task($configSpec);
        while ($true) {{ Start-Sleep -Seconds 3; $t = Get-View -Id $task -Property Info.State,Info.Error -ErrorAction SilentlyContinue; if ($null -ne $t -and $t.Info.State -eq 'success') {{ Write-Host '数据盘扩容完成'; break }}; if ($null -ne $t -and $t.Info.State -eq 'error') {{ throw ('扩容磁盘失败: ' + $t.Info.Error.LocalizedMessage) }} }}
    }} elseif ({diskCap} -lt $existingCap) {{
        Write-Host ('目标容量小于当前容量，为避免数据丢失，跳过缩容操作')
    }} else {{
        Write-Host ('目标容量与当前容量一致，无需操作')
    }}
}}
";
                return scriptBody + "; " + diskScript;
            }

            return scriptBody;
        }

        private string ComputeSafeHostname(Dictionary<string, string> vmData)
        {
            string nameRaw = vmData.GetValueOrDefault("Name") ?? "";
            string ipRaw = vmData.GetValueOrDefault("IP") ?? "";

            string ipSuffix = "";
            if (!string.IsNullOrEmpty(ipRaw))
            {
                var parts = ipRaw.Split('.');
                if (parts.Length > 0 && int.TryParse(parts[parts.Length - 1], out int suffix))
                {
                    ipSuffix = suffix.ToString();
                }
            }

            string safeHostname;
            if (ContainsNonAscii(nameRaw))
            {
                string englishPart = ExtractEnglishPart(nameRaw);
                if (string.IsNullOrEmpty(englishPart)) englishPart = "vm";
                safeHostname = !string.IsNullOrEmpty(ipSuffix) ? $"{englishPart}-IP{ipSuffix}" : englishPart;
            }
            else
            {
                safeHostname = SafeHostname(nameRaw);
                if (safeHostname == "vm") safeHostname = "vm";
                else if (!string.IsNullOrEmpty(ipSuffix)) safeHostname = $"{safeHostname}-IP{ipSuffix}";
            }

            if (safeHostname.Length > 15)
            {
                if (!string.IsNullOrEmpty(ipSuffix))
                {
                    string suffixPart = "-IP" + ipSuffix;
                    int prefixMaxLen = 15 - suffixPart.Length;
                    if (prefixMaxLen < 2) prefixMaxLen = 2;
                    string prefix = SafeHostname(nameRaw);
                    if (prefix.Length > prefixMaxLen) prefix = prefix.Substring(0, prefixMaxLen);
                    safeHostname = prefix + suffixPart;
                }
                else
                {
                    safeHostname = SafeHostname(nameRaw).Substring(0, Math.Min(15, SafeHostname(nameRaw).Length));
                }
                safeHostname = safeHostname.TrimEnd('-');
                if (safeHostname.Length > 15) safeHostname = safeHostname.Substring(0, 15);
            }
            return safeHostname;
        }

        private string BuildReconfigScript(Dictionary<string, string> vmData, string vmMoRef)
        {
            int cpu = SafeInt(vmData.GetValueOrDefault("CPU", "1"), 1, 1, 4096);
            int memory = SafeInt(vmData.GetValueOrDefault("Memory", "1"), 1, 1, 65536);
            string notes = EscapePsString(vmData.GetValueOrDefault("Description") ?? vmData.GetValueOrDefault("Name") ?? "");
            string network = EscapePsString(vmData.GetValueOrDefault("Vlan") ?? "");
            string vmMoRefPs = EscapePsString(vmMoRef);

            string networkCond = !string.IsNullOrEmpty(network) ? $@"
  if ($dev -is [VMware.Vim.VirtualEthernetCard]) {{
    $ns = New-Object VMware.Vim.VirtualDeviceConfigSpec;
    $ns.Operation = [VMware.Vim.VirtualDeviceConfigSpecOperation]::edit;
    $ns.Device = $dev;
    $pgName = '{network}';
    $pgNameLower = $pgName.ToLower();
    $dvPg = Get-View -ViewType DistributedVirtualPortgroup -ErrorAction SilentlyContinue | Where-Object {{ $_.Name.ToLower() -eq $pgNameLower }} | Select-Object -First 1;
    $stdPg = $null;
    if (-not $dvPg) {{
      $stdPg = Get-View -ViewType Network -ErrorAction SilentlyContinue | Where-Object {{ $_.Name.ToLower() -eq $pgNameLower -and $_.MoRef.Type -eq 'Network' }} | Select-Object -First 1;
    }};
    if ($dvPg) {{
      $backing = New-Object VMware.Vim.VirtualEthernetCardDistributedVirtualPortBackingInfo;
      $backing.Port = New-Object VMware.Vim.DistributedVirtualSwitchPortConnection;
      $backing.Port.PortgroupKey = $dvPg.MoRef.Value;
      $switchUuid = $null;
      if ($dvPg.DistributedVirtualSwitch -and $dvPg.DistributedVirtualSwitch.Value) {{
        $dvsw = Get-View -Id $dvPg.DistributedVirtualSwitch -ErrorAction SilentlyContinue;
        if ($dvsw) {{ $switchUuid = $dvsw.Uuid }}
      }};
      if (-not $switchUuid) {{
        $dvSwitchList = Get-View -ViewType DistributedVirtualSwitch -ErrorAction SilentlyContinue;
        foreach ($dvs in $dvSwitchList) {{
          if ($dvs.Portgroup -and ($dvs.Portgroup | Where-Object {{ $_.Value -eq $dvPg.MoRef.Value }})) {{
                    $switchUuid = $dvs.Uuid; break
          }}
        }}
      }};
      if ($switchUuid) {{ $backing.Port.SwitchUuid = $switchUuid }};
      $ns.Device.Backing = $backing;
    }} elseif ($stdPg) {{
      $backing = New-Object VMware.Vim.VirtualEthernetCardNetworkBackingInfo;
      $backing.DeviceName = $pgName;
      $backing.Network = $stdPg.MoRef;
      $ns.Device.Backing = $backing;
    }} else {{
      Write-Warning ('WARN 未找到端口组: ' + $pgName + ', 跳过网卡切换');
      continue;
    }};
    $ns.Device.Connectable = New-Object VMware.Vim.VirtualDeviceConnectInfo;
    $ns.Device.Connectable.Connected = $true;
    $ns.Device.Connectable.StartConnected = $true;
    $ns.Device.Connectable.AllowGuestControl = $true;
    $configSpec.DeviceChange += $ns;
  }}" : "";

            return $@"
$ErrorActionPreference = 'Stop';
$vmView = Get-View -Id '{vmMoRefPs}' -Property Config.Hardware.Device,Config.Annotation -ErrorAction Stop;
if ($null -eq $vmView) {{ throw '未找到虚拟机 MoRef: {vmMoRefPs}' }};
$configSpec = New-Object VMware.Vim.VirtualMachineConfigSpec;
$configSpec.NumCPUs = {cpu};
$configSpec.MemoryMB = {memory} * 1024;
$configSpec.Annotation = '{notes}';
foreach ($dev in $vmView.Config.Hardware.Device) {{{networkCond}
}};
$reconfigTask = $vmView.ReconfigVM_Task($configSpec);
while ($true) {{
  Start-Sleep -Seconds 3;
  $t = Get-View -Id $reconfigTask -Property Info.State,Info.Error -ErrorAction SilentlyContinue;
  if ($null -ne $t -and $t.Info.State -eq 'success') {{ break }}
  if ($null -ne $t -and $t.Info.State -eq 'error') {{ throw ('ReconfigVM 失败: ' + $t.Info.Error.LocalizedMessage) }}
}}
";
        }

        public static string BuildGuestOpsScript(Dictionary<string, string> vmData, string vmMoRef, string guestPassword, string? customDns = null, bool guestOpsMode = false, string? guestUser = null)
        {
            string ipRaw = vmData.GetValueOrDefault("IP") ?? "";
            string gateway = vmData.GetValueOrDefault("Gateway") ?? "";
            string mask = vmData.GetValueOrDefault("Mask") ?? "";
            int prefixLen = ParsePrefixLenStatic(mask);
            string rawHostname = vmData.GetValueOrDefault("Hostname") ?? "";
            string vmNameRaw = vmData.GetValueOrDefault("Name") ?? "";

            static int ParsePrefixLenStatic(string mask)
            {
                if (string.IsNullOrEmpty(mask)) return 24;
                if (int.TryParse(mask, out int p) && p >= 0 && p <= 32) return p;
                var parts = mask.Split('.');
                if (parts.Length != 4) return 24;
                int bits = 0;
                foreach (var oct in parts)
                {
                    if (!int.TryParse(oct, out int v) || v < 0 || v > 255) return 24;
                    while (v > 0) { bits += v & 1; v >>= 1; }
                }
                return bits > 0 ? bits : 24;
            }

            static string ComputeSafeHostnameStatic(string raw, string vmNameRaw)
            {
                string source = !string.IsNullOrEmpty(raw) ? raw : vmNameRaw;
                if (string.IsNullOrEmpty(source)) return "";
                var sb = new System.Text.StringBuilder();
                foreach (char c in source)
                {
                    if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
                    else sb.Append('-');
                }
                string safe = sb.ToString();
                if (safe.Length > 63) safe = safe.Substring(0, 63);
                return safe;
            }

            string scriptTemplateLinux;
            string scriptTemplateWindows;
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            var asmName = asm.GetName().Name;
            string linuxRes = guestOpsMode ? "guestops-linux-network.sh" : "linux-network-config.sh";
            string windowsRes = guestOpsMode ? "guestops-windows-network.ps1" : "windows-network-config.ps1";
            string safeHostname = ComputeSafeHostnameStatic(rawHostname, vmNameRaw);

            try {
                string resName = asmName + ".scripts." + linuxRes;
                using var stream = asm.GetManifestResourceStream(resName);
                if (stream != null) {
                    using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);
                    scriptTemplateLinux = reader.ReadToEnd();
                } else {
                    scriptTemplateLinux = "#!/bin/bash\necho 'ERROR: Linux script resource not found: " + resName + "';\necho 'Guest Ops OK'; exit 0";
                }
            } catch (Exception ex) {
                scriptTemplateLinux = "#!/bin/bash\necho 'ERROR: Linux resource load: " + ex.Message.Replace("'", "\\'") + "';\necho 'Guest Ops OK'; exit 0";
            }
            try {
                string resName = asmName + ".scripts." + windowsRes;
                using var stream = asm.GetManifestResourceStream(resName);
                if (stream != null) {
                    using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);
                    scriptTemplateWindows = reader.ReadToEnd();
                } else {
                    scriptTemplateWindows = "Write-Host 'ERROR: Windows script resource not found: " + resName + "';\nWrite-Host 'Guest Ops OK'; exit 0";
                }
            } catch (Exception ex) {
                scriptTemplateWindows = "Write-Host 'ERROR: Windows resource load: " + ex.Message.Replace("'", "\\'") + "';\nWrite-Host 'Guest Ops OK'; exit 0";
            }

            string dnsLinux, dnsWindows;
            if (guestOpsMode)
            {
                dnsLinux = customDns ?? "";
                dnsWindows = customDns ?? "";
            }
            else
            {
                dnsLinux = customDns ?? DEFAULT_DNS_LINUX;
                dnsWindows = customDns ?? DEFAULT_DNS_WINDOWS;
                if (string.IsNullOrEmpty(safeHostname)) safeHostname = "vm-guest";
                if (string.IsNullOrEmpty(mask)) mask = "255.255.255.0";
            }

            string linuxScript = scriptTemplateLinux
                .Replace("${IP}", ipRaw)
                .Replace("${MASK}", prefixLen.ToString())
                .Replace("${GW}", gateway)
                .Replace("${DNS}", dnsLinux)
                .Replace("${HOSTNAME}", safeHostname);
            string windowsScript = scriptTemplateWindows
                .Replace("${IP}", ipRaw)
                .Replace("${MASK}", prefixLen.ToString())
                .Replace("${GW}", gateway)
                .Replace("${DNS}", dnsWindows)
                .Replace("${COMPUTERNAME}", safeHostname);

            string linuxB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(linuxScript));
            string windowsB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(windowsScript));
            string vmMoRefPs = vmMoRef.Replace("'", "''");
            string pwPs = guestPassword.Replace("'", "''");
            string userPs = (guestUser ?? "").Replace("'", "''");

            return $@"
$ErrorActionPreference = 'Stop';
Write-Host ('GuestOps Assembly=' + '{asmName}' + ' Scripts embedded=OK');
Write-Host ('GuestOps GuestOS 信息:');
$vmView = $null;
for ($i = 0; $i -lt 15; $i++) {{
  $vmView = Get-View -Id '{vmMoRefPs}' -Property Guest,Config.GuestFullName -ErrorAction Stop;
  if ($vmView.Guest -and ($vmView.Guest.GuestFamily -or $vmView.Guest.GuestFullName -or $vmView.Guest.IpAddress)) {{ break }}
  Write-Host ('GuestOps Guest 信息未就绪, 重试 ' + ($i+1) + '/15...');
  Start-Sleep -Seconds 3;
}}
$isLinux = $false;
if ($vmView.Guest -and $vmView.Guest.GuestFamily) {{
  $isLinux = ($vmView.Guest.GuestFamily -eq 'linuxGuest');
  Write-Host ('GuestOps GuestFamily: ' + $vmView.Guest.GuestFamily);
}} else {{
  Write-Host 'GuestOps GuestFamily 为空, 尝试从 GuestFullName 推断...';
  $osName = '';
  if ($vmView.Guest -and $vmView.Guest.GuestFullName) {{ $osName = $vmView.Guest.GuestFullName }}
  if ([string]::IsNullOrEmpty($osName) -and $vmView.Config -and $vmView.Config.GuestFullName) {{ $osName = $vmView.Config.GuestFullName }}
  Write-Host ('GuestOps OS Name: ' + $osName);
  $isLinux = $osName -match 'linux|kylin|openEuler|centos|ubuntu|debian|redhat|suse|fedora|alibaba|neokylin|uos';
  if (-not $isLinux -and $osName -and -not ($osName -match 'windows|Windows')) {{ $isLinux = $true; Write-Host 'GuestOps 未匹配 Windows，默认为 Linux' }}
  Write-Host ('GuestOps 推断 isLinux=' + $isLinux);
}}
$scriptContent = if ($isLinux) {{ [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{linuxB64}')) }} else {{ [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{windowsB64}')) }};
$userPsVal = '{userPs}';
$user = if ($userPsVal) {{ $userPsVal }} else {{ if ($isLinux) {{ 'root' }} else {{ 'Administrator' }} }};
$vi = Get-VM -Id '{vmMoRefPs}' -ErrorAction Stop;

$localTmp = Join-Path $env:TEMP ('vmt_ops_' + [IO.Path]::GetRandomFileName());
$guestTmp = if ($isLinux) {{ '/tmp/vmt_ops_' + [IO.Path]::GetRandomFileName() + '.sh' }} else {{ 'C:\Windows\Temp\vmt_ops_' + [IO.Path]::GetRandomFileName() + '.ps1' }};

$utf8NoBom = [Text.UTF8Encoding]::new($false);
$utf8Bom = [Text.UTF8Encoding]::new($true);
$enc = if ($isLinux) {{ $utf8NoBom }} else {{ $utf8Bom }};
try {{
  Write-Host ('GuestOps 推送脚本到 Guest...');
  [IO.File]::WriteAllText($localTmp, $scriptContent, $enc);
  Copy-VMGuestFile -VM $vi -Source $localTmp -Destination $guestTmp -LocalToGuest -GuestUser $user -GuestPassword '{pwPs}' -Force -ErrorAction Stop | Out-Null;
  Write-Host ('GuestOps 脚本已推送: ' + $guestTmp);

  if ($isLinux) {{
    $runCmd = 'bash ' + $guestTmp;
  }} else {{
    $runCmd = 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File ' + $guestTmp;
  }}
  Write-Host ('GuestOps 执行: ' + $runCmd);
  $result = Invoke-VMScript -VM $vi -ScriptText $runCmd -GuestUser $user -GuestPassword '{pwPs}' -ErrorAction SilentlyContinue;
  Write-Host ('GuestOps ExitCode=' + $result.ExitCode + ' GuestOS=' + $result.GuestOS);
  Write-Host ('GuestOps ScriptOutput:'); Write-Host $result.ScriptOutput;
  if ($result.ScriptError) {{ Write-Host ('GuestOps ScriptError:'); Write-Host $result.ScriptError }};
  if ($result.ExitCode -ne 0) {{ throw ('脚本执行失败 ExitCode=' + $result.ExitCode) }}
}} finally {{
  Remove-Item $localTmp -Force -ErrorAction SilentlyContinue;
}}
Write-Host 'GuestOps 网络配置完成';
";
        }

        private (bool isValid, string error) ValidateVmData(Dictionary<string, string> vmData, bool forNewVm = true)
        {
            var errors = new List<string>();
            var unsafeChars = new HashSet<char>("$`\";&|{}<>");

            void CheckField(string name, string value, bool allowEmpty = false)
            {
                string v = allowEmpty ? (value ?? "") : (value ?? "");
                if (string.IsNullOrEmpty(v) && !allowEmpty)
                {
                    errors.Add($"{name} 不能为空");
                    return;
                }
                foreach (char ch in v)
                {
                    int o = (int)ch;
                    if (o < 0x20 || o == 0x7F)
                    {
                        errors.Add($"{name} 包含控制字符（不允许）");
                        return;
                    }
                    if (unsafeChars.Contains(ch))
                    {
                        errors.Add($"{name} 包含非法字符 '{ch}'（不允许 $ ` 引号 ; & | 等）");
                        return;
                    }
                }
            }

            string name = (vmData.GetValueOrDefault("Name") ?? "").Trim();
            string ip = (vmData.GetValueOrDefault("IP") ?? "").Trim();
            CheckField("Name", name);
            CheckField("IP", ip);
            if (!string.IsNullOrEmpty(ip))
            {
                var parts = ip.Split('.');
                if (parts.Length != 4 || !parts.All(p => int.TryParse(p, out int n) && n >= 0 && n <= 255))
                {
                    errors.Add("IP 格式不正确（应为 x.x.x.x）");
                }
            }

            if (forNewVm)
            {
                string template = (vmData.GetValueOrDefault("Template") ?? "").Trim();
                CheckField("Template", template);
            }
            try
            {
                int cpuVal = SafeInt(vmData.GetValueOrDefault("CPU", "1"), 1, 1, 4096);
                if (cpuVal <= 0) errors.Add("CPU 必须大于 0");
                if (cpuVal > 4096) errors.Add("CPU 数值过大");
            }
            catch { errors.Add("CPU 必须是整数"); }

            try
            {
                int memVal = SafeInt(vmData.GetValueOrDefault("Memory", "1"), 1, 1, 65536);
                if (memVal <= 0) errors.Add("Memory 必须大于 0");
                if (memVal > 65536) errors.Add("Memory 数值过大");
            }
            catch { errors.Add("Memory 必须是整数"); }

            string vlan = (vmData.GetValueOrDefault("Vlan") ?? "").Trim();
            CheckField("Vlan", vlan);
            string gateway = (vmData.GetValueOrDefault("Gateway") ?? "").Trim();
            if (!string.IsNullOrEmpty(gateway))
            {
                CheckField("Gateway", gateway);
                var parts = gateway.Split('.');
                if (parts.Length != 4 || !parts.All(p => int.TryParse(p, out int n) && n >= 0 && n <= 255))
                {
                    errors.Add("Gateway 格式不正确");
                }
            }
            string mask = (vmData.GetValueOrDefault("Mask") ?? "").Trim();
            if (!string.IsNullOrEmpty(mask)) CheckField("Mask", mask);

            try
            {
                int disk = SafeInt(vmData.GetValueOrDefault("DiskCapacityGB", "0"), 0, 0, 65535);
                if (disk < 0) errors.Add("DiskCapacityGB 不能为负数");
                if (disk > 65535) errors.Add("DiskCapacityGB 数值过大");
            }
            catch { errors.Add("DiskCapacityGB 必须是整数"); }

            string host = (vmData.GetValueOrDefault("Host") ?? "").Trim();
            if (!string.IsNullOrEmpty(host)) CheckField("Host", host);
            string datastore = (vmData.GetValueOrDefault("Datastore") ?? "").Trim();
            if (!string.IsNullOrEmpty(datastore)) CheckField("Datastore", datastore);
            string desc = (vmData.GetValueOrDefault("Description") ?? "").Trim();
            if (!string.IsNullOrEmpty(desc)) CheckField("Description", desc);

            if (errors.Count > 0)
            {
                return (false, string.Join("; ", errors));
            }
            return (true, "");
        }

        private string ComputeVmname(Dictionary<string, string> vmData)
        {
            string nameRaw = (vmData.GetValueOrDefault("Name") ?? "").Trim();
            string ipRaw = (vmData.GetValueOrDefault("IP") ?? "").Trim();
            if (string.IsNullOrEmpty(nameRaw)) nameRaw = "vm";

            string ipSuffix = "x";
            if (!string.IsNullOrEmpty(ipRaw))
            {
                var parts = ipRaw.Split('.');
                string last = parts.Length > 0 ? parts[parts.Length - 1] : "";
                if (!string.IsNullOrEmpty(last) && int.TryParse(last, out _))
                {
                    ipSuffix = last;
                }
            }

            string safeName = SafeName(nameRaw, 50);
            return $"{safeName}-IP{ipSuffix}";
        }

        private string SafeName(string s, int maxLen = 64)
        {
            if (string.IsNullOrEmpty(s)) return "default";
            string text = s.Trim();
            if (string.IsNullOrEmpty(text)) return "default";

            var outChars = new List<char>();
            foreach (char ch in text)
            {
                if (_safeNamePattern.Contains(ch))
                {
                    outChars.Add(ch);
                }
                else
                {
                    int o = (int)ch;
                    if (o >= 0x4E00 && o <= 0x9FFF)
                    {
                        outChars.Add(ch);
                    }
                    else
                    {
                        outChars.Add('_');
                    }
                }
            }

            string result = new string(outChars.ToArray()).Trim('.', '_');
            if (string.IsNullOrEmpty(result)) result = "default";
            if (result.Length > maxLen) result = result.Substring(0, maxLen);
            return result;
        }

        private string SafeHostname(string s)
        {
            if (string.IsNullOrEmpty(s)) return "vm";
            string text = s.Trim();
            if (string.IsNullOrEmpty(text)) return "vm";

            var outChars = new List<char>();
            foreach (char ch in text)
            {
                if (char.IsLetterOrDigit(ch) || ch == '-')
                {
                    outChars.Add(ch);
                }
            }

            string result = new string(outChars.ToArray());
            if (string.IsNullOrEmpty(result)) return "vm";
            result = result.Trim('-');
            if (result.Length > 15)
            {
                result = result.Substring(0, 15).TrimEnd('-');
                if (string.IsNullOrEmpty(result)) result = "vm";
            }
            return result;
        }

        /// <summary>
        /// 检测字符串是否包含非ASCII字符（如中文）
        /// </summary>
        private bool ContainsNonAscii(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char ch in s)
            {
                if (ch > 127) return true;
            }
            return false;
        }

        /// <summary>
        /// 从混合字符串中提取英文部分（转小写）
        /// 例如：浏阳中医院NGINX → nginx
        /// </summary>
        private string ExtractEnglishPart(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            
            var outChars = new List<char>();
            foreach (char ch in s)
            {
                // 只保留英文字母和数字
                if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9'))
                {
                    outChars.Add(char.ToLower(ch));
                }
            }
            
            string result = new string(outChars.ToArray());
            // 去除首尾的连字符和数字（如果有的话）
            result = result.TrimStart('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '-');
            result = result.TrimEnd('-');
            
            return result;
        }

        private int SafeInt(string val, int defaultValue = 1, int minVal = 1, int maxVal = 65536)
        {
            if (string.IsNullOrEmpty(val)) return defaultValue;
            try
            {
                int v = (int)double.Parse(val);
                if (v < minVal) return minVal;
                if (v > maxVal) return maxVal;
                return v;
            }
            catch
            {
                return defaultValue;
            }
        }

        private int ParsePrefixLen(string mask)
        {
            if (string.IsNullOrEmpty(mask)) return 24;
            if (int.TryParse(mask, out int p) && p >= 0 && p <= 32) return p;
            var parts = mask.Split('.');
            if (parts.Length != 4) return 24;
            int bits = 0;
            foreach (var part in parts)
            {
                if (!int.TryParse(part, out int b) || b < 0 || b > 255) return 24;
                bits += Convert.ToString(b, 2).Count(c => c == '1');
            }
            return bits;
        }

        private string CidrToNetmask(string mask)
        {
            if (string.IsNullOrEmpty(mask)) return "255.255.255.0";
            if (mask.Contains('.')) return mask;
            if (int.TryParse(mask, out int cidr) && cidr >= 0 && cidr <= 32)
            {
                uint netmask = cidr == 0 ? 0 : 0xFFFFFFFF << (32 - cidr);
                return $"{(netmask >> 24) & 0xFF}.{(netmask >> 16) & 0xFF}.{(netmask >> 8) & 0xFF}.{netmask & 0xFF}";
            }
            return "255.255.255.0";
        }

        private string EscapePsString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var cleanChars = new List<char>();
            foreach (char ch in s)
            {
                int o = (int)ch;
                if (o < 0x20 || o == 0x7F)
                {
                    cleanChars.Add(' ');
                }
                else
                {
                    cleanChars.Add(ch);
                }
            }
            string text = new string(cleanChars.ToArray());
            return text.Replace("'", "''");
        }

        private List<Dictionary<string, string>> LoadCsv(string filePath)
        {
            var result = new List<Dictionary<string, string>>();
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, true);
            
            string? headerLine = reader.ReadLine();
            if (string.IsNullOrEmpty(headerLine)) return result;
            
            var headers = headerLine.Split(',');

            while (!reader.EndOfStream)
            {
                string? line = reader.ReadLine();
                if (string.IsNullOrEmpty(line)) continue;

                var fields = line.Split(',');
                var row = new Dictionary<string, string>();
                for (int i = 0; i < headers.Length; i++)
                {
                    string key = headers[i].Trim();
                    string value = i < fields.Length ? fields[i].Trim() : "";
                    row[key] = value;
                }

                // 过滤所有字段均为空的行（编辑列表中的空行）
                bool hasAnyValue = false;
                foreach (var kvp in row)
                {
                    if (!string.IsNullOrEmpty(kvp.Value))
                    {
                        hasAnyValue = true;
                        break;
                    }
                }
                if (!hasAnyValue) continue;

                result.Add(row);
            }

            return result;
        }

        private void LogStageHeader(string message)
        {
            _logCallback("info", new string('=', 60));
            _logCallback("info", message);
            _logCallback("info", new string('=', 60));
        }

        private void LogVmHeader(int vmIndex, int vmTotal, string label)
        {
            _logCallback("info", "");
            _logCallback("info", $"[{vmIndex}/{vmTotal}] 开始处理: {label}");
            _logCallback("info", new string('-', 40));
        }

        private void LogVmStep(string message)
        {
            _logCallback("info", $"  {message}");
        }

        private void LogVmResult(string message)
        {
            _logCallback("info", $"  {message}");
        }

        private void LogVmListPreview(List<Dictionary<string, string>> vmList)
        {
            _logCallback("info", $"待部署虚拟机列表:");
            for (int i = 0; i < Math.Min(vmList.Count, 5); i++)
            {
                var row = vmList[i];
                string name = row.GetValueOrDefault("Name") ?? "";
                string ip = row.GetValueOrDefault("IP") ?? "";
                _logCallback("info", $"  {i + 1}. {name} ({ip})");
            }
            if (vmList.Count > 5)
            {
                _logCallback("info", $"  ... 及其他 {vmList.Count - 5} 台");
            }
        }

        private void LogSummaryTable(List<DeployResult> results, TimeSpan elapsed, bool cancelled = false)
        {
            _logCallback("info", "");
            _logCallback("info", new string('=', 60));
            _logCallback("info", cancelled ? "部署已终止" : "部署完成摘要");
            _logCallback("info", new string('=', 60));
            
            int successCount = results.Count(r => r.Success);
            int failCount = results.Count - successCount;
            
            if (cancelled)
            {
                _logCallback("info", $"已终止: 共 {results.Count} 台 | 成功: {successCount} 台 | 失败: {failCount} 台");
            }
            else
            {
                _logCallback("info", $"总计: {results.Count} 台 | 成功: {successCount} 台 | 失败: {failCount} 台");
            }
            _logCallback("info", $"总耗时: {FormatDuration((int)elapsed.TotalSeconds)}");
            _logCallback("info", "");

            foreach (var r in results)
            {
                string icon = r.Success ? "✓" : "✗";
                _logCallback("info", $"  {icon} {r.Label} ({r.Ip}) - {r.Status} ({FormatDuration(r.Elapsed)})");
            }
        }

        private string FormatDuration(int seconds)
        {
            if (seconds < 60) return $"{seconds}秒";
            int minutes = seconds / 60;
            int secs = seconds % 60;
            return $"{minutes}分{secs}秒";
        }
    }

    public class DeployResult
    {
        public string Label { get; set; } = "";
        public string Ip { get; set; } = "";
        public string Status { get; set; } = "";
        public int Elapsed { get; set; }
        public bool Success { get; set; }
        public string VmMoRef { get; set; }
    }
}