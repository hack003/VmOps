// ============================================================================
// ResourceQueryTab.BatchOps.cs - 批量操作与快照管理中心
// ============================================================================
// 职责范围：
//   ✅ 快照管理（List/Create/Revert/Remove Snapshots）
//   ✅ 批量电源操作（BatchPowerOn/Off/Restart/ForceOff）
//   ✅ 批量配置操作（BatchOptimize/BatchNtpSync）
//   ✅ 批量快照操作（BatchCreate/RemoveAllSnapshots）
//   ✅ 批量迁移操作（BatchMigrateToHost/ResourcePool/Datastore）
//
// 开发规范：
//   新增批量操作 → 在此文件添加方法
//   新增快照相关功能 → 在此文件添加方法
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using VMwareOps.Core;
using VMwareOps.Utils;

namespace VMwareOps.UI
{
    public partial class ResourceQueryTab
    {
        // ====== 快照管理操作 ======

        private void ListSnapshots(List<string> row)
        {
            string vmName = GetVmNameFromRow(row);
            string vmMoRef = FullVmRef(GetVmIdFromRow(row));
            if (string.IsNullOrEmpty(vmMoRef)) { LogMessage("警告", "未获取到虚拟机 ID"); return; }

            LogMessage("信息", $"查看快照列表: {vmName}");
            Task.Run(async () =>
            {
                try
                {
                    var result = await SessionManager.Instance.ExecuteQueryAsync(
                        $@"
$ErrorActionPreference = 'Stop';
$vm = Get-View -Id '{vmMoRef}' -Property Snapshot.RootSnapshotList,Snapshot.CurrentSnapshot,LayoutEx,Config.Template -ErrorAction Stop;
if ($vm.Config.Template) {{ throw '模板不支持快照操作' }}
$sizeMap = @{{}}; if ($vm.LayoutEx -and $vm.LayoutEx.Snapshot -and $vm.LayoutEx.Snapshot.Snapshot) {{ foreach ($leSnap in $vm.LayoutEx.Snapshot.Snapshot) {{ $sizeMap[$leSnap.Id] = $leSnap.Size }} }};
function Flatten-Snapshots($snapList) {{ $r=@(); foreach($s in $snapList){{ $r += $s; if($s.ChildSnapshotList){{$r += Flatten-Snapshots $s.ChildSnapshotList}} }}; return $r }}
if ($vm.Snapshot -and $vm.Snapshot.RootSnapshotList) {{ $flat = Flatten-Snapshots $vm.Snapshot.RootSnapshotList; $flat | Sort-Object CreateTime -Descending | ForEach-Object {{ [PSCustomObject]@{{Name=$_.Name; Description=$_.Description; SizeGB=if($sizeMap.ContainsKey($_.Id)){{[math]::Round($sizeMap[$_.Id]/1GB,2)}}else{{0}}; Created=$_.CreateTime; IsCurrent=($_.Snapshot.Value -eq $vm.Snapshot.CurrentSnapshot.Value); IsReplaySupported=$_.ReplaySupported; SnapshotRef=""$($_.Snapshot.Type)-$($_.Snapshot.Value)"" }} }} }}
",
                        (msg, level) => LogMessage(level ?? "info", msg ?? "")
                    );

                    _root.Invoke(new Action(() =>
                    {
                        ShowSnapshotDialog(vmName, result);
                    }));
                }
                catch (Exception ex)
                {
                    _root.Invoke(new Action(() =>
                    {
                        LogMessage("错误", $"查询快照失败: {vmName} - {ex.Message}");
                        MessageBox.Show($"查询快照失败: {vmName}\n\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
            });
        }

        private void ShowSnapshotDialog(string vmName, List<Dictionary<string, object>> snapshots)
        {
            using var dlg = new Form
            {
                Text = $"快照列表 — {vmName}",
                Icon = CoreUtils.GetAppIcon(),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                Font = new Font("Microsoft YaHei", 9),
                AutoScaleMode = AutoScaleMode.Dpi,
                AutoScaleDimensions = new SizeF(96F, 96F),
                Size = new Size(880, 450),
                MinimumSize = new Size(880, 450)
            };

            var listView = new ListView
            {
                Location = new Point(16, 16),
                Size = new Size(830, 300),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false
            };
            listView.Columns.Add("ID", 120);
            listView.Columns.Add("快照名称", 160);
            listView.Columns.Add("大小 (GB)", 80);
            listView.Columns.Add("创建时间", 160);
            listView.Columns.Add("当前", 50);
            listView.Columns.Add("描述", 200);

            if (snapshots != null && snapshots.Count > 0)
            {
                foreach (var snap in snapshots)
                {
                    var name = snap.TryGetValue("Name", out var n) ? n?.ToString() ?? "" : "";
                    var desc = snap.TryGetValue("Description", out var d) ? d?.ToString() ?? "" : "";
                    var size = snap.TryGetValue("SizeGB", out var s) ? s?.ToString() ?? "" : "";
                    var created = snap.TryGetValue("Created", out var c) ? c?.ToString() ?? "" : "";
                    var isCurrent = snap.TryGetValue("IsCurrent", out var ic) ? ic?.ToString() ?? "" : "";
                    var snapRef = snap.TryGetValue("SnapshotRef", out var sr) ? sr?.ToString() ?? "" : "";
                    string shortId = snapRef.Contains('-') ? snapRef.Substring(snapRef.IndexOf('-') + 1) : snapRef;

                    if (double.TryParse(size, out double sizeVal))
                        size = sizeVal.ToString("F2");

                    if (DateTime.TryParse(created, out DateTime dt))
                        created = dt.ToString("yyyy/MM/dd HH:mm:ss");

                    var item = new ListViewItem(shortId);
                    item.SubItems.Add(name);
                    item.SubItems.Add(size);
                    item.SubItems.Add(created);
                    item.SubItems.Add(isCurrent == "True" ? "✓" : "");
                    item.SubItems.Add(desc);
                    item.Tag = snapRef;
                    listView.Items.Add(item);
                }
            }
            else
            {
                var item = new ListViewItem("");
                item.SubItems.Add("(无快照)");
                item.SubItems.Add("");
                item.SubItems.Add("");
                item.SubItems.Add("");
                item.SubItems.Add("该虚拟机尚未创建任何快照");
                listView.Items.Add(item);
            }

            Utils.TableConvertUtils.AutoResizeColumns(listView);

            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, Height = 44,
                Padding = new Padding(0, 6, 0, 6),
                BackColor = Color.FromArgb(240, 240, 240)
            };
            var btnRevert = new Button { Text = "↩ 回滚到选中快照", Size = new Size(150, 28), Font = new Font("Microsoft YaHei", 9) };
            btnRevert.Click += (s, ev) =>
            {
                if (listView.SelectedItems.Count == 0)
                {
                    MessageBox.Show("请先在列表中选择一个快照", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                var selectedItem = listView.SelectedItems[0];
                string selectedSnapName = selectedItem.Text;
                string snapRef = selectedItem.Tag?.ToString() ?? "";
                if (string.IsNullOrEmpty(snapRef))
                {
                    MessageBox.Show("该快照信息缺失，无法回滚", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                var confirm = MessageBox.Show(
                    $"确定要回滚到快照「{selectedSnapName}」吗？\n\n" +
                    "⚠ 警告：回滚后当前磁盘上的所有更改将丢失，且虚拟机将立即恢复到该快照创建时的状态。\n\n" +
                    "建议：回滚前先确认虚拟机已备份或当前工作已保存。",
                    "确认回滚快照",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (confirm != DialogResult.Yes) return;

                LogMessage("信息", $"回滚快照: {vmName} → {selectedSnapName}");
                string script = $@"
$ErrorActionPreference = 'Stop';
{PsWaitTaskFunc}
$snapView = Get-View -Id '{EscapePsString(snapRef)}' -ErrorAction Stop;
$task = $snapView.RevertToSnapshot_Task($null, $null);
Wait-Task $task '回滚快照';
Write-Output '回滚快照成功: {EscapePsString(selectedSnapName)}';
";
                dlg.Close();
                ExecuteVmOperation(script, $"回滚快照: {vmName} → {selectedSnapName}", onSuccess: () => { });
            };
            var btnClose = new Button { Text = "关闭", DialogResult = DialogResult.OK, Size = new Size(80, 28), Font = new Font("Microsoft YaHei", 9) };
            btnPanel.Controls.Add(btnRevert);
            btnPanel.Controls.Add(btnClose);
            btnPanel.Controls.Add(new Label { Text = $"共 {snapshots?.Count ?? 0} 个快照", AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(0, 6, 120, 0) });

            CenterFlowPanel(btnPanel);

            dlg.Controls.Add(btnPanel);
            dlg.Controls.Add(listView);
            dlg.AcceptButton = btnClose;
            dlg.CancelButton = btnClose;

            dlg.ShowDialog(_root);
        }

        private void CreateSnapshot(List<string> row)
        {
            string vmName = GetVmNameFromRow(row);
            string vmMoRef = FullVmRef(GetVmIdFromRow(row));
            if (string.IsNullOrEmpty(vmMoRef)) { LogMessage("警告", "未获取到虚拟机 ID"); return; }

            using var dlg = new Form
            {
                Text = "创建快照",
                Icon = CoreUtils.GetAppIcon(),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                Font = new Font("Microsoft YaHei", 9),
                AutoScaleMode = AutoScaleMode.Dpi,
                AutoScaleDimensions = new SizeF(96F, 96F),
                Size = new Size(450, 280)
            };

            var lblVm = new Label { Text = $"虚拟机: {vmName}", Font = new Font("Microsoft YaHei", 10, FontStyle.Bold), Location = new Point(16, 12), AutoSize = true };
            var lblName = new Label { Text = "快照名称:", Location = new Point(16, 45), AutoSize = true };
            var txtName = new TextBox { Text = $"{vmName}_snapshot", Location = new Point(100, 42), Width = 300 };
            var lblDesc = new Label { Text = "快照描述:", Location = new Point(16, 75), AutoSize = true };
            var txtDesc = new TextBox { Text = "手动创建的快照", Location = new Point(16, 95), Width = 390, Height = 50, Multiline = true };
            var chkMemory = new CheckBox { Text = "包含内存状态（还原时恢复到运行状态，较慢）", Location = new Point(16, 155), AutoSize = true };
            var lblHint = new Label { Text = "不勾选则仅磁盘快照，还原时等于冷启动（较快）", ForeColor = Color.Gray, Location = new Point(16, 178), AutoSize = true };

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
            dlg.Controls.AddRange(new Control[] { lblVm, lblName, txtName, lblDesc, txtDesc, chkMemory, lblHint });
            dlg.AcceptButton = btnOk;
            dlg.CancelButton = btnCancel;

            if (dlg.ShowDialog(_root) != DialogResult.OK) return;

            string snapName = txtName.Text.Trim();
            if (string.IsNullOrEmpty(snapName))
            {
                MessageBox.Show("请输入快照名称", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string snapDesc = txtDesc.Text.Trim();
            bool includeMemory = chkMemory.Checked;
            string memoryFlag = includeMemory ? "$true" : "$false";

            string script = $@"
$ErrorActionPreference = 'Stop';
{PsWaitTaskFunc}
$vm = Get-View -Id '{vmMoRef}' -Property Config.Template -ErrorAction Stop;
if ($vm.Config.Template) {{ throw '模板不支持快照操作' }}
$task = $vm.CreateSnapshot_Task('{EscapePsString(snapName)}', '{EscapePsString(snapDesc)}', $false, {memoryFlag});
Wait-Task $task '创建快照';
";

            string extraInfo = $"快照名称: {snapName} | 描述: {snapDesc} | 包含内存: {(includeMemory ? "是" : "否")}";

            var confirmResult = MessageBox.Show(
                $"确认创建快照？\n\n{extraInfo}",
                "创建快照确认",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question);

            if (confirmResult != DialogResult.OK) return;

            ExecuteVmOperation(script, $"创建快照: {vmName}", onSuccess: () => { });
        }

        private void RevertToLatestSnapshot(List<string> row)
        {
            string vmName = GetVmNameFromRow(row);
            string vmMoRef = FullVmRef(GetVmIdFromRow(row));
            if (string.IsNullOrEmpty(vmMoRef)) { LogMessage("警告", "未获取到虚拟机 ID"); return; }

            var confirmResult = MessageBox.Show(
                $"确认回滚到 [{vmName}] 的最新快照？\n\n" +
                "⚠ 会丢失最新快照之后的所有磁盘变更（回滚过程自动触发）\n" +
                "此操作不可逆，请确认后再执行！",
                "回滚到最新快照确认",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);

            if (confirmResult != DialogResult.OK) return;

            string script = $@"
$ErrorActionPreference = 'Stop';
{PsWaitTaskFunc}
$vm = Get-View -Id '{vmMoRef}' -Property Snapshot.RootSnapshotList,Config.Template -ErrorAction Stop;
if ($vm.Config.Template) {{ throw '模板不支持快照操作' }}
function Flatten-Snapshots($snapList) {{ $r=@(); foreach($s in $snapList){{ $r += $s; if($s.ChildSnapshotList){{$r += Flatten-Snapshots $s.ChildSnapshotList}} }}; return $r }}
if ($vm.Snapshot -and $vm.Snapshot.RootSnapshotList) {{
    $flat = Flatten-Snapshots $vm.Snapshot.RootSnapshotList;
    if ($flat.Count -gt 0) {{
        $latest = $flat | Sort-Object CreateTime -Descending | Select-Object -First 1;
        $snapView = Get-View -Id $latest.Snapshot -ErrorAction SilentlyContinue;
        if (-not $snapView) {{ throw '无法获取快照对象' }};
        $task = $snapView.RevertToSnapshot_Task($null, $null);
        Wait-Task $task '回滚快照';
    }} else {{ throw '暂无快照，无法回滚' }}
}} else {{ throw '暂无快照，无法回滚' }}
";

            ExecuteVmOperation(script, $"回滚到最新快照: {vmName}", onSuccess: () => { });
        }

        private void RemoveAllSnapshots(List<string> row)
        {
            string vmName = GetVmNameFromRow(row);
            string vmMoRef = FullVmRef(GetVmIdFromRow(row));
            if (string.IsNullOrEmpty(vmMoRef)) { LogMessage("警告", "未获取到虚拟机 ID"); return; }

            var confirmResult = MessageBox.Show(
                $"⚠ 确认要删除 [{vmName}] 的所有快照吗？\n\n" +
                "警告：删除后无法恢复！\n" +
                "这将释放占用的存储空间，但会丢失所有快照数据。\n\n" +
                "此操作不可撤销！",
                "删除快照确认",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Error);

            if (confirmResult != DialogResult.OK) return;

            string script = $@"
$ErrorActionPreference = 'Stop';
{PsWaitTaskFunc}
$vm = Get-View -Id '{vmMoRef}' -Property Snapshot.RootSnapshotList,Config.Template -ErrorAction Stop;
if ($vm.Config.Template) {{ throw '模板不支持快照操作' }}
if ($vm.Snapshot -and $vm.Snapshot.RootSnapshotList) {{ $task = $vm.RemoveAllSnapshots_Task($null); Wait-Task $task '删除所有快照' }} else {{ throw '暂无快照' }}
";

            ExecuteVmOperation(script, $"删除所有快照: {vmName}", onSuccess: () => { });
        }

        // ====== 批量电源操作 ======

        private void BatchPowerOn(List<List<string>> rows)
        {
            var vmMoRefs = rows.Select(r => FullVmRef(GetVmIdFromRow(r))).Where(n => !string.IsNullOrEmpty(n)).ToList();
            if (vmMoRefs.Count == 0) return;
            string moRefArray = string.Join(",", vmMoRefs.Select(n => $"'{n}'"));
            string script = $@"
$ErrorActionPreference = 'Stop';
$vms = Get-View -Id @({moRefArray}) -Property Name,Runtime.PowerState,Config.Template -ErrorAction SilentlyContinue | Where-Object {{ -not $_.Config.Template }};
foreach ($vm in $vms) {{
    if ($vm.Runtime.PowerState -ne 'poweredOn') {{ $task = $vm.PowerOnVM_Task($null); Wait-Task $task '批量开机' }} else {{ Write-Output ('[已开机] ' + $vm.Name) }}
}}
";
            ExecuteBatchOperation(script, $"批量开机 ({vmMoRefs.Count} 台)", vmMoRefs);
        }

        private void BatchPowerOff(List<List<string>> rows)
        {
            var vmMoRefs = rows.Select(r => FullVmRef(GetVmIdFromRow(r))).Where(n => !string.IsNullOrEmpty(n)).ToList();
            if (vmMoRefs.Count == 0) return;
            string moRefArray = string.Join(",", vmMoRefs.Select(n => $"'{n}'"));
            string script = $@"
$ErrorActionPreference = 'Stop';
$vms = Get-View -Id @({moRefArray}) -Property Name,Runtime.PowerState,Guest.ToolsStatus,Config.Template -ErrorAction SilentlyContinue | Where-Object {{ -not $_.Config.Template }};
foreach ($vm in $vms) {{
    if ($vm.Runtime.PowerState -ne 'poweredOn') {{ Write-Output ('[已关机] ' + $vm.Name); continue }}
    $toolsOk = $vm.Guest -and ($vm.Guest.ToolsStatus -eq 'toolsOk' -or $vm.Guest.ToolsStatus -eq 'toolsOld');
    if ($toolsOk) {{ try {{ $vm.ShutdownGuest(); Write-Output ('[已发送关闭指令] ' + $vm.Name) }} catch {{ Write-Output ('[发送失败] ' + $vm.Name + ' - ' + $_.Exception.Message) }} }}
    else {{ Write-Output ('[Tools未运行，无法关闭] ' + $vm.Name) }}
}}
";
            ExecuteBatchOperation(script, $"批量关闭客户机 ({vmMoRefs.Count} 台)", vmMoRefs);
        }

        private void BatchPowerOffForce(List<List<string>> rows)
        {
            var vmMoRefs = rows.Select(r => FullVmRef(GetVmIdFromRow(r))).Where(n => !string.IsNullOrEmpty(n)).ToList();
            if (vmMoRefs.Count == 0) return;
            string moRefArray = string.Join(",", vmMoRefs.Select(n => $"'{n}'"));
            string script = $@"
$ErrorActionPreference = 'Stop';
$vms = Get-View -Id @({moRefArray}) -Property Name,Runtime.PowerState,Config.Template -ErrorAction SilentlyContinue | Where-Object {{ -not $_.Config.Template }};
foreach ($vm in $vms) {{
    if ($vm.Runtime.PowerState -eq 'poweredOn') {{ $task = $vm.PowerOffVM_Task(); Wait-Task $task '批量关闭电源' }} else {{ Write-Output ('[已关机] ' + $vm.Name) }}
}}
";
            ExecuteBatchOperation(script, $"批量关闭电源 ({vmMoRefs.Count} 台)", vmMoRefs);
        }

        private void BatchRestart(List<List<string>> rows)
        {
            var vmMoRefs = rows.Select(r => FullVmRef(GetVmIdFromRow(r))).Where(n => !string.IsNullOrEmpty(n)).ToList();
            if (vmMoRefs.Count == 0) return;
            string moRefArray = string.Join(",", vmMoRefs.Select(n => $"'{n}'"));
            string script = $@"
$ErrorActionPreference = 'Stop';
$vms = Get-View -Id @({moRefArray}) -Property Name,Runtime.PowerState,Config.Template -ErrorAction SilentlyContinue | Where-Object {{ -not $_.Config.Template }};
foreach ($vm in $vms) {{
    if ($vm.Runtime.PowerState -eq 'poweredOn') {{ $task = $vm.ResetVM_Task(); Wait-Task $task '批量重置电源' }} else {{ Write-Output ('[非开机状态，跳过] ' + $vm.Name) }}
}}
";
            ExecuteBatchOperation(script, $"批量重置电源 ({vmMoRefs.Count} 台)", vmMoRefs);
        }

        private void BatchDeleteVm(List<List<string>> rows)
        {
            var vmNames = rows.Select(GetVmNameFromRow).Where(n => !string.IsNullOrEmpty(n)).ToList();
            var vmMoRefs = rows.Select(r => FullVmRef(GetVmIdFromRow(r))).Where(n => !string.IsNullOrEmpty(n)).ToList();
            if (vmNames.Count == 0) return;

            var confirmResult = MessageBox.Show(
                $"⚠ 危险操作：确认要从磁盘永久删除以下 {vmNames.Count} 台虚拟机吗？\n\n" +
                string.Join(", ", vmNames) + "\n\n" +
                "此操作不可恢复！建议先确认 VM 已关机。",
                "批量删除虚拟机确认",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Error);

            if (confirmResult != DialogResult.OK) return;

            LogMessage("信息", $"执行批量操作: 批量删除虚拟机 ({vmNames.Count} 台)");

            Task.Run(async () =>
            {
                try
                {
                    string moRefArray = string.Join(",", vmMoRefs.Select(n => $"'{n}'"));
                    string script = $@"
$ErrorActionPreference = 'Stop';
$vms = Get-View -Id @({moRefArray}) -Property Name,Runtime.PowerState,Config.Template -ErrorAction SilentlyContinue | Where-Object {{ -not $_.Config.Template }};
$poweredOn = $vms | Where-Object {{ $_.Runtime.PowerState -eq 'poweredOn' }};
$toDelete = $vms | Where-Object {{ $_.Runtime.PowerState -ne 'poweredOn' }};
if ($poweredOn) {{ $names = ($poweredOn | ForEach-Object Name) -join ' '; Write-Output '[警告] 以下虚拟机处于开机状态，已跳过: ' + $names }};
foreach ($vm in $toDelete) {{ $task = $vm.Destroy_Task(); Wait-Task $task '批量删除' }};
if (-not $toDelete) {{ Write-Output '[提示] 没有可删除的虚拟机（全部处于开机状态）' }}
";
                    string injected = $"$ErrorActionPreference = 'Stop'; {PsWaitTaskFunc} {script}";

                    await SessionManager.Instance.ExecuteQueryAsync(
                        injected,
                        (msg, level) => LogMessage(level ?? "info", msg ?? "")
                    );

                    _root.Invoke(new Action(() =>
                    {
                        LogMessage("成功", $"批量删除虚拟机操作完成");
                        ToastUtils.ShowToast(_root, $"批量删除完成: {vmNames.Count} 台");
                        DoQuery();
                    }));
                }
                catch (Exception ex)
                {
                    _root.Invoke(new Action(() =>
                    {
                        LogMessage("错误", $"批量删除虚拟机失败: {ex.Message}");
                        MessageBox.Show($"批量删除失败:\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
            });
        }

        private void BatchCreateSnapshot(List<List<string>> rows)
        {
            var vmMoRefs = rows.Select(r => FullVmRef(GetVmIdFromRow(r))).Where(n => !string.IsNullOrEmpty(n)).ToList();
            if (vmMoRefs.Count == 0) return;
            string moRefArray = string.Join(",", vmMoRefs.Select(n => $"'{n}'"));
            string snapLabel = $"BatchSnapshot_{DateTime.Now:yyyyMMdd_HHmmss}";
            string script = $@"
$ErrorActionPreference = 'Stop';
$vms = Get-View -Id @({moRefArray}) -Property Name,Config.Template -ErrorAction SilentlyContinue | Where-Object {{ -not $_.Config.Template }};
foreach ($vm in $vms) {{ $task = $vm.CreateSnapshot_Task('{snapLabel}', '批量创建', $false, $false); Wait-Task $task '批量创建快照' }}
";
            ExecuteBatchOperation(script, $"批量创建快照 ({vmMoRefs.Count} 台)", onSuccess: () => { });
        }

        private void BatchRemoveAllSnapshots(List<List<string>> rows)
        {
            var vmMoRefs = rows.Select(r => FullVmRef(GetVmIdFromRow(r))).Where(n => !string.IsNullOrEmpty(n)).ToList();
            if (vmMoRefs.Count == 0) return;
            string moRefArray = string.Join(",", vmMoRefs.Select(n => $"'{n}'"));
            string script = $@"
$ErrorActionPreference = 'Stop';
$vms = Get-View -Id @({moRefArray}) -Property Name,Snapshot.RootSnapshotList,Config.Template -ErrorAction SilentlyContinue | Where-Object {{ -not $_.Config.Template }};
foreach ($vm in $vms) {{
    if ($vm.Snapshot -and $vm.Snapshot.RootSnapshotList) {{ $task = $vm.RemoveAllSnapshots_Task($null); Wait-Task $task '批量删除所有快照' }} else {{ Write-Output ('[无快照] ' + $vm.Name) }}
}}
";
            ExecuteBatchOperation(script, $"批量删除所有快照 ({vmMoRefs.Count} 台)", onSuccess: () => { });
        }

        private void BatchMigrateToHost(List<List<string>> rows)
        {
            var vmMoRefs = rows.Select(r => FullVmRef(GetVmIdFromRow(r))).Where(n => !string.IsNullOrEmpty(n)).ToList();
            if (vmMoRefs.Count == 0) return;
            int vmCount = vmMoRefs.Count;

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
                                if (dict.TryGetValue("Name", out var n) && !string.IsNullOrEmpty(n?.ToString()))
                                    hostList.Add(n.ToString().Trim());
                            UnifiedCache.UpdateResourceCache(hosts: hostList);
                        }
                    }
                    catch (Exception ex) { LogMessage("error", $"[批量迁移主机] 查询异常: {ex.Message}"); }
                }

                _root.Invoke(new Action(() =>
                {
                    string? targetHost = ShowBatchSelectDialog(
                        $"批量迁移到主机（共 {vmCount} 台）",
                        "请选择目标 ESXi 主机：",
                        hostList);
                    if (string.IsNullOrEmpty(targetHost)) return;

                    var confirm = MessageBox.Show(
                        $"确认要将选中的 {vmCount} 台虚拟机迁移到主机 [{targetHost}] 吗？\n\n" +
                        "⚠ vMotion 零停机迁移\n" +
                        "⚠ 迁移过程中请勿断开网络",
                        "批量迁移到主机确认",
                        MessageBoxButtons.OKCancel,
                        MessageBoxIcon.Question);
                    if (confirm != DialogResult.OK) return;

                    string moRefArray = string.Join(",", vmMoRefs.Select(n => $"'{n}'"));
                    string script = $@"
$ErrorActionPreference = 'Stop';
$targetHost = Get-View -ViewType HostSystem -Filter @{{Name='{EscapePsString(targetHost)}'}} -ErrorAction Stop;
$vms = Get-View -Id @({moRefArray}) -Property Name,Config.Template -ErrorAction SilentlyContinue | Where-Object {{ -not $_.Config.Template }};
foreach ($vm in $vms) {{ $spec = New-Object VMware.Vim.VirtualMachineRelocateSpec; $spec.Host = $targetHost.MoRef; $task = $vm.RelocateVM_Task($spec, [VMware.Vim.VirtualMachineMovePriority]::defaultPriority); Wait-Task $task '批量迁移主机' }}
";
                    ExecuteBatchOperation(script, $"批量迁移到主机 ({vmCount} 台)", vmMoRefs);
                }));
            });
        }

        private void BatchMigrateToResourcePool(List<List<string>> rows)
        {
            var vmMoRefs = rows.Select(r => FullVmRef(GetVmIdFromRow(r))).Where(n => !string.IsNullOrEmpty(n)).ToList();
            if (vmMoRefs.Count == 0) return;
            int vmCount = vmMoRefs.Count;

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
                                if (dict.TryGetValue("Name", out var n) && !string.IsNullOrEmpty(n?.ToString()))
                                    rpList.Add(n.ToString().Trim());
                            UnifiedCache.UpdateResourceCache(resourcePools: rpList);
                        }
                    }
                    catch (Exception ex) { LogMessage("error", $"[批量迁移资源池] 查询异常: {ex.Message}"); }
                }

                _root.Invoke(new Action(() =>
                {
                    string? targetPool = ShowBatchSelectDialog(
                        $"批量迁移到资源池（共 {vmCount} 台）",
                        "请选择目标资源池：",
                        rpList);
                    if (string.IsNullOrEmpty(targetPool)) return;

                    var confirm = MessageBox.Show(
                        $"确认要将选中的 {vmCount} 台虚拟机迁移到资源池 [{targetPool}] 吗？",
                        "批量迁移到资源池确认",
                        MessageBoxButtons.OKCancel,
                        MessageBoxIcon.Question);
                    if (confirm != DialogResult.OK) return;

                    string moRefArray = string.Join(",", vmMoRefs.Select(n => $"'{n}'"));
                    string script = $@"
$ErrorActionPreference = 'Stop';
$targetPool = Get-View -ViewType ResourcePool -Filter @{{Name='{EscapePsString(targetPool)}'}} -ErrorAction Stop;
$vms = Get-View -Id @({moRefArray}) -Property Name,Config.Template -ErrorAction SilentlyContinue | Where-Object {{ -not $_.Config.Template }};
foreach ($vm in $vms) {{ $spec = New-Object VMware.Vim.VirtualMachineRelocateSpec; $spec.Pool = $targetPool.MoRef; $task = $vm.RelocateVM_Task($spec, [VMware.Vim.VirtualMachineMovePriority]::defaultPriority); Wait-Task $task '批量迁移资源池' }}
";
                    ExecuteBatchOperation(script, $"批量迁移到资源池 ({vmCount} 台)", vmMoRefs);
                }));
            });
        }

        private void BatchMigrateToDatastore(List<List<string>> rows)
        {
            var vmMoRefs = rows.Select(r => FullVmRef(GetVmIdFromRow(r))).Where(n => !string.IsNullOrEmpty(n)).ToList();
            if (vmMoRefs.Count == 0) return;
            int vmCount = vmMoRefs.Count;

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
                                if (dict.TryGetValue("Name", out var n) && !string.IsNullOrEmpty(n?.ToString()))
                                    dsList.Add(n.ToString().Trim());
                            UnifiedCache.UpdateResourceCache(datastores: dsList);
                        }
                    }
                    catch (Exception ex) { LogMessage("error", $"[批量迁移存储] 查询异常: {ex.Message}"); }
                }

                _root.Invoke(new Action(() =>
                {
                    string? targetDs = ShowBatchSelectDialog(
                        $"批量迁移到存储（共 {vmCount} 台）",
                        "请选择目标存储：",
                        dsList);
                    if (string.IsNullOrEmpty(targetDs)) return;

                    var confirm = MessageBox.Show(
                        $"确认要将选中的 {vmCount} 台虚拟机迁移到存储 [{targetDs}] 吗？\n\n" +
                        "⚠ Storage vMotion 后台迁移磁盘\n" +
                        "⚠ 开机状态也可执行",
                        "批量迁移到存储确认",
                        MessageBoxButtons.OKCancel,
                        MessageBoxIcon.Question);
                    if (confirm != DialogResult.OK) return;

                    string moRefArray = string.Join(",", vmMoRefs.Select(n => $"'{n}'"));
                    string script = $@"
$ErrorActionPreference = 'Stop';
$targetDs = Get-View -ViewType Datastore -Filter @{{Name='{EscapePsString(targetDs)}'}} -ErrorAction Stop;
$vms = Get-View -Id @({moRefArray}) -Property Name,Config.Template -ErrorAction SilentlyContinue | Where-Object {{ -not $_.Config.Template }};
foreach ($vm in $vms) {{
    $relocate = New-Object VMware.Vim.VirtualMachineRelocateSpec;
    $relocate.Datastore = $targetDs.MoRef;
    $task = $vm.RelocateVM_Task($relocate, [VMware.Vim.VirtualMachineMovePriority]::defaultPriority);
    Wait-Task $task '批量迁移存储'
}}
";
                    ExecuteBatchOperation(script, $"批量迁移到存储 ({vmCount} 台)", vmMoRefs);
                }));
            });
        }

        private string? ShowBatchSelectDialog(string title, string prompt, List<string> itemList)
        {
            if (itemList.Count == 0)
            {
                MessageBox.Show("未查询到可用的目标列表，请检查 vCenter 连接。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            using var dlg = new Form
            {
                Text = title,
                Icon = CoreUtils.GetAppIcon(),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                Font = new Font("Microsoft YaHei", 9),
                AutoScaleMode = AutoScaleMode.Dpi,
                AutoScaleDimensions = new SizeF(96F, 96F),
                Size = new Size(480, 180)
            };

            var lbl = new Label { Text = prompt, Location = new Point(16, 14), AutoSize = true };
            var combo = new ComboBox
            {
                Location = new Point(16, 42),
                Width = 432,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            combo.Items.AddRange(itemList.ToArray());
            combo.SelectedIndex = 0;

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
            dlg.Controls.AddRange(new Control[] { lbl, combo });
            dlg.AcceptButton = btnOk;
            dlg.CancelButton = btnCancel;

            if (dlg.ShowDialog(_root) == DialogResult.OK)
                return combo.SelectedItem?.ToString();
            return null;
        }
    }
}