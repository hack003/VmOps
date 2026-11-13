// ============================================================================
// ResourceQueryTab.VmOpsDialog.cs - 虚拟机操作对话框中心
// ============================================================================
// 职责范围：
//   ✅ ShowEditConfigDialog       - CPU/内存/端口组编辑对话框
//   ✅ ShowDiskEditorDialog       - 磁盘编辑对话框
//   ✅ ShowPortGroupDialog        - 新建端口组对话框
//   ✅ ShowPortgroupEditDialog    - 编辑端口组对话框
//   ✅ ShowMigrateHostDialog      - 迁移目标主机选择
//   ✅ ShowMigrateResourcePoolDialog - 迁移目标资源池选择
//   ✅ ShowMigrateDatastoreDialog - 迁移目标存储选择
//   ✅ ShowCloneDialog            - 克隆参数对话框
//   ✅ ShowNotesDialog            - 编辑备注对话框
//   ✅ ShowGuestOpsNetworkDialog   - 客户机网络配置对话框
//   ✅ ShowGuestOpsPasswordDialog  - 客户机密码配置对话框（独立脚本，不混用 DeployManager）
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
using VmOps.Core;
using VmOps.Utils;

namespace VmOps.UI
{
    public partial class ResourceQueryTab
    {
        private void ShowEditConfigDialog(List<string> row)
        {
            string vmMoRef = FullVmRef(GetVmIdFromRow(row));
            string vmName = GetVmNameFromRow(row);
            if (string.IsNullOrEmpty(vmName)) return;

            int currentCpu = GetIntFromRow(row, "NumCpu");
            if (currentCpu <= 0) currentCpu = 2;

            int currentMem = 0;
            int memIdx = _columns.FindIndex(c => c.Equals("MemoryGB", StringComparison.OrdinalIgnoreCase));
            if (memIdx >= 0 && memIdx < row.Count)
            {
                int.TryParse(row[memIdx].Trim(), out currentMem);
            }
            if (currentMem <= 0) currentMem = 4;

            string currentPortGroup = "";

            var cpuPresets = new[] { "1", "2", "4", "8", "16", "32", "48", "64", "96", "128" };
            var memPresets = new[] { "1", "2", "4", "8", "16", "32", "64", "128", "256" };
            var pgCache = new List<string>();

            using var dlg = new Form
            {
                Text = $"编辑配置 - {vmName}",
                Icon = CoreUtils.GetAppIcon(),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                Font = new Font("Microsoft YaHei", 9),
                AutoScaleMode = AutoScaleMode.Dpi,
                AutoScaleDimensions = new SizeF(96F, 96F),
                Size = new Size(600, 300),
                MinimumSize = new Size(600, 300)
            };

            var configGrid = new DataGridView
            {
                Location = new Point(16, 16),
                Size = new Size(550, 120),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Margin = Padding.Empty,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AllowUserToResizeColumns = false,
                ReadOnly = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Microsoft YaHei", 9)
            };

            configGrid.Columns.Add("configItem", "配置项");
            configGrid.Columns.Add("currentConfig", "当前配置");

            var comboCol = new DataGridViewComboBoxColumn
            {
                Name = "newConfig",
                HeaderText = "新配置",
                Width = 150,
                FlatStyle = FlatStyle.Standard,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox,
                DisplayStyleForCurrentCellOnly = true
            };
            // 添加所有可能的值到 ComboBox 列，确保当前值能正确显示
            var allComboItems = new List<string>();
            allComboItems.AddRange(cpuPresets);
            allComboItems.AddRange(memPresets);
            // 确保当前值也在列表中，避免显示异常
            if (!allComboItems.Contains(currentCpu.ToString()))
                allComboItems.Add(currentCpu.ToString());
            string currentMemStr = currentMem.ToString();
            if (!allComboItems.Contains(currentMemStr))
                allComboItems.Add(currentMemStr);
            comboCol.Items.AddRange(allComboItems.ToArray());
            configGrid.Columns.Add(comboCol);

            configGrid.Columns.Add("description", "说明");

            configGrid.Columns["configItem"].Width = 120;
            configGrid.Columns["currentConfig"].Width = 120;
            configGrid.Columns["description"].Width = 140;

            configGrid.Columns["configItem"].ReadOnly = true;
            configGrid.Columns["currentConfig"].ReadOnly = true;
            configGrid.Columns["description"].ReadOnly = true;
            configGrid.Columns["description"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            configGrid.Rows.Add("vCPU 数量", $"{currentCpu} 核", currentCpu.ToString(), "范围: 1 ~ 128");
            configGrid.Rows.Add("内存大小", $"{currentMem} GB", currentMem.ToString(), "范围: 1 ~ 256 GB");
            configGrid.Rows.Add("网络端口组", "加载中...", currentPortGroup, "可选: 点击加载后选择");

            var lblPresetHint = new Label
            {
                Text = "预设: 1, 2, 4, 8, 16, 32, 48, 64, 96, 128",
                Location = new Point(16, 145),
                AutoSize = true,
                ForeColor = Color.Gray,
                Font = new Font("Microsoft YaHei", 8),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Margin = Padding.Empty
            };

            var lblHint = new Label
            {
                Text = " 若不支持 Hot-Add，需要先关机", Location = new Point(16, 165),
                AutoSize = true,
                ForeColor = ColorTranslator.FromHtml("#EF6C00"),
                Font = new Font("Microsoft YaHei", 9),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Margin = Padding.Empty
            };

            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, Height = 44,
                Padding = new Padding(0, 6, 0, 6),
                BackColor = Color.FromArgb(240, 240, 240)
            };
            var btnApply = new Button { Text = "应用变更", Size = new Size(80, 28), Font = new Font("Microsoft YaHei", 9), DialogResult = DialogResult.OK };
            var btnCancel = new Button { Text = "取消", Size = new Size(80, 28), Font = new Font("Microsoft YaHei", 9), DialogResult = DialogResult.Cancel };
            btnPanel.Controls.Add(btnApply);
            btnPanel.Controls.Add(btnCancel);

            CenterFlowPanel(btnPanel);

            dlg.Controls.Add(btnPanel);
            dlg.Controls.AddRange(new Control[] { configGrid, lblPresetHint, lblHint });
            dlg.AcceptButton = btnApply;
            dlg.CancelButton = btnCancel;

            configGrid.CellBeginEdit += (s, e) =>
            {
                if (e.ColumnIndex == 2 && e.RowIndex == 0)
                {
                    lblPresetHint.Text = "预设: 1, 2, 4, 8, 16, 32, 48, 64, 96, 128";
                }
                else if (e.ColumnIndex == 2 && e.RowIndex == 1)
                {
                    lblPresetHint.Text = "预设: 1, 2, 4, 8, 16, 32, 64, 128, 256 (GB)";
                }
                else if (e.ColumnIndex == 2 && e.RowIndex == 2)
                {
                    lblPresetHint.Text = "提示: 点击主界面\"查看网络\"可获得完整端口组列表";
                }
            };

            configGrid.EditingControlShowing += (s, e) =>
            {
                if (e.Control is ComboBox cb && configGrid.CurrentCell.ColumnIndex == 2)
                {
                    cb.DropDownStyle = ComboBoxStyle.DropDown;
                    cb.Items.Clear();

                    int rowIndex = configGrid.CurrentCell.RowIndex;
                    if (rowIndex == 0)
                    {
                        cb.Items.AddRange(cpuPresets);
                    }
                    else if (rowIndex == 1)
                    {
                        cb.Items.AddRange(memPresets);
                    }
                    else if (rowIndex == 2)
                    {
                        if (pgCache.Count > 0)
                        {
                            cb.Items.AddRange(pgCache.ToArray());
                        }
                    }

                    // 获取单元格当前值（可能是用户刚选择的值）
                    string currentVal = configGrid.CurrentCell.Value?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(currentVal) && !cb.Items.Contains(currentVal))
                    {
                        cb.Items.Add(currentVal);
                    }

                    // 设置默认值为当前值
                    if (!string.IsNullOrEmpty(currentVal))
                    {
                        cb.Text = currentVal;
                    }
                }
            };

            // 编辑完成后，确保选中的值保留在单元格的 Value 中
            configGrid.CellEndEdit += (s, e) =>
            {
                if (e.ColumnIndex == 2)
                {
                    var cell = configGrid.Rows[e.RowIndex].Cells[2];
                    string selectedVal = cell.Value?.ToString() ?? "";
                    // 确保选中的值在 ComboBox 列的 Items 中，避免显示异常
                    if (!string.IsNullOrEmpty(selectedVal) && !comboCol.Items.Contains(selectedVal))
                    {
                        comboCol.Items.Add(selectedVal);
                    }
                }
            };

            configGrid.DataError += (s, e) =>
            {
                e.ThrowException = false;
            };

            Task.Run(() =>
            {
                try
                {
                    var pgCacheLocal = UnifiedCache.GetCachedPortGroups();
                    if (pgCacheLocal.Count == 0)
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
                                    if (!string.IsNullOrEmpty(name))
                                    {
                                        pgCacheLocal.Add(name);
                                    }
                                }
                            }
                            if (pgCacheLocal.Count > 0)
                            {
                                UnifiedCache.UpdateResourceCache(portGroups: pgCacheLocal);
                            }
                        }
                    }

                    var vmResult = SessionManager.Instance.ExecuteQueryAsync(
                        $"Get-View -Id '{vmMoRef}' -Property Config.Hardware.Device -ErrorAction Stop | ForEach-Object {{ $_.Config.Hardware.Device | Where-Object {{ $_ -is [VMware.Vim.VirtualEthernetCard] }} | Select-Object -First 1 | ForEach-Object {{ if ($_.Backing -is [VMware.Vim.VirtualEthernetCardDistributedVirtualPortBackingInfo]){{[PSCustomObject]@{{NetworkName=$_.Backing.NetworkName}}}}else{{[PSCustomObject]@{{NetworkName=$_.Backing.DeviceName}}}} }} }}",
                        (msg, level) => { }
                    ).Result;

                    string vmPg = "";
                    if (vmResult != null && vmResult.Count > 0 && vmResult[0].TryGetValue("NetworkName", out var pgObj))
                    {
                        vmPg = pgObj?.ToString()?.Trim() ?? "";
                    }

                    _root.Invoke(new Action(() =>
                    {
                        currentPortGroup = vmPg;
                        pgCache = new List<string>(pgCacheLocal);

                        configGrid.Rows[2].Cells[1].Value = string.IsNullOrEmpty(vmPg) ? "(未获取)" : vmPg;
                        configGrid.Rows[2].Cells[2].Value = vmPg;

                        // 将当前端口组值加入 ComboBox 列的 Items，确保能正确显示
                        if (!string.IsNullOrEmpty(vmPg) && !comboCol.Items.Contains(vmPg))
                        {
                            comboCol.Items.Add(vmPg);
                        }

                        if (pgCacheLocal.Count > 0)
                        {
                            configGrid.Rows[2].Cells[3].Value = "可选: " + string.Join(", ", pgCacheLocal.Take(5)) + (pgCacheLocal.Count > 5 ? "..." : "");
                        }
                    }));
                }
                catch (Exception)
                {
                    _root.Invoke(new Action(() =>
                    {
                        configGrid.Rows[2].Cells[1].Value = "(查询失败)";
                    }));
                }
            });

            btnApply.Click += async (s, e) =>
            {
                int newCpu = currentCpu;
                int newMem = currentMem;
                string newPortGroup = "";

                string cpuText = (configGrid.Rows[0].Cells[2].Value?.ToString() ?? "").Trim();
                if (!int.TryParse(cpuText, out newCpu) || newCpu < 1 || newCpu > 128)
                {
                    MessageBox.Show("vCPU 数量必须为 1~128 之间的整数", "输入错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string memText = (configGrid.Rows[1].Cells[2].Value?.ToString() ?? "").Trim();
                memText = memText.Replace("GB", "").Replace("gb", "").Replace("Gb", "").Trim();
                if (!int.TryParse(memText, out newMem) || newMem < 1 || newMem > 256)
                {
                    MessageBox.Show("内存大小必须为 1~256 之间的整数", "输入错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                newPortGroup = (configGrid.Rows[2].Cells[2].Value?.ToString() ?? "").Trim();

                bool cpuChanged = newCpu != currentCpu;
                bool memChanged = newMem != currentMem;
                bool pgChanged = !string.IsNullOrEmpty(newPortGroup) && newPortGroup != currentPortGroup;

                if (!cpuChanged && !memChanged && !pgChanged)
                {
                    ToastUtils.ShowToast(dlg, "配置未发生变化");
                    return;
                }

                // 智能判断是否需要关机
                bool needShutdown = false;
                List<string> shutdownReasons = new List<string>();

                // CPU 调小必须关机
                if (cpuChanged && newCpu < currentCpu)
                {
                    needShutdown = true;
                    shutdownReasons.Add($"vCPU 调小（{currentCpu} → {newCpu}）需要关机");
                }

                // 内存调小必须关机
                if (memChanged && newMem < currentMem)
                {
                    needShutdown = true;
                    shutdownReasons.Add($"内存调小（{currentMem} → {newMem} GB）需要关机");
                }

                // CPU/内存调大需要检查 Hot-Add 状态
                bool cpuNeedHotAdd = cpuChanged && newCpu > currentCpu;
                bool memNeedHotAdd = memChanged && newMem > currentMem;

                string hotAddStatus = "";
                bool cpuHotAddEnabled = false;
                bool memHotAddEnabled = false;

                if (cpuNeedHotAdd || memNeedHotAdd)
                {
                    try
                    {
                        var hotAddResult = await SessionManager.Instance.ExecuteQueryAsync(
                            $"Get-View -Id '{vmMoRef}' -Property Runtime.PowerState,Config.CpuHotAddEnabled,Config.MemoryHotAddEnabled -ErrorAction Stop | Select-Object @{{N='PowerState';E={{$_.Runtime.PowerState}}}}, @{{N='CpuHotAddEnabled';E={{$_.Config.CpuHotAddEnabled}}}}, @{{N='MemoryHotAddEnabled';E={{$_.Config.MemoryHotAddEnabled}}}}",
                            (msg, level) => { }
                        );

                        if (hotAddResult != null && hotAddResult.Count > 0)
                        {
                            if (hotAddResult[0].TryGetValue("CpuHotAddEnabled", out var cpuHotObj))
                                cpuHotAddEnabled = cpuHotObj?.ToString()?.Equals("True", StringComparison.OrdinalIgnoreCase) == true;
                            if (hotAddResult[0].TryGetValue("MemoryHotAddEnabled", out var memHotObj))
                                memHotAddEnabled = memHotObj?.ToString()?.Equals("True", StringComparison.OrdinalIgnoreCase) == true;

                            hotAddStatus = $"CPU Hot-Add: {(cpuHotAddEnabled ? "已启用" : "未启用")} | 内存 Hot-Add: {(memHotAddEnabled ? "已启用" : "未启用")}";
                        }
                    }
                    catch (Exception)
                    {
                    }
                }

                // CPU 调大但未启用 Hot-Add，需要关机
                if (cpuNeedHotAdd && !cpuHotAddEnabled)
                {
                    needShutdown = true;
                    shutdownReasons.Add($"vCPU 调大（{currentCpu} → {newCpu}）但未启用 CPU Hot-Add");
                }

                // 内存调大但未启用 Hot-Add，需要关机
                if (memNeedHotAdd && !memHotAddEnabled)
                {
                    needShutdown = true;
                    shutdownReasons.Add($"内存调大（{currentMem} → {newMem} GB）但未启用内存 Hot-Add");
                }

                // 构建确认消息
                string confirmMsg = $"确认要将 [{vmName}] 的配置调整为：\n\n";
                if (cpuChanged)
                    confirmMsg += $"• vCPU: {currentCpu} 核 → {newCpu} 核\n";
                if (memChanged)
                    confirmMsg += $"• 内存: {currentMem} GB → {newMem} GB\n";
                if (pgChanged)
                    confirmMsg += $"• 端口组: {currentPortGroup} → {newPortGroup}\n";

                if (!string.IsNullOrEmpty(hotAddStatus))
                    confirmMsg += $"\n📊 Hot-Add 状态：{hotAddStatus}";

                if (needShutdown)
                {
                    confirmMsg += "\n\n⚠ 以下操作需要关机：\n";
                    foreach (var reason in shutdownReasons)
                        confirmMsg += $"  • {reason}\n";
                    confirmMsg += "\n系统将自动执行：关机 → 修改配置 → 开机";
                }
                else if (cpuNeedHotAdd || memNeedHotAdd)
                {
                    confirmMsg += "\n\n✅ 支持在线变更（Hot-Add 已启用），无需关机";
                }

                DialogResult confirmResult;

                if (needShutdown)
                {
                    // 需要关机时，使用醒目的自定义对话框
                    using var shutdownDlg = new Form
                    {
                        Text = "⚠ 需要关机确认",
                        Icon = CoreUtils.GetAppIcon(),
                        FormBorderStyle = FormBorderStyle.FixedDialog,
                        StartPosition = FormStartPosition.CenterParent,
                        MaximizeBox = false,
                        MinimizeBox = false,
                        ShowInTaskbar = false,
                        Font = new Font("Microsoft YaHei", 9),
                        AutoScaleMode = AutoScaleMode.Dpi,
                        AutoScaleDimensions = new SizeF(96F, 96F),
                        Size = new Size(520, 420),
                        MinimumSize = new Size(520, 420),
                        BackColor = Color.White
                    };

                    var lblTitle = new Label
                    {
                        Text = "⚠ 配置变更需要关机",
                        Location = new Point(20, 15),
                        AutoSize = true,
                        Font = new Font("Microsoft YaHei", 12, FontStyle.Bold),
                        ForeColor = Color.FromArgb(211, 47, 47),
                        Anchor = AnchorStyles.Top | AnchorStyles.Left,
                        Margin = Padding.Empty
                    };

                    var lblContent = new Label
                    {
                        Text = confirmMsg,
                        Location = new Point(20, 50),
                        Size = new Size(460, 180),
                        AutoSize = false,
                        Font = new Font("Microsoft YaHei", 9), ForeColor = Color.FromArgb(50, 50, 50),
                        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                        Margin = Padding.Empty
                    };

                    var lblAutoAction = new Label
                    {
                        Text = "🔄 自动操作流程：\n  1️⃣ 关闭虚拟机 → 2️⃣ 修改配置 → 3️⃣ 自动开机\n\n 注：若虚拟机原为关机状态，修改后仍保持关机",
                        Location = new Point(20, 240),
                        Size = new Size(460, 60),
                        AutoSize = false,
                        Font = new Font("Microsoft YaHei", 9, FontStyle.Bold),
                        ForeColor = Color.FromArgb(25, 118, 210),
                        BackColor = Color.FromArgb(232, 245, 255),
                        Padding = new Padding(10),
                        TextAlign = ContentAlignment.MiddleLeft,
                        Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                        Margin = Padding.Empty
                    };

                    var btnPanel = new FlowLayoutPanel
                    {
                        Dock = DockStyle.Bottom, Height = 44,
                        Padding = new Padding(0, 6, 0, 6),
                        BackColor = Color.FromArgb(240, 240, 240)
                    };
                    var btnConfirm = new Button
                    {
                        Text = "确认关机并应用",
                        DialogResult = DialogResult.OK,
                        Size = new Size(150, 28),
                        Font = new Font("Microsoft YaHei", 9, FontStyle.Bold),
                        BackColor = Color.FromArgb(211, 47, 47),
                        ForeColor = Color.White,
                        FlatStyle = FlatStyle.Flat
                    };
                    btnConfirm.FlatAppearance.BorderSize = 0;
                    var btnCancel = new Button
                    {
                        Text = "取消",
                        DialogResult = DialogResult.Cancel,
                        Size = new Size(80, 28),
                        Font = new Font("Microsoft YaHei", 9)
                    };
                    btnPanel.Controls.Add(btnConfirm);
                    btnPanel.Controls.Add(btnCancel);

                    CenterFlowPanel(btnPanel);

                    shutdownDlg.Controls.Add(btnPanel);
                    shutdownDlg.Controls.AddRange(new Control[] { lblTitle, lblContent, lblAutoAction });
                    shutdownDlg.AcceptButton = btnConfirm;
                    shutdownDlg.CancelButton = btnCancel;

                    confirmResult = shutdownDlg.ShowDialog(_root);
                }
                else
                {
                    confirmResult = MessageBox.Show(
                        confirmMsg,
                        "配置变更确认",
                        MessageBoxButtons.OKCancel,
                        MessageBoxIcon.Question);
                }

                if (confirmResult != DialogResult.OK) return;

                dlg.DialogResult = DialogResult.OK;
                dlg.Close();

                // 构建 PowerShell 脚本
                string script = $"$vm = Get-View -Id '{vmMoRef}' -ErrorAction Stop | Select-Object -First 1; ";

                if (needShutdown)
                {
                    // 需要关机：先关机 → 修改配置 → 开机
                    script += "$originalState = $vm.Runtime.PowerState; ";
                    script += "if ($vm.Runtime.PowerState -eq 'poweredOn') { " +
                              "  Write-Output '【正在关机】请稍候...'; " +
                              "  $offTask = $vm.PowerOffVM_Task(); Wait-Task $offTask '关机' " +
                              "  Write-Output '【关机完成】'; " +
                              "} ";

                    script += "Write-Output '【修改配置中...】'; ";
                    script += "$cfg = New-Object VMware.Vim.VirtualMachineConfigSpec; ";

                    if (cpuChanged)
                        script += $"Write-Output '【修改 vCPU: {currentCpu} → {newCpu}】'; ";
                    if (memChanged)
                        script += $"Write-Output '【修改内存: {currentMem} → {newMem} GB】'; ";

                    if (cpuChanged)
                        script += $"$cfg.NumCPUs = {newCpu}; ";
                    if (memChanged)
                        script += $"$cfg.MemoryMB = {newMem * 1024}; ";

                    if (pgChanged)
                    {
                        script += $"Write-Output '【修改端口组: {currentPortGroup} → {newPortGroup}】'; ";
                        script += "$adapters = $vm.Config.Hardware.Device | Where-Object { $_ -is [VMware.Vim.VirtualEthernetCard] }; ";
                        script += "$devSpecs = @(); ";
                        script += $"$pgName = '{EscapePsString(newPortGroup)}'; ";
                        script += "$pgNameLower = $pgName.ToLower(); ";
                        script += "$dvPg = Get-View -ViewType DistributedVirtualPortgroup -ErrorAction Stop | Where-Object { $_.Name.ToLower() -eq $pgNameLower } | Select-Object -First 1; ";
                        script += "$stdPg = $null; ";
                        script += "if (-not $dvPg) { $stdPg = Get-View -ViewType Network -ErrorAction Stop | Where-Object { $_.Name.ToLower() -eq $pgNameLower -and $_.MoRef.Type -eq 'Network' } | Select-Object -First 1 }; ";
                        script += "$switchUuid = $null; ";
                        script += "if ($dvPg) { ";
                        script += "  Write-Output ('→ DVPortgroup: ' + $dvPg.Name + ' (PGKey=' + $dvPg.MoRef.Value + ')'); ";
                        script += "  $switch = Get-View -ViewType DistributedVirtualSwitch -ErrorAction Stop | Where-Object { $_.Portgroup -and ($_.Portgroup | Where-Object { $_.Value -eq $dvPg.MoRef.Value }) } | Select-Object -First 1; ";
                        script += "  if ($switch) { $switchUuid = $switch.Uuid }; ";
                        script += "} elseif ($stdPg) { ";
                        script += "  Write-Output ('→ 标准端口组: ' + $stdPg.Name); ";
                        script += "} else { throw ('找不到端口组: ' + $pgName) }; ";
                        script += "foreach ($nic in $adapters) { ";
                        script += "  if ($dvPg) { ";
                        script += "    $nic.Backing = New-Object VMware.Vim.VirtualEthernetCardDistributedVirtualPortBackingInfo; ";
                        script += "    $nic.Backing.Port = New-Object VMware.Vim.DistributedVirtualSwitchPortConnection; ";
                        script += "    $nic.Backing.Port.PortgroupKey = $dvPg.MoRef.Value; ";
                        script += "    if ($switchUuid) { $nic.Backing.Port.SwitchUuid = $switchUuid }; ";
                        script += "  } else { ";
                        script += "    $nic.Backing = New-Object VMware.Vim.VirtualEthernetCardNetworkBackingInfo; ";
                        script += "    $nic.Backing.DeviceName = $stdPg.Name; ";
                        script += "    $nic.Backing.Network = $stdPg.MoRef; ";
                        script += "  }; ";
                        script += "  $ds = New-Object VMware.Vim.VirtualDeviceConfigSpec; ";
                        script += "  $ds.Operation = [VMware.Vim.VirtualDeviceConfigSpecOperation]::edit; ";
                        script += "  $ds.Device = $nic; ";
                        script += "  $devSpecs += $ds; ";
                        script += "}; ";
                        script += "$cfg.DeviceChange = $devSpecs; ";
                    }

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
                    // 无需关机，直接修改
                    script += "$cfg = New-Object VMware.Vim.VirtualMachineConfigSpec; ";

                    if (cpuChanged)
                    {
                        script += $"Write-Output '【在线修改 vCPU: {currentCpu} → {newCpu}】'; ";
                        script += $"$cfg.NumCPUs = {newCpu}; ";
                    }
                    if (memChanged)
                    {
                        script += $"Write-Output '【在线修改内存: {currentMem} → {newMem} GB】'; ";
                        script += $"$cfg.MemoryMB = {newMem * 1024}; ";
                    }
                    if (pgChanged)
                    {
                        script += $"Write-Output '【修改端口组: {currentPortGroup} → {newPortGroup}】'; ";
                        script += "$adapters = $vm.Config.Hardware.Device | Where-Object { $_ -is [VMware.Vim.VirtualEthernetCard] }; ";
                        script += "$devSpecs = @(); ";
                        script += $"$pgName = '{EscapePsString(newPortGroup)}'; ";
                        script += "$pgNameLower = $pgName.ToLower(); ";
                        script += "$dvPg = Get-View -ViewType DistributedVirtualPortgroup -ErrorAction Stop | Where-Object { $_.Name.ToLower() -eq $pgNameLower } | Select-Object -First 1; ";
                        script += "$stdPg = $null; ";
                        script += "if (-not $dvPg) { $stdPg = Get-View -ViewType Network -ErrorAction Stop | Where-Object { $_.Name.ToLower() -eq $pgNameLower -and $_.MoRef.Type -eq 'Network' } | Select-Object -First 1 }; ";
                        script += "$switchUuid = $null; ";
                        script += "if ($dvPg) { ";
                        script += "  Write-Output ('→ DVPortgroup: ' + $dvPg.Name + ' (PGKey=' + $dvPg.MoRef.Value + ')'); ";
                        script += "  $switch = Get-View -ViewType DistributedVirtualSwitch -ErrorAction Stop | Where-Object { $_.Portgroup -and ($_.Portgroup | Where-Object { $_.Value -eq $dvPg.MoRef.Value }) } | Select-Object -First 1; ";
                        script += "  if ($switch) { $switchUuid = $switch.Uuid }; ";
                        script += "} elseif ($stdPg) { ";
                        script += "  Write-Output ('→ 标准端口组: ' + $stdPg.Name); ";
                        script += "} else { throw ('找不到端口组: ' + $pgName) }; ";
                        script += "foreach ($nic in $adapters) { ";
                        script += "  if ($dvPg) { ";
                        script += "    $nic.Backing = New-Object VMware.Vim.VirtualEthernetCardDistributedVirtualPortBackingInfo; ";
                        script += "    $nic.Backing.Port = New-Object VMware.Vim.DistributedVirtualSwitchPortConnection; ";
                        script += "    $nic.Backing.Port.PortgroupKey = $dvPg.MoRef.Value; ";
                        script += "    if ($switchUuid) { $nic.Backing.Port.SwitchUuid = $switchUuid }; ";
                        script += "  } else { ";
                        script += "    $nic.Backing = New-Object VMware.Vim.VirtualEthernetCardNetworkBackingInfo; ";
                        script += "    $nic.Backing.DeviceName = $stdPg.Name; ";
                        script += "    $nic.Backing.Network = $stdPg.MoRef; ";
                        script += "  }; ";
                        script += "  $ds = New-Object VMware.Vim.VirtualDeviceConfigSpec; ";
                        script += "  $ds.Operation = [VMware.Vim.VirtualDeviceConfigSpecOperation]::edit; ";
                        script += "  $ds.Device = $nic; ";
                        script += "  $devSpecs += $ds; ";
                        script += "}; ";
                        script += "$cfg.DeviceChange = $devSpecs; ";
                    }

                    script += "$task = $vm.ReconfigVM_Task($cfg); ";
                    script += "Wait-Task $task '配置修改'; ";
                }

                script += $"Write-Output '【{EscapePsString(vmName)}】配置更新完成'";

                ExecuteVmOperation(script, $"编辑配置: {vmName}", vmName);
            };

            dlg.ShowDialog(_root);
        }

        private void ShowPortgroupEditDialog(string vmName, string vmMoRef, List<Dictionary<string, string>> nicList, List<string> pgCache)
        {
            using var dlg = new Form
            {
                Text = $"网卡配置 - {vmName}",
                Icon = CoreUtils.GetAppIcon(),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                Font = new Font("Microsoft YaHei", 9),
                AutoScaleMode = AutoScaleMode.Dpi,
                AutoScaleDimensions = new SizeF(96F, 96F),
                Size = new Size(560, 480),
                MinimumSize = new Size(560, 480)
            };

            var lblNicHint = new Label { Text = $"检测到 {nicList.Count} 个网卡，可批量切换端口组、添加或删除网卡：", Location = new Point(16, 16), AutoSize = true, Font = new Font("Microsoft YaHei", 9), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Margin = Padding.Empty };

            var nicListView = new ListView
            {
                Location = new Point(16, 45),
                Size = new Size(515, 150),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = true,
                HeaderStyle = ColumnHeaderStyle.Clickable,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Margin = Padding.Empty
            };
            nicListView.Columns.Add("网卡名称", 140);
            nicListView.Columns.Add("当前端口组", 180);
            nicListView.Columns.Add("连接", 60);
            nicListView.Columns.Add("开机连", 60);
            nicListView.Columns.Add("Key", 60);

            foreach (var nic in nicList)
            {
                var label = nic.TryGetValue("DeviceLabel", out var dl) ? dl : "";
                var pg = nic.TryGetValue("NetworkName", out var nn) ? nn : "";
                var key = nic.TryGetValue("Key", out var k) ? k : "";
                var conn = nic.TryGetValue("Connected", out var c) && c.Equals("True", StringComparison.OrdinalIgnoreCase) ? "● 已连" : "○ 断开";
                var startConn = nic.TryGetValue("StartConnected", out var sc) && sc.Equals("True", StringComparison.OrdinalIgnoreCase) ? "✓ 是" : "✗ 否";
                var item = new ListViewItem(label);
                item.SubItems.Add(pg);
                item.SubItems.Add(conn);
                item.SubItems.Add(startConn);
                item.SubItems.Add(key);
                nicListView.Items.Add(item);
            }

            if (nicListView.Items.Count > 0) nicListView.Items[0].Selected = true;

            var lblPg = new Label { Text = "新端口组:", Location = new Point(16, 210), AutoSize = true, Margin = Padding.Empty };
            var cbPg = new ComboBox { Location = new Point(100, 207), Width = 200, DropDownStyle = ComboBoxStyle.DropDown, Anchor = AnchorStyles.Top | AnchorStyles.Left, Margin = Padding.Empty };
            cbPg.Items.AddRange(pgCache.ToArray());
            if (nicList.Count > 0 && nicList[0].TryGetValue("NetworkName", out var firstPg))
                cbPg.Text = firstPg;

            var btnSelectAll = new Button { Text = "全选", Location = new Point(310, 205), Width = 55, Height = 28, Anchor = AnchorStyles.Top | AnchorStyles.Left, Margin = Padding.Empty };
            var btnAddNic = new Button { Text = "➕ 新增网卡", Location = new Point(430, 205), Width = 105, Height = 28, Anchor = AnchorStyles.Top | AnchorStyles.Right, Margin = Padding.Empty };

            var chkConnected = new CheckBox { Text = "连接网卡 (Connected)", Location = new Point(16, 250), AutoSize = true, Font = new Font("Microsoft YaHei", 9), Margin = Padding.Empty };
            var chkStartConnected = new CheckBox { Text = "开机自动连接 (StartConnected)", Location = new Point(16, 272), AutoSize = true, Font = new Font("Microsoft YaHei", 9), Margin = Padding.Empty };

            var btnDeleteNic = new Button
            {
                Text = "🗑 删除所选网卡",
                Location = new Point(16, 300),
                Width = 130,
                Height = 28,
                Font = new Font("Microsoft YaHei", 9),
                BackColor = Color.FromArgb(255, 235, 238),
                ForeColor = Color.FromArgb(183, 28, 28),
                FlatStyle = FlatStyle.Flat,
                Enabled = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                Margin = Padding.Empty
            };
            btnDeleteNic.FlatAppearance.BorderColor = Color.FromArgb(183, 28, 28);

            var lblDeleteWarn = new Label
            {
                Text = "⚠ 高风险操作，按 Key 精确删除",
                Location = new Point(155, 305),
                AutoSize = true,
                Font = new Font("Microsoft YaHei", 8),
                ForeColor = Color.FromArgb(183, 28, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Margin = Padding.Empty
            };

            var lblTarget = new Label { Text = "修改目标: 所选网卡", Location = new Point(16, 340), AutoSize = true, Font = new Font("Microsoft YaHei", 9, FontStyle.Bold), ForeColor = Color.FromArgb(25, 118, 210), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Margin = Padding.Empty };

            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, Height = 44,
                Padding = new Padding(0, 6, 0, 6),
                BackColor = Color.FromArgb(240, 240, 240)
            };
            var btnApply = new Button { Text = "✅ 应用变更", DialogResult = DialogResult.OK, Size = new Size(90, 28), Font = new Font("Microsoft YaHei", 9) };
            var btnCancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Size = new Size(80, 28), Font = new Font("Microsoft YaHei", 9) };
            btnPanel.Controls.Add(btnApply);
            btnPanel.Controls.Add(btnCancel);

            CenterFlowPanel(btnPanel);

            dlg.Controls.Add(btnPanel);
            dlg.Controls.AddRange(new Control[] { lblNicHint, nicListView, lblPg, cbPg, btnSelectAll, btnAddNic, chkConnected, chkStartConnected, btnDeleteNic, lblDeleteWarn, lblTarget });
            dlg.AcceptButton = btnApply;
            dlg.CancelButton = btnCancel;

            void RefreshNicListAsync()
            {
                Task.Run(async () =>
                {
                    try
                    {
                        var nicResult = await SessionManager.Instance.ExecuteQueryAsync(
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
                        );

                        _root.Invoke(new Action(() =>
                        {
                            nicListView.BeginUpdate();
                            try
                            {
                                nicListView.Items.Clear();
                                if (nicResult != null && nicResult.Count > 0)
                                {
                                    foreach (var dict in nicResult)
                                    {
                                        var label2 = dict.TryGetValue("DeviceLabel", out var dlObj) ? dlObj?.ToString()?.Trim() ?? "" : "";
                                        var pg2 = dict.TryGetValue("NetworkName", out var nnObj) ? nnObj?.ToString()?.Trim() ?? "" : "";
                                        var key2 = dict.TryGetValue("Key", out var kObj) ? kObj?.ToString()?.Trim() ?? "" : "";
                                        var conn2 = dict.TryGetValue("Connected", out var cObj) && cObj?.ToString()?.Equals("True", StringComparison.OrdinalIgnoreCase) == true ? "● 已连" : "○ 断开";
                                        var startConn2 = dict.TryGetValue("StartConnected", out var scObj) && scObj?.ToString()?.Equals("True", StringComparison.OrdinalIgnoreCase) == true ? "✓ 是" : "✗ 否";
                                        var item = new ListViewItem(label2);
                                        item.SubItems.Add(pg2);
                                        item.SubItems.Add(conn2);
                                        item.SubItems.Add(startConn2);
                                        item.SubItems.Add(key2);
                                        nicListView.Items.Add(item);
                                    }
                                }
                                lblNicHint.Text = $"检测到 {nicListView.Items.Count} 个网卡，可批量切换端口组、添加或删除网卡：";
                            }
                            finally { nicListView.EndUpdate(); }

                            btnDeleteNic.Enabled = nicListView.SelectedItems.Count > 0;
                            if (nicListView.Items.Count > 0 && nicListView.SelectedItems.Count == 0)
                                nicListView.Items[0].Selected = true;
                        }));
                    }
                    catch (Exception ex)
                    {
                        _root.Invoke(new Action(() =>
                        {
                            MessageBox.Show($"刷新网卡列表失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }));
                    }
                });
            }

            btnSelectAll.Click += (s, e) => { foreach (ListViewItem i in nicListView.Items) i.Selected = true; };

            nicListView.SelectedIndexChanged += (s, e) =>
            {
                btnDeleteNic.Enabled = nicListView.SelectedItems.Count > 0;

                if (nicListView.SelectedItems.Count == 0)
                {
                    lblTarget.Text = "修改目标: 未选择";
                }
                else if (nicListView.SelectedItems.Count == 1)
                {
                    var selected = nicListView.SelectedItems[0];
                    cbPg.Text = selected.SubItems[1].Text;
                    lblTarget.Text = $"修改目标: {selected.Text}";
                    nicListView.BeginUpdate();
                    try
                    {
                        chkConnected.Checked = selected.SubItems[2].Text.Contains("已连");
                        chkStartConnected.Checked = selected.SubItems[3].Text.Contains("是");
                    }
                    finally { nicListView.EndUpdate(); }
                }
                else
                {
                    lblTarget.Text = $"修改目标: {nicListView.SelectedItems.Count} 个网卡";
                }
            };

            btnAddNic.Click += (s, e) =>
            {
                string newPg = cbPg.Text.Trim();
                if (string.IsNullOrEmpty(newPg))
                {
                    MessageBox.Show("请先在上方选择新网卡要使用的端口组", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var confirmResult = MessageBox.Show(
                    $"确认要向 [{vmName}] 新增 1 个网卡（VirtualVmxnet3），端口组为 [{newPg}] 吗？",
                    "新增网卡确认",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Question);
                if (confirmResult != DialogResult.OK) return;

                string script =
                    $"$vm = Get-View -Id '{vmMoRef}' -Property Config.Hardware.Device -ErrorAction Stop | Select-Object -First 1; " +
                    $"$pgName = '{EscapePsString(newPg)}'; " +
                    "$pgNameLower = $pgName.ToLower(); " +
                    "$dvPg = Get-View -ViewType DistributedVirtualPortgroup -ErrorAction Stop | Where-Object { $_.Name.ToLower() -eq $pgNameLower } | Select-Object -First 1; " +
                    "$stdPg = $null; " +
                    "if (-not $dvPg) { $stdPg = Get-View -ViewType Network -ErrorAction Stop | Where-Object { $_.Name.ToLower() -eq $pgNameLower -and $_.MoRef.Type -eq 'Network' } | Select-Object -First 1 }; " +
                    "$switchUuid = $null; " +
                    "if ($dvPg) { " +
                    "  $switch = Get-View -ViewType DistributedVirtualSwitch -ErrorAction Stop | Where-Object { $_.Portgroup -and ($_.Portgroup | Where-Object { $_.Value -eq $dvPg.MoRef.Value }) } | Select-Object -First 1; " +
                    "  if ($switch) { $switchUuid = $switch.Uuid }; " +
                    "} elseif (-not $stdPg) { throw ('找不到端口组: ' + $pgName) }; " +
                    "$nic = New-Object VMware.Vim.VirtualVmxnet3; " +
                    "if ($dvPg) { " +
                    "  $nic.Backing = New-Object VMware.Vim.VirtualEthernetCardDistributedVirtualPortBackingInfo; " +
                    "  $nic.Backing.Port = New-Object VMware.Vim.DistributedVirtualSwitchPortConnection; " +
                    "  $nic.Backing.Port.PortgroupKey = $dvPg.MoRef.Value; " +
                    "  if ($switchUuid) { $nic.Backing.Port.SwitchUuid = $switchUuid }; " +
                    "} else { " +
                    "  $nic.Backing = New-Object VMware.Vim.VirtualEthernetCardNetworkBackingInfo; " +
                    "  $nic.Backing.DeviceName = $stdPg.Name; " +
                    "  $nic.Backing.Network = $stdPg.MoRef; " +
                    "}; " +
                    "$nic.Connectable = New-Object VMware.Vim.VirtualDeviceConnectInfo; " +
                    "$nic.Connectable.Connected = $true; " +
                    "$nic.Connectable.StartConnected = $true; " +
                    "$nic.Connectable.AllowGuestControl = $true; " +
                    "$ds = New-Object VMware.Vim.VirtualDeviceConfigSpec; " +
                    "$ds.Operation = [VMware.Vim.VirtualDeviceConfigSpecOperation]::add; " +
                    "$ds.Device = $nic; " +
                    "$cfg = New-Object VMware.Vim.VirtualMachineConfigSpec; " +
                    "$cfg.DeviceChange = @($ds); " +
                    "$task = $vm.ReconfigVM_Task($cfg); " +
                    "Wait-Task $task '新增网卡'; " +
                    $"Write-Output '【{EscapePsString(vmName)}】新增网卡成功 (VirtualVmxnet3 → {EscapePsString(newPg)})';";

                ExecuteVmOperation(script, $"新增网卡: {vmName} (→ {newPg})", vmMoRef, onSuccess: () => RefreshNicListAsync());
            };

            btnDeleteNic.Click += (s, e) =>
            {
                if (nicListView.SelectedItems.Count == 0) return;

                var selectedInfos = new List<(string label, string pg, string key)>();
                foreach (ListViewItem item in nicListView.SelectedItems)
                {
                    selectedInfos.Add((item.Text, item.SubItems[1].Text, item.SubItems[4].Text));
                }

                using var confirmDlg = new Form
                {
                    Text = "⚠ 删除网卡确认",
                    Icon = CoreUtils.GetAppIcon(),
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    StartPosition = FormStartPosition.CenterParent,
                    MaximizeBox = false,
                    MinimizeBox = false,
                    ShowInTaskbar = false,
                    Font = new Font("Microsoft YaHei", 9),
                    AutoScaleMode = AutoScaleMode.Dpi,
                    AutoScaleDimensions = new SizeF(96F, 96F),
                    Size = new Size(480, 400),
                    MinimumSize = new Size(480, 400),
                    BackColor = Color.White
                };

                var lblTitle = new Label
                {
                    Text = $"将从 [{vmName}] 删除以下 {selectedInfos.Count} 个网卡：",
                    Location = new Point(16, 16),
                    AutoSize = true,
                    Font = new Font("Microsoft YaHei", 10, FontStyle.Bold),
                    ForeColor = Color.FromArgb(183, 28, 28),
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                    Margin = Padding.Empty
                };

                var detailBox = new TextBox
                {
                    Location = new Point(16, 48),
                    Size = new Size(430, 180),
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical,
                    Font = new Font("Consolas", 9),
                    BackColor = Color.FromArgb(250, 250, 250),
                    ForeColor = Color.FromArgb(50, 50, 50),
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                    Margin = Padding.Empty
                };
                var sb = new StringBuilder();
                sb.AppendLine($"{"网卡名称",-22} {"端口组",-20} {"Key",8}");
                sb.AppendLine(new string('─', 55));
                foreach (var info in selectedInfos)
                {
                    sb.AppendLine($"{info.label,-22} {info.pg,-20} {info.key,8}");
                }
                detailBox.Text = sb.ToString();

                var chkConfirm = new CheckBox
                {
                    Text = "我已核对以上信息，确认删除（此操作无法撤销）",
                    Location = new Point(16, 240),
                    AutoSize = true,
                    Font = new Font("Microsoft YaHei", 9, FontStyle.Bold),
                    ForeColor = Color.FromArgb(183, 28, 28),
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                    Margin = Padding.Empty
                };

                var btnConfirmDelete = new Button
                {
                    Text = "确认删除",
                    Location = new Point(300, 320),
                    Size = new Size(90, 30),
                    Font = new Font("Microsoft YaHei", 9, FontStyle.Bold),
                    BackColor = Color.FromArgb(183, 28, 28),
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Enabled = false,
                    Anchor = AnchorStyles.Top | AnchorStyles.Right,
                    Margin = Padding.Empty
                };
                btnConfirmDelete.FlatAppearance.BorderSize = 0;

                var btnCancelDelete = new Button
                {
                    Text = "取消",
                    Location = new Point(395, 320),
                    Size = new Size(70, 30),
                    Font = new Font("Microsoft YaHei", 9),
                    Anchor = AnchorStyles.Top | AnchorStyles.Right,
                    Margin = Padding.Empty
                };

                chkConfirm.CheckedChanged += (cs, ce) => { btnConfirmDelete.Enabled = chkConfirm.Checked; };

                confirmDlg.Controls.AddRange(new Control[] { lblTitle, detailBox, chkConfirm, btnConfirmDelete, btnCancelDelete });
                confirmDlg.AcceptButton = btnConfirmDelete;
                confirmDlg.CancelButton = btnCancelDelete;

                btnConfirmDelete.Click += (cs, ce) => { confirmDlg.DialogResult = DialogResult.OK; confirmDlg.Close(); };
                btnCancelDelete.Click += (cs, ce) => { confirmDlg.DialogResult = DialogResult.Cancel; confirmDlg.Close(); };

                if (confirmDlg.ShowDialog(dlg) != DialogResult.OK) return;

                var targetKeys = selectedInfos.Select(i => i.key).ToList();

                string deleteScript =
                    $"$vm = Get-View -Id '{vmMoRef}' -Property Config.Hardware.Device -ErrorAction Stop | Select-Object -First 1; " +
                    $"$targetKeys = @({string.Join(",", targetKeys.Select(k => $"'{k}'"))}); " +
                    "$adapters = $vm.Config.Hardware.Device | Where-Object { $_ -is [VMware.Vim.VirtualEthernetCard] -and $_.Key -in $targetKeys }; " +
                    "if ($adapters.Count -ne $targetKeys.Count) { throw ('设备数量不匹配：预期 ' + $targetKeys.Count + ' 个，实际筛选到 ' + $adapters.Count + ' 个，请刷新后重试') }; " +
                    "$devSpecs = @(); " +
                    "foreach ($nic in $adapters) { " +
                    "  $ds = New-Object VMware.Vim.VirtualDeviceConfigSpec; " +
                    "  $ds.Operation = [VMware.Vim.VirtualDeviceConfigSpecOperation]::remove; " +
                    "  $ds.Device = $nic; " +
                    "  $devSpecs += $ds; " +
                    "}; " +
                    "$cfg = New-Object VMware.Vim.VirtualMachineConfigSpec; " +
                    "$cfg.DeviceChange = $devSpecs; " +
                    "$task = $vm.ReconfigVM_Task($cfg); " +
                    "Wait-Task $task '删除网卡'; " +
                    $"Write-Output '【{EscapePsString(vmName)}】已删除 {targetKeys.Count} 个网卡';";

                ExecuteVmOperation(deleteScript, $"删除网卡: {vmName} ({targetKeys.Count}个)", vmMoRef, onSuccess: () => RefreshNicListAsync());
            };

            if (dlg.ShowDialog(_root) != DialogResult.OK) return;

            if (nicListView.SelectedItems.Count == 0)
            {
                MessageBox.Show("请先在列表中选择要修改的网卡", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string newPg2 = cbPg.Text.Trim();
            if (string.IsNullOrEmpty(newPg2))
            {
                MessageBox.Show("请输入或选择目标端口组", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var selectedKeys2 = new List<string>();
            var selectedLabels2 = new List<string>();
            foreach (ListViewItem item in nicListView.SelectedItems)
            {
                selectedKeys2.Add(item.SubItems[4].Text);
                selectedLabels2.Add(item.Text);
            }

            string connectedVal = chkConnected.Checked ? "$true" : "$false";
            string startConnVal = chkStartConnected.Checked ? "$true" : "$false";

            string editScript =
                $"$vm = Get-View -Id '{vmMoRef}' -Property Config.Hardware.Device -ErrorAction Stop | Select-Object -First 1; " +
                $"$pgName = '{EscapePsString(newPg2)}'; " +
                "$pgNameLower = $pgName.ToLower(); " +
                "$dvPg = Get-View -ViewType DistributedVirtualPortgroup -ErrorAction Stop | Where-Object { $_.Name.ToLower() -eq $pgNameLower } | Select-Object -First 1; " +
                "$stdPg = $null; " +
                "if (-not $dvPg) { $stdPg = Get-View -ViewType Network -ErrorAction Stop | Where-Object { $_.Name.ToLower() -eq $pgNameLower -and $_.MoRef.Type -eq 'Network' } | Select-Object -First 1 }; " +
                "$switchUuid = $null; " +
                "if ($dvPg) { " +
                "  $switch = Get-View -ViewType DistributedVirtualSwitch -ErrorAction Stop | Where-Object { $_.Portgroup -and ($_.Portgroup | Where-Object { $_.Value -eq $dvPg.MoRef.Value }) } | Select-Object -First 1; " +
                "  if ($switch) { $switchUuid = $switch.Uuid }; " +
                "} elseif (-not $stdPg) { throw ('找不到端口组: ' + $pgName) }; " +
                "$devSpecs = @(); " +
                $"$targetKeys = @({string.Join(",", selectedKeys2.Select(k => $"'{k}'"))}); " +
                "$adapters = $vm.Config.Hardware.Device | Where-Object { $_ -is [VMware.Vim.VirtualEthernetCard] -and $_.Key -in $targetKeys }; " +
                "foreach ($nic in $adapters) { " +
                "  if ($dvPg) { " +
                "    $nic.Backing = New-Object VMware.Vim.VirtualEthernetCardDistributedVirtualPortBackingInfo; " +
                "    $nic.Backing.Port = New-Object VMware.Vim.DistributedVirtualSwitchPortConnection; " +
                "    $nic.Backing.Port.PortgroupKey = $dvPg.MoRef.Value; " +
                "    if ($switchUuid) { $nic.Backing.Port.SwitchUuid = $switchUuid }; " +
                "  } else { " +
                "    $nic.Backing = New-Object VMware.Vim.VirtualEthernetCardNetworkBackingInfo; " +
                "    $nic.Backing.DeviceName = $stdPg.Name; " +
                "    $nic.Backing.Network = $stdPg.MoRef; " +
                "  }; " +
                $"  $nic.Connectable.Connected = {connectedVal}; " +
                $"  $nic.Connectable.StartConnected = {startConnVal}; " +
                "  $ds = New-Object VMware.Vim.VirtualDeviceConfigSpec; " +
                "  $ds.Operation = [VMware.Vim.VirtualDeviceConfigSpecOperation]::edit; " +
                "  $ds.Device = $nic; " +
                "  $devSpecs += $ds; " +
                "}; " +
                "$cfg = New-Object VMware.Vim.VirtualMachineConfigSpec; " +
                "$cfg.DeviceChange = $devSpecs; " +
                "$task = $vm.ReconfigVM_Task($cfg); " +
                "Wait-Task $task '端口组修改'; " +
                $"Write-Output '【{EscapePsString(vmName)}】{selectedLabels2.Count} 个网卡已切换到端口组 ''{EscapePsString(newPg2)}''  (Connected={chkConnected.Checked}, StartConnected={chkStartConnected.Checked})';";

            ExecuteVmOperation(editScript, $"修改端口组: {vmName} ({selectedLabels2.Count}网卡 → {newPg2})", vmMoRef);
        }

        private void ShowDiskEditorDialog(string vmName, string vmMoRef, List<Dictionary<string, string>> diskList)
        {
            using var dlg = new Form
            {
                Text = $"磁盘管理 - {vmName}",
                Icon = CoreUtils.GetAppIcon(),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                Font = new Font("Microsoft YaHei", 9),
                AutoScaleMode = AutoScaleMode.Dpi,
                AutoScaleDimensions = new SizeF(96F, 96F),
                Size = new Size(700, 470),
                MinimumSize = new Size(700, 470)
            };

            var listView = new ListView
            {
                Location = new Point(16, 16),
                Size = new Size(650, 200),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                Margin = Padding.Empty,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false
            };
            listView.Columns.Add("磁盘名称", 100);
            listView.Columns.Add("容量 (GB)", 100);
            listView.Columns.Add("置备模式", 100);
            listView.Columns.Add("存储路径", 330);

            // 自适应列宽
            Utils.TableConvertUtils.AutoResizeColumns(listView);

            foreach (var disk in diskList)
            {
                var name = disk.TryGetValue("Name", out var n) ? n : "";
                var cap = disk.TryGetValue("CapacityGB", out var c) ? c : "";
                var fmt = disk.TryGetValue("StorageFormat", out var f) ? f : "";
                var file = disk.TryGetValue("Filename", out var fi) ? fi : "";
                var key = disk.TryGetValue("Key", out var k) ? k : "";
                var hasBacking = disk.TryGetValue("HasBacking", out var hb) ? hb : "True";

                if (double.TryParse(cap, out double capVal))
                    cap = capVal.ToString("F2");

                var displayName = string.IsNullOrEmpty(name) ? "(未命名磁盘)" : name;
                if (hasBacking.Equals("False", StringComparison.OrdinalIgnoreCase))
                    displayName += " ⚠";

                var item = new ListViewItem(displayName);
                item.SubItems.Add(cap);
                item.SubItems.Add(fmt);
                item.SubItems.Add(file);
                item.SubItems.Add(key);
                listView.Items.Add(item);
            }

            var lblStatus = new Label
            {
                Text = diskList.Count == 0 ? "⚠ 未查询到任何磁盘，请确认虚拟机状态后重试" : $"✓ 已加载 {diskList.Count} 块磁盘",
                Location = new Point(16, 215),
                AutoSize = true,
                Font = new Font("Microsoft YaHei", 9),
                ForeColor = diskList.Count == 0 ? Color.FromArgb(211, 47, 47) : Color.FromArgb(46, 125, 50),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Margin = Padding.Empty
            };

            // 4列 TableLayoutPanel：标签 | 输入框 | 下拉框(置备模式) | 按钮
            var table = new TableLayoutPanel
            {
                Location = new Point(16, 238),
                Size = new Size(650, 105),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Margin = Padding.Empty,
                RowCount = 3,
                ColumnCount = 4
            };

            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));

            // 第1行：新增磁盘
            var lblAdd = new Label { Text = "新增磁盘容量 (GB)：", AutoSize = true, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
            var cboFormat = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = Padding.Empty };
            cboFormat.Items.AddRange(new[] { "Thin", "Thick", "LazyZeroedThick" });
            cboFormat.SelectedIndex = 0;
            var txtNewDisk = new TextBox { Text = "100", Width = 70, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = Padding.Empty };
            var btnAdd = new Button { Text = "新增磁盘", Width = 110, Anchor = AnchorStyles.Left, Margin = Padding.Empty };

            table.Controls.Add(lblAdd, 0, 0);
            table.Controls.Add(cboFormat, 1, 0);
            table.Controls.Add(txtNewDisk, 2, 0);
            table.Controls.Add(btnAdd, 3, 0);

            // 第2行：调整大小
            var lblResize = new Label { Text = "调整磁盘的总容量 (GB)：", AutoSize = true, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
            var lblCurrentCap = new Label { Text = "", AutoSize = true, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
            var txtResize = new TextBox { Text = "", Width = 70, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = Padding.Empty };
            var btnResize = new Button { Text = "📏 调整", Width = 110, Anchor = AnchorStyles.Left, Margin = Padding.Empty };

            table.Controls.Add(lblResize, 0, 1);
            table.Controls.Add(lblCurrentCap, 1, 1);
            table.Controls.Add(txtResize, 2, 1);
            table.Controls.Add(btnResize, 3, 1);

            // 第3行：删除磁盘
            var lblDelete = new Label { Text = "删除选中磁盘：", AutoSize = true, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
            var chkDeleteFiles = new CheckBox { Text = "同时删除存储文件", AutoSize = true, Anchor = AnchorStyles.Left, Margin = Padding.Empty, Checked = true };
            var btnDelete = new Button { Text = "🗑 删除磁盘", Width = 110, Anchor = AnchorStyles.Left, Margin = Padding.Empty, BackColor = Color.FromArgb(220, 53, 69), ForeColor = Color.White };

            table.Controls.Add(lblDelete, 0, 2);
            table.Controls.Add(chkDeleteFiles, 1, 2);
            table.Controls.Add(btnDelete, 3, 2);

            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, Height = 44,
                Padding = new Padding(0, 6, 0, 6),
                BackColor = Color.FromArgb(240, 240, 240)
            };
            var btnClose = new Button { Text = "关闭", DialogResult = DialogResult.OK, Size = new Size(80, 28), Font = new Font("Microsoft YaHei", 9) };
            btnPanel.Controls.Add(btnClose);

            CenterFlowPanel(btnPanel);

            dlg.Controls.Add(btnPanel);
            dlg.Controls.AddRange(new Control[] { listView, lblStatus, table });
            dlg.AcceptButton = btnClose;
            dlg.CancelButton = btnClose;

            listView.SelectedIndexChanged += (s, e) =>
            {
                if (listView.SelectedItems.Count > 0)
                {
                    var capText = listView.SelectedItems[0].SubItems[1].Text;
                    lblCurrentCap.Text = capText;
                }
                else
                {
                    lblCurrentCap.Text = "";
                }
            };

            void RefreshDiskListAsync()
            {
                Task.Run(() =>
                {
                    List<Dictionary<string, string>> newDiskList = new List<Dictionary<string, string>>();
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
                                var info = new Dictionary<string, string>();
                                if (dict.TryGetValue("Key", out var ko)) info["Key"] = ko?.ToString()?.Trim() ?? "";
                                if (dict.TryGetValue("Name", out var no)) info["Name"] = no?.ToString()?.Trim() ?? "";
                                if (dict.TryGetValue("CapacityGB", out var co)) info["CapacityGB"] = co?.ToString()?.Trim() ?? "";
                                if (dict.TryGetValue("StorageFormat", out var fo)) info["StorageFormat"] = fo?.ToString()?.Trim() ?? "";
                                if (dict.TryGetValue("Filename", out var fio)) info["Filename"] = fio?.ToString()?.Trim() ?? "";
                                if (dict.TryGetValue("HasBacking", out var hbo)) info["HasBacking"] = hbo?.ToString()?.Trim() ?? "";
                                newDiskList.Add(info);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogMessage("error", $"[磁盘管理] 刷新磁盘列表异常: {ex.Message}");
                    }

                    _root.Invoke(new Action(() =>
                    {
                        if (dlg.IsDisposed) return;
                        listView.BeginUpdate();
                        listView.Items.Clear();
                        foreach (var disk in newDiskList)
                        {
                            var name = disk.TryGetValue("Name", out var n) ? n : "";
                            var cap = disk.TryGetValue("CapacityGB", out var c) ? c : "";
                            var fmt = disk.TryGetValue("StorageFormat", out var f) ? f : "";
                            var file = disk.TryGetValue("Filename", out var fi) ? fi : "";
                            var key = disk.TryGetValue("Key", out var k) ? k : "";
                            var hasBacking = disk.TryGetValue("HasBacking", out var hb) ? hb : "True";
                            if (double.TryParse(cap, out double capVal)) cap = capVal.ToString("F2");
                            var displayName = string.IsNullOrEmpty(name) ? "(未命名磁盘)" : name;
                            if (hasBacking.Equals("False", StringComparison.OrdinalIgnoreCase)) displayName += " ⚠";
                            var item = new ListViewItem(displayName);
                            item.SubItems.Add(cap);
                            item.SubItems.Add(fmt);
                            item.SubItems.Add(file);
                            item.SubItems.Add(key);
                            listView.Items.Add(item);
                        }
                        Utils.TableConvertUtils.AutoResizeColumns(listView);
                        listView.EndUpdate();

                        lblStatus.Text = newDiskList.Count == 0
                            ? "⚠ 未查询到任何磁盘，请确认虚拟机状态后重试"
                            : $"✓ 已加载 {newDiskList.Count} 块磁盘";
                        lblStatus.ForeColor = newDiskList.Count == 0
                            ? Color.FromArgb(211, 47, 47)
                            : Color.FromArgb(46, 125, 50);

                        lblCurrentCap.Text = "";
                    }));
                });
            }

            btnAdd.Click += (s, e) =>
            {
                if (!int.TryParse(txtNewDisk.Text.Trim(), out int newSize) || newSize < 1 || newSize > 65536)
                {
                    MessageBox.Show("请输入 1~65536 之间的整数", "输入错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string storageFormat = cboFormat.SelectedItem?.ToString() ?? "Thin";

                var confirmResult = MessageBox.Show(
                    $"确认要为 [{vmName}] 新增 {newSize} GB 虚拟磁盘吗？\n\n" +
                    $"⚠ 置备模式：{storageFormat}\n" +
                    "新增后需要在客户机内初始化并格式化",
                    "新增磁盘确认",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning);

                if (confirmResult != DialogResult.OK) return;

                string thinFlag = storageFormat.Equals("Thin", StringComparison.OrdinalIgnoreCase) ? "$true" : "$false";
                string eagerFlag = storageFormat.Equals("EagerZeroedThick", StringComparison.OrdinalIgnoreCase) ? "$true" : "$false";
                string script =
                    $"{PsWaitTaskFunc}" +
                    $"$ErrorActionPreference = 'Stop'; " +
                    $"$vm = Get-View -Id '{vmMoRef}' -Property Name,Runtime.PowerState,Config.Hardware.Device -ErrorAction Stop | Select-Object -First 1; " +
                    "Write-Host ('=== VM 硬件诊断 ==='); " +
                    "foreach ($d in $vm.Config.Hardware.Device) { $t = $d.GetType().Name; if ($t -like '*Controller*' -or $t -like '*Disk*') { Write-Host ('  [' + $t + '] Key=' + $d.Key + ' ' + $(if ($d -is [VMware.Vim.VirtualController]) { 'BusNumber=' + $d.BusNumber } else { '' }) + ' ' + $(if ($d -is [VMware.Vim.VirtualDisk]) { 'CtrlKey=' + $d.ControllerKey + ' Unit=' + $d.UnitNumber + ' Label=' + $d.DeviceInfo.Label } else { '' })) } }; " +
                    "$allScsi = @($vm.Config.Hardware.Device | Where-Object { $_ -is [VMware.Vim.VirtualSCSIController] }); " +
                    "if ($allScsi.Count -eq 0) { throw '未找到 SCSI 控制器' }; " +
                    "$allDisks = @($vm.Config.Hardware.Device | Where-Object { $_ -is [VMware.Vim.VirtualDisk] -and $_.Backing }); " +
                    "if ($allDisks.Count -eq 0) { throw 'VM 无有效系统盘' }; " +
                    "$scsiCtrl = $null; $sysDisk = $null; " +
                    "foreach ($ctrl in ($allScsi | Sort-Object BusNumber, Key)) { $disksOnCtrl = @($allDisks | Where-Object { $_.ControllerKey -eq $ctrl.Key }); if ($disksOnCtrl.Count -gt 0) { $scsiCtrl = $ctrl; $sysDisk = $disksOnCtrl[0]; break } }; " +
                    "if (-not $scsiCtrl) { throw '所有 SCSI 控制器上均无磁盘' }; " +
                    "Write-Host ('选中 SCSI 控制器 Type=' + $scsiCtrl.GetType().Name + ' Key=' + $scsiCtrl.Key + ' BusNumber=' + $scsiCtrl.BusNumber); " +
                    "Write-Host ('系统盘 Key=' + $sysDisk.Key + ' CtrlKey=' + $sysDisk.ControllerKey + ' Unit=' + $sysDisk.UnitNumber + ' DS=' + $sysDisk.Backing.Datastore); " +
                    "$scsiDisks = @($allDisks | Where-Object { $_.ControllerKey -eq $scsiCtrl.Key }); " +
                    "$usedUnits = @($scsiDisks | ForEach-Object { [int]$_.UnitNumber }); " +
                    "$nextUnit = 0; foreach ($u in $usedUnits) { if ($u -ge $nextUnit) { $nextUnit = $u + 1 } }; " +
                    $"$newSizeGB = [int64]{newSize}; " +
                    "$newDisk = New-Object VMware.Vim.VirtualDisk; " +
                    "$newDisk.Key = -1; " +
                    "$newDisk.ControllerKey = $scsiCtrl.Key; " +
                    "$newDisk.UnitNumber = $nextUnit; " +
                    "$newDisk.CapacityInBytes = $newSizeGB * 1GB; " +
                    "$newDisk.DeviceInfo = New-Object VMware.Vim.Description; " +
                    "$newDisk.DeviceInfo.Label = ('Hard disk ' + ($scsiDisks.Count + 1)); " +
                    "$newDisk.DeviceInfo.Summary = ('Hard disk ' + ($scsiDisks.Count + 1)); " +
                    "$backing = New-Object VMware.Vim.VirtualDiskFlatVer2BackingInfo; " +
                    "$backing.DiskMode = [VMware.Vim.VirtualDiskMode]::persistent; " +
                    "$backing.ThinProvisioned = $thinFlag; " +
                    "$backing.EagerlyScrub = $eagerFlag; " +
                    "$backing.Datastore = $sysDisk.Backing.Datastore; " +
                    "$backing.FileName = ''; " +
                    "$newDisk.Backing = $backing; " +
                    "$ds = New-Object VMware.Vim.VirtualDeviceConfigSpec; " +
                    "$ds.Operation = [VMware.Vim.VirtualDeviceConfigSpecOperation]::add; " +
                    "$ds.FileOperation = [VMware.Vim.VirtualDeviceConfigSpecFileOperation]::create; " +
                    "$ds.Device = $newDisk; " +
                    "$cfg = New-Object VMware.Vim.VirtualMachineConfigSpec; " +
                    "$cfg.DeviceChange = @($ds); " +
                    "Write-Host ('准备新增磁盘: CtrlKey=' + $newDisk.ControllerKey + ' Unit=' + $newDisk.UnitNumber + ' Size=' + $newDisk.CapacityInBytes + 'B Thin=' + $backing.ThinProvisioned); " +
                    "$task = $vm.ReconfigVM_Task($cfg); Wait-Task $task '新增磁盘' " +
                    $"Write-Output '【{EscapePsString(vmName)}】已新增 {newSize} GB 虚拟磁盘（{storageFormat}）'";

                ExecuteVmOperation(script, $"新增磁盘: {vmName} → {newSize} GB ({storageFormat})", onSuccess: () =>
                {
                    RefreshDiskListAsync();
                });
            };

            btnResize.Click += (s, e) =>
            {
                if (listView.SelectedItems.Count == 0)
                {
                    ToastUtils.ShowToast(dlg, "请先在列表中选中要调整的磁盘");
                    return;
                }

                var diskName = listView.SelectedItems[0].SubItems[0].Text;
                var currentCap = listView.SelectedItems[0].SubItems[1].Text;
                var diskPath = listView.SelectedItems[0].SubItems[3].Text;
                var diskKey = listView.SelectedItems[0].SubItems[4].Text;

                if (!double.TryParse(txtResize.Text.Trim(), out double newSize) || newSize < 1 || newSize > 65536)
                {
                    MessageBox.Show("请输入 1~65536 之间的有效数值", "输入错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (double.TryParse(currentCap, out double currentVal) && newSize <= currentVal)
                {
                    MessageBox.Show($"新容量必须大于当前容量（{currentCap} GB）\n磁盘只能调大，不能调小", "输入错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (diskPath.Contains("-000") && diskPath.Contains(".vmdk"))
                {
                    MessageBox.Show(
                        $"磁盘 [{diskName}] 是快照增量磁盘（Delta Disk），无法直接调整大小。\n\n" +
                        "请先合并/删除所有快照后，再尝试调整磁盘大小。",
                        "无法调整",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                var confirmResult = MessageBox.Show(
                    $"确认要将 [{vmName}] 的磁盘 [{diskName}] 调整为 {newSize} GB 吗？\n\n" +
                    "⚠ 此操作不可逆，请确保客户机内文件系统支持在线扩容",
                    "调整磁盘大小确认",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning);

                if (confirmResult != DialogResult.OK) return;

                string script =
                    $"{PsWaitTaskFunc}" +
                    $"$vm = Get-View -Id '{vmMoRef}' -Property Config.Hardware.Device -ErrorAction Stop | Select-Object -First 1; " +
                    $"$hd = $vm.Config.Hardware.Device | Where-Object {{ $_ -is [VMware.Vim.VirtualDisk] -and $_.Key -eq {diskKey} }} | Select-Object -First 1; " +
                    "if (-not $hd) { throw \"未找到磁盘 Key=$diskKey，请刷新后重试\" }; " +
                    $"$hd.CapacityInBytes = [int64]$([int64]{newSize.ToString(System.Globalization.CultureInfo.InvariantCulture)} * 1GB); " +
                    "$ds = New-Object VMware.Vim.VirtualDeviceConfigSpec; " +
                    "$ds.Operation = [VMware.Vim.VirtualDeviceConfigSpecOperation]::edit; " +
                    "$ds.Device = $hd; " +
                    "$cfg = New-Object VMware.Vim.VirtualMachineConfigSpec; " +
                    "$cfg.DeviceChange = @($ds); " +
                    "$task = $vm.ReconfigVM_Task($cfg); Wait-Task $task '磁盘扩容' " +
                    $"Write-Output '【{EscapePsString(vmName)}】磁盘 [{EscapePsString(diskName)}] 已扩容至 {newSize} GB'";

                ExecuteVmOperation(script, $"磁盘扩容: {vmName} [{diskName}] → {newSize} GB", onSuccess: () =>
                {
                    RefreshDiskListAsync();
                });
            };

            btnDelete.Click += (s, e) =>
            {
                if (listView.SelectedItems.Count == 0)
                {
                    ToastUtils.ShowToast(dlg, "请先在列表中选中要删除的磁盘");
                    return;
                }

                var diskName = listView.SelectedItems[0].SubItems[0].Text;
                var diskKey = listView.SelectedItems[0].SubItems[4].Text;
                bool deleteFiles = chkDeleteFiles.Checked;

                string extraMsg = deleteFiles
                    ? "\n\n⚠ 将同时删除 .vmdk 存储文件（不可恢复）"
                    : "\n\n⚠ 仅从虚拟机移除，保留 .vmdk 文件";

                var confirmResult = MessageBox.Show(
                    $"确认要从 [{vmName}] 删除磁盘 [{diskName}] 吗？{extraMsg}",
                    "删除磁盘确认",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning);

                if (confirmResult != DialogResult.OK) return;

                string fileOp = deleteFiles
                    ? "[VMware.Vim.VirtualDeviceConfigSpecFileOperation]::destroy"
                    : "[VMware.Vim.VirtualDeviceConfigSpecFileOperation]::keep";

                string script =
                    $"{PsWaitTaskFunc}" +
                    $"$vm = Get-View -Id '{vmMoRef}' -Property Config.Hardware.Device -ErrorAction Stop | Select-Object -First 1; " +
                    $"$hd = $vm.Config.Hardware.Device | Where-Object {{ $_ -is [VMware.Vim.VirtualDisk] -and $_.Key -eq {diskKey} }} | Select-Object -First 1; " +
                    "if (-not $hd) { throw \"未找到磁盘 Key=$diskKey，请刷新后重试\" }; " +
                    "$ds = New-Object VMware.Vim.VirtualDeviceConfigSpec; " +
                    "$ds.Operation = [VMware.Vim.VirtualDeviceConfigSpecOperation]::remove; " +
                    "$ds.FileOperation = " + fileOp + "; " +
                    "$ds.Device = $hd; " +
                    "$cfg = New-Object VMware.Vim.VirtualMachineConfigSpec; " +
                    "$cfg.DeviceChange = @($ds); " +
                    "$task = $vm.ReconfigVM_Task($cfg); Wait-Task $task '删除磁盘' " +
                    $"Write-Output '【{EscapePsString(vmName)}】磁盘 [{EscapePsString(diskName)}] 已删除（删除文件={deleteFiles}）'";

                ExecuteVmOperation(script, $"删除磁盘: {vmName} [{diskName}] (删除文件={deleteFiles})", onSuccess: () =>
                {
                    RefreshDiskListAsync();
                });
            };

            dlg.ShowDialog(_root);
        }

        private void ShowMigrateHostDialog(string vmName, string vmMoRef, List<string> hostList, string? curHostName)
        {
            string? targetHost = null;
            if (hostList.Count > 0)
            {
                using var dlg = new Form
                {
                    Text = "vMotion 迁移到指定主机",
                    Icon = CoreUtils.GetAppIcon(),
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    StartPosition = FormStartPosition.CenterParent,
                    MaximizeBox = false,
                    MinimizeBox = false,
                    ShowInTaskbar = false,
                    Font = new Font("Microsoft YaHei", 9),
                    AutoScaleMode = AutoScaleMode.Dpi,
                    AutoScaleDimensions = new SizeF(96F, 96F),
                    Size = new Size(400, 200),
                    MinimumSize = new Size(400, 200)
                };

                var lblCur = new Label { Text = $"当前主机：{curHostName ?? "?"}", Location = new Point(16, 12), AutoSize = true, ForeColor = Color.DimGray };
                var lblPrompt = new Label { Text = "选择目标主机：", Location = new Point(16, 36), AutoSize = true };
                var combo = new ComboBox { Location = new Point(16, 60), Width = 350, DropDownStyle = ComboBoxStyle.DropDownList };
                combo.Items.AddRange(hostList.ToArray());
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
                dlg.Controls.AddRange(new Control[] { lblCur, lblPrompt, combo });
                dlg.AcceptButton = btnOk;
                dlg.CancelButton = btnCancel;

                if (dlg.ShowDialog(_root) == DialogResult.OK)
                {
                    targetHost = combo.SelectedItem?.ToString();
                }
            }
            else
            {
                using var dlg = new Form
                {
                    Text = "vMotion 迁移到指定主机",
                    Icon = CoreUtils.GetAppIcon(),
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    StartPosition = FormStartPosition.CenterParent,
                    MaximizeBox = false,
                    MinimizeBox = false,
                    ShowInTaskbar = false,
                    Font = new Font("Microsoft YaHei", 9),
                    AutoScaleMode = AutoScaleMode.Dpi,
                    AutoScaleDimensions = new SizeF(96F, 96F),
                    Size = new Size(500, 200),
                    MinimumSize = new Size(500, 200)
                };

                var lblCur = new Label { Text = $"当前主机：{curHostName ?? "?"}", Location = new Point(16, 12), AutoSize = true, ForeColor = Color.DimGray };
                var lblPrompt = new Label
                {
                    Text = "请输入目标 ESXi 主机的名称（FQDN 或 IP）：",
                    Location = new Point(16, 36),
                    AutoSize = true
                };
                var txtHost = new TextBox { Location = new Point(16, 60), Width = 450 };

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
                dlg.Controls.AddRange(new Control[] { lblCur, lblPrompt, txtHost });
                dlg.AcceptButton = btnOk;
                dlg.CancelButton = btnCancel;

                if (dlg.ShowDialog(_root) == DialogResult.OK)
                {
                    targetHost = txtHost.Text.Trim();
                }
            }

            if (string.IsNullOrEmpty(targetHost)) return;

            var confirmResult = MessageBox.Show(
                $"确认要将 [{vmName}] 迁移到主机 [{targetHost}] 吗？\n\n" +
                "⚠ 若 VM 处于开机状态将执行 vMotion（零停机）\n" +
                "此操作具有破坏性，请谨慎操作",
                "迁移到指定主机确认",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Error);

            if (confirmResult != DialogResult.OK) return;

            string script =
                $"$vm = Get-View -Id '{vmMoRef}' -ErrorAction Stop; " +
                $"$targetHost = Get-View -ViewType HostSystem -Filter @{{Name='{EscapePsString(targetHost)}'}} -ErrorAction SilentlyContinue; " +
                "if (-not $targetHost) { " +
                $"  Write-Output '【错误】未找到目标主机【{EscapePsString(targetHost)}】，请检查名称拼写并确保其已接入 vCenter' " +
                "} else { " +
                "  $spec = New-Object VMware.Vim.VirtualMachineRelocateSpec;  $spec.Host = $targetHost.MoRef;  $task = $vm.RelocateVM_Task($spec, [VMware.Vim.VirtualMachineMovePriority]::defaultPriority); " +
                "  Wait-Task $task '迁移主机' " +
                $"  Write-Output '【{EscapePsString(vmName)}】已迁移到主机【{EscapePsString(targetHost)}】' " +
                "}";

            ExecuteVmOperation(script, $"迁移到主机: {vmName} → {targetHost}", vmName);
        }

        private void ShowMigrateResourcePoolDialog(string vmName, string vmMoRef, List<string> rpList, string? curRpName)
        {
            string? targetPool = null;
            if (rpList.Count > 0)
            {
                using var dlg = new Form
                {
                    Text = "迁移到指定资源池",
                    Icon = CoreUtils.GetAppIcon(),
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    StartPosition = FormStartPosition.CenterParent,
                    MaximizeBox = false,
                    MinimizeBox = false,
                    ShowInTaskbar = false,
                    Font = new Font("Microsoft YaHei", 9),
                    AutoScaleMode = AutoScaleMode.Dpi,
                    AutoScaleDimensions = new SizeF(96F, 96F),
                    Size = new Size(450, 200),
                    MinimumSize = new Size(450, 200)
                };

                var lblCur = new Label { Text = $"当前资源池：{curRpName ?? "?"}", Location = new Point(16, 12), AutoSize = true, ForeColor = Color.DimGray };
                var lblPrompt = new Label { Text = "选择目标资源池：", Location = new Point(16, 36), AutoSize = true };
                var combo = new ComboBox { Location = new Point(16, 60), Width = 400, DropDownStyle = ComboBoxStyle.DropDownList };
                combo.Items.AddRange(rpList.ToArray());
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
                dlg.Controls.AddRange(new Control[] { lblCur, lblPrompt, combo });
                dlg.AcceptButton = btnOk;
                dlg.CancelButton = btnCancel;

                if (dlg.ShowDialog(_root) == DialogResult.OK)
                    targetPool = combo.SelectedItem?.ToString();
            }
            else
            {
                using var dlg = new Form
                {
                    Text = "迁移到指定资源池",
                    Icon = CoreUtils.GetAppIcon(),
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    StartPosition = FormStartPosition.CenterParent,
                    MaximizeBox = false,
                    MinimizeBox = false,
                    ShowInTaskbar = false,
                    Font = new Font("Microsoft YaHei", 9),
                    AutoScaleMode = AutoScaleMode.Dpi,
                    AutoScaleDimensions = new SizeF(96F, 96F),
                    Size = new Size(500, 200),
                    MinimumSize = new Size(500, 200)
                };

                var lblCur = new Label { Text = $"当前资源池：{curRpName ?? "?"}", Location = new Point(16, 12), AutoSize = true, ForeColor = Color.DimGray };
                var lblPrompt = new Label { Text = "请输入目标资源池名称：", Location = new Point(16, 36), AutoSize = true };
                var txtPool = new TextBox { Location = new Point(16, 60), Width = 450 };

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
                dlg.Controls.AddRange(new Control[] { lblCur, lblPrompt, txtPool });
                dlg.AcceptButton = btnOk;
                dlg.CancelButton = btnCancel;

                if (dlg.ShowDialog(_root) == DialogResult.OK)
                    targetPool = txtPool.Text.Trim();
            }

            if (string.IsNullOrEmpty(targetPool)) return;

            var confirmResult = MessageBox.Show(
                $"确认要将 [{vmName}] 迁移到资源池 [{targetPool}] 吗？\n\n" +
                "⚠ 开机状态执行 vMotion（零停机）",
                "迁移到资源池确认",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Error);

            if (confirmResult != DialogResult.OK) return;

            string script =
                $"$vm = Get-View -Id '{vmMoRef}' -ErrorAction Stop; " +
                $"$targetPool = Get-View -ViewType ResourcePool -Filter @{{Name='{EscapePsString(targetPool)}'}} -ErrorAction SilentlyContinue; " +
                "if (-not $targetPool) { " +
                $"  Write-Output '【错误】未找到目标资源池【{EscapePsString(targetPool)}】' " +
                "} else { " +
                "  $spec = New-Object VMware.Vim.VirtualMachineRelocateSpec;  $spec.Pool = $targetPool.MoRef;  $task = $vm.RelocateVM_Task($spec, [VMware.Vim.VirtualMachineMovePriority]::defaultPriority); " +
                "  Wait-Task $task '迁移主机' " +
                $"  Write-Output '【{EscapePsString(vmName)}】已迁移到资源池【{EscapePsString(targetPool)}】' " +
                "}";

            ExecuteVmOperation(script, $"迁移到资源池: {vmName} → {targetPool}", vmName);
        }

        private void ShowMigrateDatastoreDialog(string vmName, string vmMoRef, List<string> dsList, string? curDsName)
        {
            string? targetDs = null;
            if (dsList.Count > 0)
            {
                using var dlg = new Form
                {
                    Text = "Storage vMotion 迁移存储",
                    Icon = CoreUtils.GetAppIcon(),
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    StartPosition = FormStartPosition.CenterParent,
                    MaximizeBox = false,
                    MinimizeBox = false,
                    ShowInTaskbar = false,
                    Font = new Font("Microsoft YaHei", 9),
                    AutoScaleMode = AutoScaleMode.Dpi,
                    AutoScaleDimensions = new SizeF(96F, 96F),
                    Size = new Size(450, 200),
                    MinimumSize = new Size(450, 200)
                };

                var lblCur = new Label { Text = $"当前存储：{curDsName ?? "?"}", Location = new Point(16, 12), AutoSize = true, ForeColor = Color.DimGray };
                var lblPrompt = new Label { Text = "选择目标存储：", Location = new Point(16, 36), AutoSize = true };
                var combo = new ComboBox { Location = new Point(16, 60), Width = 400, DropDownStyle = ComboBoxStyle.DropDownList };
                combo.Items.AddRange(dsList.ToArray());
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
                dlg.Controls.AddRange(new Control[] { lblCur, lblPrompt, combo });
                dlg.AcceptButton = btnOk;
                dlg.CancelButton = btnCancel;

                if (dlg.ShowDialog(_root) == DialogResult.OK)
                    targetDs = combo.SelectedItem?.ToString();
            }
            else
            {
                using var dlg = new Form
                {
                    Text = "Storage vMotion 迁移存储",
                    Icon = CoreUtils.GetAppIcon(),
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    StartPosition = FormStartPosition.CenterParent,
                    MaximizeBox = false,
                    MinimizeBox = false,
                    ShowInTaskbar = false,
                    Font = new Font("Microsoft YaHei", 9),
                    AutoScaleMode = AutoScaleMode.Dpi,
                    AutoScaleDimensions = new SizeF(96F, 96F),
                    Size = new Size(500, 200),
                    MinimumSize = new Size(500, 200)
                };

                var lblCur = new Label { Text = $"当前存储：{curDsName ?? "?"}", Location = new Point(16, 12), AutoSize = true, ForeColor = Color.DimGray };
                var lblPrompt = new Label { Text = "请输入目标存储名称：", Location = new Point(16, 36), AutoSize = true };
                var txtDs = new TextBox { Location = new Point(16, 60), Width = 450 };

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
                dlg.Controls.AddRange(new Control[] { lblCur, lblPrompt, txtDs });
                dlg.AcceptButton = btnOk;
                dlg.CancelButton = btnCancel;

                if (dlg.ShowDialog(_root) == DialogResult.OK)
                    targetDs = txtDs.Text.Trim();
            }

            if (string.IsNullOrEmpty(targetDs)) return;

            var confirmResult = MessageBox.Show(
                $"确认要将 [{vmName}] 迁移到存储 [{targetDs}] 吗？\n\n" +
                "⚠ Storage vMotion 会在后台迁移磁盘\n" +
                "开机状态也可执行，不影响业务",
                "迁移存储确认",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Error);

            if (confirmResult != DialogResult.OK) return;

            string script =
                $"$vm = Get-View -Id '{vmMoRef}' -ErrorAction Stop; " +
                $"$targetDs = Get-View -ViewType Datastore -Filter @{{Name='{EscapePsString(targetDs)}'}} -ErrorAction SilentlyContinue; " +
                "if (-not $targetDs) { " +
                $"  Write-Output '【错误】未找到目标存储【{EscapePsString(targetDs)}】' " +
                "} else { " +
                "  $spec = New-Object VMware.Vim.VirtualMachineRelocateSpec;  $spec.Datastore = $targetDs.MoRef;  $task = $vm.RelocateVM_Task($spec, [VMware.Vim.VirtualMachineMovePriority]::defaultPriority); " +
                "  Wait-Task $task '迁移主机' " +
                $"  Write-Output '【{EscapePsString(vmName)}】已迁移到存储【{EscapePsString(targetDs)}】' " +
                "}";

            ExecuteVmOperation(script, $"迁移存储: {vmName} → {targetDs}", vmName);
        }

        private void ShowCloneDialog(string sourceVmName, string sourceVmMoRef, List<string> hostList, List<string> dsList, string? curHost, string? curDs)
        {
            string defaultName = sourceVmName + "_clone";
            string? newName = null;
            string? selectedHost = null;
            string? selectedDs = null;

            using var dlg = new Form
            {
                Text = $"克隆虚拟机：{sourceVmName}",
                Icon = CoreUtils.GetAppIcon(),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                Font = new Font("Microsoft YaHei", 9),
                AutoScaleMode = AutoScaleMode.Dpi,
                AutoScaleDimensions = new SizeF(96F, 96F),
                Size = new Size(500, 280),
                MinimumSize = new Size(500, 280)
            };

            var lblName = new Label { Text = "新虚拟机名称：", Location = new Point(16, 14), AutoSize = true };
            var txtName = new TextBox { Location = new Point(16, 38), Width = 450, Text = defaultName };

            var lblHostCur = new Label { Text = $"当前主机：{curHost ?? "?"}", Location = new Point(16, 74), AutoSize = true, ForeColor = Color.DimGray };
            var lblHostTarget = new Label { Text = "目标主机：", Location = new Point(16, 96), AutoSize = true };
            var comboHost = new ComboBox { Location = new Point(94, 92), Width = 372, DropDownStyle = ComboBoxStyle.DropDownList };
            comboHost.Items.Add("（克隆到原主机）");
            comboHost.SelectedIndex = 0;
            foreach (var h in hostList) comboHost.Items.Add(h);

            var lblDsCur = new Label { Text = $"当前存储：{curDs ?? "?"}", Location = new Point(16, 130), AutoSize = true, ForeColor = Color.DimGray };
            var lblDsTarget = new Label { Text = "目标存储：", Location = new Point(16, 152), AutoSize = true };
            var comboDs = new ComboBox { Location = new Point(94, 148), Width = 372, DropDownStyle = ComboBoxStyle.DropDownList };
            comboDs.Items.Add("（克隆到原存储）");
            comboDs.SelectedIndex = 0;
            foreach (var d in dsList) comboDs.Items.Add(d);

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
            dlg.Controls.AddRange(new Control[] { lblName, txtName, lblHostCur, lblHostTarget, comboHost, lblDsCur, lblDsTarget, comboDs });
            dlg.AcceptButton = btnOk;
            dlg.CancelButton = btnCancel;

            if (dlg.ShowDialog(_root) != DialogResult.OK) return;

            newName = txtName.Text.Trim();
            if (string.IsNullOrEmpty(newName))
            {
                MessageBox.Show("新虚拟机名称不能为空", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (comboHost.SelectedIndex > 0) selectedHost = comboHost.SelectedItem?.ToString();
            if (comboDs.SelectedIndex > 0) selectedDs = comboDs.SelectedItem?.ToString();

            DoCloneVm(sourceVmName, sourceVmMoRef, newName, selectedHost, selectedDs);
        }

        private string? ShowRenameDialog(string resourceName, string currentName)
        {
            var dlg = new Form
            {
                AutoScaleMode = AutoScaleMode.Dpi,
                AutoScaleDimensions = new SizeF(96F, 96F),
                Text = $"重命名 — {resourceName}",
                Font = new Font("Microsoft YaHei", 9),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false,
                ShowInTaskbar = false,
                ClientSize = new Size(400, 150),
                MinimumSize = new Size(400, 150),
                BackColor = Color.FromArgb(245, 245, 245)
            };

            var lbl = new Label
            {
                Text = $"当前名称：{currentName}",
                AutoSize = true,
                Location = new Point(12, 14),
                Margin = new Padding(0)
            };

            var txt = new TextBox
            {
                Text = currentName ?? "",
                Location = new Point(12, 44),
                Size = new Size(dlg.ClientSize.Width - 24, 26),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

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
            dlg.Controls.AddRange(new Control[] { lbl, txt });
            dlg.AcceptButton = btnOk;
            dlg.CancelButton = btnCancel;
            txt.SelectAll();
            txt.Focus();

            return dlg.ShowDialog(_root) == DialogResult.OK ? (txt.Text?.Trim() ?? null) : null;
        }

        private string? ShowNotesDialog(string resourceName, string initialText)
        {
            var dlg = new Form
            {
                AutoScaleMode = AutoScaleMode.Dpi,
                AutoScaleDimensions = new SizeF(96F, 96F),
                Text = $"编辑备注 — {resourceName}",
                Font = new Font("Microsoft YaHei", 9),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false,
                ShowInTaskbar = false,
                ClientSize = new Size(440, 340),
                MinimumSize = new Size(440, 340),
                BackColor = Color.FromArgb(245, 245, 245)
            };

            var lbl = new Label
            {
                Text = $"资源：{resourceName}",
                AutoSize = true,
                Location = new Point(12, 10),
                Margin = new Padding(0)
            };

            var txt = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true,
                AcceptsTab = false,
                WordWrap = true,
                Text = initialText ?? "",
                Location = new Point(12, 32),
                Size = new Size(dlg.ClientSize.Width - 24, dlg.ClientSize.Height - 136),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

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
            dlg.Controls.AddRange(new Control[] { lbl, txt });
            dlg.AcceptButton = btnOk;
            dlg.CancelButton = btnCancel;

            return dlg.ShowDialog(_root) == DialogResult.OK ? txt.Text : null;
        }

        private void ShowGuestOpsNetworkDialog(
            string vmName, string vmMoRef, string powerState, string toolsStatus,
            string guestFamily, string guestFullName, string guestIp)
        {
            bool isLinux = guestFamily == "linuxGuest" ||
                (string.IsNullOrEmpty(guestFamily) && !string.IsNullOrEmpty(guestFullName) &&
                 guestFullName.ToLower().Contains("linux"));
            if (string.IsNullOrEmpty(guestFamily) && string.IsNullOrEmpty(guestFullName)) isLinux = true;

            string defaultUser = isLinux ? "root" : "Administrator";
            const string DEFAULT_PW = "Admin@9000";

            using var dlg = new Form
            {
                Text = $"🌐 配置客户机网络 — {vmName}",
                Icon = CoreUtils.GetAppIcon(),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                AutoScaleMode = AutoScaleMode.Dpi,
                AutoScaleDimensions = new SizeF(96F, 96F),
                Size = new Size(440, 430),
                MinimumSize = new Size(440, 430),
                Font = new Font("Microsoft YaHei", 9)
            };

            const int LBL_W = 65;
            const int LBL_X = 12;
            const int CTRL_X = LBL_X + LBL_W + 6;
            const int ROW_H = 28;
            int y = 10;

            var lblVmInfo = new Label
            {
                Text = $"虚拟机：{vmName}    电源：{(powerState == "poweredOn" ? "🔵 开机" : "⚫ 关机")}",
                ForeColor = powerState == "poweredOn" ? Color.FromArgb(0, 122, 204) : Color.DimGray,
                Location = new Point(LBL_X, y),
                AutoSize = true
            };
            dlg.Controls.Add(lblVmInfo); y += 20;

            bool toolsOk = toolsStatus == "toolsOk" || toolsStatus == "toolsOld";
            var lblTools = new Label
            {
                Text = $"VMware Tools：{(string.IsNullOrEmpty(toolsStatus) ? "未知" : toolsStatus)}    GuestOS：{(guestFullName ?? guestFamily ?? "未知")}",
                ForeColor = toolsOk ? Color.FromArgb(0, 128, 0) : Color.Firebrick,
                Location = new Point(LBL_X, y),
                AutoSize = true
            };
            dlg.Controls.Add(lblTools); y += 22;

            if (!toolsOk)
            {
                var warnLabel = new Label
                {
                    Text = "⚠ VMware Tools 未就绪，无法执行 GuestOps 网络配置",
                    ForeColor = Color.Firebrick,
                    AutoSize = true,
                    Location = new Point(LBL_X, y)
                };
                dlg.Controls.Add(warnLabel); y += 28;

                var btnPanelWarn = new FlowLayoutPanel
                {
                    Dock = DockStyle.Bottom, Height = 44,
                    Padding = new Padding(0, 6, 0, 6),
                    BackColor = Color.FromArgb(240, 240, 240)
                };
                var btnClose = new Button { Text = "关闭", DialogResult = DialogResult.Cancel, Size = new Size(80, 28) };
                btnPanelWarn.Controls.Add(btnClose);
                CenterFlowPanel(btnPanelWarn);
                dlg.Controls.Add(btnPanelWarn);
                dlg.ShowDialog(_root);
                return;
            }

            if (powerState != "poweredOn")
            {
                var warnLabel = new Label
                {
                    Text = "⚠ 虚拟机未开机，无法执行 GuestOps 网络配置，请先开机",
                    ForeColor = Color.Firebrick,
                    AutoSize = true,
                    Location = new Point(LBL_X, y)
                };
                dlg.Controls.Add(warnLabel); y += 28;

                var btnPanelWarn = new FlowLayoutPanel
                {
                    Dock = DockStyle.Bottom, Height = 44,
                    Padding = new Padding(0, 6, 0, 6),
                    BackColor = Color.FromArgb(240, 240, 240)
                };
                var btnClose = new Button { Text = "关闭", DialogResult = DialogResult.Cancel, Size = new Size(80, 28) };
                btnPanelWarn.Controls.Add(btnClose);
                CenterFlowPanel(btnPanelWarn);
                dlg.Controls.Add(btnPanelWarn);
                dlg.ShowDialog(_root);
                return;
            }

            var lblSection1 = new Label { Text = "── 网络配置（留空则该项不修改） ──", ForeColor = Color.DimGray, Location = new Point(LBL_X, y), AutoSize = true };
            dlg.Controls.Add(lblSection1); y += 22;

            var lblIp = new Label { Text = "IP 地址：", Location = new Point(LBL_X, y + 3), Size = new Size(LBL_W, 20), TextAlign = ContentAlignment.MiddleRight };
            dlg.Controls.Add(lblIp);
            var txtIp = new TextBox { Location = new Point(CTRL_X, y), Width = 200 };
            dlg.Controls.Add(txtIp);

            var lblCidr = new Label { Text = "/", Location = new Point(CTRL_X + 203, y + 3), AutoSize = true };
            dlg.Controls.Add(lblCidr);
            var cbMask = new ComboBox { Location = new Point(CTRL_X + 216, y), Width = 82, DropDownStyle = ComboBoxStyle.DropDown };
            cbMask.Items.AddRange(new object[] { "8", "16", "24", "25", "26", "27", "28", "29", "30", "31", "32" });
            cbMask.SelectedItem = "24";
            dlg.Controls.Add(cbMask);
            y += ROW_H;

            var lblGw = new Label { Text = "网关：", Location = new Point(LBL_X, y + 3), Size = new Size(LBL_W, 20), TextAlign = ContentAlignment.MiddleRight };
            dlg.Controls.Add(lblGw);
            var txtGw = new TextBox { Location = new Point(CTRL_X, y), Width = 300 };
            dlg.Controls.Add(txtGw); y += ROW_H;

            var lblDns = new Label { Text = "DNS：", Location = new Point(LBL_X, y + 3), Size = new Size(LBL_W, 20), TextAlign = ContentAlignment.MiddleRight };
            dlg.Controls.Add(lblDns);
            var cbDns = new ComboBox { Location = new Point(CTRL_X, y), Width = 300, DropDownStyle = ComboBoxStyle.DropDown };
            cbDns.Items.AddRange(new object[] { "223.5.5.5,223.6.6.6", "8.8.8.8,8.8.4.4", "114.114.114.114,114.114.115.115", "1.1.1.1,1.0.0.1", "119.29.29.29" });
            dlg.Controls.Add(cbDns); y += ROW_H;

            var lblHost = new Label { Text = "主机名：", Location = new Point(LBL_X, y + 3), Size = new Size(LBL_W, 20), TextAlign = ContentAlignment.MiddleRight };
            dlg.Controls.Add(lblHost);
            var txtHost = new TextBox { Location = new Point(CTRL_X, y), Width = 300 };
            dlg.Controls.Add(txtHost); y += ROW_H + 4;

            var lblSection2 = new Label { Text = "── Guest OS 凭证（默认 root / Admin@9000） ──", ForeColor = Color.DimGray, Location = new Point(LBL_X, y), AutoSize = true };
            dlg.Controls.Add(lblSection2); y += 22;

            int clientRight = dlg.ClientSize.Width - 12;

            var lblUser = new Label { Text = "用户名：", Location = new Point(LBL_X, y + 3), Size = new Size(LBL_W - 4, 20), TextAlign = ContentAlignment.MiddleRight };
            dlg.Controls.Add(lblUser);
            var txtUser = new TextBox { Text = defaultUser, Location = new Point(CTRL_X, y), Width = clientRight - CTRL_X };
            dlg.Controls.Add(txtUser); y += ROW_H;

            var lblPw = new Label { Text = "密码：", Location = new Point(LBL_X, y + 3), Size = new Size(LBL_W - 4, 20), TextAlign = ContentAlignment.MiddleRight };
            dlg.Controls.Add(lblPw);
            var txtPw = new TextBox { UseSystemPasswordChar = true, Text = DEFAULT_PW, Location = new Point(CTRL_X, y), Width = clientRight - CTRL_X - 64 };
            dlg.Controls.Add(txtPw);
            var chkShowPw = new CheckBox { Text = "显示", AutoSize = true, Location = new Point(clientRight - 60, y + 3), Cursor = Cursors.Hand };
            chkShowPw.CheckedChanged += (s, e) => txtPw.UseSystemPasswordChar = !chkShowPw.Checked;
            dlg.Controls.Add(chkShowPw); y += ROW_H + 2;

            var btnValidate = new Button { Text = "🔍 验证凭证", Location = new Point(LBL_X, y), Size = new Size(100, 26), Margin = Padding.Empty };
            var lblValidateStatus = new Label { Text = "凭证未验证", Location = new Point(LBL_X + 106, y + 4), AutoSize = true, ForeColor = Color.Gray, Margin = Padding.Empty };
            bool credVerified = false;
            btnValidate.Click += async (s, e) =>
            {
                string u = txtUser.Text.Trim();
                string p = txtPw.Text;
                if (string.IsNullOrEmpty(u) || string.IsNullOrEmpty(p))
                {
                    lblValidateStatus.Text = "请先填写用户名和密码";
                    lblValidateStatus.ForeColor = Color.Orange;
                    return;
                }
                btnValidate.Enabled = false;
                btnValidate.Text = "验证中...";
                lblValidateStatus.Text = "⏳ 正在通过 VMware Tools 验证凭证...";
                lblValidateStatus.ForeColor = Color.Gray;

                string psCheck = global::VmOps.Core.GuestOpsHelper.BuildCredentialCheckScript(vmMoRef, u, p, isLinux);
                string checkOutput = await global::VmOps.Core.SessionManager.Instance.ExecutePsScriptAsync(psCheck);

                btnValidate.Enabled = true;
                btnValidate.Text = "🔍 验证凭证";

                string trimmed = checkOutput?.Trim() ?? "";
                if (trimmed.Contains("VMT_OK:"))
                {
                    string ident = trimmed.Substring(trimmed.IndexOf("VMT_OK:") + 7).Trim();
                    credVerified = true;
                    lblValidateStatus.Text = $"✅ 凭证正确 (身份: {ident})";
                    lblValidateStatus.ForeColor = Color.FromArgb(0, 128, 0);
                }
                else if (trimmed.Contains("VMT_BADAUTH"))
                {
                    credVerified = false;
                    lblValidateStatus.Text = "❌ 用户名或密码错误";
                    lblValidateStatus.ForeColor = Color.Firebrick;
                }
                else if (trimmed.Contains("VMT_NOTOOLS"))
                {
                    credVerified = false;
                    lblValidateStatus.Text = "⚠ VMware Tools 未就绪";
                    lblValidateStatus.ForeColor = Color.Orange;
                }
                else
                {
                    credVerified = false;
                    string detail = trimmed.StartsWith("VMT_ERROR:") ? trimmed.Substring(10) : trimmed;
                    lblValidateStatus.Text = $"❌ 验证失败: {detail}";
                    lblValidateStatus.ForeColor = Color.Firebrick;
                }
            };
            txtUser.TextChanged += (s, e) => { credVerified = false; lblValidateStatus.Text = "凭证未验证"; lblValidateStatus.ForeColor = Color.Gray; };
            txtPw.TextChanged += (s, e) => { credVerified = false; lblValidateStatus.Text = "凭证未验证"; lblValidateStatus.ForeColor = Color.Gray; };
            dlg.Controls.Add(btnValidate);
            dlg.Controls.Add(lblValidateStatus); y += ROW_H;

            var lblHint = new Label
            {
                Text = "💡 网卡断开状态下仍可执行（走 VMware Tools RPC，不依赖虚拟网卡链路）",
                ForeColor = Color.FromArgb(100, 100, 100),
                AutoSize = true,
                Location = new Point(LBL_X, y),
                MaximumSize = new Size(410, 0)
            };
            dlg.Controls.Add(lblHint); y += 20;

            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, Height = 44,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 6, 0, 6),
                BackColor = Color.FromArgb(240, 240, 240)
            };
            var btnOk = new Button { Text = "⚡ 执行配置", DialogResult = DialogResult.OK, Size = new Size(110, 28) };
            var btnCancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Size = new Size(80, 28) };
            btnPanel.Controls.Add(btnOk);
            btnPanel.Controls.Add(btnCancel);
            CenterFlowPanel(btnPanel);

            dlg.Controls.Add(btnPanel);
            dlg.AcceptButton = btnOk;
            dlg.CancelButton = btnCancel;

            btnOk.Click += (s, e) =>
            {
                if (!credVerified)
                {
                    var dr = MessageBox.Show("尚未验证 Guest OS 凭证，是否仍然继续执行？", "提示",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (dr != DialogResult.Yes) return;
                }

                string ipVal = txtIp.Text.Trim();
                string gwVal = txtGw.Text.Trim();
                string maskVal = cbMask.Text.Trim();
                string dnsVal = cbDns.Text.Trim();
                string hostVal = txtHost.Text.Trim();
                string userVal = txtUser.Text.Trim();
                string pwVal = txtPw.Text;

                bool hasAnyChange = !string.IsNullOrEmpty(ipVal) || !string.IsNullOrEmpty(gwVal) ||
                    !string.IsNullOrEmpty(maskVal) || !string.IsNullOrEmpty(dnsVal) || !string.IsNullOrEmpty(hostVal);
                if (!hasAnyChange)
                {
                    MessageBox.Show("请至少填写一项要修改的网络参数（IP/网关/DNS/主机名）", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (string.IsNullOrEmpty(userVal)) userVal = defaultUser;
                if (string.IsNullOrEmpty(pwVal)) pwVal = DEFAULT_PW;

                dlg.Tag = new Tuple<string, string, string, string, string, string, string>(ipVal, gwVal, maskVal, dnsVal, hostVal, userVal, pwVal);
                dlg.DialogResult = DialogResult.OK;
                dlg.Close();
            };

            if (dlg.ShowDialog(_root) != DialogResult.OK) return;

            var t = (Tuple<string, string, string, string, string, string, string>)dlg.Tag;
            string ipFinal = t.Item1, gwFinal = t.Item2, maskFinal = t.Item3,
                   dnsFinal = t.Item4, hostFinal = t.Item5, userFinal = t.Item6, pwFinal = t.Item7;

            var vmData = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(ipFinal)) vmData["IP"] = ipFinal;
            if (!string.IsNullOrEmpty(gwFinal)) vmData["Gateway"] = gwFinal;
            if (!string.IsNullOrEmpty(maskFinal)) vmData["Mask"] = maskFinal;
            if (!string.IsNullOrEmpty(hostFinal)) vmData["Hostname"] = hostFinal;

            string psScript = DeployManager.BuildGuestOpsScript(vmData, vmMoRef, pwFinal,
                string.IsNullOrEmpty(dnsFinal) ? null : dnsFinal,
                guestOpsMode: true,
                guestUser: string.IsNullOrEmpty(userFinal) ? null : userFinal);

            var changes = new List<string>();
            if (!string.IsNullOrEmpty(ipFinal)) changes.Add($"IP={ipFinal}");
            if (!string.IsNullOrEmpty(gwFinal)) changes.Add($"GW={gwFinal}");
            if (!string.IsNullOrEmpty(maskFinal)) changes.Add($"Mask={maskFinal}");
            if (!string.IsNullOrEmpty(dnsFinal)) changes.Add($"DNS={dnsFinal}");
            if (!string.IsNullOrEmpty(hostFinal)) changes.Add($"Host={hostFinal}");

            LogMessage("信息", $"为【{vmName}】执行客户机网络配置: {string.Join(", ", changes)}");
            ExecuteVmOperation(psScript, $"客户机网络配置: {vmName}", null);
        }

        // ====================================================================
        // 密码配置对话框（独立脚本，不混用 DeployManager）
        // ====================================================================

        private static readonly char[] PwLower = "abcdefghijklmnopqrstuvwxyz".ToCharArray();
        private static readonly char[] PwUpper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();
        private static readonly char[] PwDigit = "0123456789".ToCharArray();
        private static readonly char[] PwSpecial = "!@#$%^&*()_+=-".ToCharArray();

        private static string GenerateRandomPassword()
        {
            var rnd = new Random(Guid.NewGuid().GetHashCode());
            int len = rnd.Next(12, 17);
            var sb = new StringBuilder(len);
            sb.Append(PwLower[rnd.Next(PwLower.Length)]);
            sb.Append(PwUpper[rnd.Next(PwUpper.Length)]);
            sb.Append(PwDigit[rnd.Next(PwDigit.Length)]);
            sb.Append(PwSpecial[rnd.Next(PwSpecial.Length)]);
            var all = PwLower.Concat(PwUpper).Concat(PwDigit).Concat(PwSpecial).ToArray();
            for (int i = 4; i < len; i++)
                sb.Append(all[rnd.Next(all.Length)]);
            var arr = sb.ToString().ToCharArray();
            for (int i = arr.Length - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                (arr[i], arr[j]) = (arr[j], arr[i]);
            }
            return new string(arr);
        }

        private static void ShowRulePasswordConfigDialog(IWin32Window owner)
        {
            var current = PasswordCrypto.LoadRuleConfig() ?? new PasswordCrypto.RulePasswordConfig();

            using var dlg = new Form
            {
                Text = "规律密码配置",
                Icon = CoreUtils.GetAppIcon(),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                AutoScaleMode = AutoScaleMode.Dpi,
                AutoScaleDimensions = new SizeF(96F, 96F),
                Size = new Size(440, 340),
                Font = new Font("Microsoft YaHei", 9)
            };

            const int LBL_X = 20;
            const int CTRL_X = 130;
            const int W = 260;
            int y = 20;

            var lblTip = new Label
            {
                Text = "密码格式：前缀{sep1}第三段{sep2}{y}{z}\n" +
                       "y = 第四段各位之和 + 偏移量\n" +
                       "z = 第四段末位 × 乘数",
                ForeColor = Color.DimGray,
                Location = new Point(LBL_X, y),
                AutoSize = true
            };
            dlg.Controls.Add(lblTip); y += 60;

            var lblPrefix = new Label { Text = "前缀：", Location = new Point(LBL_X, y + 3), Size = new Size(100, 20), TextAlign = ContentAlignment.MiddleRight };
            dlg.Controls.Add(lblPrefix);
            var txtPrefix = new TextBox { Text = current.Prefix, Location = new Point(CTRL_X, y), Width = W };
            dlg.Controls.Add(txtPrefix); y += 32;

            var lblSep = new Label { Text = "分隔符1（sep1）：", Location = new Point(LBL_X, y + 3), Size = new Size(100, 20), TextAlign = ContentAlignment.MiddleRight };
            dlg.Controls.Add(lblSep);
            var txtSep = new TextBox { Text = current.Separator, Location = new Point(CTRL_X, y), Width = 60, MaxLength = 3 };
            dlg.Controls.Add(txtSep); y += 32;

            var lblSep2 = new Label { Text = "分隔符2（sep2）：", Location = new Point(LBL_X, y + 3), Size = new Size(100, 20), TextAlign = ContentAlignment.MiddleRight };
            dlg.Controls.Add(lblSep2);
            var txtSep2 = new TextBox { Text = current.Separator2, Location = new Point(CTRL_X, y), Width = 60, MaxLength = 3 };
            dlg.Controls.Add(txtSep2); y += 32;

            var lblMul = new Label { Text = "乘数：", Location = new Point(LBL_X, y + 3), Size = new Size(100, 20), TextAlign = ContentAlignment.MiddleRight };
            dlg.Controls.Add(lblMul);
            var txtMul = new TextBox { Text = current.Multiplier.ToString(), Location = new Point(CTRL_X, y), Width = 60 };
            dlg.Controls.Add(txtMul); y += 32;

            var lblOff = new Label { Text = "偏移量：", Location = new Point(LBL_X, y + 3), Size = new Size(100, 20), TextAlign = ContentAlignment.MiddleRight };
            dlg.Controls.Add(lblOff);
            var txtOff = new TextBox { Text = current.Offset.ToString(), Location = new Point(CTRL_X, y), Width = 60 };
            dlg.Controls.Add(txtOff); y += 40;

            var btnOk = new Button { Text = "保存", DialogResult = DialogResult.OK, Location = new Point(CTRL_X, y), Size = new Size(80, 30) };
            var btnCancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(CTRL_X + 90, y), Size = new Size(80, 30) };
            dlg.Controls.Add(btnOk);
            dlg.Controls.Add(btnCancel);
            dlg.AcceptButton = btnOk;
            dlg.CancelButton = btnCancel;

            var lblSample = new Label { Text = "", Location = new Point(LBL_X, y + 36), AutoSize = true, ForeColor = Color.FromArgb(0, 100, 0) };
            dlg.Controls.Add(lblSample);

            void UpdateSample()
            {
                try
                {
                    string p = txtPrefix.Text;
                    string s1 = string.IsNullOrEmpty(txtSep.Text) ? "#" : txtSep.Text;
                    string s2 = string.IsNullOrEmpty(txtSep2.Text) ? "$" : txtSep2.Text;
                    int.TryParse(txtMul.Text, out int m);
                    int.TryParse(txtOff.Text, out int o);
                    if (m <= 0) m = 4;
                    int var2 = 201;
                    int c = var2 % 10;
                    int a = 2; int b = 0;
                    string y2 = (a + b + c + o).ToString("D2");
                    string z2 = (c * m).ToString("D2");
                    lblSample.Text = $"示例（IP后两段 10.201）：{p}{s1}10{s2}{y2}{z2}";
                }
                catch { lblSample.Text = ""; }
            }

            txtPrefix.TextChanged += (s, e) => UpdateSample();
            txtSep.TextChanged += (s, e) => UpdateSample();
            txtSep2.TextChanged += (s, e) => UpdateSample();
            txtMul.TextChanged += (s, e) => UpdateSample();
            txtOff.TextChanged += (s, e) => UpdateSample();
            UpdateSample();

            if (dlg.ShowDialog(owner) == DialogResult.OK)
            {
                if (string.IsNullOrEmpty(txtPrefix.Text))
                {
                    MessageBox.Show("前缀不能为空", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                var newCfg = new PasswordCrypto.RulePasswordConfig
                {
                    Prefix = txtPrefix.Text,
                    Separator = string.IsNullOrEmpty(txtSep.Text) ? "#" : txtSep.Text,
                    Separator2 = string.IsNullOrEmpty(txtSep2.Text) ? "$" : txtSep2.Text,
                    Multiplier = int.TryParse(txtMul.Text, out int m) && m > 0 ? m : 4,
                    Offset = int.TryParse(txtOff.Text, out int o) ? o : 0
                };
                PasswordCrypto.SaveRuleConfig(newCfg);
            }
        }

        private static string GenerateRulePassword(string ipInput)
        {
            string[] parts = ipInput.Trim().Split('.');
            if (parts.Length != 2) return "格式错误，请输入后两段（如 10.1）";
            if (!int.TryParse(parts[0], out int xInt) || xInt < 0 || xInt > 255) return "第三段无效";
            if (!int.TryParse(parts[1], out int varInt) || varInt < 0 || varInt > 255) return "第四段无效";

            var cfg = PasswordCrypto.LoadRuleConfig();
            if (cfg == null || string.IsNullOrEmpty(cfg.Prefix)) return "请先配置规律密码";

            string x = xInt.ToString();
            string var2 = varInt.ToString("D3");
            int a = var2[0] - '0';
            int b = var2[1] - '0';
            int c = var2[2] - '0';
            string y = (a + b + c + cfg.Offset).ToString("D2");
            string z = (c * cfg.Multiplier).ToString("D2");
            return $"{cfg.Prefix}{cfg.Separator}{x}{cfg.Separator2}{y}{z}";
        }

        private static List<string> FilterGuestIpv4List(string guestIpCsv)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(guestIpCsv)) return result;
            foreach (var raw in guestIpCsv.Split(','))
            {
                string ip = raw.Trim();
                if (string.IsNullOrEmpty(ip)) continue;
                if (ip.Contains(':')) continue;
                string[] p = ip.Split('.');
                if (p.Length != 4) continue;
                bool valid = true;
                foreach (var s in p) { if (!int.TryParse(s, out int n) || n < 0 || n > 255) { valid = false; break; } }
                if (!valid) continue;
                if (p[0] == "127") continue;
                if (p[0] == "169" && p[1] == "254") continue;
                result.Add(ip);
            }
            return result;
        }

        private static string ExtractLastTwoOctets(string fullIp)
        {
            if (string.IsNullOrEmpty(fullIp)) return "";
            string[] parts = fullIp.Split('.');
            if (parts.Length >= 4) return $"{parts[2]}.{parts[3]}";
            return "";
        }

        private void ShowGuestOpsPasswordDialog(
            string vmName, string vmMoRef, string powerState, string toolsStatus,
            string guestFamily, string guestFullName, string guestIp)
        {
            bool isLinux = guestFamily == "linuxGuest" ||
                (string.IsNullOrEmpty(guestFamily) && !string.IsNullOrEmpty(guestFullName) &&
                 guestFullName.ToLower().Contains("linux"));
            if (string.IsNullOrEmpty(guestFamily) && string.IsNullOrEmpty(guestFullName)) isLinux = true;

            string defaultUser = isLinux ? "root" : "Administrator";
            const string DEFAULT_PW = "Admin@9000";

            using var dlg = new Form
            {
                Text = $"🔑 配置客户机密码 — {vmName}",
                Icon = CoreUtils.GetAppIcon(),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                AutoScaleMode = AutoScaleMode.Dpi,
                AutoScaleDimensions = new SizeF(96F, 96F),
                Size = new Size(480, 460),
                MinimumSize = new Size(480, 460),
                Font = new Font("Microsoft YaHei", 9)
            };

            const int LBL_W = 80;
            const int LBL_X = 12;
            const int CTRL_X = LBL_X + LBL_W + 6;
            const int ROW_H = 28;
            int y = 10;

            var lblVmInfo = new Label
            {
                Text = $"虚拟机：{vmName}    电源：{(powerState == "poweredOn" ? "🔵 开机" : "⚫ 关机")}",
                ForeColor = powerState == "poweredOn" ? Color.FromArgb(0, 122, 204) : Color.DimGray,
                Location = new Point(LBL_X, y),
                AutoSize = true
            };
            dlg.Controls.Add(lblVmInfo); y += 20;

            bool toolsOk = toolsStatus == "toolsOk" || toolsStatus == "toolsOld";
            var lblTools = new Label
            {
                Text = $"VMware Tools：{(string.IsNullOrEmpty(toolsStatus) ? "未知" : toolsStatus)}",
                ForeColor = toolsOk ? Color.FromArgb(0, 128, 0) : Color.Firebrick,
                Location = new Point(LBL_X, y),
                AutoSize = true
            };
            dlg.Controls.Add(lblTools); y += 22;

            bool canExecute = toolsOk && powerState == "poweredOn";

            var lblSection1 = new Label { Text = "── 新密码（三选一） ──", ForeColor = Color.DimGray, Location = new Point(LBL_X, y), AutoSize = true };
            dlg.Controls.Add(lblSection1); y += 22;

            var rbInit = new RadioButton { Text = $"初始密码：{DEFAULT_PW}", Location = new Point(LBL_X, y), Width = 220, Checked = true };
            dlg.Controls.Add(rbInit); y += ROW_H;

            var rbRandom = new RadioButton { Text = "随机密码（12~16位，符合复杂度）", Location = new Point(LBL_X, y), Width = 260 };
            dlg.Controls.Add(rbRandom);
            var btnRandomRegen = new Button { Text = "↻ 换一个", Location = new Point(LBL_X + 260 + 6, y - 1), Size = new Size(72, 26), Enabled = false };
            dlg.Controls.Add(btnRandomRegen); y += ROW_H;

            var ruleCfg = PasswordCrypto.LoadRuleConfig();
            string ruleText = ruleCfg == null ? "规律密码（未配置）" : "规律密码（物理网卡 IP 自动生成）";
            var rbRule = new RadioButton { Text = ruleText, Location = new Point(LBL_X, y), Width = 260 };
            dlg.Controls.Add(rbRule);
            var btnRuleCfg = new Button { Text = "⚙ 配置", Location = new Point(LBL_X + 260 + 6, y - 1), Size = new Size(72, 26), Cursor = Cursors.Hand };
            dlg.Controls.Add(btnRuleCfg); y += ROW_H;

            var lblIpCombo = new Label { Text = "网卡 IP：", Location = new Point(LBL_X + 18, y + 3), Size = new Size(LBL_W - 18, 20), TextAlign = ContentAlignment.MiddleRight };
            dlg.Controls.Add(lblIpCombo);
            var ipv4List = FilterGuestIpv4List(guestIp);
            var cbIpv4 = new ComboBox { Location = new Point(CTRL_X, y), Width = 190, DropDownStyle = ComboBoxStyle.DropDown, Enabled = false };
            foreach (var ip in ipv4List) cbIpv4.Items.Add(ip);
            if (cbIpv4.Items.Count > 0) cbIpv4.SelectedIndex = 0;
            dlg.Controls.Add(cbIpv4);
            var lblRuleLast2 = new Label { Text = "(后两段: 自动计算)", Location = new Point(CTRL_X + 196, y + 3), AutoSize = true, ForeColor = Color.FromArgb(0, 100, 0) };
            dlg.Controls.Add(lblRuleLast2); y += ROW_H + 6;

            int clientRight = dlg.ClientSize.Width - 12;

            var lblNewPw = new Label
            {
                Text = "新密码：",
                Location = new Point(LBL_X, y + 5),
                Size = new Size(LBL_W - 4, 20),
                TextAlign = ContentAlignment.MiddleRight
            };
            dlg.Controls.Add(lblNewPw);

            int btnCopyRightX = clientRight - 82;
            var txtNewPw = new TextBox
            {
                Text = DEFAULT_PW,
                Location = new Point(CTRL_X, y),
                Width = btnCopyRightX - CTRL_X - 6,
                Font = new Font("Consolas", 11, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 100, 0),
                BorderStyle = BorderStyle.FixedSingle
            };
            dlg.Controls.Add(txtNewPw);

            var btnCopyPw = new Button
            {
                Text = "📋 复制",
                Location = new Point(btnCopyRightX, y - 1),
                Size = new Size(78, 28),
                Cursor = Cursors.Hand
            };
            dlg.Controls.Add(btnCopyPw); y += 34;

            string currentRandomPw = GenerateRandomPassword();
            string currentRulePw = !string.IsNullOrEmpty(cbIpv4.Text)
                ? GenerateRulePassword(ExtractLastTwoOctets(cbIpv4.Text))
                : "";

            void UpdatePreview()
            {
                if (rbInit.Checked)
                {
                    txtNewPw.Text = DEFAULT_PW;
                    txtNewPw.ForeColor = Color.FromArgb(0, 100, 0);
                }
                else if (rbRandom.Checked)
                {
                    txtNewPw.Text = currentRandomPw;
                    txtNewPw.ForeColor = Color.FromArgb(0, 100, 0);
                }
                else
                {
                    if (!string.IsNullOrEmpty(cbIpv4.Text))
                    {
                        txtNewPw.Text = currentRulePw;
                        txtNewPw.ForeColor = Color.FromArgb(0, 100, 0);
                    }
                    else
                    {
                        txtNewPw.Text = "";
                        txtNewPw.ForeColor = Color.Firebrick;
                    }
                }
            }

            btnCopyPw.Click += (s, e) =>
            {
                string pw = txtNewPw.Text;
                if (string.IsNullOrEmpty(pw))
                {
                    ToastUtils.ShowToast(dlg, "⚠ 密码为空，无法复制", 1500);
                    return;
                }
                Clipboard.SetText(pw);
                ToastUtils.ShowToast(dlg, "✓ 密码已复制到剪贴板", 1500);
            };

            rbInit.CheckedChanged += (s, e) => { btnRandomRegen.Enabled = rbRandom.Checked; cbIpv4.Enabled = rbRule.Checked; UpdatePreview(); };
            rbRandom.CheckedChanged += (s, e) => { btnRandomRegen.Enabled = rbRandom.Checked; cbIpv4.Enabled = rbRule.Checked; UpdatePreview(); };
            rbRule.CheckedChanged += (s, e) =>
            {
                btnRandomRegen.Enabled = rbRandom.Checked;
                cbIpv4.Enabled = rbRule.Checked;
                if (!string.IsNullOrEmpty(cbIpv4.Text))
                {
                    string last2 = ExtractLastTwoOctets(cbIpv4.Text);
                    currentRulePw = GenerateRulePassword(last2);
                    lblRuleLast2.Text = "(后两段: " + last2 + ")";
                }
                UpdatePreview();
            };
            cbIpv4.SelectedIndexChanged += (s, e) =>
            {
                if (!string.IsNullOrEmpty(cbIpv4.Text))
                {
                    string last2 = ExtractLastTwoOctets(cbIpv4.Text);
                    currentRulePw = GenerateRulePassword(last2);
                    lblRuleLast2.Text = "(后两段: " + last2 + ")";
                    if (rbRule.Checked) UpdatePreview();
                }
            };
            cbIpv4.TextChanged += (s, e) =>
            {
                if (!string.IsNullOrEmpty(cbIpv4.Text))
                {
                    string last2 = ExtractLastTwoOctets(cbIpv4.Text);
                    currentRulePw = GenerateRulePassword(last2);
                    lblRuleLast2.Text = "(后两段: " + last2 + ")";
                    if (rbRule.Checked) UpdatePreview();
                }
            };
            btnRandomRegen.Click += (s, e) => { currentRandomPw = GenerateRandomPassword(); UpdatePreview(); };
            btnRuleCfg.Click += (s, e) =>
            {
                ShowRulePasswordConfigDialog(dlg);
                if (!string.IsNullOrEmpty(cbIpv4.Text))
                {
                    string last2 = ExtractLastTwoOctets(cbIpv4.Text);
                    currentRulePw = GenerateRulePassword(last2);
                    lblRuleLast2.Text = "(后两段: " + last2 + ")";
                    if (rbRule.Checked) UpdatePreview();
                }
            };

            var lblSection2 = new Label { Text = "── Guest OS 凭证（用于登录执行改密脚本） ──", ForeColor = Color.DimGray, Location = new Point(LBL_X, y), AutoSize = true };
            dlg.Controls.Add(lblSection2); y += 22;

            var lblUser = new Label { Text = "用户名：", Location = new Point(LBL_X, y + 3), Size = new Size(LBL_W - 4, 20), TextAlign = ContentAlignment.MiddleRight };
            dlg.Controls.Add(lblUser);
            var txtUser = new TextBox { Text = defaultUser, Location = new Point(CTRL_X, y), Width = clientRight - CTRL_X };
            dlg.Controls.Add(txtUser); y += ROW_H;

            var lblPw = new Label { Text = "当前密码：", Location = new Point(LBL_X, y + 3), Size = new Size(LBL_W - 4, 20), TextAlign = ContentAlignment.MiddleRight };
            dlg.Controls.Add(lblPw);
            var txtPw = new TextBox { UseSystemPasswordChar = true, Text = DEFAULT_PW, Location = new Point(CTRL_X, y), Width = clientRight - CTRL_X - 64 };
            dlg.Controls.Add(txtPw);
            var chkShowPw = new CheckBox { Text = "显示", AutoSize = true, Location = new Point(clientRight - 60, y + 3), Cursor = Cursors.Hand };
            chkShowPw.CheckedChanged += (s, e) => txtPw.UseSystemPasswordChar = !chkShowPw.Checked;
            dlg.Controls.Add(chkShowPw); y += ROW_H + 2;

            var btnValidate = new Button { Text = "🔍 验证凭证", Location = new Point(LBL_X, y), Size = new Size(110, 26), Margin = Padding.Empty, Enabled = canExecute };
            var lblValidateStatus = new Label { Text = canExecute ? "凭证未验证" : "VMware Tools 未就绪，无法验证", Location = new Point(LBL_X + 116, y + 4), AutoSize = true, ForeColor = canExecute ? Color.Gray : Color.Orange, Margin = Padding.Empty };
            bool credVerified = false;
            btnValidate.Click += async (s, e) =>
            {
                if (!canExecute) return;
                string u = txtUser.Text.Trim();
                string p = txtPw.Text;
                if (string.IsNullOrEmpty(u) || string.IsNullOrEmpty(p))
                {
                    lblValidateStatus.Text = "请先填写用户名和密码";
                    lblValidateStatus.ForeColor = Color.Orange;
                    return;
                }
                btnValidate.Enabled = false;
                btnValidate.Text = "验证中...";
                lblValidateStatus.Text = "⏳ 正在通过 VMware Tools 验证凭证...";
                lblValidateStatus.ForeColor = Color.Gray;

                string psCheck = global::VmOps.Core.GuestOpsHelper.BuildCredentialCheckScript(vmMoRef, u, p, isLinux);
                string checkOutput = await global::VmOps.Core.SessionManager.Instance.ExecutePsScriptAsync(psCheck);

                btnValidate.Enabled = true;
                btnValidate.Text = "🔍 验证凭证";

                string trimmed = checkOutput?.Trim() ?? "";
                if (trimmed.Contains("VMT_OK:"))
                {
                    string ident = trimmed.Substring(trimmed.IndexOf("VMT_OK:") + 7).Trim();
                    credVerified = true;
                    lblValidateStatus.Text = $"✅ 凭证正确 (身份: {ident})";
                    lblValidateStatus.ForeColor = Color.FromArgb(0, 128, 0);
                }
                else if (trimmed.Contains("VMT_BADAUTH"))
                {
                    credVerified = false;
                    lblValidateStatus.Text = "❌ 用户名或密码错误";
                    lblValidateStatus.ForeColor = Color.Firebrick;
                }
                else if (trimmed.Contains("VMT_NOTOOLS"))
                {
                    credVerified = false;
                    lblValidateStatus.Text = "⚠ VMware Tools 未就绪";
                    lblValidateStatus.ForeColor = Color.Orange;
                }
                else
                {
                    credVerified = false;
                    string detail = trimmed.StartsWith("VMT_ERROR:") ? trimmed.Substring(10) : trimmed;
                    lblValidateStatus.Text = $"❌ 验证失败: {detail}";
                    lblValidateStatus.ForeColor = Color.Firebrick;
                }
            };
            txtUser.TextChanged += (s, e) => { credVerified = false; lblValidateStatus.Text = "凭证未验证"; lblValidateStatus.ForeColor = Color.Gray; };
            txtPw.TextChanged += (s, e) => { credVerified = false; lblValidateStatus.Text = "凭证未验证"; lblValidateStatus.ForeColor = Color.Gray; };
            dlg.Controls.Add(btnValidate);
            dlg.Controls.Add(lblValidateStatus); y += ROW_H;

            var lblHint = new Label
            {
                Text = "💡 改密脚本通过 VMware Tools RPC 下发，执行完毕后自动自删除",
                ForeColor = Color.FromArgb(100, 100, 100),
                AutoSize = true,
                Location = new Point(LBL_X, y),
                MaximumSize = new Size(450, 0)
            };
            dlg.Controls.Add(lblHint); y += 20;

            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, Height = 44,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 6, 0, 6),
                BackColor = Color.FromArgb(240, 240, 240)
            };
            var btnOk = new Button { Text = "⚡ 执行改密", DialogResult = DialogResult.OK, Size = new Size(110, 28), Enabled = canExecute };
            var btnCancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Size = new Size(80, 28) };
            btnPanel.Controls.Add(btnOk);
            btnPanel.Controls.Add(btnCancel);
            CenterFlowPanel(btnPanel);

            dlg.Controls.Add(btnPanel);
            dlg.AcceptButton = btnOk;
            dlg.CancelButton = btnCancel;

            btnOk.Click += (s, e) =>
            {
                if (!credVerified)
                {
                    var dr = MessageBox.Show("尚未验证 Guest OS 凭证，是否仍然继续执行？", "提示",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (dr != DialogResult.Yes) return;
                }

                string newPw = txtNewPw.Text;
                if (string.IsNullOrWhiteSpace(newPw))
                {
                    MessageBox.Show("请先填写新密码", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string userVal = txtUser.Text.Trim();
                string pwVal = txtPw.Text;
                if (string.IsNullOrEmpty(userVal)) userVal = defaultUser;
                if (string.IsNullOrEmpty(pwVal)) pwVal = DEFAULT_PW;

                dlg.Tag = new Tuple<string, string, string, string>(userVal, pwVal, userVal, newPw);
                dlg.DialogResult = DialogResult.OK;
                dlg.Close();
            };

            if (dlg.ShowDialog(_root) != DialogResult.OK) return;

            var t = (Tuple<string, string, string, string>)dlg.Tag;
            string guestUserFinal = t.Item1;
            string guestPwFinal = t.Item2;
            string targetUserFinal = t.Item3;
            string newPasswordFinal = t.Item4;

            string psScript = BuildGuestOpsPasswordPsScript(vmMoRef, guestUserFinal, guestPwFinal, targetUserFinal, newPasswordFinal, isLinux);

            LogMessage("信息", $"为【{vmName}】执行客户机密码配置：目标用户={targetUserFinal}，新密码={newPasswordFinal}");
            ExecuteVmOperation(psScript, $"客户机密码配置: {vmName}", null, () => { });
        }

        // ====================================================================
        // 密码脚本构建（与 DeployManager.BuildGuestOpsScript 同模式：资源脚本 + 占位符替换 + base64）
        // ====================================================================

        private static string BuildGuestOpsPasswordPsScript(string vmMoRef, string guestUser, string guestPassword, string targetUser, string newPassword, bool isLinux)
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            const string resNs = "VmOps";
            string scriptTemplate;
            string resName = resNs + ".scripts." + (isLinux ? "guestops-linux-pass.sh" : "guestops-windows-pass.ps1");

            try
            {
                using var stream = asm.GetManifestResourceStream(resName);
                if (stream != null)
                {
                    using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);
                    scriptTemplate = reader.ReadToEnd();
                }
                else
                {
                    scriptTemplate = isLinux
                        ? "#!/bin/bash\necho \"FAIL:resource not found: " + resName + "\"; exit 1"
                        : "Write-Output 'FAIL:resource not found: " + resName + "'; exit 1";
                }
            }
            catch (Exception ex)
            {
                scriptTemplate = isLinux
                    ? "#!/bin/bash\necho \"FAIL:load error: " + ex.Message.Replace("'", "\\'") + "\"; exit 1"
                    : "Write-Output 'FAIL:load error: " + ex.Message.Replace("'", "\\'") + "'; exit 1";
            }

            string userFinal = string.IsNullOrEmpty(targetUser) ? (isLinux ? "root" : "Administrator") : targetUser;
            string safeUser = isLinux ? userFinal.Replace("'", "'\\''") : userFinal.Replace("\"", "\"\"");
            string safePw = isLinux ? newPassword.Replace("'", "'\\''") : newPassword.Replace("\"", "\"\"");
            string scriptFilled = scriptTemplate
                .Replace("${TARGET_USER}", safeUser)
                .Replace("${NEW_PASSWORD}", safePw);

            string scriptB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(scriptFilled));
            string vmMoRefPs = vmMoRef.Replace("'", "''");
            string pwPs = guestPassword.Replace("'", "''");
            string newPwPs = newPassword.Replace("'", "''");
            string userPs = (guestUser ?? "").Replace("'", "''");

            string userLogin = string.IsNullOrEmpty(userPs) ? (isLinux ? "root" : "Administrator") : userPs;
            string guestTmp = isLinux
                ? "/tmp/vmt_pass_" + Guid.NewGuid().ToString("N") + ".sh"
                : "C:\\Windows\\Temp\\vmt_pass_" + Guid.NewGuid().ToString("N") + ".ps1";
            string runCmd = isLinux
                ? "bash " + guestTmp
                : "powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + guestTmp;

            return $@"
$ErrorActionPreference = 'Stop';
Write-Host ('GuestOps Password: isLinux=' + '{isLinux}' + ' target=' + '{userFinal}' + ' Assembly=' + '{resNs}');
$vi = Get-VM -Id '{vmMoRefPs}' -ErrorAction Stop;
$scriptContent = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{scriptB64}'));
$userLogin = '{userLogin}';
$pwLogin = '{pwPs}';
$newPwLogin = '{newPwPs}';
$localTmp = Join-Path $env:TEMP ('vmt_pass_' + [IO.Path]::GetRandomFileName());
$utf8NoBom = [Text.UTF8Encoding]::new($false);
$utf8Bom = [Text.UTF8Encoding]::new($true);
$enc = if ('{isLinux}' -eq 'True') {{ $utf8NoBom }} else {{ $utf8Bom }};
$verifyCmd = if ('{isLinux}' -eq 'True') {{ 'echo VMT_VERIFY_OK' }} else {{ 'Write-Output VMT_VERIFY_OK' }};
try {{
  Write-Host ('GuestOps Password: push script...');
  [IO.File]::WriteAllText($localTmp, $scriptContent, $enc);
  Copy-VMGuestFile -VM $vi -Source $localTmp -Destination '{guestTmp}' -LocalToGuest -GuestUser $userLogin -GuestPassword $pwLogin -Force -ErrorAction Stop | Out-Null;
  Write-Host ('GuestOps Password: script pushed');
  Write-Host ('GuestOps Password: execute with OLD credentials...');
  try {{
    $result = Invoke-VMScript -VM $vi -ScriptText '{runCmd}' -GuestUser $userLogin -GuestPassword $pwLogin -ErrorAction Stop;
    if ($null -eq $result) {{ throw 'Invoke-VMScript returned null' }};
    Write-Host ('GuestOps Password ExitCode=' + $result.ExitCode);
    if ($result.ScriptOutput) {{ Write-Host ('GuestOps Password Output:'); Write-Host $result.ScriptOutput }};
    if ($result.ScriptError) {{ Write-Host ('GuestOps Password Error:'); Write-Host $result.ScriptError }};
    $nl = [Environment]::NewLine;
    $combined = (@($result.ScriptOutput) + @($result.ScriptError)) -join $nl;
    if ($combined -match 'SUCCESS') {{
      Write-Host 'GuestOps Password: SUCCESS 标记确认';
    }} elseif ($combined -match 'FAIL') {{
      $failLine = ($combined -split $nl | Where-Object {{ $_ -match 'FAIL' }} | Select-Object -Last 1);
      throw ('密码脚本返回失败标记: ' + $failLine.Trim());
    }} elseif ($result.ExitCode -ne 0) {{
      throw ('密码设置失败 ExitCode=' + $result.ExitCode);
    }} else {{
      Write-Host ('GuestOps Password: 未检测到 SUCCESS/FAIL 标记，ExitCode=' + $result.ExitCode);
    }}
    Write-Host 'GuestOps Password: 完成';
  }} catch {{
    $msg = $_.Exception.Message;
    if ($msg -match 'authenticate|Authentication') {{
      Write-Host ('GuestOps Password: session auth lost — password changed by script, SUCCESS');
      Write-Host 'GuestOps Password: 密码已成功更改（VMware Tools session 因密码变更而断开，属预期行为）';
    }} else {{
      Write-Host ('GuestOps Password: UNEXPECTED exception: ' + $msg);
      if ($_.Exception.InnerException) {{ Write-Host ('Inner: ' + $_.Exception.InnerException.Message) }};
      throw;
    }}
  }}
}} finally {{
  Remove-Item $localTmp -Force -ErrorAction SilentlyContinue;
}}
";
        }
    }
}