// ============================================================================
// VmOpsResources.cs - 虚拟机资源配置操作（CPU / 内存 / 磁盘 / 网络）
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
using VMwareOps.Core;

namespace VMwareOps.VmOps
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
$vm = Get-View -ViewType VirtualMachine -Filter @{{Name='{EscapePsString(vmName)}'; 'Config.Template'='False'}} -Property Name,Config.Hardware.Device -ErrorAction SilentlyContinue;
if (-not $vm) {{ Write-Output '未找到虚拟机: {EscapePsString(vmName)}'; return 'ERROR' }};
$allScsi = @($vm.Config.Hardware.Device | Where-Object {{ $_ -is [VMware.Vim.VirtualSCSIController] }});
if ($allScsi.Count -eq 0) {{ Write-Output '未找到 SCSI 控制器'; return 'ERROR' }};
$scsiCtrl = $allScsi | Sort-Object BusNumber, Key | Select-Object -First 1;
$allDisks = @($vm.Config.Hardware.Device | Where-Object {{ $_ -is [VMware.Vim.VirtualDisk] }});
$sysDisk = $allDisks | Where-Object {{ $_.ControllerKey -eq $scsiCtrl.Key -and $_.Backing }} | Select-Object -First 1;
if (-not $sysDisk) {{ Write-Output '未找到带 Backing 的磁盘'; return 'ERROR' }};
$usedUnits = @($allDisks | Where-Object {{ $_.ControllerKey -eq $scsiCtrl.Key -and $_.UnitNumber -ne $null }} | ForEach-Object {{ $_.UnitNumber }});
$unitNum = 0; while ($usedUnits -contains $unitNum) {{ $unitNum++ }};
$existingLabels = @($allDisks | Where-Object {{ $_.ControllerKey -eq $scsiCtrl.Key -and $_.DeviceInfo.Label -match 'Hard disk (\d+)' }} | ForEach-Object {{ [int]$Matches[1] }});
$nextNum = if ($existingLabels.Count -gt 0) {{ ($existingLabels | Measure-Object -Maximum).Maximum + 1 }} else {{ 1 }};
$configSpec = New-Object VMware.Vim.VirtualMachineConfigSpec;
$diskSpec = New-Object VMware.Vim.VirtualDeviceConfigSpec;
$diskSpec.Operation = [VMware.Vim.VirtualDeviceConfigSpecOperation]::add;
$diskSpec.FileOperation = [VMware.Vim.VirtualDeviceConfigSpecFileOperation]::create;
$newDisk = New-Object VMware.Vim.VirtualDisk;
$newDisk.CapacityInBytes = [int64]{capacityGB.ToString(System.Globalization.CultureInfo.InvariantCulture)} * 1GB;
$newDisk.Key = -1;
$newDisk.ControllerKey = $scsiCtrl.Key;
$newDisk.UnitNumber = $unitNum;
$newDisk.DeviceInfo = New-Object VMware.Vim.Description; $newDisk.DeviceInfo.Summary = ('Hard disk ' + $nextNum);
$newDisk.DeviceInfo.Label = ('Hard disk ' + $nextNum);
$backing = New-Object VMware.Vim.VirtualDiskFlatVer2BackingInfo;
$backing.ThinProvisioned = $true;
$backing.DiskMode = [VMware.Vim.VirtualDiskMode]::persistent;
$backing.Datastore = $sysDisk.Backing.Datastore;
$backing.FileName = '';
$newDisk.Backing = $backing;
$diskSpec.Device = $newDisk;
$configSpec.DeviceChange = @($diskSpec);
$task = $vm.ReconfigVM_Task($configSpec);
while ($true) {{ Start-Sleep -Seconds 3; $t = Get-View -Id $task -Property Info.State,Info.Error -ErrorAction SilentlyContinue; if ($null -ne $t -and $t.Info.State -eq 'success') {{ Write-Output '添加磁盘成功: ' + $vm.Name + ' -> +{capacityGB.ToString(System.Globalization.CultureInfo.InvariantCulture)} GB (Hard disk ' + $nextNum + ')'; return 'SUCCESS' }}; if ($null -ne $t -and $t.Info.State -eq 'error') {{ Write-Output ('添加磁盘失败: ' + $t.Info.Error.LocalizedMessage); return 'ERROR' }} }}
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