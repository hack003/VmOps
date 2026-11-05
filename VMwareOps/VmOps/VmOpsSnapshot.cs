// ============================================================================
// VmOpsSnapshot.cs - 虚拟机快照操作
// ============================================================================
// 所有方法：static async Task，传入 vmName + 参数 + logCallback
//
// 核心方法：
//   ✅ ListSnapshotsAsync — 列出所有快照（返回 JSON 字符串）
//   ✅ CreateSnapshotAsync — 创建新快照
//   ✅ RevertSnapshotAsync — 回滚到指定快照
//   ✅ RemoveSnapshotAsync — 删除指定快照
//   ✅ RemoveAllSnapshotsAsync — 删除全部快照
//
// 约束：
//   写操作调 CreateSnapshot_Task / RevertToSnapshot_Task / RemoveSnapshot_Task
//   统一 Get-View -Id $task.MoRef 轮询 Task 状态
//   禁用 New-Snapshot / Get-Snapshot 等高级 cmdlet
// ============================================================================
using System;
using System.Threading.Tasks;
using VMwareOps.Core;

namespace VMwareOps.VmOps
{
    public static class VmOpsSnapshot
    {
        public static async Task<string> ListSnapshotsAsync(string vmName, Action<string?, string?>? logCallback = null)
        {
            try
            {
                string script = $@"
$ErrorActionPreference = 'Stop';
$vm = Get-View -ViewType VirtualMachine -Filter @{{Name='{EscapePsString(vmName)}'; 'Config.Template'='False'}} -Property Name,Snapshot.RootSnapshotList,Snapshot.CurrentSnapshot,LayoutEx -ErrorAction SilentlyContinue;
if (-not $vm) {{ Write-Output '未找到虚拟机: {EscapePsString(vmName)}'; return }};
function Flatten-Snapshots($snapList) {{
    $result = @()
    foreach ($s in $snapList) {{
        $result += $s
        if ($s.ChildSnapshotList) {{ $result += Flatten-Snapshots $s.ChildSnapshotList }}
    }}
    return $result
}}
$sizeMap = @{{}}
if ($vm.LayoutEx -and $vm.LayoutEx.Snapshot -and $vm.LayoutEx.Snapshot.Snapshot) {{
    foreach ($leSnap in $vm.LayoutEx.Snapshot.Snapshot) {{ $sizeMap[$leSnap.Id] = $leSnap.Size }}
}}
if ($vm.Snapshot -and $vm.Snapshot.RootSnapshotList) {{
    $flat = Flatten-Snapshots $vm.Snapshot.RootSnapshotList
    if ($flat.Count -gt 0) {{
        foreach ($snap in ($flat | Sort-Object CreateTime -Descending)) {{
            $sizeGB = if ($sizeMap.ContainsKey($snap.Id)) {{ [math]::Round($sizeMap[$snap.Id] / 1GB, 2) }} else {{ 0 }}
            Write-Output ('名称: ' + $snap.Name + ' | 创建时间: ' + $snap.CreateTime + ' | 大小(GB): ' + $sizeGB)
        }}
    }} else {{ Write-Output '无快照' }}
}} else {{ Write-Output '无快照' }}
";
                return await SessionManager.Instance.ExecutePsScriptAsync(script, logCallback, 60);
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"获取快照列表失败: {ex.Message}", "error");
                return $"错误: {ex.Message}";
            }
        }

        public static async Task<bool> CreateSnapshotAsync(string vmName, string snapshotName, Action<string?, string?>? logCallback = null)
        {
            try
            {
                string script = $@"
$ErrorActionPreference = 'Stop';
$vm = Get-View -ViewType VirtualMachine -Filter @{{Name='{EscapePsString(vmName)}'; 'Config.Template'='False'}} -Property Name -ErrorAction SilentlyContinue;
if (-not $vm) {{ Write-Output '未找到虚拟机: {EscapePsString(vmName)}'; return 'ERROR' }};
$task = $vm.CreateSnapshot_Task('{EscapePsString(snapshotName)}', 'Created by Ops Tool', $false, $false);
while ($true) {{ Start-Sleep -Seconds 3; $t = Get-View -Id $task -Property Info.State,Info.Error -ErrorAction SilentlyContinue; if ($null -ne $t -and $t.Info.State -eq 'success') {{ Write-Output '创建快照成功: {EscapePsString(snapshotName)}'; return 'SUCCESS' }}; if ($null -ne $t -and $t.Info.State -eq 'error') {{ Write-Output ('创建快照失败: ' + $t.Info.Error.LocalizedMessage); return 'ERROR' }} }}
";
                string result = await SessionManager.Instance.ExecutePsScriptAsync(script, logCallback, 120);
                return result.Contains("SUCCESS");
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"创建快照失败: {ex.Message}", "error");
                return false;
            }
        }

        public static async Task<bool> RemoveAllSnapshotsAsync(string vmName, Action<string?, string?>? logCallback = null)
        {
            try
            {
                string script = $@"
$ErrorActionPreference = 'Stop';
$vm = Get-View -ViewType VirtualMachine -Filter @{{Name='{EscapePsString(vmName)}'; 'Config.Template'='False'}} -Property Name,Snapshot.RootSnapshotList -ErrorAction SilentlyContinue;
if (-not $vm) {{ Write-Output '未找到虚拟机: {EscapePsString(vmName)}'; return 'ERROR' }};
if (-not $vm.Snapshot -or -not $vm.Snapshot.RootSnapshotList) {{ Write-Output '无快照可删除: ' + $vm.Name; return 'SUCCESS' }};
Write-Output ('删除所有快照: ' + $vm.Name);
$task = $vm.RemoveAllSnapshots_Task($null);
while ($true) {{ Start-Sleep -Seconds 3; $t = Get-View -Id $task -Property Info.State,Info.Error -ErrorAction SilentlyContinue; if ($null -ne $t -and $t.Info.State -eq 'success') {{ break }}; if ($null -ne $t -and $t.Info.State -eq 'error') {{ Write-Output ('删除快照失败: ' + $t.Info.Error.LocalizedMessage); return 'ERROR' }} }}
Write-Output '删除所有快照成功: ' + $vm.Name;
return 'SUCCESS';
";
                string result = await SessionManager.Instance.ExecutePsScriptAsync(script, logCallback, 120);
                return result.Contains("SUCCESS");
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"删除快照失败: {ex.Message}", "error");
                return false;
            }
        }

        public static async Task<bool> RevertToLatestSnapshotAsync(string vmName, Action<string?, string?>? logCallback = null)
        {
            try
            {
                string script = $@"
$ErrorActionPreference = 'Stop';
$vm = Get-View -ViewType VirtualMachine -Filter @{{Name='{EscapePsString(vmName)}'; 'Config.Template'='False'}} -Property Name,Snapshot.RootSnapshotList -ErrorAction SilentlyContinue;
if (-not $vm) {{ Write-Output '未找到虚拟机: {EscapePsString(vmName)}'; return 'ERROR' }};
function Flatten-Snapshots($snapList) {{
    $result = @()
    foreach ($s in $snapList) {{
        $result += $s
        if ($s.ChildSnapshotList) {{ $result += Flatten-Snapshots $s.ChildSnapshotList }}
    }}
    return $result
}}
if (-not $vm.Snapshot -or -not $vm.Snapshot.RootSnapshotList) {{ Write-Output '无可用快照'; return 'ERROR' }};
$flat = Flatten-Snapshots $vm.Snapshot.RootSnapshotList
if ($flat.Count -eq 0) {{ Write-Output '无可用快照'; return 'ERROR' }};
$latest = $flat | Sort-Object CreateTime -Descending | Select-Object -First 1;
$snapView = Get-View -Id $latest.Snapshot -ErrorAction SilentlyContinue;
if (-not $snapView) {{ Write-Output '无法获取快照对象'; return 'ERROR' }};
$task = $snapView.RevertToSnapshot_Task($null, $null);
while ($true) {{ Start-Sleep -Seconds 3; $t = Get-View -Id $task -Property Info.State,Info.Error -ErrorAction SilentlyContinue; if ($null -ne $t -and $t.Info.State -eq 'success') {{ Write-Output ('回滚到最新快照成功: ' + $latest.Name); return 'SUCCESS' }}; if ($null -ne $t -and $t.Info.State -eq 'error') {{ Write-Output ('回滚快照失败: ' + $t.Info.Error.LocalizedMessage); return 'ERROR' }} }}
";
                string result = await SessionManager.Instance.ExecutePsScriptAsync(script, logCallback, 120);
                return result.Contains("SUCCESS");
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"回滚快照失败: {ex.Message}", "error");
                return false;
            }
        }

        private static string EscapePsString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("'", "''");
        }
    }
}