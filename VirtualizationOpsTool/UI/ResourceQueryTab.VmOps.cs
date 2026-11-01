// ============================================================================
// ResourceQueryTab.VmOps.cs - 虚拟机操作中心
// ============================================================================
// 职责范围：
//   ✅ 电源管理（PowerOn/Off/Restart/Suspend/ForceOff）
//   ✅ 配置编辑（ShowEditConfigDialog - CPU/内存智能变更）
//   ✅ 磁盘管理（EditHardDisk / ShowDiskEditorDialog）
//   ✅ 网络配置（ChangeNetworkPortgroup / ShowPortGroupDialog）
//   ✅ 其他操作（DisconnectCdDrive / OptimizeVmConfig / MigrateVmToHost）
//   ✅ 迁移克隆（MigrateVmToResourcePool / MigrateVmToDatastore / CloneVm）
//   ✅ 远程连接（Vmrc/Rdp/Ssh连接）
//   ✅ 详情查看（ShowDetailFromItem / ShowDetailWindow）
//
// 开发规范：
//   新增单台虚拟机操作 → 在此文件添加方法
//   右键菜单注册 → 在 ResourceQueryTab.cs 的 SetupContextMenu() 中注册
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using VirtualizationOpsTool.Core;
using VirtualizationOpsTool.Utils;

namespace VirtualizationOpsTool.UI
{
    public partial class ResourceQueryTab
    {
        // ====== 电源管理操作 ======

        private void PowerOnVm(List<string> row)
        {
            string vmName = GetVmNameFromRow(row);
            if (string.IsNullOrEmpty(vmName)) return;
            string vmMoRef = FullVmRef(GetVmIdFromRow(row));

            string script = $@"
$ErrorActionPreference = 'Stop';
{PsWaitTaskFunc}
$vm = Get-View -Id '{vmMoRef}' -Property Name,Runtime.PowerState -ErrorAction Stop;
if ($vm.Runtime.PowerState -eq 'poweredOn') {{ Write-Output '【{EscapePsString(vmName)}】已是开机状态，无需操作' }} else {{ $task = $vm.PowerOnVM_Task($null); Wait-Task $task '开机' }}
";

            ExecuteVmOperation(script, $"开机: {vmName}", GetVmIdFromRow(row));
        }

        private void PowerOffVm(List<string> row)
        {
            string vmName = GetVmNameFromRow(row);
            if (string.IsNullOrEmpty(vmName)) return;
            string vmMoRef = FullVmRef(GetVmIdFromRow(row));

            var confirmResult = MessageBox.Show(
                $"确认要对 [{vmName}] 执行关闭客户机操作系统吗？\n\n" +
                "需要 VMware Tools 正常运行\n" +
                "虚拟机将执行优雅关机（等同于在客户机内点关机）\n\n" +
                "若客户机无响应或 Tools 未运行，可使用「关闭电源」强制断电",
                "关闭客户机确认",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);

            if (confirmResult != DialogResult.OK) return;

            string script = $@"
$ErrorActionPreference = 'Stop';
{PsWaitTaskFunc}
$vm = Get-View -Id '{vmMoRef}' -Property Name,Runtime.PowerState,Guest.ToolsStatus -ErrorAction Stop;
if ($vm.Runtime.PowerState -eq 'poweredOff') {{ Write-Output '【{EscapePsString(vmName)}】已是关机状态，无需操作' }} else {{
  $toolsOk = $vm.Guest -and ($vm.Guest.ToolsStatus -eq 'toolsOk' -or $vm.Guest.ToolsStatus -eq 'toolsOld');
  if (-not $toolsOk) {{ Write-Output '【{EscapePsString(vmName)}】VMware Tools 未运行，无法发送关闭客户机指令。请使用「关闭电源」强制断电'; return }}
  try {{ $vm.ShutdownGuest(); Write-Output '【{EscapePsString(vmName)}】已发送关闭客户机指令，等待关机...' }} catch {{ Write-Output ('【{EscapePsString(vmName)}】发送关闭指令失败: ' + $_.Exception.Message); return }}
  $deadline = (Get-Date).AddMinutes(5)
  while ((Get-Date) -lt $deadline) {{
    Start-Sleep -Seconds 5
    $v = Get-View -Id '{vmMoRef}' -Property Runtime.PowerState -ErrorAction SilentlyContinue
    if ($v -and $v.Runtime.PowerState -eq 'poweredOff') {{ Write-Output ('DONE:关闭客户机'); Write-Output '【{EscapePsString(vmName)}】客户机已关闭'; break }}
    Write-Output ('PROGRESS:关闭客户机:等待中...')
  }}
  if (-not $v -or $v.Runtime.PowerState -ne 'poweredOff') {{ Write-Output '【{EscapePsString(vmName)}】关闭客户机超时（5分钟），可使用「关闭电源」强制断电' }}
}}
";

            ExecuteVmOperation(script, $"关闭客户机: {vmName}", GetVmIdFromRow(row));
        }

        private void PowerOffVmForce(List<string> row)
        {
            string vmName = GetVmNameFromRow(row);
            if (string.IsNullOrEmpty(vmName)) return;
            string vmMoRef = FullVmRef(GetVmIdFromRow(row));

            var confirmResult = MessageBox.Show(
                $"⚠ 确认要对 [{vmName}] 执行关闭电源吗？\n\n" +
                "此操作等同于物理机直接断电\n" +
                "可能导致客户机数据丢失或文件系统损坏！\n\n" +
                "建议先使用「关闭客户机」进行优雅关机",
                "关闭电源确认",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Error);

            if (confirmResult != DialogResult.OK) return;

            string script = $@"
$ErrorActionPreference = 'Stop';
{PsWaitTaskFunc}
$vm = Get-View -Id '{vmMoRef}' -Property Name,Runtime.PowerState -ErrorAction Stop;
if ($vm.Runtime.PowerState -eq 'poweredOff') {{ Write-Output '【{EscapePsString(vmName)}】已是关机状态，无需操作' }} else {{ $task = $vm.PowerOffVM_Task(); Wait-Task $task '关闭电源' }}
";

            ExecuteVmOperation(script, $"关闭电源: {vmName}", GetVmIdFromRow(row));
        }

        private void RestartVm(List<string> row)
        {
            string vmName = GetVmNameFromRow(row);
            if (string.IsNullOrEmpty(vmName)) return;
            string vmMoRef = FullVmRef(GetVmIdFromRow(row));

            var confirmResult = MessageBox.Show(
                $"确认要对 [{vmName}] 执行重置电源吗？\n\n" +
                "硬件级电源复位（等同物理机按 Reset 键）\n" +
                "可能导致客户机数据丢失\n\n" +
                "建议先使用「重新启动客户机操作系统」（需 VMware Tools）",
                "重置电源确认",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);

            if (confirmResult != DialogResult.OK) return;

            string script = $@"
$ErrorActionPreference = 'Stop';
{PsWaitTaskFunc}
$vm = Get-View -Id '{vmMoRef}' -Property Name,Runtime.PowerState -ErrorAction Stop;
if ($vm.Runtime.PowerState -ne 'poweredOn') {{ Write-Output '【{EscapePsString(vmName)}】当前不是开机状态，无法重置电源' }} else {{
    $task = $vm.ResetVM_Task(); Wait-Task $task '重置电源'
    Write-Output '【{EscapePsString(vmName)}】已重置电源'
}}
";

            ExecuteVmOperation(script, $"重置电源: {vmName}", GetVmIdFromRow(row));
        }
        private void EditCpuOnly(List<string> row)
        {
            string vmName = GetVmNameFromRow(row);
            if (string.IsNullOrEmpty(vmName)) return;
            string vmMoRef = FullVmRef(GetVmIdFromRow(row));

            int currentCpu = GetIntFromRow(row, "NumCpu");
            if (currentCpu <= 0) currentCpu = 2;

            var cpuPresets = new[] { "1", "2", "4", "8", "16", "32", "48", "64", "96", "128" };

            using var dlg = new Form
            {
                Text = $"修改 vCPU - {vmName}",
                Icon = CoreUtils.GetAppIcon(),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                Font = new Font("Microsoft YaHei", 9),
                AutoScaleMode = AutoScaleMode.Dpi,
                AutoScaleDimensions = new SizeF(96F, 96F),
                Size = new Size(380, 180),
                MinimumSize = new Size(380, 180)
            };

            var lblCurrent = new Label { Text = $"当前 vCPU: {currentCpu} 核", Location = new Point(20, 20), AutoSize = true, Font = new Font("Microsoft YaHei", 10), Margin = Padding.Empty };
            var lblNew = new Label { Text = "新 vCPU 数量:", Location = new Point(20, 55), AutoSize = true, Margin = Padding.Empty };
            var cbCpu = new ComboBox { Location = new Point(120, 52), Width = 120, DropDownStyle = ComboBoxStyle.DropDown, Anchor = AnchorStyles.Top | AnchorStyles.Left, Margin = Padding.Empty };
            cbCpu.Items.AddRange(cpuPresets);
            if (!cbCpu.Items.Contains(currentCpu.ToString())) cbCpu.Items.Add(currentCpu.ToString());
            cbCpu.Text = currentCpu.ToString();

            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, Height = 44,
                Padding = new Padding(0, 6, 0, 6),
                BackColor = Color.FromArgb(240, 240, 240)
            };
            var btnOk = new Button { Text = "确定", DialogResult = DialogResult.OK, Size = new Size(80, 28), Font = new Font("Microsoft YaHei", 9) };
            var btnCancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Size = new Size(80, 28), Font = new Font("Microsoft YaHei", 9) };
            btnPanel.Controls.Add(btnOk);
            btnPanel.Controls.Add(btnCancel);

            CenterFlowPanel(btnPanel);

            dlg.Controls.Add(btnPanel);
            dlg.Controls.AddRange(new Control[] { lblCurrent, lblNew, cbCpu });
            dlg.AcceptButton = btnOk;
            dlg.CancelButton = btnCancel;

            if (dlg.ShowDialog(_root) != DialogResult.OK) return;

            if (!int.TryParse(cbCpu.Text.Trim(), out int newCpu) || newCpu < 1 || newCpu > 128)
            {
                MessageBox.Show("vCPU 数量必须为 1~128 之间的整数", "输入错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (newCpu == currentCpu)
            {
                ToastUtils.ShowToast(_root, "vCPU 数量未发生变化");
                return;
            }

            bool needShutdown = newCpu < currentCpu;

            string script = $"$vm = Get-View -Id '{vmMoRef}' -Property Runtime.PowerState -ErrorAction Stop | Select-Object -First 1; ";

            if (needShutdown)
            {
                script += "$originalState = $vm.Runtime.PowerState; ";
                script += "if ($vm.Runtime.PowerState -eq 'poweredOn') { " +
                          "  Write-Output '【正在关机】请稍候...'; " +
                          "  $offTask = $vm.PowerOffVM_Task(); " +
                          "  Wait-Task $offTask '关机'; " +
                          "  Write-Output '【关机完成】'; " +
                          "} ";
                script += "$cfg = New-Object VMware.Vim.VirtualMachineConfigSpec; ";
                script += $"$cfg.NumCPUs = {newCpu}; ";
                script += "$task = $vm.ReconfigVM_Task($cfg); ";
                script += "Wait-Task $task '配置修改'; ";
                script += "Write-Output '【配置修改完成】'; ";
                script += "if ($originalState -eq 'poweredOn') { " +
                          "  Write-Output '【正在开机】请稍候...'; " +
                          "  $onTask = $vm.PowerOnVM_Task($null); " +
                          "  Wait-Task $onTask '开机'; " +
                          "  Write-Output '【开机完成】'; " +
                          "} ";
            }
            else
            {
                script += "$cfg = New-Object VMware.Vim.VirtualMachineConfigSpec; ";
                script += $"$cfg.NumCPUs = {newCpu}; ";
                script += "$task = $vm.ReconfigVM_Task($cfg); ";
                script += "Wait-Task $task '配置修改'; ";
            }

            script += $"Write-Output '【{EscapePsString(vmName)}】vCPU 已更新: {currentCpu} → {newCpu} 核'";

            ExecuteVmOperation(script, $"修改 vCPU: {vmName} ({currentCpu}→{newCpu})", GetVmIdFromRow(row));
        }

        private void EditMemoryOnly(List<string> row)
        {
            string vmName = GetVmNameFromRow(row);
            if (string.IsNullOrEmpty(vmName)) return;
            string vmMoRef = FullVmRef(GetVmIdFromRow(row));

            int currentMem = 0;
            int memIdx = _columns.FindIndex(c => c.Equals("MemoryGB", StringComparison.OrdinalIgnoreCase));
            if (memIdx >= 0 && memIdx < row.Count)
            {
                int.TryParse(row[memIdx].Trim(), out currentMem);
            }
            if (currentMem <= 0) currentMem = 4;

            var memPresets = new[] { "1", "2", "4", "8", "16", "32", "64", "128", "256" };

            using var dlg = new Form
            {
                Text = $"修改内存 - {vmName}",
                Icon = CoreUtils.GetAppIcon(),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                Font = new Font("Microsoft YaHei", 9),
                AutoScaleMode = AutoScaleMode.Dpi,
                AutoScaleDimensions = new SizeF(96F, 96F),
                Size = new Size(380, 180),
                MinimumSize = new Size(380, 180)
            };

            var lblCurrent = new Label { Text = $"当前内存: {currentMem} GB", Location = new Point(20, 20), AutoSize = true, Font = new Font("Microsoft YaHei", 10), Margin = Padding.Empty };
            var lblNew = new Label { Text = "新内存大小:", Location = new Point(20, 55), AutoSize = true, Margin = Padding.Empty };
            var cbMem = new ComboBox { Location = new Point(120, 52), Width = 120, DropDownStyle = ComboBoxStyle.DropDown, Anchor = AnchorStyles.Top | AnchorStyles.Left, Margin = Padding.Empty };
            cbMem.Items.AddRange(memPresets);
            if (!cbMem.Items.Contains(currentMem.ToString())) cbMem.Items.Add(currentMem.ToString());
            cbMem.Text = currentMem.ToString();

            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, Height = 44,
                Padding = new Padding(0, 6, 0, 6),
                BackColor = Color.FromArgb(240, 240, 240)
            };
            var btnOk = new Button { Text = "确定", DialogResult = DialogResult.OK, Size = new Size(80, 28), Font = new Font("Microsoft YaHei", 9) };
            var btnCancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Size = new Size(80, 28), Font = new Font("Microsoft YaHei", 9) };
            btnPanel.Controls.Add(btnOk);
            btnPanel.Controls.Add(btnCancel);

            CenterFlowPanel(btnPanel);

            dlg.Controls.Add(btnPanel);
            dlg.Controls.AddRange(new Control[] { lblCurrent, lblNew, cbMem });
            dlg.AcceptButton = btnOk;
            dlg.CancelButton = btnCancel;

            if (dlg.ShowDialog(_root) != DialogResult.OK) return;

            string memText = cbMem.Text.Trim().Replace("GB", "").Replace("gb", "").Trim();
            if (!int.TryParse(memText, out int newMem) || newMem < 1 || newMem > 256)
            {
                MessageBox.Show("内存大小必须为 1~256 之间的整数（GB）", "输入错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (newMem == currentMem)
            {
                ToastUtils.ShowToast(_root, "内存大小未发生变化");
                return;
            }

            bool needShutdown = newMem < currentMem;

            string script = $"$vm = Get-View -Id '{vmMoRef}' -Property Runtime.PowerState -ErrorAction Stop | Select-Object -First 1; ";

            if (needShutdown)
            {
                script += "$originalState = $vm.Runtime.PowerState; ";
                script += "if ($vm.Runtime.PowerState -eq 'poweredOn') { " +
                          "  Write-Output '【正在关机】请稍候...'; " +
                          "  $offTask = $vm.PowerOffVM_Task(); " +
                          "  Wait-Task $offTask '关机'; " +
                          "  Write-Output '【关机完成】'; " +
                          "} ";
                script += "$cfg = New-Object VMware.Vim.VirtualMachineConfigSpec; ";
                script += $"$cfg.MemoryMB = {newMem * 1024}; ";
                script += "$task = $vm.ReconfigVM_Task($cfg); ";
                script += "Wait-Task $task '配置修改'; ";
                script += "Write-Output '【配置修改完成】'; ";
                script += "if ($originalState -eq 'poweredOn') { " +
                          "  Write-Output '【正在开机】请稍候...'; " +
                          "  $onTask = $vm.PowerOnVM_Task($null); " +
                          "  Wait-Task $onTask '开机'; " +
                          "  Write-Output '【开机完成】'; " +
                          "} ";
            }
            else
            {
                script += "$cfg = New-Object VMware.Vim.VirtualMachineConfigSpec; ";
                script += $"$cfg.MemoryMB = {newMem * 1024}; ";
                script += "$task = $vm.ReconfigVM_Task($cfg); ";
                script += "Wait-Task $task '配置修改'; ";
            }

            script += $"Write-Output '【{EscapePsString(vmName)}】内存已更新: {currentMem} → {newMem} GB'";

            ExecuteVmOperation(script, $"修改内存: {vmName} ({currentMem}→{newMem}GB)", GetVmIdFromRow(row));
        }

        private void EditPortgroupOnly(List<string> row)
        {
            string vmName = GetVmNameFromRow(row);
            if (string.IsNullOrEmpty(vmName)) return;
            string vmMoRef = FullVmRef(GetVmIdFromRow(row));

            Task.Run(() =>
            {
                var pgCache = UnifiedCache.GetCachedPortGroups();
                if (pgCache.Count == 0)
                {
                    try
                    {
                        var result = SessionManager.Instance.ExecuteQueryAsync(
                            "Get-VirtualNetwork -ErrorAction SilentlyContinue | Select-Object Name | Sort-Object Name -Unique",
                            (msg, level) => { }
                        ).Result;
                        if (result != null && result.Count > 0)
                        {
                            foreach (var dict in result)
                            {
                                if (dict.TryGetValue("Name", out var nameObj))
                                {
                                    var name = nameObj?.ToString()?.Trim();
                                    if (!string.IsNullOrEmpty(name)) pgCache.Add(name);
                                }
                            }
                            if (pgCache.Count > 0) UnifiedCache.UpdateResourceCache(portGroups: pgCache);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogMessage("error", $"[端口组修改] 查询端口组列表异常: {ex.Message}");
                    }
                }

                var nicList = new List<Dictionary<string, string>>();
                try
                {
                    var nicResult = SessionManager.Instance.ExecuteQueryAsync(
                        $@"
$ErrorActionPreference = 'Stop';
$vm = Get-View -Id '{vmMoRef}' -Property Config.Hardware.Device -ErrorAction Stop;
$pgMap = @{{}};
Get-View -ViewType Network -Property Name -ErrorAction SilentlyContinue | ForEach-Object {{ $pgMap[$_.MoRef.Value] = $_.Name }};
Get-View -ViewType DistributedVirtualPortgroup -Property Name -ErrorAction SilentlyContinue | ForEach-Object {{ $pgMap[$_.MoRef.Value] = $_.Name }};
$vm.Config.Hardware.Device | Where-Object {{ $_ -is [VMware.Vim.VirtualEthernetCard] }} | ForEach-Object {{
  $nic = $_;
  $pgName = '';
  if ($nic.Backing) {{
    if ($nic.Backing -is [VMware.Vim.VirtualEthernetCardDistributedVirtualPortBackingInfo] -and $nic.Backing.Port -and $pgMap.ContainsKey($nic.Backing.Port.PortgroupKey)) {{
      $pgName = $pgMap[$nic.Backing.Port.PortgroupKey];
    }} elseif ($nic.Backing -is [VMware.Vim.VirtualEthernetCardNetworkBackingInfo] -and $nic.Backing.DeviceName) {{
      $pgName = $nic.Backing.DeviceName;
    }}
  }};
  [PSCustomObject]@{{ DeviceLabel=$nic.DeviceInfo.Label; NetworkName=$pgName; Key=$nic.Key; Connected=$nic.Connectable.Connected; StartConnected=$nic.Connectable.StartConnected }}
}}",
                        (msg, level) => { }
                    ).Result;
                    if (nicResult != null && nicResult.Count > 0)
                    {
                        foreach (var dict in nicResult)
                        {
                            var nicInfo = new Dictionary<string, string>();
                            if (dict.TryGetValue("DeviceLabel", out var dlObj)) nicInfo["DeviceLabel"] = dlObj?.ToString()?.Trim() ?? "";
                            if (dict.TryGetValue("NetworkName", out var nnObj)) nicInfo["NetworkName"] = nnObj?.ToString()?.Trim() ?? "";
                            if (dict.TryGetValue("Key", out var kObj)) nicInfo["Key"] = kObj?.ToString()?.Trim() ?? "";
                            if (dict.TryGetValue("Connected", out var cObj)) nicInfo["Connected"] = (cObj?.ToString()?.Equals("True", StringComparison.OrdinalIgnoreCase) ?? false).ToString();
                            if (dict.TryGetValue("StartConnected", out var scObj)) nicInfo["StartConnected"] = (scObj?.ToString()?.Equals("True", StringComparison.OrdinalIgnoreCase) ?? true).ToString();
                            nicList.Add(nicInfo);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogMessage("error", $"[端口组修改] 查询网卡列表异常: {ex.Message}");
                }

                _root.Invoke(new Action(() =>
                {
                    ShowPortgroupEditDialog(vmName, vmMoRef, nicList, pgCache);
                }));
            });
        }

        private void GuestOpsNetworkConfig(List<string> row)
        {
            try
            {
                string vmName = GetVmNameFromRow(row);
                string vmId = GetVmIdFromRow(row);
                string vmMoRef = FullVmRef(vmId);
                if (string.IsNullOrEmpty(vmName) || string.IsNullOrEmpty(vmMoRef))
                {
                    MessageBox.Show("无法获取虚拟机信息", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                Task.Run(async () =>
                {
                    try
                    {
                        string queryScript = $@"
$ErrorActionPreference = 'Stop';
$vm = Get-View -Id '{EscapePsString(vmMoRef)}' -Property Name,Runtime.PowerState,Guest.ToolsStatus,Guest.GuestFamily,Config.GuestFullName,Guest.IpAddress -ErrorAction SilentlyContinue;
if (-not $vm) {{ throw 'VM not found: {EscapePsString(vmName)}' }};
[PSCustomObject]@{{
    PowerState     = $vm.Runtime.PowerState
    ToolsStatus    = if ($vm.Guest) {{ $vm.Guest.ToolsStatus }} else {{ '' }}
    GuestFamily    = if ($vm.Guest) {{ $vm.Guest.GuestFamily }} else {{ '' }}
    GuestFullName  = if ($vm.Config) {{ $vm.Config.GuestFullName }} else {{ '' }}
    GuestIpAddress = if ($vm.Guest) {{ ($vm.Guest.IpAddress -join ',') }} else {{ '' }}
}}";

                        List<Dictionary<string, object>> result = null;
                        try
                        {
                            result = await SessionManager.Instance.ExecuteQueryAsync(queryScript, null, 30);
                        }
                        catch (Exception ex)
                        {
                            _root?.Invoke(new Action(() =>
                                MessageBox.Show($"查询虚拟机状态失败：\n{ex.Message}", "错误",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error)));
                            return;
                        }

                        string powerState = result?.FirstOrDefault()?.GetValueOrDefault("PowerState")?.ToString() ?? "";
                        string toolsStatus = result?.FirstOrDefault()?.GetValueOrDefault("ToolsStatus")?.ToString() ?? "";
                        string guestFamily = result?.FirstOrDefault()?.GetValueOrDefault("GuestFamily")?.ToString() ?? "";
                        string guestFullName = result?.FirstOrDefault()?.GetValueOrDefault("GuestFullName")?.ToString() ?? "";
                        string guestIp = result?.FirstOrDefault()?.GetValueOrDefault("GuestIpAddress")?.ToString() ?? "";

                        _root?.Invoke(new Action(() =>
                            ShowGuestOpsNetworkDialog(vmName, vmMoRef, powerState, toolsStatus, guestFamily, guestFullName, guestIp)));
                    }
                    catch (Exception ex)
                    {
                        _root?.Invoke(new Action(() =>
                            MessageBox.Show($"GuestOps 对话框异常：\n{ex.Message}\n\n{ex.StackTrace}", "错误",
                                MessageBoxButtons.OK, MessageBoxIcon.Error)));
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"入口异常：\n{ex.Message}\n\n{ex.StackTrace}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void GuestOpsPasswordConfig(List<string> row)
        {
            try
            {
                string vmName = GetVmNameFromRow(row);
                string vmId = GetVmIdFromRow(row);
                string vmMoRef = FullVmRef(vmId);
                if (string.IsNullOrEmpty(vmName) || string.IsNullOrEmpty(vmMoRef))
                {
                    MessageBox.Show("无法获取虚拟机信息", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                Task.Run(async () =>
                {
                    try
                    {
                        string queryScript = $@"
$ErrorActionPreference = 'Stop';
$vm = Get-View -Id '{EscapePsString(vmMoRef)}' -Property Name,Runtime.PowerState,Guest.ToolsStatus,Guest.GuestFamily,Config.GuestFullName,Guest.IpAddress -ErrorAction SilentlyContinue;
if (-not $vm) {{ throw 'VM not found: {EscapePsString(vmName)}' }};
[PSCustomObject]@{{
    PowerState     = $vm.Runtime.PowerState
    ToolsStatus    = if ($vm.Guest) {{ $vm.Guest.ToolsStatus }} else {{ '' }}
    GuestFamily    = if ($vm.Guest) {{ $vm.Guest.GuestFamily }} else {{ '' }}
    GuestFullName  = if ($vm.Config) {{ $vm.Config.GuestFullName }} else {{ '' }}
    GuestIpAddress = if ($vm.Guest) {{ ($vm.Guest.IpAddress -join ',') }} else {{ '' }}
}}";

                        List<Dictionary<string, object>> result = null;
                        try
                        {
                            result = await SessionManager.Instance.ExecuteQueryAsync(queryScript, null, 30);
                        }
                        catch (Exception ex)
                        {
                            _root?.Invoke(new Action(() =>
                                MessageBox.Show($"查询虚拟机状态失败：\n{ex.Message}", "错误",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error)));
                            return;
                        }

                        string powerState = result?.FirstOrDefault()?.GetValueOrDefault("PowerState")?.ToString() ?? "";
                        string toolsStatus = result?.FirstOrDefault()?.GetValueOrDefault("ToolsStatus")?.ToString() ?? "";
                        string guestFamily = result?.FirstOrDefault()?.GetValueOrDefault("GuestFamily")?.ToString() ?? "";
                        string guestFullName = result?.FirstOrDefault()?.GetValueOrDefault("GuestFullName")?.ToString() ?? "";
                        string guestIp = result?.FirstOrDefault()?.GetValueOrDefault("GuestIpAddress")?.ToString() ?? "";

                        _root?.Invoke(new Action(() =>
                            ShowGuestOpsPasswordDialog(vmName, vmMoRef, powerState, toolsStatus, guestFamily, guestFullName, guestIp)));
                    }
                    catch (Exception ex)
                    {
                        _root?.Invoke(new Action(() =>
                            MessageBox.Show($"GuestOps 对话框异常：\n{ex.Message}\n\n{ex.StackTrace}", "错误",
                                MessageBoxButtons.OK, MessageBoxIcon.Error)));
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"入口异常：\n{ex.Message}\n\n{ex.StackTrace}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ====== 磁盘管理操作 ======

        private void EditHardDisk(List<string> row)
        {
            string vmName = GetVmNameFromRow(row);
            if (string.IsNullOrEmpty(vmName)) return;
            string vmMoRef = FullVmRef(GetVmIdFromRow(row));

            Task.Run(() =>
            {
                var diskList = new List<Dictionary<string, string>>();
                try
                {
                    var result = SessionManager.Instance.ExecuteQueryAsync(
                        $"Get-View -Id '{vmMoRef}' -Property Config.Hardware.Device -ErrorAction Stop | ForEach-Object {{ $_.Config.Hardware.Device | Where-Object {{ $_ -is [VMware.Vim.VirtualDisk] }} | ForEach-Object {{ $vd = $_; $fmt = if(-not $vd.Backing){{'无'}}elseif($vd.Backing.ThinProvisioned){{'Thin'}}elseif($vd.Backing.EagerlyScrub){{'EagerZeroedThick'}}else{{'LazyZeroedThick'}}; [PSCustomObject]@{{ Key=$vd.Key; Name=$vd.DeviceInfo.Label; CapacityGB=[Math]::Round($vd.CapacityInBytes/1GB,2); StorageFormat=$fmt; Filename=if($vd.Backing){{$vd.Backing.FileName}}else{{''}}; HasBacking=($null -ne $vd.Backing) }} }} }}",
                        (msg, level) => { }
                    ).Result;

                    if (result != null && result.Count > 0)
                    {
                        foreach (var dict in result)
                        {
                            var diskInfo = new Dictionary<string, string>();
                            if (dict.TryGetValue("Key", out var keyObj))
                                diskInfo["Key"] = keyObj?.ToString()?.Trim() ?? "";
                            if (dict.TryGetValue("Name", out var nameObj))
                                diskInfo["Name"] = nameObj?.ToString()?.Trim() ?? "";
                            if (dict.TryGetValue("CapacityGB", out var capObj))
                                diskInfo["CapacityGB"] = capObj?.ToString()?.Trim() ?? "";
                            if (dict.TryGetValue("StorageFormat", out var fmtObj))
                                diskInfo["StorageFormat"] = fmtObj?.ToString()?.Trim() ?? "";
                            if (dict.TryGetValue("Filename", out var fileObj))
                                diskInfo["Filename"] = fileObj?.ToString()?.Trim() ?? "";
                            if (dict.TryGetValue("HasBacking", out var hbObj))
                                diskInfo["HasBacking"] = hbObj?.ToString()?.Trim() ?? "";
                            diskList.Add(diskInfo);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogMessage("error", $"[磁盘编辑] 查询磁盘异常: {ex.Message}");
                }

                _root.Invoke(new Action(() =>
                {
                    ShowDiskEditorDialog(vmName, vmMoRef, diskList);
                }));
            });
        }
        // ====== 其他VM操作 ======

        private void DisconnectCdDrive(List<string> row)
        {
            string vmName = GetVmNameFromRow(row);
            if (string.IsNullOrEmpty(vmName)) return;
            string vmMoRef = FullVmRef(GetVmIdFromRow(row));
            string script =
                $"$vm = Get-View -Id '{vmMoRef}' -Property Config.Hardware.Device -ErrorAction Stop; " +
                "$cdrom = $vm.Config.Hardware.Device | Where-Object { $_ -is [VMware.Vim.VirtualCdrom] } | Select-Object -First 1; " +
                $"if (-not $cdrom) {{ Write-Output '【{EscapePsString(vmName)}】该虚拟机没有光驱设备，跳过'; return }}; " +
                "$cfg = New-Object VMware.Vim.VirtualMachineConfigSpec; " +
                "$devSpec = New-Object VMware.Vim.VirtualDeviceConfigSpec; " +
                "$devSpec.Operation = [VMware.Vim.VirtualDeviceConfigSpecOperation]::edit; " +
                "$cdrom.Connectable = New-Object VMware.Vim.VirtualDeviceConnectInfo; " +
                "$cdrom.Connectable.Connected = $false; " +
                "$cdrom.Connectable.StartConnected = $false; " +
                "$devSpec.Device = $cdrom; " +
                "$cfg.DeviceChange = @($devSpec); " +
                "$task = $vm.ReconfigVM_Task($cfg); " +
                "Wait-Task $task '断开光驱'; " +
                $"Write-Output '【{EscapePsString(vmName)}】光驱已断开'";
            ExecuteVmOperation(script, $"断开光驱: {vmName}", GetVmIdFromRow(row));
        }

        private void OptimizeVmConfig(List<string> row)
        {
            string vmName = GetVmNameFromRow(row);
            if (string.IsNullOrEmpty(vmName)) return;
            string vmMoRef = FullVmRef(GetVmIdFromRow(row));

            var confirmResult = MessageBox.Show(
                $"确认要优化 [{vmName}] 的 VMX 高级配置吗？\n\n" +
                "将修改以下配置：\n" +
                "• isolation.tools.copy.disable = FALSE\n" +
                "• isolation.tools.paste.disable = FALSE\n" +
                "• isolation.tools.dnd.disable = FALSE\n" +
                "• isolation.tools.setGUIOptions.enable = TRUE\n" +
                "• tools.syncTime = TRUE\n" +
                "• sched.mem.pshare.enable = FALSE\n\n" +
                "注意：部分选项需要虚拟机关机后才能生效。",
                "优化VM配置确认",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question);

            if (confirmResult != DialogResult.OK) return;

            string script =
                $"$vm = Get-View -Id '{vmMoRef}' -ErrorAction Stop; " +
                "$extra = @(" +
                "  (New-Object VMware.Vim.OptionValue('isolation.tools.copy.disable','FALSE')), " +
                "  (New-Object VMware.Vim.OptionValue('isolation.tools.paste.disable','FALSE')), " +
                "  (New-Object VMware.Vim.OptionValue('isolation.tools.dnd.disable','FALSE')), " +
                "  (New-Object VMware.Vim.OptionValue('isolation.tools.setGUIOptions.enable','TRUE')), " +
                "  (New-Object VMware.Vim.OptionValue('tools.syncTime','TRUE')), " +
                "  (New-Object VMware.Vim.OptionValue('sched.mem.pshare.enable','FALSE')) " +
                "); " +
                "$cfg = New-Object VMware.Vim.VirtualMachineConfigSpec; " +
                "$cfg.ExtraConfig = $extra; " +
                "$task = $vm.ReconfigVM_Task($cfg); " +
                "Wait-Task $task '优化配置'; " +
                $"Write-Output '【{EscapePsString(vmName)}】配置优化完成'";

            ExecuteVmOperation(script, $"优化配置: {vmName}", GetVmIdFromRow(row));
        }

        private void MigrateVmToHost(List<string> row)
        {
            string vmName = GetVmNameFromRow(row);
            if (string.IsNullOrEmpty(vmName)) return;
            string vmMoRef = FullVmRef(GetVmIdFromRow(row));

            Task.Run(() =>
            {
                var hostList = UnifiedCache.GetCachedHosts();
                if (hostList.Count == 0)
                {
                    try
                    {
                        var result = SessionManager.Instance.ExecuteQueryAsync(
                            "Get-View -ViewType HostSystem -Property Name | Select-Object Name | Sort-Object Name",
                            (msg, level) => { }
                        ).Result;

                        if (result != null && result.Count > 0)
                        {
                            foreach (var dict in result)
                            {
                                if (dict.TryGetValue("Name", out var nameObj))
                                {
                                    var name = nameObj?.ToString()?.Trim();
                                    if (!string.IsNullOrEmpty(name))
                                    {
                                        hostList.Add(name);
                                    }
                                }
                            }
                            if (hostList.Count > 0)
                            {
                                UnifiedCache.UpdateResourceCache(hosts: hostList);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogMessage("error", $"[迁移主机] 查询异常: {ex.Message}");
                    }
                }

                string? curHostName = null;
                try
                {
                    var curResult = SessionManager.Instance.ExecuteQueryAsync(
                        $"Get-View -Id '{vmMoRef}' -Property Runtime.Host -ErrorAction Stop | Select-Object @{{N='Host';E={{ if($_.Runtime.Host){{(Get-View -Id $_.Runtime.Host -Property Name).Name}}else{{''}} }}}}",
                        (msg, level) => { }
                    ).Result;
                    if (curResult != null && curResult.Count > 0 && curResult[0].TryGetValue("Host", out var ch))
                        curHostName = ch?.ToString();
                }
                catch { }

                _root.Invoke(new Action(() =>
                {
                    ShowMigrateHostDialog(vmName, vmMoRef, hostList, curHostName);
                }));
            });
        }
        private void MigrateVmToResourcePool(List<string> row)
        {
            string vmName = GetVmNameFromRow(row);
            if (string.IsNullOrEmpty(vmName)) return;
            string vmMoRef = FullVmRef(GetVmIdFromRow(row));

            Task.Run(() =>
            {
                var rpList = UnifiedCache.GetCachedResourcePools();
                if (rpList.Count == 0)
                {
                    try
                    {
                        var result = SessionManager.Instance.ExecuteQueryAsync(
                            "Get-View -ViewType ResourcePool -Property Name | Select-Object Name | Sort-Object Name",
                            (msg, level) => { }
                        ).Result;

                        if (result != null && result.Count > 0)
                        {
                            foreach (var dict in result)
                            {
                                if (dict.TryGetValue("Name", out var nameObj))
                                {
                                    var name = nameObj?.ToString()?.Trim();
                                    if (!string.IsNullOrEmpty(name)) rpList.Add(name);
                                }
                            }
                            UnifiedCache.UpdateResourceCache(resourcePools: rpList);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogMessage("error", $"[迁移资源池] 查询异常: {ex.Message}");
                    }
                }

                string? curRpName = null;
                try
                {
                    var curResult = SessionManager.Instance.ExecuteQueryAsync(
                        $"Get-View -Id '{vmMoRef}' -Property ResourcePool -ErrorAction Stop | Select-Object @{{N='Pool';E={{ if($_.ResourcePool){{(Get-View -Id $_.ResourcePool -Property Name).Name}}else{{''}} }}}}",
                        (msg, level) => { }
                    ).Result;
                    if (curResult != null && curResult.Count > 0 && curResult[0].TryGetValue("Pool", out var cp))
                        curRpName = cp?.ToString();
                }
                catch { }

                _root.Invoke(new Action(() =>
                {
                    ShowMigrateResourcePoolDialog(vmName, vmMoRef, rpList, curRpName);
                }));
            });
        }
        private void MigrateVmToDatastore(List<string> row)
        {
            string vmName = GetVmNameFromRow(row);
            if (string.IsNullOrEmpty(vmName)) return;
            string vmMoRef = FullVmRef(GetVmIdFromRow(row));

            Task.Run(() =>
            {
                var dsList = UnifiedCache.GetCachedDatastores();
                if (dsList.Count == 0)
                {
                    try
                    {
                        var result = SessionManager.Instance.ExecuteQueryAsync(
                            "Get-View -ViewType Datastore -Property Name | Select-Object Name | Sort-Object Name",
                            (msg, level) => { }
                        ).Result;

                        if (result != null && result.Count > 0)
                        {
                            foreach (var dict in result)
                            {
                                if (dict.TryGetValue("Name", out var nameObj))
                                {
                                    var name = nameObj?.ToString()?.Trim();
                                    if (!string.IsNullOrEmpty(name)) dsList.Add(name);
                                }
                            }
                            UnifiedCache.UpdateResourceCache(datastores: dsList);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogMessage("error", $"[迁移存储] 查询异常: {ex.Message}");
                    }
                }

                string? curDsName = null;
                try
                {
                    var curResult = SessionManager.Instance.ExecuteQueryAsync(
                        $"Get-View -Id '{vmMoRef}' -Property Datastore -ErrorAction Stop | Select-Object @{{N='Ds';E={{ ($_.Datastore | ForEach-Object {{ (Get-View -Id $_ -Property Name -ErrorAction SilentlyContinue).Name }} | Where-Object {{ $_ }}) -join ', ' }}}}",
                        (msg, level) => { }
                    ).Result;
                    if (curResult != null && curResult.Count > 0 && curResult[0].TryGetValue("Ds", out var cd))
                        curDsName = cd?.ToString();
                }
                catch { }

                _root.Invoke(new Action(() =>
                {
                    ShowMigrateDatastoreDialog(vmName, vmMoRef, dsList, curDsName);
                }));
            });
        }
        private void CloneVm(List<string> row)
        {
            string vmName = GetVmNameFromRow(row);
            if (string.IsNullOrEmpty(vmName)) return;
            string vmMoRef = FullVmRef(GetVmIdFromRow(row));

            Task.Run(() =>
            {
                var hostList = UnifiedCache.GetCachedHosts();
                var dsList = UnifiedCache.GetCachedDatastores();

                if (hostList.Count == 0)
                {
                    try
                    {
                        var result = SessionManager.Instance.ExecuteQueryAsync(
                            "Get-View -ViewType HostSystem -Property Name | Select-Object Name | Sort-Object Name",
                            (msg, level) => { }
                        ).Result;
                        if (result != null && result.Count > 0)
                        {
                            foreach (var dict in result)
                                if (dict.TryGetValue("Name", out var n) && !string.IsNullOrEmpty(n?.ToString()))
                                    hostList.Add(n.ToString().Trim());
                            UnifiedCache.UpdateResourceCache(hosts: hostList);
                        }
                    }
                    catch (Exception ex) { LogMessage("error", $"[克隆] 查询主机异常: {ex.Message}"); }
                }

                if (dsList.Count == 0)
                {
                    try
                    {
                        var result = SessionManager.Instance.ExecuteQueryAsync(
                            "Get-View -ViewType Datastore -Property Name | Select-Object Name | Sort-Object Name",
                            (msg, level) => { }
                        ).Result;
                        if (result != null && result.Count > 0)
                        {
                            foreach (var dict in result)
                                if (dict.TryGetValue("Name", out var n) && !string.IsNullOrEmpty(n?.ToString()))
                                    dsList.Add(n.ToString().Trim());
                            UnifiedCache.UpdateResourceCache(datastores: dsList);
                        }
                    }
                    catch (Exception ex) { LogMessage("error", $"[克隆] 查询存储异常: {ex.Message}"); }
                }

                string? curHost = null;
                string? curDs = null;
                try
                {
                    var vmInfo = SessionManager.Instance.ExecuteQueryAsync(
                        $"Get-View -Id '{vmMoRef}' -Property Runtime.Host,Datastore -ErrorAction Stop | Select-Object @{{N='Host';E={{ if($_.Runtime.Host){{(Get-View -Id $_.Runtime.Host -Property Name -ErrorAction SilentlyContinue).Name}}else{{''}} }}}}, @{{N='Datastore';E={{ $ds = $_.Datastore | Select-Object -First 1; if($ds){{(Get-View -Id $ds -Property Name -ErrorAction SilentlyContinue).Name}}else{{''}} }}}}",
                        (msg, level) => { }
                    ).Result;
                    if (vmInfo != null && vmInfo.Count > 0)
                    {
                        var d = vmInfo[0];
                        curHost = d.TryGetValue("Host", out var h) ? h?.ToString() : null;
                        curDs = d.TryGetValue("Datastore", out var s) ? s?.ToString() : null;
                    }
                }
                catch (Exception ex) { LogMessage("error", $"[克隆] 查询VM当前位置异常: {ex.Message}"); }

                _root.Invoke(new Action(() =>
                {
                    ShowCloneDialog(vmName, vmMoRef, hostList, dsList, curHost, curDs);
                }));
            });
        }
        private void DoCloneVm(string sourceVmName, string sourceVmMoRef, string newName, string? targetHost, string? targetDs)
        {
            var confirmResult = MessageBox.Show(
                $"确认要克隆虚拟机吗？\n\n" +
                $"源虚拟机：{sourceVmName}\n" +
                $"新名称：  {newName}\n" +
                $"目标主机：{targetHost ?? "（同主机）"}\n" +
                $"目标存储：{targetDs ?? "（同存储）"}",
                "克隆虚拟机确认",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Error);

            if (confirmResult != DialogResult.OK) return;

            StringBuilder sb = new StringBuilder();
            sb.Append($"$vm = Get-View -Id '{sourceVmMoRef}' -ErrorAction Stop; ");
            sb.Append("$cloneName = '" + EscapePsString(newName) + "'; ");
            sb.Append("$parentObj = Get-View -Id $vm.Parent -ErrorAction Stop; ");
            sb.Append("$folderMoRef = $null; ");
            sb.Append("if ($parentObj.MoRef.Type -eq 'Folder') { $folderMoRef = $parentObj.MoRef } elseif ($parentObj.MoRef.Type -eq 'ResourcePool' -and $parentObj.Owner) { $rpOwner = Get-View -Id $parentObj.Owner -ErrorAction Stop; if ($rpOwner.MoRef.Type -eq 'Folder') { $folderMoRef = $rpOwner.MoRef } }; ");
            sb.Append("if (-not $folderMoRef) { throw '无法解析目标 Folder' }; ");
            sb.Append($"$dup = Get-View -ViewType VirtualMachine -Filter @{{Name='{EscapePsString(newName)}'}} -ErrorAction SilentlyContinue | Where-Object {{ $_.Parent -eq $folderMoRef }}; if ($dup) {{ throw \"同 Folder 下已存在同名虚拟机：$cloneName\" }}; ");
            sb.Append("$cloneSpec = New-Object VMware.Vim.VirtualMachineCloneSpec; ");
            sb.Append("$cloneSpec.PowerOn = $false; ");
            sb.Append("$cloneSpec.Template = $false; ");
            sb.Append("$loc = New-Object VMware.Vim.VirtualMachineRelocateSpec; ");

            if (!string.IsNullOrEmpty(targetHost))
            {
                sb.Append($"$targetHost = Get-View -ViewType HostSystem -Filter @{{Name='{EscapePsString(targetHost)}'}} -ErrorAction SilentlyContinue; ");
                sb.Append("if (-not $targetHost) { ");
                sb.Append($"  Write-Output '【错误】未找到目标主机【{EscapePsString(targetHost)}】'; return ");
                sb.Append("} ");
                sb.Append("$loc.Host = $targetHost.MoRef; ");
            }
            else
            {
                sb.Append("$loc.Host = $vm.Runtime.Host; ");
            }

            if (!string.IsNullOrEmpty(targetDs))
            {
                sb.Append($"$targetDs = Get-View -ViewType Datastore -Filter @{{Name='{EscapePsString(targetDs)}'}} -ErrorAction SilentlyContinue; ");
                sb.Append("if (-not $targetDs) { ");
                sb.Append($"  Write-Output '【错误】未找到目标存储【{EscapePsString(targetDs)}】'; return ");
                sb.Append("} ");
                sb.Append("$loc.Datastore = $targetDs.MoRef; ");
            }

            sb.Append("$cloneSpec.Location = $loc; ");
            sb.Append("$task = $vm.CloneVM_Task($folderMoRef, $cloneName, $cloneSpec); ");
            sb.Append("Wait-Task $task '克隆'; ");
            sb.Append($"Write-Output '【{EscapePsString(sourceVmName)}】已克隆为【{EscapePsString(newName)}】'");

            ExecuteLongVmOperation(sb.ToString(), $"克隆虚拟机: {sourceVmName} → {newName}", onSuccess: DoQuery);
        }

        // ====== 远程连接操作 ======

        private void VmrcConnect(List<string> row)
        {
            try
            {
                string vmName = GetVmNameFromRow(row);
                if (string.IsNullOrEmpty(vmName))
                {
                    LogMessage("警告", "未获取到虚拟机名称");
                    return;
                }
                string vmrcMoid = GetVmIdFromRow(row);

                if (!SessionManager.Instance.IsConnected)
                {
                    LogMessage("警告", "请先连接到 vCenter Server");
                    return;
                }

                string psCmd = "$moid = '" + vmrcMoid + "'; " +
                    "$vc = $global:DefaultVIServer.Name; " +
                    "$sessionMgr = Get-View SessionManager; " +
                    "$ticket = $sessionMgr.AcquireCloneTicket(); " +
                    "Write-Output ('__VMRC_OK__:' + $vc + '|' + $moid + '|' + $ticket)";

                Task.Run(async () =>
                {
                    try
                    {
                        var result = await SessionManager.Instance.ExecutePsScriptAsync(psCmd, null, 15);
                        if (!result.Contains("__VMRC_OK__:"))
                        {
                            LogMessage("错误", "获取 VMRC 票据失败");
                            return;
                        }

                        var parts = result.Split(new[] { "__VMRC_OK__:" }, StringSplitOptions.None)[1].Trim().Split('|');
                        if (parts.Length < 3)
                        {
                            LogMessage("错误", "VMRC 票据格式异常");
                            return;
                        }

                        string vcHost = parts[0].Trim();
                        string moid = parts[1].Trim();
                        string ticket = parts[2].Trim();
                        string vmrcUrl = $"vmrc://clone:{ticket}@{vcHost}/?moid={moid}";

                        string vmrcExe = @"C:\Program Files (x86)\VMware\VMware Remote Console\vmrc.exe";
                        if (!File.Exists(vmrcExe))
                        {
                            LogMessage("错误", "VMRC 客户端未安装，请安装 VMware Remote Console");
                            return;
                        }

                        System.Diagnostics.Process.Start(vmrcExe, vmrcUrl);
                        LogMessage("成功", $"VMRC 控制台已启动: {vmName}");
                    }
                    catch (Exception ex)
                    {
                        LogMessage("错误", $"启动 VMRC 失败: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                LogMessage("错误", $"VMRC 连接异常: {ex.Message}");
            }
        }

        private void RdpConnect(List<string> row)
        {
            try
            {
                string vmIp = GetVmIpFromRow(row);
                if (string.IsNullOrEmpty(vmIp))
                {
                    LogMessage("警告", "未获取到虚拟机 IP 地址，请确认虚拟机已开机且 VMware Tools 已运行");
                    return;
                }

                string rdpDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VmOps", "rdp");
                Directory.CreateDirectory(rdpDir);
                string rdpFile = Path.Combine(rdpDir, $"vmops_{vmIp}.rdp");

                var sb = new StringBuilder();
                sb.AppendLine($"full address:s:{vmIp}");
                sb.AppendLine("username:s:administrator");
                sb.AppendLine("administrative session:i:0");
                sb.AppendLine("authentication level:i:2");
                sb.AppendLine("enablecredsspsupport:i:1");
                sb.AppendLine("prompt for credentials:i:0");
                sb.AppendLine("promptcredentialonce:i:1");
                sb.AppendLine("autoreconnection enabled:i:1");
                sb.AppendLine("redirectclipboard:i:1");
                sb.AppendLine("redirectprinters:i:0");
                sb.AppendLine("redirectsmartcards:i:0");
                sb.AppendLine("redirectwebauthn:i:0");
                sb.AppendLine("redirectcomports:i:0");
                sb.AppendLine("redirectdrives:i:0");
                sb.AppendLine("screen mode id:i:2");
                sb.AppendLine("desktopwidth:i:1920");
                sb.AppendLine("desktopheight:i:1080");
                sb.AppendLine("session bpp:i:32");
                sb.AppendLine("compression:i:1");
                sb.AppendLine("bitmapcachepersistenable:i:1");
                sb.AppendLine("disable wallpaper:i:0");
                sb.AppendLine("disable full window drag:i:0");
                sb.AppendLine("disable menu anims:i:0");
                sb.AppendLine("disable themes:i:0");

                File.WriteAllText(rdpFile, sb.ToString(), Encoding.UTF8);
                System.Diagnostics.Process.Start("mstsc.exe", rdpFile);
                LogMessage("成功", $"已启动 RDP 连接: administrator@{vmIp}");
            }
            catch (Exception ex)
            {
                LogMessage("错误", $"RDP 连接失败: {ex.Message}");
            }
        }

        private void SshConnectToVm(List<string> row)
        {
            string vmIp = GetVmIpFromRow(row);
            if (string.IsNullOrEmpty(vmIp))
            {
                LogMessage("警告", "未获取到虚拟机 IP 地址");
                return;
            }

            string mobaPath = FindMobaXterm();
            if (string.IsNullOrEmpty(mobaPath))
            {
                LogMessage("警告", "未找到 MobaXterm，请确认已安装");
                return;
            }

            Task.Run(() =>
            {
                try
                {
                    System.Diagnostics.Process.Start(mobaPath);
                    Thread.Sleep(1000);

                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = mobaPath,
                        Arguments = $"-newtab \"ssh root@{vmIp}\"",
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(psi);
                    LogMessage("成功", $"已启动 MobaXterm SSH 连接: root@{vmIp}");
                }
                catch (Exception ex)
                {
                    LogMessage("错误", $"SSH 连接失败: {ex.Message}");
                }
            });
        }

        private void SshConnectToHost(List<string> row)
        {
            string hostName = GetHostNameFromRow(row);
            if (string.IsNullOrEmpty(hostName))
            {
                LogMessage("错误", "无法获取主机名");
                return;
            }

            string mobaPath = FindMobaXterm();
            if (string.IsNullOrEmpty(mobaPath))
            {
                LogMessage("警告", "未找到 MobaXterm，请确认已安装");
                return;
            }

            Task.Run(() =>
            {
                try
                {
                    System.Diagnostics.Process.Start(mobaPath);
                    Thread.Sleep(1000);

                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = mobaPath,
                        Arguments = $"-newtab \"ssh root@{hostName}\"",
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(psi);
                    LogMessage("成功", $"已启动 MobaXterm SSH 连接: root@{hostName}");
                }
                catch (Exception ex)
                {
                    LogMessage("错误", $"SSH 连接失败: {ex.Message}");
                }
            });
        }

        private void OpenHostWebConsole(List<string> row)
        {
            string mgmtIp = "";
            int ipIdx = _columns.FindIndex(c => c.Equals("ManagementIp", StringComparison.OrdinalIgnoreCase));
            if (ipIdx >= 0 && ipIdx < row.Count) mgmtIp = row[ipIdx].Trim();

            if (string.IsNullOrEmpty(mgmtIp))
            {
                int nameIdx = _columns.FindIndex(c => c.Equals("Name", StringComparison.OrdinalIgnoreCase));
                if (nameIdx >= 0 && nameIdx < row.Count) mgmtIp = row[nameIdx].Trim();
            }

            if (string.IsNullOrEmpty(mgmtIp))
            {
                LogMessage("错误", "无法获取主机管理地址，请先查询主机列表");
                return;
            }

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = $"https://{mgmtIp}",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
                LogMessage("成功", $"已打开主机管理页面: https://{mgmtIp}");
            }
            catch (Exception ex)
            {
                LogMessage("错误", $"打开浏览器失败: {ex.Message}");
            }
        }

        // ====== 详情查看操作 ======

        private void ShowDetailFromItem(ListViewItem item)
        {
            var rowValues = GetItemRowValues(item);
            if (rowValues == null || rowValues.Count == 0) return;

            int nameIdx = _columns.FindIndex(c => c.Equals("Name", StringComparison.OrdinalIgnoreCase));
            if (nameIdx < 0) nameIdx = 0;
            if (nameIdx >= rowValues.Count) return;
            string targetName = rowValues[nameIdx].Trim();
            if (string.IsNullOrEmpty(targetName)) return;

            int idIdx = _columns.FindIndex(c => c.Equals("Id", StringComparison.OrdinalIgnoreCase));
            string targetId = (idIdx >= 0 && idIdx < rowValues.Count) ? FullMoRef(rowValues[idIdx].Trim()) : "";

            string actualType = _queryType == "vm" ? _currentSubtype : _queryType;
            var (psCmd, titlePrefix) = QueryBuilder.BuildDetailQuery(actualType, targetId, targetName);

            if (string.IsNullOrEmpty(psCmd)) return;

            if (!SessionManager.Instance.IsConnected)
            {
                LogMessage("警告", "请先连接到 vCenter Server");
                return;
            }

            LogMessage("信息", $"{titlePrefix}: {targetName}");

            Task.Run(async () =>
            {
                try
                {
                    string result = await SessionManager.Instance.ExecutePsScriptAsync(psCmd.Trim(), null, 30);
                    if (string.IsNullOrEmpty(result))
                    {
                        LogMessage("警告", "查询返回空结果");
                        return;
                    }

                    _root.Invoke(new Action(() =>
                    {
                        ShowDetailWindow(targetName, result, psCmd, titlePrefix);
                    }));
                }
                catch (Exception ex)
                {
                    LogMessage("错误", $"查询详情失败: {ex.Message}");
                }
            });
        }

        private void ShowDetailWindow(string name, string detail, string psCmd, string titlePrefix)
        {
            DetailForm? detailWindow = null;

            Action<string> refreshAction = (oldDetail) =>
            {
                if (!SessionManager.Instance.IsConnected)
                {
                    LogMessage("警告", "请先连接到 vCenter Server");
                    return;
                }
                LogMessage("信息", $"刷新 {titlePrefix}: {name}");
                Task.Run(async () =>
                {
                    try
                    {
                        string result = await SessionManager.Instance.ExecutePsScriptAsync(psCmd.Trim(), null, 30);
                        if (string.IsNullOrEmpty(result))
                        {
                            LogMessage("警告", "刷新返回空结果");
                            return;
                        }
                        _root.Invoke(new Action(() =>
                        {
                            detailWindow?.UpdateContent(result);
                        }));
                    }
                    catch (Exception ex)
                    {
                        LogMessage("错误", $"刷新详情失败: {ex.Message}");
                    }
                });
            };

            detailWindow = new DetailForm($"{_title} - {name}", detail, refreshAction);
            detailWindow.ShowDialog(_root);
        }

        // ====== 删除 / 模板转换操作 ======

        private void DeleteVm(List<string> row)
        {
            string vmName = GetVmNameFromRow(row);
            if (string.IsNullOrEmpty(vmName)) return;
            string vmMoRef = FullVmRef(GetVmIdFromRow(row));

            LogMessage("信息", $"执行操作: 删除虚拟机: {vmName}");

            Task.Run(async () =>
            {
                try
                {
                    var stateResult = await SessionManager.Instance.ExecuteQueryAsync(
                        $"Get-View -Id '{vmMoRef}' -Property Runtime.PowerState -ErrorAction Stop | Select-Object @{{N='PowerState';E={{$_.Runtime.PowerState}}}}",
                        (msg, level) => { }
                    );

                    string? powerState = null;
                    if (stateResult != null && stateResult.Count > 0)
                        powerState = stateResult[0].Values.FirstOrDefault()?.ToString();

                    if (powerState?.Equals("poweredOn", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        _root.Invoke(new Action(() =>
                        {
                            ToastUtils.ShowToast(_root, $"【{vmName}】处于开机状态，无法删除");
                        }));
                        return;
                    }

                    string script =
                        $"$ErrorActionPreference = 'Stop'; {PsWaitTaskFunc} " +
                        $"$vm = Get-View -Id '{vmMoRef}' -ErrorAction Stop | Select-Object -First 1; " +
                        "$task = $vm.Destroy_Task(); " +
                        "Wait-Task $task '删除虚拟机'; " +
                        $"Write-Output '【{EscapePsString(vmName)}】已从磁盘删除'";

                    await SessionManager.Instance.ExecuteQueryAsync(
                        script,
                        (msg, level) => LogMessage(level ?? "info", msg ?? "")
                    );

                    _root.Invoke(new Action(() =>
                    {
                        LogMessage("成功", $"操作完成: 删除虚拟机: {vmName}");
                        ToastUtils.ShowToast(_root, $"【{vmName}】已删除");
                        RemoveVmRow(vmMoRef);
                    }));
                }
                catch (Exception ex)
                {
                    _root.Invoke(new Action(() =>
                    {
                        LogMessage("错误", $"删除虚拟机失败: {ex.Message}");
                        MessageBox.Show($"删除虚拟机失败:\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
            });
        }

        private void ConvertVmToTemplate(List<string> row)
        {
            string vmName = GetVmNameFromRow(row);
            if (string.IsNullOrEmpty(vmName)) return;
            string vmMoRef = FullVmRef(GetVmIdFromRow(row));

            LogMessage("信息", $"执行操作: 转为模板（就地）: {vmName}");

            Task.Run(async () =>
            {
                try
                {
                    var stateResult = await SessionManager.Instance.ExecuteQueryAsync(
                        $"Get-View -Id '{vmMoRef}' -Property Runtime.PowerState -ErrorAction Stop | Select-Object @{{N='PowerState';E={{$_.Runtime.PowerState}}}}",
                        (msg, level) => { }
                    );

                    string? powerState = null;
                    if (stateResult != null && stateResult.Count > 0)
                        powerState = stateResult[0].Values.FirstOrDefault()?.ToString();

                    if (powerState?.Equals("poweredOn", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        _root.Invoke(new Action(() =>
                        {
                            ToastUtils.ShowToast(_root, $"【{vmName}】处于开机状态，无法执行转换");
                        }));
                        return;
                    }

                    string script =
                        $"$vm = Get-View -Id '{vmMoRef}' -ErrorAction Stop | Select-Object -First 1; " +
                        "$vm.MarkAsTemplate(); " +
                        $"Write-Output '【{EscapePsString(vmName)}】已转为模板'";

                    await SessionManager.Instance.ExecuteQueryAsync(
                        script,
                        (msg, level) => LogMessage(level ?? "info", msg ?? "")
                    );

                    _root.Invoke(new Action(() =>
                    {
                        LogMessage("成功", $"操作完成: 转为模板（就地）: {vmName}");
                        ToastUtils.ShowToast(_root, $"【{vmName}】已转为模板");
                    }));
                }
                catch (Exception ex)
                {
                    _root.Invoke(new Action(() =>
                    {
                        LogMessage("错误", $"转为模板失败: {ex.Message}");
                        MessageBox.Show($"转为模板失败:\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
            });
        }

        private void RenameVm(List<string> row)
        {
            string vmName = GetVmNameFromRow(row);
            if (string.IsNullOrEmpty(vmName)) return;
            string vmMoRef = FullVmRef(GetVmIdFromRow(row));

            string? newName = null;
            _root.Invoke(new Action(() =>
            {
                newName = ShowRenameDialog("虚拟机", vmName);
            }));

            if (string.IsNullOrEmpty(newName) || newName == vmName) return;

            LogMessage("信息", $"执行操作: 重命名虚拟机: {vmName} → {newName}");

            Task.Run(async () =>
            {
                try
                {
                    string script =
                        $"$ErrorActionPreference = 'Stop'; {PsWaitTaskFunc} " +
                        $"$vm = Get-View -Id '{vmMoRef}' -ErrorAction Stop; " +
                        $"$task = $vm.Rename_Task('{EscapePsString(newName)}'); " +
                        "Wait-Task $task '重命名虚拟机'; " +
                        $"Write-Output '【{EscapePsString(newName)}】已重命名'";

                    await SessionManager.Instance.ExecuteQueryAsync(
                        script,
                        (msg, level) => LogMessage(level ?? "info", msg ?? "")
                    );

                    _root.Invoke(new Action(() =>
                    {
                        LogMessage("成功", $"操作完成: 重命名虚拟机: {vmName} → {newName}");
                        ToastUtils.ShowToast(_root, $"已重命名为：{newName}");
                        RefreshSingleVm(GetVmIdFromRow(row));
                    }));
                }
                catch (Exception ex)
                {
                    _root.Invoke(new Action(() =>
                    {
                        LogMessage("错误", $"重命名虚拟机失败: {ex.Message}");
                        MessageBox.Show($"重命名虚拟机失败:\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
            });
        }

        private void RenameTemplate(List<string> row)
        {
            string tplName = GetVmNameFromRow(row);
            if (string.IsNullOrEmpty(tplName)) return;
            string tplMoRef = FullVmRef(GetVmIdFromRow(row));

            string? newName = null;
            _root.Invoke(new Action(() =>
            {
                newName = ShowRenameDialog("模板", tplName);
            }));

            if (string.IsNullOrEmpty(newName) || newName == tplName) return;

            LogMessage("信息", $"执行操作: 重命名模板: {tplName} → {newName}");

            Task.Run(async () =>
            {
                try
                {
                    string script =
                        $"$ErrorActionPreference = 'Stop'; {PsWaitTaskFunc} " +
                        $"$tpl = Get-View -Id '{tplMoRef}' -ErrorAction Stop; " +
                        $"$task = $tpl.Rename_Task('{EscapePsString(newName)}'); " +
                        "Wait-Task $task '重命名模板'; " +
                        $"Write-Output '【{EscapePsString(newName)}】已重命名'";

                    await SessionManager.Instance.ExecuteQueryAsync(
                        script,
                        (msg, level) => LogMessage(level ?? "info", msg ?? "")
                    );

                    _root.Invoke(new Action(() =>
                    {
                        LogMessage("成功", $"操作完成: 重命名模板: {tplName} → {newName}");
                        ToastUtils.ShowToast(_root, $"已重命名为：{newName}");
                        RefreshSingleVm(GetVmIdFromRow(row));
                    }));
                }
                catch (Exception ex)
                {
                    _root.Invoke(new Action(() =>
                    {
                        LogMessage("错误", $"重命名模板失败: {ex.Message}");
                        MessageBox.Show($"重命名模板失败:\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
            });
        }

        private void DeleteTemplate(List<string> row)
        {
            string tplName = GetVmNameFromRow(row);
            if (string.IsNullOrEmpty(tplName)) return;
            string tplMoRef = FullVmRef(GetVmIdFromRow(row));

            LogMessage("信息", $"执行操作: 删除模板: {tplName}");

            Task.Run(async () =>
            {
                try
                {
                    string script =
                        $"$ErrorActionPreference = 'Stop'; {PsWaitTaskFunc} " +
                        $"$tpl = Get-View -Id '{tplMoRef}' -ErrorAction Stop; " +
                        "$task = $tpl.Destroy_Task(); " +
                        "Wait-Task $task '删除模板'; " +
                        $"Write-Output '【{EscapePsString(tplName)}】已删除'";

                    await SessionManager.Instance.ExecuteQueryAsync(
                        script,
                        (msg, level) => LogMessage(level ?? "info", msg ?? "")
                    );

                    _root.Invoke(new Action(() =>
                    {
                        LogMessage("成功", $"操作完成: 删除模板: {tplName}");
                        ToastUtils.ShowToast(_root, $"【{tplName}】已删除");
                        RemoveVmRow(tplMoRef);
                    }));
                }
                catch (Exception ex)
                {
                    _root.Invoke(new Action(() =>
                    {
                        LogMessage("错误", $"删除模板失败: {ex.Message}");
                        MessageBox.Show($"删除模板失败:\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
            });
        }

        private void ConvertTemplateToVm(List<string> row)
        {
            string tplName = GetVmNameFromRow(row);
            if (string.IsNullOrEmpty(tplName)) return;
            string tplMoRef = FullVmRef(GetVmIdFromRow(row));

            LogMessage("信息", $"执行操作: 转为虚拟机（就地）: {tplName}");

            Task.Run(async () =>
            {
                try
                {
                    string script =
                        $"$ErrorActionPreference = 'Stop'; " +
                        $"$tpl = Get-View -Id '{tplMoRef}' -ErrorAction Stop; " +
                        "$rp = Get-View -ViewType ResourcePool -ErrorAction Stop | Select-Object -First 1; " +
                        "$tpl.MarkAsVirtualMachine($rp.MoRef, $null); " +
                        $"Write-Output '【{EscapePsString(tplName)}】已转为虚拟机'";

                    await SessionManager.Instance.ExecuteQueryAsync(
                        script,
                        (msg, level) => LogMessage(level ?? "info", msg ?? "")
                    );

                    _root.Invoke(new Action(() =>
                    {
                        LogMessage("成功", $"操作完成: 转为虚拟机（就地）: {tplName}");
                        ToastUtils.ShowToast(_root, $"【{tplName}】已转为虚拟机");
                    }));
                }
                catch (Exception ex)
                {
                    _root.Invoke(new Action(() =>
                    {
                        LogMessage("错误", $"转为虚拟机失败: {ex.Message}");
                        MessageBox.Show($"转为虚拟机失败:\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
            });
        }

        // ====== 编辑备注 ======

        private void EditVmNotes(List<string> row)
        {
            string vmName = GetVmNameFromRow(row);
            if (string.IsNullOrEmpty(vmName)) return;
            string vmMoRef = FullVmRef(GetVmIdFromRow(row));

            LogMessage("信息", $"执行操作: 编辑备注: {vmName}");

            Task.Run(async () =>
            {
                try
                {
                    var readResult = await SessionManager.Instance.ExecuteQueryAsync(
                        $"Get-View -Id '{vmMoRef}' -Property Config.Annotation -ErrorAction Stop | Select-Object @{{N='Notes';E={{$_.Config.Annotation}}}}",
                        (msg, level) => { }
                    );

                    string currentNotes = "";
                    if (readResult != null && readResult.Count > 0 && readResult[0].TryGetValue("Notes", out var notesObj))
                        currentNotes = notesObj?.ToString() ?? "";

                    string? newNotes = null;
                    _root.Invoke(new Action(() =>
                    {
                        newNotes = ShowNotesDialog(vmName, currentNotes);
                    }));

                    if (newNotes == null) return;

                    string notesBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(newNotes));
                    string script =
                        $"$ErrorActionPreference = 'Stop'; {PsWaitTaskFunc} " +
                        $"$notesBytes = [Convert]::FromBase64String('{notesBase64}'); " +
                        $"$notes = [System.Text.Encoding]::UTF8.GetString($notesBytes); " +
                        $"$vm = Get-View -Id '{vmMoRef}' -ErrorAction Stop; " +
                        "$cfg = New-Object VMware.Vim.VirtualMachineConfigSpec; " +
                        "$cfg.Annotation = $notes; " +
                        "$task = $vm.ReconfigVM_Task($cfg); " +
                        "Wait-Task $task '编辑备注'; " +
                        $"Write-Output '【{EscapePsString(vmName)}】备注已更新'";

                    await SessionManager.Instance.ExecuteQueryAsync(
                        script,
                        (msg, level) => LogMessage(level ?? "info", msg ?? "")
                    );

                    _root.Invoke(new Action(() =>
                    {
                        LogMessage("成功", $"操作完成: 编辑备注: {vmName}");
                        ToastUtils.ShowToast(_root, $"【{vmName}】备注已更新");
                    }));
                }
                catch (Exception ex)
                {
                    _root.Invoke(new Action(() =>
                    {
                        LogMessage("错误", $"编辑备注失败: {ex.Message}");
                        MessageBox.Show($"编辑备注失败:\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
            });
        }
    }
}