// ============================================================================
// VmOpsPower.cs - 虚拟机电源操作（Get-View + VIM API）
// ============================================================================
// 所有方法：static async Task，传入 vmName + logCallback，通过 SessionManager 执行
//
// 核心方法：
//   ✅ PowerOnVmAsync / PowerOffVmAsync / RestartVmAsync / SuspendVmAsync / ForceOffVmAsync
//   ✅ PowerStateQueryAsync — 查询电源状态
//
// 约束：
//   统一用 Get-View -ViewType VirtualMachine + MoRef + *_Task() 写操作
//   禁用 Get-VM / Start-VM / Stop-VM 等高级 cmdlet
// ============================================================================
using System;
using System.Threading.Tasks;
using VMwareOps.Core;

namespace VMwareOps.VmOps
{
    public static class VmOpsPower
    {
        public static async Task<bool> PowerOnVmAsync(string vmName, Action<string?, string?>? logCallback = null)
        {
            try
            {
                string script = $@"
$ErrorActionPreference = 'Stop';
$vm = Get-View -ViewType VirtualMachine -Filter @{{Name='{EscapePsString(vmName)}'; 'Config.Template'='False'}} -Property Name,Runtime.PowerState -ErrorAction SilentlyContinue;
if (-not $vm) {{ Write-Output '未找到虚拟机: {EscapePsString(vmName)}'; return 'ERROR' }};
if ($vm.Runtime.PowerState -eq 'poweredOn') {{ Write-Output '虚拟机已开机: ' + $vm.Name; return 'SUCCESS' }};
$task = $vm.PowerOnVM_Task($null);
while ($true) {{ Start-Sleep -Seconds 3; $t = Get-View -Id $task -Property Info.State,Info.Error -ErrorAction SilentlyContinue; if ($null -ne $t -and $t.Info.State -eq 'success') {{ Write-Output '开机成功: ' + $vm.Name; return 'SUCCESS' }}; if ($null -ne $t -and $t.Info.State -eq 'error') {{ Write-Output ('开机失败: ' + $t.Info.Error.LocalizedMessage); return 'ERROR' }} }}
";
                string result = await SessionManager.Instance.ExecutePsScriptAsync(script, logCallback, 60);
                return result.Contains("SUCCESS");
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"开机失败: {ex.Message}", "error");
                return false;
            }
        }

        public static async Task<bool> PowerOffVmAsync(string vmName, Action<string?, string?>? logCallback = null)
        {
            try
            {
                string script = $@"
$ErrorActionPreference = 'Stop';
$vm = Get-View -ViewType VirtualMachine -Filter @{{Name='{EscapePsString(vmName)}'; 'Config.Template'='False'}} -Property Name,Runtime.PowerState,Guest.ToolsStatus -ErrorAction SilentlyContinue;
if (-not $vm) {{ Write-Output '未找到虚拟机: {EscapePsString(vmName)}'; return 'ERROR' }};
if ($vm.Runtime.PowerState -ne 'poweredOn') {{ Write-Output '虚拟机已关机: ' + $vm.Name; return 'SUCCESS' }};
$toolsOk = $vm.Guest -and ($vm.Guest.ToolsStatus -eq 'toolsOk' -or $vm.Guest.ToolsStatus -eq 'toolsOld');
if (-not $toolsOk) {{ Write-Output 'VMware Tools 未运行，无法发送关闭客户机指令'; return 'ERROR' }};
try {{ $vm.ShutdownGuest(); Write-Output '已发送关闭客户机指令: ' + $vm.Name }} catch {{ Write-Output ('发送关闭指令失败: ' + $_.Exception.Message); return 'ERROR' }};
$deadline = (Get-Date).AddMinutes(5);
while ((Get-Date) -lt $deadline) {{ Start-Sleep -Seconds 10; $ch = Get-View -Id $vm.MoRef -Property Runtime.PowerState -ErrorAction SilentlyContinue; if ($ch -and $ch.Runtime.PowerState -ne 'poweredOn') {{ Write-Output '关闭客户机成功: ' + $vm.Name; return 'SUCCESS' }} }}
Write-Output '关闭客户机超时（5分钟），客户机可能未响应'; return 'ERROR';
";
                string result = await SessionManager.Instance.ExecutePsScriptAsync(script, logCallback, 60);
                return result.Contains("SUCCESS");
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"关闭客户机失败: {ex.Message}", "error");
                return false;
            }
        }

        public static async Task<bool> PowerOffHardVmAsync(string vmName, Action<string?, string?>? logCallback = null)
        {
            try
            {
                string script = $@"
$ErrorActionPreference = 'Stop';
$vm = Get-View -ViewType VirtualMachine -Filter @{{Name='{EscapePsString(vmName)}'; 'Config.Template'='False'}} -Property Name,Runtime.PowerState -ErrorAction SilentlyContinue;
if (-not $vm) {{ Write-Output '未找到虚拟机: {EscapePsString(vmName)}'; return 'ERROR' }};
if ($vm.Runtime.PowerState -ne 'poweredOn') {{ Write-Output '虚拟机已关机: ' + $vm.Name; return 'SUCCESS' }};
$task = $vm.PowerOffVM_Task();
while ($true) {{ Start-Sleep -Seconds 3; $t = Get-View -Id $task -Property Info.State,Info.Error -ErrorAction SilentlyContinue; if ($null -ne $t -and $t.Info.State -eq 'success') {{ Write-Output '关闭电源成功: ' + $vm.Name; return 'SUCCESS' }}; if ($null -ne $t -and $t.Info.State -eq 'error') {{ Write-Output ('关闭电源失败: ' + $t.Info.Error.LocalizedMessage); return 'ERROR' }} }}
";
                string result = await SessionManager.Instance.ExecutePsScriptAsync(script, logCallback, 60);
                return result.Contains("SUCCESS");
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"关闭电源失败: {ex.Message}", "error");
                return false;
            }
        }

        public static async Task<bool> ResetVmAsync(string vmName, Action<string?, string?>? logCallback = null)
        {
            try
            {
                string script = $@"
$ErrorActionPreference = 'Stop';
$vm = Get-View -ViewType VirtualMachine -Filter @{{Name='{EscapePsString(vmName)}'; 'Config.Template'='False'}} -Property Name,Runtime.PowerState -ErrorAction SilentlyContinue;
if (-not $vm) {{ Write-Output '未找到虚拟机: {EscapePsString(vmName)}'; return 'ERROR' }};
if ($vm.Runtime.PowerState -ne 'poweredOn') {{ Write-Output '虚拟机非开机状态，无法重置电源'; return 'ERROR' }};
$task = $vm.ResetVM_Task();
while ($true) {{ Start-Sleep -Seconds 3; $t = Get-View -Id $task -Property Info.State,Info.Error -ErrorAction SilentlyContinue; if ($null -ne $t -and $t.Info.State -eq 'success') {{ Write-Output '重置电源成功: ' + $vm.Name; return 'SUCCESS' }}; if ($null -ne $t -and $t.Info.State -eq 'error') {{ Write-Output ('重置电源失败: ' + $t.Info.Error.LocalizedMessage); return 'ERROR' }} }}
";
                string result = await SessionManager.Instance.ExecutePsScriptAsync(script, logCallback, 60);
                return result.Contains("SUCCESS");
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"重置电源失败: {ex.Message}", "error");
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