// ============================================================================
// DetailForm.cs - 资源详情查看弹窗
// ============================================================================
// 职责：显示 VM / Host / Datastore / Network 等资源的详情查询结果
//
// 核心功能：
//   ✅ 构造参数：title / detailText / onRefresh 刷新回调
//   ✅ InitializeForm() — TableLayoutPanel 布局：ListView(属性值) + 底部按钮栏
//   ✅ UpdateContent() — 外部刷新详情内容
//   ✅ 刷新按钮回调 → 重新执行 PowerCLI 脚本 → UpdateContent 更新
//
// 调用方：ResourceQueryTab.ShowDetailWindow()
// ============================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Newtonsoft.Json;
using VmOps.Utils;

namespace VmOps.UI
{
    public class DetailForm : Form
    {
        private readonly string _title;
        private string _detailText;
        private readonly Action<string>? _onRefresh;
        private DetailListView? _listView;

        public DetailForm(string title, string detailText, Action<string>? onRefresh = null)
        {
            _title = title;
            _detailText = detailText;
            _onRefresh = onRefresh;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            InitializeForm();
        }

        private void InitializeForm()
        {
            Text = _title;
            Icon = CoreUtils.GetAppIcon();
            Size = new Size(750, 550);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei", 9);
            BackColor = Color.FromArgb(245, 245, 245);
            MinimumSize = new Size(550, 350);

            var mainPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1,
                Padding = new Padding(8)
            };
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

            _listView = new DetailListView
            {
                Dock = DockStyle.Fill, View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Font = new Font("Microsoft YaHei", 9), BorderStyle = BorderStyle.FixedSingle,
                LabelEdit = false,
                Scrollable = true
            };

            _listView.Columns.Add("属性", 200, HorizontalAlignment.Left);
            _listView.Columns.Add("值", 500, HorizontalAlignment.Left);

            // 自适应列宽
            Utils.TableConvertUtils.AutoResizeColumns(_listView);

            ParseAndFillListView(_listView, _detailText);

            _listView.Resize += (s, e) =>
            {
                _listView.Columns[0].Width = 200;
                _listView.Columns[1].Width = Math.Max(200, _listView.ClientSize.Width - _listView.Columns[0].Width - 4);
            };

            // 右键菜单：复制选中行
            var contextMenu = new ContextMenuStrip();
            var copyItem = new ToolStripMenuItem("复制选中内容");
            copyItem.Click += (s, e) =>
            {
                if (_listView.SelectedItems.Count > 0)
                {
                    var sb = new StringBuilder();
                    foreach (ListViewItem item in _listView.SelectedItems)
                    {
                        if (item.SubItems.Count >= 2)
                        {
                            sb.AppendLine($"{item.Text}\t{item.SubItems[1].Text}");
                        }
                        else
                        {
                            sb.AppendLine(item.Text);
                        }
                    }
                    Clipboard.SetText(sb.ToString().TrimEnd());
                }
            };
            var copyAllItem = new ToolStripMenuItem("复制全部内容");
            copyAllItem.Click += (s, e) =>
            {
                var sb = new StringBuilder();
                foreach (ListViewItem item in _listView.Items)
                {
                    if (item.SubItems.Count >= 2)
                    {
                        sb.AppendLine($"{item.Text}\t{item.SubItems[1].Text}");
                    }
                    else
                    {
                        sb.AppendLine(item.Text);
                    }
                }
                Clipboard.SetText(sb.ToString().TrimEnd());
            };
            contextMenu.Items.Add(copyItem);
            contextMenu.Items.Add(copyAllItem);

            // 刷新功能：如果有刷新回调，显示刷新菜单项
            if (_onRefresh != null)
            {
                contextMenu.Items.Add(new ToolStripSeparator());
                var refreshItem = new ToolStripMenuItem("刷新");
                refreshItem.Click += (s, e) =>
                {
                    _onRefresh.Invoke(_detailText);
                };
                contextMenu.Items.Add(refreshItem);
            }

            _listView.ContextMenuStrip = contextMenu;

            _listView.MouseDoubleClick += (s, e) =>
            {
                ListViewHitTestInfo hit;
                try { hit = _listView.HitTest(e.X, e.Y); }
                catch { return; }
                string? text = hit.SubItem?.Text ?? hit.Item?.Text;
                if (!string.IsNullOrEmpty(text))
                {
                    try { Clipboard.SetText(text); ToastUtils.ShowToast(this, "已复制到剪贴板"); } catch { }
                }
            };

            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(4), AutoSize = true
            };

            var btnClose = new Button
            {
                Text = "关闭", Size = new Size(80, 28),
                DialogResult = DialogResult.OK
            };

            btnPanel.Controls.Add(btnClose);
            mainPanel.Controls.Add(_listView, 0, 0);
            mainPanel.Controls.Add(btnPanel, 0, 1);

            Controls.Add(mainPanel);
            AcceptButton = btnClose;
        }

        public void UpdateContent(string newDetailText)
        {
            _detailText = newDetailText;
            if (_listView != null)
            {
                _listView.Items.Clear();
                ParseAndFillListView(_listView, newDetailText);
            }
        }

        private string? ExtractField(string keyName)
        {
            if (_listView == null) return null;
            foreach (ListViewItem item in _listView.Items)
            {
                if ((item.Text?.Trim() ?? "") == keyName)
                {
                    if (item.SubItems.Count > 1)
                        return item.SubItems[1].Text?.Trim();
                }
            }
            return null;
        }

        private void ParseAndFillListView(ListView listView, string detailText)
        {
            var lines = detailText.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
            var jsonBuffer = new StringBuilder();
            bool inJsonBlock = false;
            string jsonSectionTitle = "";

            for (int i = 0; i < lines.Length; i++)
            {
                var rawLine = lines[i];
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line))
                {
                    // 如果在 JSON 块中遇到空行，结束 JSON 块
                    if (inJsonBlock && jsonBuffer.Length > 0)
                    {
                        RenderJsonSection(listView, jsonSectionTitle, jsonBuffer.ToString());
                        jsonBuffer.Clear();
                        inJsonBlock = false;
                    }
                    continue;
                }

                // 移除 PowerShell 提示符前缀
                if (line.StartsWith("PS "))
                {
                    line = line.Substring(3).Trim();
                    if (string.IsNullOrEmpty(line)) continue;
                }

                // 检测错误信息
                if (line.StartsWith("【错误】"))
                {
                    var errorItem = new ListViewItem("错误") { ForeColor = Color.FromArgb(198, 40, 40), BackColor = Color.FromArgb(255, 235, 238) };
                    errorItem.SubItems.Add(line.Substring(4));
                    listView.Items.Add(errorItem);
                    continue;
                }

                // 检测章节标题 === xxx ===
                if (line.StartsWith("===") && line.EndsWith("==="))
                {
                    // 如果之前在 JSON 块中，先渲染
                    if (inJsonBlock && jsonBuffer.Length > 0)
                    {
                        RenderJsonSection(listView, jsonSectionTitle, jsonBuffer.ToString());
                        jsonBuffer.Clear();
                        inJsonBlock = false;
                    }

                    var sectionTitle = line.Trim('=').Trim();
                    // 添加上分割线（贯穿两列）
                    var separatorItem = new ListViewItem("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                    separatorItem.ForeColor = Color.FromArgb(150, 150, 150);
                    separatorItem.SubItems.Add("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                    separatorItem.SubItems[1].ForeColor = Color.FromArgb(150, 150, 150);
                    listView.Items.Add(separatorItem);
                    
                    // 章节标题（字体加大加粗，带前缀符号）
                    var sectionItem = new ListViewItem("▶ " + sectionTitle);
                    sectionItem.Font = new Font("Microsoft YaHei", 10, FontStyle.Bold);
                    sectionItem.ForeColor = Color.FromArgb(20, 80, 160);
                    sectionItem.UseItemStyleForSubItems = true;
                    var subItem = sectionItem.SubItems.Add("");
                    subItem.ForeColor = Color.FromArgb(20, 80, 160);
                    listView.Items.Add(sectionItem);

                    // 检查下一个章节是否需要 JSON 渲染
                    if (sectionTitle.Contains("磁盘信息") || sectionTitle.Contains("光驱信息") || sectionTitle.Contains("快照信息") || sectionTitle.Contains("网络信息"))
                    {
                        inJsonBlock = true;
                        jsonSectionTitle = sectionTitle;
                    }
                    continue;
                }

                // 如果在 JSON 块中，收集 JSON 内容
                if (inJsonBlock)
                {
                    // 检测是否为"(无xxx)"文本
                    if (line.StartsWith("(无") && (line.EndsWith(")")))
                    {
                        var noDataItem = new ListViewItem("  " + line) { Font = new Font("Microsoft YaHei", 8, FontStyle.Italic), ForeColor = Color.FromArgb(150, 150, 150) };
                        noDataItem.SubItems.Add("");
                        listView.Items.Add(noDataItem);
                        inJsonBlock = false;
                    }
                    else if (line.StartsWith("[") || line.StartsWith("{") || jsonBuffer.Length > 0)
                    {
                        jsonBuffer.AppendLine(line);
                    }
                    continue;
                }

                // 普通文本行 - 按冒号分割
                if (rawLine.StartsWith("  ") || rawLine.StartsWith("\t"))
                {
                    var trimmedLine = rawLine.TrimStart();
                    var colonIndex = trimmedLine.IndexOf('：');
                    if (colonIndex > 0)
                    {
                        string key = trimmedLine.Substring(0, colonIndex).Trim();
                        string val = trimmedLine.Substring(colonIndex + 1).Trim();

                        // 注释_B64：解码后显示
                        if (key == "注释_B64")
                        {
                            try
                            {
                                val = Encoding.UTF8.GetString(Convert.FromBase64String(val));
                                if (string.IsNullOrEmpty(val)) val = "(无)";
                            }
                            catch { val = "(解码失败)"; }
                            key = "注释";
                        }

                        var item = new ListViewItem(key);
                        item.SubItems.Add(val);
                        listView.Items.Add(item);
                    }
                    else
                    {
                        var parts = trimmedLine.Split(new[] { ':' }, 2);
                        if (parts.Length == 2)
                        {
                            // 如果是"IPv4 地址: xxx"格式，保持在一行显示
                            if (parts[0].Trim().Contains("IPv4 地址"))
                            {
                                var item = new ListViewItem(parts[0].Trim());
                                item.SubItems.Add(parts[1].Trim());
                                listView.Items.Add(item);
                            }
                            else
                            {
                                var item = new ListViewItem(parts[0].Trim());
                                item.SubItems.Add(parts[1].Trim());
                                listView.Items.Add(item);
                            }
                        }
                        else
                        {
                            var item = new ListViewItem("");
                            item.SubItems.Add(trimmedLine);
                            listView.Items.Add(item);
                        }
                    }
                }
                else
                {
                    var colonIdx = line.IndexOf('：');
                    if (colonIdx > 0)
                    {
                        string key = line.Substring(0, colonIdx).Trim();
                        string val = line.Substring(colonIdx + 1).Trim();

                        // 注释_B64：解码后显示
                        if (key == "注释_B64")
                        {
                            try
                            {
                                val = Encoding.UTF8.GetString(Convert.FromBase64String(val));
                                if (string.IsNullOrEmpty(val)) val = "(无)";
                            }
                            catch { val = "(解码失败)"; }
                            key = "注释";
                        }

                        var item = new ListViewItem(key);
                        item.SubItems.Add(val);
                        listView.Items.Add(item);
                    }
                    else
                    {
                        // 尝试英文冒号分割
                        var colonIdxEn = line.IndexOf(':');
                        if (colonIdxEn > 0)
                        {
                            var item = new ListViewItem(line.Substring(0, colonIdxEn).Trim());
                            item.SubItems.Add(line.Substring(colonIdxEn + 1).Trim());
                            listView.Items.Add(item);
                        }
                        else
                        {
                            var item = new ListViewItem("");
                            item.SubItems.Add(line);
                            listView.Items.Add(item);
                        }
                    }
                }
            }

            // 处理末尾的 JSON 块
            if (inJsonBlock && jsonBuffer.Length > 0)
            {
                RenderJsonSection(listView, jsonSectionTitle, jsonBuffer.ToString());
            }
        }

        private void RenderJsonSection(ListView listView, string sectionTitle, string jsonData)
        {
            try
            {
                var trimmedJson = jsonData.Trim();
                List<Dictionary<string, object>> items = null;

                // 尝试解析为数组
                if (trimmedJson.StartsWith("["))
                {
                    items = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(trimmedJson);
                }
                // 尝试解析为单个对象
                else if (trimmedJson.StartsWith("{"))
                {
                    var singleItem = JsonConvert.DeserializeObject<Dictionary<string, object>>(trimmedJson);
                    if (singleItem != null)
                    {
                        items = new List<Dictionary<string, object>> { singleItem };
                    }
                }

                if (items != null && items.Count > 0)
                {
                    // 获取列名
                    var columns = new List<string>(items[0].Keys);

                    // 添加子项分隔符
                    var sepItem = new ListViewItem($"  └ {items.Count} 条记录") { Font = new Font("Microsoft YaHei", 8, FontStyle.Italic), ForeColor = Color.FromArgb(80, 80, 80) };
                    sepItem.SubItems.Add("");
                    listView.Items.Add(sepItem);

                    // 为每个 JSON 对象创建子行
                    foreach (var item in items)
                    {
                        for (int i = 0; i < columns.Count; i++)
                        {
                            var col = columns[i];
                            var value = item.TryGetValue(col, out var v) ? v?.ToString() ?? "" : "";
                            var subItem = new ListViewItem($"    {col}") { Font = new Font("Microsoft YaHei", 8), ForeColor = Color.FromArgb(40, 40, 40) };
                            subItem.SubItems.Add(value);
                            listView.Items.Add(subItem);
                        }
                        // 添加空行分隔
                        if (items.IndexOf(item) < items.Count - 1)
                        {
                            var spacer = new ListViewItem("") { BackColor = Color.FromArgb(250, 250, 250) };
                            spacer.SubItems.Add("");
                            listView.Items.Add(spacer);
                        }
                    }
                }
                else
                {
                    var noDataItem = new ListViewItem("  (无数据)") { Font = new Font("Microsoft YaHei", 8, FontStyle.Italic), ForeColor = Color.FromArgb(150, 150, 150) };
                    noDataItem.SubItems.Add("");
                    listView.Items.Add(noDataItem);
                }
            }
            catch (Exception)
            {
                // JSON 解析失败，显示原始内容
                var rawItem = new ListViewItem("  (JSON 解析失败)") { Font = new Font("Microsoft YaHei", 8, FontStyle.Italic), ForeColor = Color.FromArgb(200, 100, 100) };
                rawItem.SubItems.Add(jsonData);
                listView.Items.Add(rawItem);
            }
        }
    }

    /// <summary>
    /// 自定义 ListView，用于详细信息页面
    /// 禁用水平滚动，仅保留垂直滚动
    /// </summary>
    public class DetailListView : ListView
    {
        private const int WM_HSCROLL = 0x0114;
        private const int EM_GETSEL = 0x00B0;
        private const int EM_SETSEL = 0x00B1;

        protected override void WndProc(ref Message m)
        {
            // 拦截水平滚动消息，阻止水平滚动条显示
            if (m.Msg == WM_HSCROLL)
            {
                return;
            }

            const int LVM_GETEDITCONTROL = 0x1048;
            if (m.Msg == LVM_GETEDITCONTROL)
            {
                m.Result = IntPtr.Zero;
                return;
            }

            base.WndProc(ref m);
        }
    }
}