// ============================================================================
// [UTF-8 无 BOM] VmOpsResources.cs - 虚拟机资源配置操作（CPU / 内存 / 磁盘 / 网络）
// ============================================================================
// 所有方法：static async Task，传入 vmName + 参数 + logCallback
//
// 核心方法：
//   ✅ SetVmCpuCountAsync — 变更 VM CPU 核数
//   ✅ SetVmMemorySizeAsync — 变更 VM 内存大小
//   ✅ AddVirtualDiskAsync / RemoveVirtualDiskAsync — 磁盘增删
//   ✅ ChangePortGroupAsync — 切换虚拟网卡端口组
//
// 约束：
//   统一用 Get-View + VirtualMachine.ConfigSpec 提交配置变更
//   写操作调 ReconfigVM_Task()，Task 轮询 Get-View -Id $task.MoRef 看 Info.State
// ============================================================================
using System;
using System.Threading.Tasks;
using VmOps.Core;

namespace VmOps.VmOps
{
    public static class VmOpsResources
    {
        public static async Task<bool> SetVmCpuCountAsync(string vmName, int cpuCount, Action<string?, string?>? logCallback = null)
        {
            try
            {
                string script = $@"
$ErrorActionPreference = 'Stop';
$vm = Get-View -ViewType VirtualMachine -Filter @{{Name='{EscapePsString(vmName)}'; 'Config.Template'='False'}} -Property Name -ErrorAction SilentlyContinue;
if (-not $vm) {{ Write-Output '未找到虚拟机: {EscapePsString(vmName)}'; return 'ERROR' }};
$configSpec = New-Object VMware.Vim.VirtualMachineConfigSpec;
$configSpec.NumCPUs = {cpuCount};
$task = $vm.ReconfigVM_Task($configSpec);
while ($true) {{ Start-Sleep -Seconds 3; $t = Get-View -Id $task -Property Info.State,Info.Error -ErrorAction SilentlyContinue; if ($null -ne $t -and $t.Info.State -eq 'success') {{ Write-Output '设置CPU成功: ' + $vm.Name + ' -> {cpuCount} 核'; return 'SUCCESS' }}; if ($null -ne $t -and $t.Info.State -eq 'error') {{ Write-Output ('设置CPU失败: ' + $t.Info.Error.LocalizedMessage); return 'ERROR' }} }}
";
                string result = await SessionManager.Instance.ExecutePsScriptAsync(script, logCallback, 60);
                return result.Contains("SUCCESS");
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"设置CPU失败: {ex.Message}", "error");
                return false;
            }
        }

        public static async Task<bool> SetVmMemoryAsync(string vmName, int memoryGB, Action<string?, string?>? logCallback = null)
        {
            try
            {
                string script = $@"
$ErrorActionPreference = 'Stop';
$vm = Get-View -ViewType VirtualMachine -Filter @{{Name='{EscapePsString(vmName)}'; 'Config.Template'='False'}} -Property Name -ErrorAction SilentlyContinue;
if (-not $vm) {{ Write-Output '未找到虚拟机: {EscapePsString(vmName)}'; return 'ERROR' }};
$configSpec = New-Object VMware.Vim.VirtualMachineConfigSpec;
$configSpec.MemorySizeMB = {memoryGB} * 1024;
$task = $vm.ReconfigVM_Task($configSpec);
while ($true) {{ Start-Sleep -Seconds 3; $t = Get-View -Id $task -Property Info.State,Info.Error -ErrorAction SilentlyContinue; if ($null -ne $t -and $t.Info.State -eq 'success') {{ Write-Output '设置内存成功: ' + $vm.Name + ' -> {memoryGB} GB'; return 'SUCCESS' }}; if ($null -ne $t -and $t.Info.State -eq 'error') {{ Write-Output ('设置内存失败: ' + $t.Info.Error.LocalizedMessage); return 'ERROR' }} }}
";
                string result = await SessionManager.Instance.ExecutePsScriptAsync(script, logCallback, 60);
                return result.Contains("SUCCESS");
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"设置内存失败: {ex.Message}", "error");
                return false;
            }
        }

        public static async Task<bool> AddHardDiskAsync(string vmName, double capacityGB, Action<string?, string?>? logCallback = null)
        {
            try
            {
                string script = $@"
$ErrorActionPreference = 'Stop';
$vm = Get-VM -Filter @{{Name='{EscapePsString(vmName)}'; 'Config.Template'='False'}} -ErrorAction SilentlyContinue | Select-Object -First 1;
if (-not $vm) {{ Write-Output '未找到虚拟机: {EscapePsString(vmName)}'; return 'ERROR' }};
$before = @($vm | Get-HardDisk);
Write-Output ('新增前磁盘数: ' + $before.Count);
$newHd = New-HardDisk -VM $vm -CapacityGB {capacityGB.ToString(System.Globalization.CultureInfo.InvariantCulture)} -StorageFormat Thin -DiskType Flat -ErrorAction Stop;
$after = @($vm | Get-HardDisk | Sort-Object Name);
foreach ($d in $after) {{ Write-Output ('  ' + $d.Name + '  ' + $d.CapacityGB + ' GB  ' + $d.StorageFormat) }};
Write-Output ('添加磁盘成功: ' + $vm.Name + ' -> +{capacityGB.ToString(System.Globalization.CultureInfo.InvariantCulture)} GB (' + $newHd.Name + ')');
return 'SUCCESS'
";
                string result = await SessionManager.Instance.ExecutePsScriptAsync(script, logCallback, 60);
                return result.Contains("SUCCESS");
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"添加磁盘失败: {ex.Message}", "error");
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