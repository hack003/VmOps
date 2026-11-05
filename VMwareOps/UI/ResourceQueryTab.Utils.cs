// ============================================================================
// ResourceQueryTab.Utils.cs - 通用工具集
// ============================================================================
// 职责范围：
//   ✅ 导出功能（ExportCsv）
//   ✅ 复制功能（CopyCell/Row/All/ColumnHeaders/CopyText）
//   ✅ 显示窗口（ShowTableWindow / ShowDetailWindow Dictionary版本）
//   ✅ 执行引擎（ExecuteVmOperation）
//   ✅ 提示信息（ShowCopyHint）
//   ✅ 业务按钮状态（SetBusinessButtonsState）
//   ✅ 双击事件（ListView_MouseDoubleClick）
//
// 开发规范：
//   通用工具方法扩展 → 在此文件添加
//   不依赖具体业务逻辑的工具方法 → 放在此文件
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using VMwareOps.Core;
using VMwareOps.Utils;

namespace VMwareOps.UI
{
    public partial class ResourceQueryTab
    {
        // ====== 复制功能 ======

        private void CopyCell(int? subItemIndex = null)
        {
            if (_listView?.SelectedItems.Count > 0)
            {
                var item = _listView.SelectedItems[0];
                string text = "";
                
                // 如果指定了列索引，复制该列的值；否则复制第一列
                int idx = subItemIndex ?? 0;
                if (idx >= 0 && idx < item.SubItems.Count)
                    text = item.SubItems[idx].Text;

                CopyText(text);
                ShowCopyHint("单元格");
            }
        }

        private void CopySelectedRow()
        {
            if (_listView?.SelectedItems.Count > 0)
            {
                var item = _listView.SelectedItems[0];
                var values = new List<string>();
                for (int i = 0; i < item.SubItems.Count; i++)
                    values.Add(item.SubItems[i].Text);
                string text = string.Join("\t", values);
                CopyText(text);
                ShowCopyHint("选中行");
            }
        }

        private void CopyAll()
        {
            if (_rows.Count == 0 || _columns.Count == 0)
            {
                ToastUtils.ShowToast(_root, "没有数据可复制！");
                return;
            }

            var lines = new List<string>();
            lines.Add(string.Join("\t", _columns));
            
            foreach (var row in _rows)
                lines.Add(string.Join("\t", row));

            string text = string.Join("\n", lines);
            CopyText(text);
            ShowCopyHint("全部数据");
        }

        private void CopyColumnHeaders()
        {
            if (_columns.Count == 0)
            {
                ToastUtils.ShowToast(_root, "没有列标题可复制！");
                return;
            }

            string text = string.Join("\t", _columns);
            CopyText(text);
            ShowCopyHint("列标题");
        }

        protected void CopyText(string text)
        {
            try
            {
                Clipboard.SetText(text);
            }
            catch (Exception ex)
            {
                LogMessage("error", $"复制到剪贴板失败: {ex.Message}");
            }
        }

        private void ShowCopyHint(string sourceType)
        {
            LogMessage("success", $"{sourceType}已复制到剪贴板");
            ToastUtils.ShowToast(_root, $"{sourceType}已复制到剪贴板");
        }

        // ====== 导出功能 ======

        private void ExportCsv()
        {
            if (_rows.Count == 0 || _columns.Count == 0)
            {
                MessageBox.Show("没有数据可导出！\n请先查询数据后再导出。",
                    "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string vcIp = SessionManager.Instance.CurrentVc ?? "unknown";
            string safeVc = vcIp.Replace(":", "-").Replace("/", "-");

            string suffix = _queryType switch
            {
                "vm" => "vms",
                "host" => "host",
                "datastore" => "storage",
                "network" => "network",
                _ => _queryType
            };

            using var saveDlg = new SaveFileDialog
            {
                Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
                DefaultExt = "csv",
                FileName = $"VmOps-{safeVc}-{suffix}.csv",
                Title = "导出"
            };

            if (saveDlg.ShowDialog() != DialogResult.OK) return;

            try
            {
                using var writer = new StreamWriter(saveDlg.FileName, false, System.Text.Encoding.UTF8);
                writer.WriteLine(string.Join(",", _columns.Select(c => $"\"{c.Replace("\"", "\"\"")}\"")));
                
                foreach (var row in _rows)
                    writer.WriteLine(string.Join(",", row.Select(c => $"\"{c.Replace("\"", "\"\"")}\"")));

                LogMessage("success", $"✓ 数据已导出到: {saveDlg.FileName}");
                MessageBox.Show($"导出成功！\n\n文件路径:\n{saveDlg.FileName}\n\n共 {_rows.Count} 条记录",
                    "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);

                if (File.Exists(saveDlg.FileName))
                {
                    Process.Start(new ProcessStartInfo(saveDlg.FileName) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                LogMessage("error", $"导出失败: {ex.Message}");
                MessageBox.Show($"导出失败:\n{ex.Message}",
                    "导出错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ====== 执行引擎（单机操作）======

        private void ExecuteVmOperation(string script, string operationName, string? targetVmShortId = null, Action? onSuccess = null)
        {
            LogMessage("信息", $"执行操作: {operationName}");
            string injected = $"$ErrorActionPreference = 'Stop'; {PsWaitTaskFunc} {script}";
            Task.Run(async () =>
            {
                try
                {
                    var result = await SessionManager.Instance.ExecuteQueryAsync(
                        injected,
                        (msg, level) => LogMessage(level ?? "info", msg ?? "")
                    );
                    _root.Invoke(new Action(() =>
                    {
                        LogMessage("成功", $"操作完成: {operationName}");
                        ToastUtils.ShowToast(_root, $"操作完成: {operationName}");
                        if (onSuccess != null)
                        {
                            onSuccess();
                        }
                        else if (!string.IsNullOrEmpty(targetVmShortId))
                        {
                            RefreshSingleVm(targetVmShortId);
                        }
                        else
                        {
                            DoQuery();
                        }
                    }));
                }
                catch (Exception ex)
                {
                    var errorMsg = ex.Message;
                    if (errorMsg.Contains("delta disk"))
                    {
                        errorMsg = "该磁盘是快照增量磁盘（Delta Disk），无法调整大小。\n请先合并/删除所有快照后再试。";
                    }
                    _root.Invoke(new Action(() =>
                    {
                        LogMessage("错误", $"操作失败: {operationName} - {errorMsg}");
                        MessageBox.Show($"操作失败: {operationName}\n\n{errorMsg}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
            });
        }

        private void ExecuteVmOperationWithTable(string script, string operationName, string tableTitle)
        {
            LogMessage("信息", $"执行操作: {operationName}");
            string injected = $"$ErrorActionPreference = 'Stop'; {PsWaitTaskFunc} {script}";
            Task.Run(async () =>
            {
                try
                {
                    var result = await SessionManager.Instance.ExecuteQueryAsync(
                        injected,
                        (msg, level) => LogMessage(level ?? "info", msg ?? "")
                    );

                    _root.Invoke(new Action(() =>
                    {
                        if (result != null && result.Count > 0)
                        {
                            try
                            {
                                var jsonStr = result[0].Values.FirstOrDefault()?.ToString();
                                if (!string.IsNullOrEmpty(jsonStr))
                                {
                                    ShowTableWindow(tableTitle, jsonStr);
                                }
                                else
                                {
                                    MessageBox.Show("查询结果为空", tableTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                                }
                            }
                            catch
                            {
                                ShowDetailWindow(operationName, result);
                            }
                        }
                        else
                        {
                            MessageBox.Show("查询结果为空", tableTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    }));
                }
                catch (Exception ex)
                {
                    _root.Invoke(new Action(() =>
                    {
                        LogMessage("错误", $"操作失败: {operationName} - {ex.Message}");
                    }));
                }
            });
        }

        private void ExecuteBatchOperation(string script, string operationName, List<string>? targetVmShortIds = null, Action? onSuccess = null)
        {
            LogMessage("信息", $"执行批量操作: {operationName}");
            string injected = $"$ErrorActionPreference = 'Stop'; {PsWaitTaskFunc} {script}";
            Task.Run(async () =>
            {
                try
                {
                    var result = await SessionManager.Instance.ExecuteQueryAsync(
                        injected,
                        (msg, level) => LogMessage(level ?? "info", msg ?? "")
                    );
                    _root.Invoke(new Action(() =>
                    {
                        LogMessage("成功", $"批量操作完成: {operationName}");
                        ToastUtils.ShowToast(_root, $"批量操作完成: {operationName}");
                        if (onSuccess != null)
                        {
                            onSuccess();
                        }
                        else if (targetVmShortIds != null && targetVmShortIds.Count > 0)
                        {
                            foreach (var id in targetVmShortIds)
                                RefreshSingleVm(id);
                        }
                        else
                        {
                            DoQuery();
                        }
                    }));
                }
                catch (Exception ex)
                {
                    _root.Invoke(new Action(() =>
                    {
                        LogMessage("错误", $"批量操作失败: {operationName} - {ex.Message}");
                    }));
                }
            });
        }

        private void ExecuteLongVmOperation(string script, string operationName, string? targetVmShortId = null, Action? onSuccess = null)
        {
            LogMessage("信息", $"执行操作(通道B): {operationName}");
            string injected = $"$ErrorActionPreference = 'Stop'; {PsWaitTaskFunc} {script}";
            Task.Run(async () =>
            {
                Core.TempDeploymentRunner? runner = null;
                try
                {
                    runner = await Core.SessionManager.Instance.GetDeploymentRunnerAsync(
                        (msg, level) => LogMessage(level ?? "info", msg ?? ""));

                    if (runner == null)
                    {
                        LogMessage("错误", $"无法获取执行通道，操作已取消: {operationName}");
                        return;
                    }

                    var result = await runner.ExecuteQueryAsync(
                        injected,
                        (msg, level) => LogMessage(level ?? "info", msg ?? ""),
                        timeoutSec: 0
                    );

                    _root.Invoke(new Action(() =>
                    {
                        LogMessage("成功", $"操作完成: {operationName}");
                        ToastUtils.ShowToast(_root, $"操作完成: {operationName}");
                        if (onSuccess != null)
                        {
                            onSuccess();
                        }
                        else if (!string.IsNullOrEmpty(targetVmShortId))
                        {
                            RefreshSingleVm(targetVmShortId);
                        }
                        else
                        {
                            DoQuery();
                        }
                    }));
                }
                catch (Exception ex)
                {
                    _root.Invoke(new Action(() =>
                    {
                        LogMessage("错误", $"操作失败: {operationName} - {ex.Message}");
                        MessageBox.Show($"操作失败: {operationName}\n\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
            });
        }

        private void ShowTableWindow(string title, string jsonData)
        {
            try
            {
                var tableForm = new Form
                {
                    Text = title,
                    Icon = CoreUtils.GetAppIcon(),
                    Size = new Size(700, 500),
                    StartPosition = FormStartPosition.CenterParent,
                    Font = new Font("Microsoft YaHei", 9),
                    AutoScaleMode = AutoScaleMode.Dpi,
                    AutoScaleDimensions = new SizeF(96F, 96F),
                    BackColor = Color.FromArgb(245, 245, 245),
                    MinimumSize = new Size(500, 300)
                };

                var listView = new NonEditableListView
                {
                    Dock = DockStyle.Fill,
                    View = View.Details,
                    FullRowSelect = true,
                    GridLines = true,
                    Font = new Font("Microsoft YaHei", 9), LabelEdit = false
                };

                var items = Newtonsoft.Json.JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(jsonData);
                if (items != null && items.Count > 0)
                {
                    var columns = new List<string>(items[0].Keys);
                    foreach (var col in columns)
                    {
                        listView.Columns.Add(GetColumnDisplayName(col), GetColumnWidth(col), HorizontalAlignment.Left);
                    }

                    foreach (var item in items)
                    {
                        var lvi = new ListViewItem();
                        for (int i = 0; i < columns.Count; i++)
                        {
                            var col = columns[i];
                            var value = item.TryGetValue(col, out var v) ? v?.ToString() ?? "" : "";
                            if (i == 0)
                                lvi.Text = value;
                            else
                                lvi.SubItems.Add(value);
                        }
                        listView.Items.Add(lvi);
                    }

                    // 自适应列宽
                    Utils.TableConvertUtils.AutoResizeColumns(listView);
                }
                else
                {
                    listView.Columns.Add("内容", 600, HorizontalAlignment.Left);
                    var lvi = new ListViewItem(jsonData);
                    listView.Items.Add(lvi);

                    // 自适应列宽
                    Utils.TableConvertUtils.AutoResizeColumns(listView);
                }

                var btnPanel = new FlowLayoutPanel
                {
                    Dock = DockStyle.Bottom, Height = 44,
                    Padding = new Padding(0, 6, 0, 6),
                    BackColor = Color.FromArgb(240, 240, 240)
                };
                var btnClose = new Button { Text = "关闭", DialogResult = DialogResult.OK, Size = new Size(80, 28), Font = new Font("Microsoft YaHei", 9) };
                btnPanel.Controls.Add(btnClose);

                CenterFlowPanel(btnPanel);

                tableForm.Controls.Add(btnPanel);
                tableForm.Controls.Add(listView);
                tableForm.AcceptButton = btnClose;
                tableForm.CancelButton = btnClose;
                tableForm.ShowDialog(_root);
            }
            catch (Exception ex)
            {
                LogMessage("错误", $"显示表格窗口失败: {ex.Message}");
                MessageBox.Show($"显示表格失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowDetailWindow(string title, List<Dictionary<string, object>> result)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"=== {title} ===");
                foreach (var item in result)
                {
                    foreach (var kvp in item)
                    {
                        sb.AppendLine($"  {kvp.Key}: {kvp.Value}");
                    }
                    sb.AppendLine();
                }

                var detailForm = new DetailForm(title, sb.ToString());
                detailForm.ShowDialog(_root);
            }
            catch (Exception ex)
            {
                LogMessage("错误", $"显示详细信息失败: {ex.Message}");
            }
        }

        // ====== 显示窗口工具 ======

        public static void ShowTableWindow(Form owner, string title, List<string> columns, List<List<string>> rows)
        {
            using var dlg = new Form
            {
                Text = title,
                Icon = CoreUtils.GetAppIcon(),
                FormBorderStyle = FormBorderStyle.Sizable,
                StartPosition = FormStartPosition.CenterParent,
                Font = new Font("Microsoft YaHei", 9),
                AutoScaleMode = AutoScaleMode.Dpi,
                AutoScaleDimensions = new SizeF(96F, 96F),
                Size = new Size(700, 500),
                MinimumSize = new Size(500, 400)
            };

            var listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = true
            };
            listView.MouseDoubleClick += (s, e) =>
            {
                ListViewHitTestInfo hit;
                try { hit = listView.HitTest(e.X, e.Y); }
                catch { return; }
                string? text = hit.SubItem?.Text ?? hit.Item?.Text;
                if (!string.IsNullOrEmpty(text))
                {
                    try { Clipboard.SetText(text); ToastUtils.ShowToast(dlg, "已复制到剪贴板"); } catch { }
                }
            };

            foreach (var col in columns)
                listView.Columns.Add(col, 120);

            foreach (var row in rows)
            {
                var item = new ListViewItem(row.Count > 0 ? row[0] : "");
                for (int i = 1; i < row.Count; i++)
                    item.SubItems.Add(row[i]);
                listView.Items.Add(item);
            }

            // 自适应列宽
            Utils.TableConvertUtils.AutoResizeColumns(listView);

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
            dlg.Controls.Add(listView);
            dlg.AcceptButton = btnClose;
            dlg.CancelButton = btnClose;

            dlg.ShowDialog(owner);
        }

        public static void ShowTableWindow(Form owner, string title, Dictionary<string, string> data)
        {
            var columns = new List<string> { "属性", "值" };
            var rows = data.Select(kv => new List<string> { kv.Key, kv.Value }).ToList();
            ShowTableWindow(owner, title, columns, rows);
        }

        public static void ShowDetailWindow(Form owner, string name, Dictionary<string, string> details)
        {
            ShowTableWindow(owner, $"详细信息 - {name}", details);
        }

        // ====== UI事件处理 ======

        private void ListView_MouseDoubleClick(object? sender, MouseEventArgs e)
        {
            if (_listView == null) return;
            ListViewHitTestInfo hitTest;
            try { hitTest = _listView.HitTest(e.X, e.Y); }
            catch { return; }
            if (hitTest.Item != null)
            {
                int subItemIdx = -1;
                if (hitTest.SubItem != null)
                {
                    subItemIdx = hitTest.Item.SubItems.IndexOf(hitTest.SubItem);
                }
                CopyCell(subItemIdx >= 0 ? subItemIdx : 0);
            }
        }

        private static void CenterFlowPanel(FlowLayoutPanel panel)
        {
            panel.Layout += (s, e) =>
            {
                int n = panel.Controls.Count;
                int totalW = 0;
                for (int i = 0; i < n; i++)
                {
                    var ctrl = panel.Controls[i];
                    ctrl.Margin = new Padding(0, 0, (i < n - 1) ? 8 : 0, 0);
                    totalW += ctrl.Width;
                }
                totalW += 8 * Math.Max(0, n - 1);
                int half = Math.Max(0, (panel.ClientSize.Width - totalW) / 2);
                panel.Padding = new Padding(half, 6, half, 6);
            };
        }
    }
}