// ============================================================================
// 资源查询标签页 - 主文件（字段定义、构造函数、UI构建、子类型切换）
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using VmOps.Core;
using VmOps.Utils;

namespace VmOps.UI
{
    public partial class ResourceQueryTab
    {
        private readonly TabPage _parent;
        private readonly Form _root;
        private readonly Control _app;
        private readonly string _queryType;
        private readonly string _title;

        private ListView? _listView;
        private Button? _refreshBtn;
        private Label? _statusLabel;
        private TextBox? _searchBox;

        private Dictionary<string, Button> _subtypeButtons = new Dictionary<string, Button>();
        private string _currentSubtype = "";

        private List<string> _columns = new List<string>();
        private List<List<string>> _rows = new List<List<string>>();
        private List<List<string>> _allRowValues = new List<List<string>>();
        private Dictionary<string, SubtypeCacheItem> _subtypeCache = new Dictionary<string, SubtypeCacheItem>();
        private HashSet<string> _subtypeQueried = new HashSet<string>();

        private string _sortColumn = "";
        private bool _sortAsc = true;
        private bool _columnsAutoResized = false; // 标记列宽是否已计算过

        private DateTime _queryStartTime;
        private int _timeoutSec = 90;

        private ContextMenuStrip? _contextMenu;

        private static readonly List<(string key, string label)> VmSubtypes = new List<(string, string)>
        {
            ("vm", "虚拟机"),
            ("template", "模板"),
        };

        private static readonly Dictionary<string, List<string>> PresetColumns = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            { "vm_vm",       new List<string> { "Id","Name","PowerState","NumCpu","MemoryGB","IPAddress","VMHost","ResourcePool","Datastore","GuestOS" } },
            { "vm_template", new List<string> { "Id","Name","MemoryGB","NumCpu" } },
            { "host",        new List<string> { "Id","Name","ConnectionState","NumCpu","CpuUsagePercent","MemoryGB","MemoryUsagePercent","ProductVersion" } },
            { "datastore",   new List<string> { "Id","Name","CapacityGB","FreeSpaceGB","UsedPercent","Type" } },
            { "network",     new List<string> { "Id","Name","PortGroupType","VDSwitch","VLAN","NumPorts","NumPortsAvailable" } },
            { "template",    new List<string> { "Id","Name","MemoryGB","NumCpu" } },
        };

        private static readonly Dictionary<string, string> TitleMap = new Dictionary<string, string>
        {
            { "vm", "虚拟机列表" },
            { "host", "主机列表" },
            { "datastore", "存储列表" },
            { "network", "网络列表" },
            { "template", "模板列表" },
        };

        private const string PsWaitTaskFunc = @"function Wait-Task($task, $label) {
    $InformationPreference = 'Continue'
    $lastProgress = -1
    while ($true) {
        Start-Sleep -Seconds 1
        $t = Get-View -Id $task -Property Info.State,Info.Progress,Info.Error,Info.Result -ErrorAction SilentlyContinue
        if (-not $t) { continue }
        $p = $t.Info.Progress
        if ($p -ne $null -and $p -ne $lastProgress) { Write-Information ('PROGRESS:' + $label + ':' + $p); $lastProgress = $p }
        if ($t.Info.State -eq 'success') {
            $result = $t.Info.Result
            if ($result -and $result.Value) { Write-Information ('RESULT:' + $label + ':' + $result.Value) }
            if ($lastProgress -lt 100) { Write-Information ('PROGRESS:' + $label + ':100') }
            Write-Information ('DONE:' + $label); return $true
        }
        if ($t.Info.State -eq 'error') { $err = $t.Info.Error; Write-Information ('ERROR:' + $label + ':' + $err.LocalizedMessage); throw ($err.LocalizedMessage + ' | ' + $err.Message) }
    }
}
";

        private class SubtypeCacheItem
        {
            public List<string> Columns { get; set; } = new List<string>();
            public List<List<string>> Rows { get; set; } = new List<List<string>>();
            public string DispName { get; set; } = "";
            public int ElapsedMs { get; set; }
        }

        public bool ConnectedToVc { get; set; }
        public object? PsProcess { get; set; }
        public object? GuiUpdateQueue { get; set; }

        public ResourceQueryTab(TabPage parent, Form root, Control app, string queryType)
        {
            _parent = parent;
            _root = root;
            _app = app;
            _queryType = queryType;
            _title = TitleMap.TryGetValue(queryType, out var t) ? t : "资源列表";

            if (queryType == "network")
                _currentSubtype = "network";
            else if (queryType == "vm")
                _currentSubtype = "vm";
            else
                _currentSubtype = queryType;

            BuildUI();
        }

        private void BuildUI()
        {
            var frame = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = Padding.Empty, ColumnCount = 1, RowCount = 3
            };
            frame.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            frame.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiUtils.Px(44)));
            frame.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiUtils.Px(24)));
            frame.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = Padding.Empty,
                Margin = new Padding(6, 6, 6, 2)
            };

            _refreshBtn = new Button
            {
                Text = "刷新", Font = new Font("Microsoft YaHei", 10, FontStyle.Bold),
                BackColor = Color.White,
                ForeColor = ColorTranslator.FromHtml("#333333"),
                FlatStyle = FlatStyle.Standard,
                AutoSize = true,
                MinimumSize = new Size(90, 0),
                Margin = new Padding(2)
            };
            _refreshBtn.Click += (s, e) => DoQuery();
            toolbar.Controls.Add(_refreshBtn);

            var subtypePanel = new FlowLayoutPanel
            {
                AutoSize = true, Margin = new Padding(2), WrapContents = true, FlowDirection = FlowDirection.LeftToRight
            };

            if (_queryType == "vm")
            {
                foreach (var (key, label) in VmSubtypes)
                {
                    var btn = CreateSubtypeButton(key, label);
                    _subtypeButtons[key] = btn;
                    subtypePanel.Controls.Add(btn);
                }
                HighlightSubtype(_currentSubtype);
            }
            toolbar.Controls.Add(subtypePanel);

            var searchPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                Margin = new Padding(2), WrapContents = false, FlowDirection = FlowDirection.LeftToRight
            };
            var lblSearch = new Label
            {
                Text = "搜索:", AutoSize = true,
                Margin = new Padding(4)
            };
            searchPanel.Controls.Add(lblSearch);

            _searchBox = new TextBox
            {
                Width = 200, Font = new Font("Microsoft YaHei", 9), Margin = new Padding(2)
            };
            _searchBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    ApplySearch();
                }
            };
            searchPanel.Controls.Add(_searchBox);

            var lblSearchHint = new Label
            {
                Text = "按回车过滤", AutoSize = true,
                ForeColor = ColorTranslator.FromHtml("#888888"),
                Font = new Font("Microsoft YaHei", 8),
                Margin = new Padding(2, 6, 2, 2)
            };
            searchPanel.Controls.Add(lblSearchHint);
            toolbar.Controls.Add(searchPanel);

            var btnExport = new Button
            {
                Text = "导出", Font = new Font("Microsoft YaHei", 10), FlatStyle = FlatStyle.Standard,
                AutoSize = true,
                MinimumSize = new Size(90, 0),
                Margin = new Padding(2)
            };
            btnExport.Click += (s, e) => ExportCsv();
            toolbar.Controls.Add(btnExport);

            _statusLabel = new Label
            {
                Text = "点击「刷新」按钮加载数据", ForeColor = ColorTranslator.FromHtml("#888888"), Font = new Font("Microsoft YaHei", 9), AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new Padding(0, 4, 0, 4)
            };

            var treeContainer = new Panel
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle
            };

            _listView = new NonEditableListView
            {
                Dock = DockStyle.Fill, Font = new Font("Microsoft YaHei", 9), FullRowSelect = true,
                GridLines = true,
                View = View.Details,
                MultiSelect = true,
                HeaderStyle = ColumnHeaderStyle.Clickable,
                LabelEdit = false
            };

            _listView.ColumnClick += ListView_ColumnClick;
            _listView.BeforeLabelEdit += ListView_BeforeLabelEdit;
            _listView.MouseDoubleClick += ListView_MouseDoubleClick;
            _listView.SizeChanged += ListView_SizeChanged;
            _listView.SelectedIndexChanged += (s, e) => RefreshStatusBar();

            treeContainer.Controls.Add(_listView);

            SetupContextMenu();

            frame.Controls.Add(toolbar);
            frame.SetRow(toolbar, 0);
            frame.Controls.Add(_statusLabel);
            frame.SetRow(_statusLabel, 1);
            frame.Controls.Add(treeContainer);
            frame.SetRow(treeContainer, 2);

            _parent.Controls.Add(frame);

            ApplyInitialColumns();
        }

        private void ApplyInitialColumns()
        {
            if (_listView == null) return;

            string key = (_queryType == "vm" ? "vm_" : "") + _currentSubtype;
            if (!PresetColumns.TryGetValue(key, out var cols)) return;

            _listView.BeginUpdate();
            _listView.Columns.Clear();
            _listView.Items.Clear();

            foreach (var col in cols)
            {
                string displayName = GetColumnDisplayName(col);
                var ch = new ColumnHeader { Text = displayName, Width = 100, Tag = col };
                _listView.Columns.Add(ch);
            }

            _listView.EndUpdate();
        }

        private Button CreateSubtypeButton(string key, string label)
        {
            var btn = new Button
            {
                Text = label,
                Font = new Font("Microsoft YaHei", 10), BackColor = Color.White,
                ForeColor = ColorTranslator.FromHtml("#333333"),
                FlatStyle = FlatStyle.Standard,
                AutoSize = true,
                MinimumSize = new Size(90, 0),
                Margin = new Padding(2)
            };
            btn.Click += (s, e) => SwitchSubtype(key);
            return btn;
        }

        private void HighlightSubtype(string activeKey)
        {
            foreach (var kvp in _subtypeButtons)
            {
                if (kvp.Key == activeKey)
                {
                    kvp.Value.BackColor = ColorTranslator.FromHtml("#2E7D32");
                    kvp.Value.ForeColor = Color.White;
                }
                else
                {
                    kvp.Value.BackColor = Color.White;
                    kvp.Value.ForeColor = ColorTranslator.FromHtml("#333333");
                }
            }
        }

        private void SwitchSubtype(string subKey)
        {
            if (_queryType != "network" && _queryType != "vm") return;
            if (_currentSubtype == subKey) return;

            _searchBox.Text = "";
            _currentSubtype = subKey;
            _columnsAutoResized = false;
            HighlightSubtype(subKey);
            ApplyInitialColumns();

            string cacheKey = GetCurrentCacheKey();
            _subtypeQueried.Add(cacheKey);
            _listView?.Items.Clear();
            DoQuery();
        }

        private string GetCurrentCacheKey()
        {
            if (_queryType == "vm") return _currentSubtype;
            if (_queryType == "network") return _currentSubtype;
            return _queryType;
        }

        private string CurrentCacheKey() => GetCurrentCacheKey();

        public void OnClosing()
        {
            try
            {
            }
            catch { }
        }

        public void OnTabActivated()
        {
            try
            {
                string key = GetCurrentCacheKey();
                if (_subtypeQueried.Contains(key)) return;
                if (SessionManager.Instance.IsConnected)
                {
                    _subtypeQueried.Add(key);
                    LogMessage("信息", $"切换到【{_title}】，自动开始查询…");
                    DoQuery();
                }
            }
            catch { }
        }

        private void SetupContextMenu()
        {
            _contextMenu = new ContextMenuStrip();

            if (_listView != null)
            {
                _listView.MouseDown += (s, e) =>
                {
                    if (e.Button == MouseButtons.Right)
                    {
                        ShowContextMenu(e.Location);
                    }
                };
            }
        }

        private void ShowContextMenu(Point location)
        {
            if (_contextMenu == null || _listView == null) return;

            _contextMenu.Items.Clear();

            ListViewItem? item = null;
            try
            {
                var hitTest = _listView.HitTest(location);
                item = hitTest.Item;
            }
            catch { }

            if (item != null)
            {
                // 如果右键点击的项未被选中，则只选中该项；否则保持当前多选状态
                if (!item.Selected)
                {
                    _listView.SelectedItems.Clear();
                    item.Selected = true;
                }
            }

            var selectedRow = item != null ? GetItemRowValues(item) : null;
            
            var selectedRows = new List<List<string>>();
            foreach (ListViewItem selItem in _listView.SelectedItems)
            {
                var rowVals = GetItemRowValues(selItem);
                if (rowVals != null)
                    selectedRows.Add(rowVals);
            }

            var supportedTypes = new[] { "vm", "host", "datastore", "network", "template" };
            if (supportedTypes.Contains(_queryType) && item != null && _currentSubtype != "spec")
            {
                _contextMenu.Items.Add(new ToolStripMenuItem("📋 详细信息", null, (s, e) => ShowDetailFromItem(item)));
                _contextMenu.Items.Add(new ToolStripSeparator());
            }

            if (_queryType == "vm" && _currentSubtype == "template" && selectedRow != null)
            {
                _contextMenu.Items.Add(new ToolStripMenuItem("✏ 重命名模板", null, (s, e) => RenameTemplate(selectedRow)));
                _contextMenu.Items.Add(new ToolStripMenuItem("📝 编辑备注", null, (s, e) => EditVmNotes(selectedRow)));
                _contextMenu.Items.Add(new ToolStripMenuItem("🗑 删除模板", null, (s, e) => DeleteTemplate(selectedRow)));
                _contextMenu.Items.Add(new ToolStripMenuItem("🔄 转为虚拟机", null, (s, e) => ConvertTemplateToVm(selectedRow)));
                _contextMenu.Items.Add(new ToolStripSeparator());
            }

            if (_queryType == "vm" && _currentSubtype != "spec" && _currentSubtype != "template" && selectedRow != null)
            {
                var remoteMenu = new ToolStripMenuItem("🌐 远程连接");
                remoteMenu.DropDownItems.Add("🖥 VMRC 连接", null, (s, e) => VmrcConnect(selectedRow));
                remoteMenu.DropDownItems.Add("🖥 RDP 连接", null, (s, e) => RdpConnect(selectedRow));
                remoteMenu.DropDownItems.Add("🔧 SSH 连接 (MobaXterm)", null, (s, e) => SshConnectToVm(selectedRow));
                _contextMenu.Items.Add(remoteMenu);
                _contextMenu.Items.Add(new ToolStripSeparator());
            }

            if (_queryType == "host" && selectedRow != null)
            {
                _contextMenu.Items.Add(new ToolStripMenuItem("🔧 SSH 连接 (MobaXterm)", null, (s, e) => SshConnectToHost(selectedRow)));
                _contextMenu.Items.Add(new ToolStripMenuItem("🌐 使用 Web 打开管理地址", null, (s, e) => OpenHostWebConsole(selectedRow)));
                _contextMenu.Items.Add(new ToolStripSeparator());
            }

            if (_queryType == "vm" && _currentSubtype != "spec" && _currentSubtype != "template" && selectedRow != null)
            {
                var powerMenu = new ToolStripMenuItem("🟢 电源管理");
                powerMenu.DropDownItems.Add("▶ 打开电源", null, (s, e) => PowerOnVm(selectedRow));
                powerMenu.DropDownItems.Add("■ 关闭客户机", null, (s, e) => PowerOffVm(selectedRow));
                powerMenu.DropDownItems.Add("✖ 关闭电源", null, (s, e) => PowerOffVmForce(selectedRow));
                powerMenu.DropDownItems.Add("↻ 重置电源", null, (s, e) => RestartVm(selectedRow));
                _contextMenu.Items.Add(powerMenu);
                _contextMenu.Items.Add(new ToolStripSeparator());

                var configMenu = new ToolStripMenuItem("⚙ 编辑配置");
                configMenu.DropDownItems.Add("🔲 修改 vCPU", null, (s, e) => EditCpuOnly(selectedRow));
                configMenu.DropDownItems.Add("📊 修改内存", null, (s, e) => EditMemoryOnly(selectedRow));
                configMenu.DropDownItems.Add("🌐 网卡配置", null, (s, e) => EditPortgroupOnly(selectedRow));
                configMenu.DropDownItems.Add("🌐 配置客户机网络", null, (s, e) => GuestOpsNetworkConfig(selectedRow));
                configMenu.DropDownItems.Add("🔑 密码配置", null, (s, e) => GuestOpsPasswordConfig(selectedRow));
                configMenu.DropDownItems.Add(new ToolStripSeparator());
                configMenu.DropDownItems.Add("✏ 重命名虚拟机", null, (s, e) => RenameVm(selectedRow));
                configMenu.DropDownItems.Add("📝 编辑备注", null, (s, e) => EditVmNotes(selectedRow));
                _contextMenu.Items.Add(configMenu);
                _contextMenu.Items.Add(new ToolStripSeparator());

                _contextMenu.Items.Add(new ToolStripMenuItem("💾 磁盘管理", null, (s, e) => EditHardDisk(selectedRow)));
                _contextMenu.Items.Add(new ToolStripSeparator());

                var migrateCloneMenu = new ToolStripMenuItem("🔄 迁移克隆");
                migrateCloneMenu.DropDownItems.Add("↔ 迁移到主机 (vMotion)", null, (s, e) => MigrateVmToHost(selectedRow));
                migrateCloneMenu.DropDownItems.Add("📦 迁移到资源池", null, (s, e) => MigrateVmToResourcePool(selectedRow));
                migrateCloneMenu.DropDownItems.Add("💾 迁移到存储 (Storage vMotion)", null, (s, e) => MigrateVmToDatastore(selectedRow));
                migrateCloneMenu.DropDownItems.Add(new ToolStripSeparator());
                migrateCloneMenu.DropDownItems.Add("🆕 克隆虚拟机", null, (s, e) => CloneVm(selectedRow));
                _contextMenu.Items.Add(migrateCloneMenu);
                _contextMenu.Items.Add(new ToolStripSeparator());

                var advancedMenu = new ToolStripMenuItem("🟡 高级操作");
                advancedMenu.DropDownItems.Add("⏏ 断开光驱（卸载 ISO）", null, (s, e) => DisconnectCdDrive(selectedRow));
                advancedMenu.DropDownItems.Add("⚙ 优化配置 (VMX)", null, (s, e) => OptimizeVmConfig(selectedRow));
                advancedMenu.DropDownItems.Add(new ToolStripSeparator());
                advancedMenu.DropDownItems.Add("🔄 转为模板", null, (s, e) => ConvertVmToTemplate(selectedRow));
                advancedMenu.DropDownItems.Add("🗑 删除虚拟机", null, (s, e) => DeleteVm(selectedRow));
                _contextMenu.Items.Add(advancedMenu);
                _contextMenu.Items.Add(new ToolStripSeparator());

                var snapshotMenu = new ToolStripMenuItem("📷 快照管理");
                snapshotMenu.DropDownItems.Add("📷 查看快照列表", null, (s, e) => ListSnapshots(selectedRow));
                snapshotMenu.DropDownItems.Add("📸 创建新快照", null, (s, e) => CreateSnapshot(selectedRow));
                snapshotMenu.DropDownItems.Add("↩ 回滚到最新快照", null, (s, e) => RevertToLatestSnapshot(selectedRow));
                snapshotMenu.DropDownItems.Add("🗑 删除所有快照", null, (s, e) => RemoveAllSnapshots(selectedRow));
                _contextMenu.Items.Add(snapshotMenu);
                _contextMenu.Items.Add(new ToolStripSeparator());
            }

            if (_queryType == "vm" && _currentSubtype != "spec" && _currentSubtype != "template" && selectedRows.Count > 0)
            {
                var batchMenu = new ToolStripMenuItem($"⚡ 批量操作（已选 {selectedRows.Count} 台）");
                batchMenu.DropDownItems.Add("▶ 批量打开电源", null, (s, e) => BatchPowerOn(selectedRows));
                batchMenu.DropDownItems.Add("■ 批量关闭客户机", null, (s, e) => BatchPowerOff(selectedRows));
                batchMenu.DropDownItems.Add("✖ 批量关闭电源", null, (s, e) => BatchPowerOffForce(selectedRows));
                batchMenu.DropDownItems.Add("↻ 批量重置电源", null, (s, e) => BatchRestart(selectedRows));
                batchMenu.DropDownItems.Add(new ToolStripSeparator());
                batchMenu.DropDownItems.Add("🗑 批量删除虚拟机（从磁盘）", null, (s, e) => BatchDeleteVm(selectedRows));
                batchMenu.DropDownItems.Add(new ToolStripSeparator());
                batchMenu.DropDownItems.Add("📷 批量创建快照", null, (s, e) => BatchCreateSnapshot(selectedRows));
                batchMenu.DropDownItems.Add("🗑 批量删除所有快照", null, (s, e) => BatchRemoveAllSnapshots(selectedRows));
                batchMenu.DropDownItems.Add(new ToolStripSeparator());
                var batchMigrateMenu = new ToolStripMenuItem("🔄 批量迁移");
                batchMigrateMenu.DropDownItems.Add("↔ 批量迁移到主机 (vMotion)", null, (s, e) => BatchMigrateToHost(selectedRows));
                batchMigrateMenu.DropDownItems.Add("📦 批量迁移到资源池", null, (s, e) => BatchMigrateToResourcePool(selectedRows));
                batchMigrateMenu.DropDownItems.Add("💾 批量迁移到存储 (Storage vMotion)", null, (s, e) => BatchMigrateToDatastore(selectedRows));
                batchMenu.DropDownItems.Add(batchMigrateMenu);
                _contextMenu.Items.Add(batchMenu);
                _contextMenu.Items.Add(new ToolStripSeparator());
            }

            int selCount = _listView.SelectedItems.Count;

            var copyShareMenu = new ToolStripMenuItem("📋 复制分享");

            if (_queryType == "vm" && _currentSubtype == "vm" && item != null)
            {
                copyShareMenu.DropDownItems.Add(new ToolStripMenuItem("📤 分享信息", null, (s, e) => CopyVmShareInfo(item)));
                copyShareMenu.DropDownItems.Add(new ToolStripSeparator());
            }

            if (selCount > 0)
            {
                copyShareMenu.DropDownItems.Add(new ToolStripMenuItem($"复制选中行 (共 {selCount} 行)", null, (s, e) => CopySelectedRows()));
            }
            else
            {
                var item_disabled = new ToolStripMenuItem("复制选中行");
                item_disabled.Enabled = false;
                copyShareMenu.DropDownItems.Add(item_disabled);
            }

            copyShareMenu.DropDownItems.Add(new ToolStripMenuItem($"复制全部 (共 {_allRowValues.Count} 行)", null, (s, e) => CopyAll()));
            copyShareMenu.DropDownItems.Add(new ToolStripMenuItem("复制列标题", null, (s, e) => CopyColumnHeaders()));

            _contextMenu.Items.Add(copyShareMenu);

            _contextMenu.Show(_listView, location);
        }

        private List<string>? GetItemRowValues(ListViewItem item)
        {
            if (item.Tag is List<string> values)
                return values;
            return null;
        }

        private string GetQueryScript()
        {
            if (_queryType == "vm")
                return QueryBuilder.GetJsonQueryScript(_currentSubtype);
            if (_queryType == "network")
                return QueryBuilder.GetJsonQueryScript(_currentSubtype);
            return QueryBuilder.GetJsonQueryScript(_queryType);
        }

        private static string GetColumnDisplayName(string columnName)
        {
            var map = new Dictionary<string, string>
            {
                { "Name", "名称" }, { "PowerState", "电源状态" }, { "NumCpu", "CPU" },
                { "MemoryGB", "内存" }, { "MemoryUsagePercent", "内存使用率(%)" },
                { "VMHost", "所在主机" }, { "ResourcePool", "资源池" }, { "Datastore", "存储" },
                { "IPAddress", "IP 地址" },
                { "CpuUsagePercent", "CPU使用率(%)" },
                { "GuestOS", "来宾系统" }, { "Id", "ID" }, { "ConnectionState", "连接状态" },
                { "CapacityGB", "容量(GB)" }, { "FreeSpaceGB", "空闲(GB)" }, { "UsedPercent", "使用率(%)" },
                { "PortGroupType", "类型" }, { "VDSwitch", "分布式交换机" }, { "VLAN", "VLAN" },
                { "NumPorts", "端口数" }, { "NumPortsAvailable", "可用端口" },
                { "ProductVersion", "ESXi 版本" }, { "Description", "描述" }, { "Type", "类型" }, { "LastUpdated", "最后更新" }
            };
            return map.TryGetValue(columnName, out var displayName) ? displayName : columnName;
        }

        private static int GetColumnWidth(string columnName)
        {
            // 初始占位值，创建列后立即被 AutoResizeColumns 按固定值 + 内容自适应覆盖，此处无实际业务意义
            return 100;
        }

        public void SetBusinessButtonsState(bool enabled)
        {
            if (_refreshBtn != null)
                _refreshBtn.Enabled = enabled;
        }

        public void ClearAllCache()
        {
            _allRowValues.Clear();
            _rows.Clear();
            _columns.Clear();
            _subtypeCache.Clear();
            _subtypeQueried.Clear();
            _columnsAutoResized = false;

            if (_searchBox != null) _searchBox.Text = "";
            if (_listView != null)
            {
                _listView.Items.Clear();
                _listView.Columns.Clear();
            }

            ApplyInitialColumns();
            UpdateStatus("未连接 vCenter", ColorTranslator.FromHtml("#757575"));
        }

        private string GetVmIdFromRow(List<string> row)
        {
            int idIdx = -1;
            for (int i = 0; i < _columns.Count; i++)
            {
                if (_columns[i].Equals("Id", StringComparison.OrdinalIgnoreCase))
                {
                    idIdx = i;
                    break;
                }
            }
            if (idIdx < 0 || idIdx >= row.Count) return "";
            return row[idIdx];
        }

        internal static string FullVmRef(string shortId)
        {
            return FullMoRef(shortId);
        }

        internal static string FullMoRef(string shortId)
        {
            if (string.IsNullOrEmpty(shortId)) return "";
            if (shortId.Contains("-") && !shortId.StartsWith("vm-") && !shortId.StartsWith("host-") && !shortId.StartsWith("datastore-") && !shortId.StartsWith("dvportgroup-") && !shortId.StartsWith("dvpg-") && !shortId.StartsWith("network-") && !shortId.StartsWith("dvswitch-") && !shortId.StartsWith("dvs-") && !shortId.StartsWith("resourcepool-") && !shortId.StartsWith("folder-") && !shortId.StartsWith("datacenter-"))
                return shortId;
            string lower = shortId.ToLowerInvariant();
            if (lower.StartsWith("vm-")) return "VirtualMachine-" + shortId;
            if (lower.StartsWith("host-")) return "HostSystem-" + shortId;
            if (lower.StartsWith("datastore-")) return "Datastore-" + shortId;
            if (lower.StartsWith("dvportgroup-") || lower.StartsWith("dvpg-")) return "DistributedVirtualPortgroup-" + shortId;
            if (lower.StartsWith("network-")) return "Network-" + shortId;
            if (lower.StartsWith("dvswitch-") || lower.StartsWith("dvs-")) return "DistributedVirtualSwitch-" + shortId;
            if (lower.StartsWith("resourcepool-")) return "ResourcePool-" + shortId;
            if (lower.StartsWith("folder-")) return "Folder-" + shortId;
            if (lower.StartsWith("datacenter-")) return "Datacenter-" + shortId;
            return shortId;
        }

        private string EscapePsString(string s)
        {
            return s.Replace("'", "''");
        }

        private int GetIntFromRow(List<string> row, string fieldName)
        {
            int idx = _columns.FindIndex(c => c.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
            if (idx < 0 || idx >= row.Count) return 0;
            int.TryParse(row[idx].Trim(), out int val);
            return val;
        }

        private string GetVmNameFromRow(List<string> row)
        {
            int idx = _columns.FindIndex(c => c.Equals("Name", StringComparison.OrdinalIgnoreCase));
            if (idx < 0) idx = 0;
            if (idx < row.Count) return row[idx].Trim();
            return "";
        }

        private string GetVmIpFromRow(List<string> row)
        {
            int idx = _columns.FindIndex(c => c.IndexOf("IP", StringComparison.OrdinalIgnoreCase) >= 0);
            if (idx < 0 && _columns.Count > 4) idx = 4;
            if (idx >= 0 && idx < row.Count)
            {
                string ip = row[idx].Trim();
                if (System.Text.RegularExpressions.Regex.IsMatch(ip, @"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$"))
                    return ip;
            }
            return "";
        }

        private string GetHostNameFromRow(List<string> row)
        {
            int idx = _columns.FindIndex(c => c.Equals("Name", StringComparison.OrdinalIgnoreCase));
            if (idx < 0) idx = 0;
            if (idx < row.Count) return row[idx].Trim();
            return "";
        }

        private string FindMobaXterm()
        {
            string[] paths = {
                @"C:\Program Files (x86)\Mobatek\MobaXterm\MobaXterm.exe",
                @"C:\Program Files\Mobatek\MobaXterm\MobaXterm.exe"
            };
            foreach (var p in paths)
            {
                if (File.Exists(p)) return p;
            }
            return "";
        }

        private void CopySelectedRows()
        {
            if (_listView?.SelectedItems.Count == 0) return;
            var lines = new List<string>();
            lines.Add(string.Join("\t", _columns));
            foreach (ListViewItem item in _listView.SelectedItems)
            {
                var values = new List<string>();
                for (int i = 0; i < item.SubItems.Count; i++)
                    values.Add(item.SubItems[i].Text);
                lines.Add(string.Join("\t", values));
            }
            CopyText(string.Join("\n", lines));
            ShowCopyHint("选中行");
        }

        private void CopyVmShareInfo(ListViewItem item)
        {
            if (_listView == null || item == null) return;

            string name = "", cpu = "", mem = "", os = "", ip = "";
            string vmhost = "", datastore = "";
            for (int i = 0; i < _listView.Columns.Count && i < item.SubItems.Count; i++)
            {
                string colName = _listView.Columns[i].Text?.Trim() ?? "";
                string val = item.SubItems[i].Text?.Trim() ?? "";
                switch (colName)
                {
                    case "名称": name = val; break;
                    case "CPU": cpu = val; break;
                    case "内存": mem = val; break;
                    case "来宾系统": os = val; break;
                    case "IP 地址": ip = val; break;
                    case "所在主机": vmhost = val; break;
                    case "存储": datastore = val; break;
                }
            }

            string vmName = name;
            if (string.IsNullOrEmpty(vmName)) return;

            Task.Run(async () =>
            {
                string disk = "", notes = "";
                try
                {
                    string esc = EscapePsString(vmName);
                    string escIp = EscapePsString(ip);
                    string script =
                        "$ErrorActionPreference = 'Stop'; " +
                        $"$ip = '{escIp}'; " +
                        $"$vm = Get-View -ViewType VirtualMachine -Filter @{{Name='{esc}'; 'Config.Template'='False'}} -Property Config.Hardware.Device,Config.Annotation,Guest,Runtime.Host,Datastore -ErrorAction Stop; " +
                        "if (-not $vm) { throw 'VM not found' }; " +
                        "$disks = @($vm.Config.Hardware.Device | Where-Object { $_ -is [VMware.Vim.VirtualDisk] } | Sort-Object ControllerKey, UnitNumber); " +
                        "$sysGB = $null; $dataGBs = @(); $idx = 0; " +
                        "foreach ($d in $disks) { " +
                        "  $gb = [math]::Round($d.CapacityInBytes / 1GB, 1); " +
                        "  if ($idx -eq 0) { $sysGB = $gb } else { $dataGBs += $gb }; $idx++ " +
                        "}; " +
                        "$notesB64 = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($vm.Config.Annotation)); " +
                        "if (-not $ip) { " +
                        "  if ($vm.Guest -and $vm.Guest.IpAddress) { " +
                        "    $ips = $vm.Guest.IpAddress | Where-Object { $_ -match '^\\d{1,3}(\\.\\d{1,3}){3}$' }; " +
                        "    $ip = ($ips -join ','); " +
                        "  }; " +
                        "}; " +
                        "[PSCustomObject]@{'SysDiskGB'=$sysGB; 'DataDisksGB'=($dataGBs -join ','); 'NotesB64'=$notesB64; 'IP'=$ip}; ";
                    var results = await SessionManager.Instance.ExecuteQueryAsync(script);
                    if (results != null && results.Count > 0)
                    {
                        results[0].TryGetValue("SysDiskGB", out var sysDiskObj);
                        results[0].TryGetValue("DataDisksGB", out var dataDisksObj);
                        results[0].TryGetValue("NotesB64", out var notesB64Obj);
                        decimal? sysDisk = null;
                        if (decimal.TryParse(sysDiskObj?.ToString(), out var sd)) sysDisk = sd;
                        string dataDisksStr = dataDisksObj?.ToString() ?? "";
                        var dataDisks = new List<decimal>();
                        foreach (var s in dataDisksStr.Split(','))
                        {
                            if (decimal.TryParse(s.Trim(), out var dd)) dataDisks.Add(dd);
                        }
                        string notesB64 = notesB64Obj?.ToString() ?? "";
                        try { notes = string.IsNullOrEmpty(notesB64) ? "" : Encoding.UTF8.GetString(Convert.FromBase64String(notesB64)); }
                        catch { notes = ""; }
                        results[0].TryGetValue("IP", out var ipObj);
                        string ipFromQuery = ipObj?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(ipFromQuery)) ip = ipFromQuery;

                        var diskParts = new List<string>();
                        if (sysDisk.HasValue) diskParts.Add($"系统盘 {sysDisk.Value:0.##}G");
                        for (int i = 0; i < dataDisks.Count; i++) diskParts.Add($"数据盘{(dataDisks.Count > 1 ? (i + 1).ToString() : "")} {dataDisks[i]:0.##}G");
                        disk = string.Join("，", diskParts);
                    }
                }
                catch { }

                _root?.BeginInvoke(new Action(() =>
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("「虚拟服务器信息分享」");
                    sb.AppendLine($"名称：{vmName}");
                    sb.AppendLine($"IP地址：{ip}");
                    sb.AppendLine($"CPU：{(cpu.Length > 0 ? cpu + " vCPU" : "")}");
                    sb.AppendLine($"内存：{(mem.Length > 0 ? mem + " GB" : "")}");
                    sb.AppendLine($"磁盘：{(disk.Length > 0 ? disk : "")}");
                    sb.AppendLine($"操作系统：{os}");
                    sb.AppendLine("初始密码：");
                    string notesDisplay = notes?.Replace("\r\n", " ")?.Replace("\n", " ")?.Replace("\r", " ") ?? "";
                    sb.AppendLine($"备注：{notesDisplay}");

                    CopyText(sb.ToString().TrimEnd());
                    ToastUtils.ShowToast(_root, "虚拟服务器分享信息已复制到剪贴板");
                }));
            });
        }
    }

    /// <summary>
    /// 自定义 ListView，完全禁止标签编辑行为
    /// 通过拦截 Windows 消息防止双击第一列时进入编辑模式
    /// </summary>
    public class NonEditableListView : ListView
    {
        protected override void WndProc(ref Message m)
        {
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