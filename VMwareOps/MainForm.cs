// ============================================================================
// MainForm.cs - 应用主窗口
// ============================================================================
// 布局：
//   ✅ 顶部标题栏（版本号来自 AppVersion.VERSION_STR）
//   ✅ 主体 TabControl — 多标签页（HomeTab / ResourceQueryTab / CsvEditorTab / OVFToolTab 等）
//   ✅ 底部状态栏（Running status label + Output RichTextBox 彩色日志）
//
// 生命周期：
//   构造函数 AutoScaleMode = AutoScaleMode.Dpi
//   Load 事件 → 初始化所有 Tab → 启动 DpiChanged 监听 → 连接 SessionManager
//
// 部分类拆分：无（单文件，Tab 拆分为独立类）
// ============================================================================
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using VMwareOps.Core;
using VMwareOps.Utils;
using VMwareOps.UI;

namespace VMwareOps
{
    public partial class MainForm : Form
    {
        private const int WINDOW_BASE_WIDTH = 1100;
        private const int WINDOW_BASE_HEIGHT = 750;
        private const string STATUS_BAR_BG = "#F5F7FA";
        private TabControl? notebook;
        private Panel? status_bar;
        private Label? running_status_label;
        private RichTextBox? output_text;
        private Button? btn_help;
        private Button? btn_check_update;

        // 状态栏左侧图标按钮
        private Button? btn_status_icon;
        private Button? btn_web;
        private Button? btn_extensions;
        private Button? btn_cmd;
        private Button? btn_ps;
        private Panel? left_icons_panel;
        private ToolTip? status_bar_tool_tip;

        // 跑马灯定时器
        private System.Windows.Forms.Timer? _marqueeTimer;
        private int _marqueeOffset = 0;
        private bool _marqueePaused = false;
        private string _marqueeFullText = "";
        private bool _marqueeOverflow = false;
        private float _marqueeTextWidth = 0;
        private int _marqueeVisibleWidth = 0;
        private Panel? full_log_view;
        private GroupBox? log_frame;
        private bool log_panel_expanded = false;
        private Panel? notebook_container;

        // Tab实例
        private HomeTab? home_tab;
        private ResourceQueryTab? vm_list_tab;
        private ResourceQueryTab? host_list_tab;
        private ResourceQueryTab? datastore_list_tab;
        private ResourceQueryTab? network_list_tab;
        private CsvEditorTab? csv_edit_tab;
        private OVFToolTab? ovf_tool_tab;
        private Dictionary<string, object>? _appContext;

        // 日志自动滚动
        private bool auto_scroll = true;

        private readonly ConcurrentQueue<(string text, string level)> _logQueue = new ConcurrentQueue<(string, string)>();
        private System.Windows.Forms.Timer? _logFlushTimer;
        private const int LOG_FLUSH_INTERVAL_MS = 200;
        private const int LOG_QUEUE_MAX = 500;

        public MainForm()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            InitializeComponent();
        }
        private void SetupFonts()
        {
            this.Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular);
        }
        private void LoadIcon()
        {
            try
            {
                var icon = CoreUtils.GetAppIcon();
                if (icon != null)
                {
                    this.Icon = icon;
                }
            }
            catch { }
        }
        private void CenterWindow(int width, int height)
        {
            try
            {
                int screenW = Screen.PrimaryScreen!.WorkingArea.Width;
                int screenH = Screen.PrimaryScreen!.WorkingArea.Height;
                int x = Math.Max((screenW - width) / 2, 0);
                int y = Math.Max((screenH - height) / 2 - (int)(screenH * 0.05), 0);
                this.StartPosition = FormStartPosition.Manual;
                this.Location = new Point(x, y);
            }
            catch
            {
                this.StartPosition = FormStartPosition.CenterScreen;
            }
        }
        // 使用 TableLayoutPanel 作为状态栏总容器，5列：图标区 | 分割线 | 日志区 | 分割线 | 按钮区
        private void CreateStatusBar()
        {
            Color bgColor = ColorTranslator.FromHtml(STATUS_BAR_BG);
            int iconSize = 24;

            // ====== 外层 TableLayoutPanel（状态栏总容器）======
            var statusLayout = new TableLayoutPanel
            {
                Name = "status_bar",
                Dock = DockStyle.Bottom,
                Height = DpiUtils.Px(28),
                BackColor = bgColor,
                Cursor = Cursors.Hand,
                ColumnCount = 5,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = new Padding(4, 2, 4, 2)
            };
            statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiUtils.Px(28)));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));          // 列0：左侧图标区
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1));        // 列1：分割线1
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));       // 列2：中间日志区
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1));        // 列3：分割线2
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, DpiUtils.Px(200)));        // 列4：右侧按钮区

            status_bar = statusLayout;

            // ToolTip 组件（用于图标按钮的悬浮提示）
            status_bar_tool_tip = new ToolTip();

            // ====== 列0：左侧图标按钮区域 ======
            left_icons_panel = new FlowLayoutPanel
            {
                BackColor = bgColor,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            // 1. 连接状态图标按钮
            btn_status_icon = CreateIconButton("\u25CF", ColorTranslator.FromHtml("#888888"), iconSize);
            status_bar_tool_tip.SetToolTip(btn_status_icon, "未连接 vCenter");
            btn_status_icon.Click += StatusIcon_Click;
            left_icons_panel.Controls.Add(btn_status_icon);

            // 2. Web 图标按钮
            btn_web = CreateIconButton("\uD83C\uDF10", ColorTranslator.FromHtml("#1976D2"), iconSize);
            status_bar_tool_tip.SetToolTip(btn_web, "打开 vCenter Web 管理页面");
            btn_web.Click += BtnWeb_Click;
            left_icons_panel.Controls.Add(btn_web);

            // 2.5 扩展图标按钮
            btn_extensions = CreateIconButton("\uD83D\uDD0C", ColorTranslator.FromHtml("#7B1FA2"), iconSize);
            status_bar_tool_tip.SetToolTip(btn_extensions, "扩展管理");
            btn_extensions.Click += BtnExtensions_Click;
            left_icons_panel.Controls.Add(btn_extensions);

            // 3. CMD 图标按钮
            btn_cmd = CreateIconButton("\u2318", ColorTranslator.FromHtml("#2E7D32"), iconSize);
            status_bar_tool_tip.SetToolTip(btn_cmd, "SSH 连接到 VCSA (root)");
            btn_cmd.Click += BtnCmd_Click;
            left_icons_panel.Controls.Add(btn_cmd);

            // 4. PS 图标按钮
            btn_ps = CreateIconButton("PS", ColorTranslator.FromHtml("#6A1B9A"), iconSize);
            status_bar_tool_tip.SetToolTip(btn_ps, "打开 PowerShell 连接 vCenter");
            btn_ps.Click += BtnPs_Click;
            left_icons_panel.Controls.Add(btn_ps);

            statusLayout.Controls.Add(left_icons_panel, 0, 0);

            // ====== 列1：第一条垂直分割线 ======
            var sep1 = new Panel
            {
                BackColor = ColorTranslator.FromHtml("#CCCCCC"),
                Dock = DockStyle.Fill,
                Margin = Padding.Empty
            };
            statusLayout.Controls.Add(sep1, 1, 0);

            // ====== 列2：中间日志显示区域（Label + 跑马灯）======
            running_status_label = new Label
            {
                Name = "running_status_label",
                Text = "\uD83D\uDCA1 就绪 - 点击此处展开完整日志",
                BackColor = bgColor,
                ForeColor = ColorTranslator.FromHtml("#555555"),
                Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular),
                Cursor = Cursors.Hand,
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(4, 0, 4, 0)
            };
            running_status_label.Click += RunningStatusLabel_Click;
            running_status_label.MouseEnter += (s, e) => _marqueePaused = true;
            running_status_label.MouseLeave += (s, e) => _marqueePaused = false;
            statusLayout.Controls.Add(running_status_label, 2, 0);

            // 初始化跑马灯定时器
            _marqueeTimer = new System.Windows.Forms.Timer();
            _marqueeTimer.Interval = 60;
            _marqueeTimer.Tick += MarqueeTimer_Tick;
            _marqueeTimer.Start();

            // ====== 列3：第二条垂直分割线 ======
            var sep2 = new Panel
            {
                BackColor = ColorTranslator.FromHtml("#CCCCCC"),
                Dock = DockStyle.Fill,
                Margin = Padding.Empty
            };
            statusLayout.Controls.Add(sep2, 3, 0);

            // ====== 列4：右侧按钮区域 ======
            var right_btns = new TableLayoutPanel
            {
                BackColor = bgColor,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                RowCount = 1,
                ColumnCount = 2
            };
            right_btns.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
            right_btns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            right_btns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            btn_help = new Button
            {
                Name = "btn_help",
                Text = "帮助",
                Height = 24,
                FlatStyle = FlatStyle.Standard,
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(2, 0, 1, 0),
                Dock = DockStyle.Fill
            };
            btn_help.Click += BtnHelp_Click;
            right_btns.Controls.Add(btn_help, 0, 0);

            btn_check_update = new Button
            {
                Name = "btn_check_update",
                Text = "检查更新",
                Height = 24,
                FlatStyle = FlatStyle.Standard,
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(1, 0, 2, 0),
                Dock = DockStyle.Fill
            };
            btn_check_update.Click += BtnCheckUpdate_Click;
            right_btns.Controls.Add(btn_check_update, 1, 0);

            statusLayout.Controls.Add(right_btns, 4, 0);
        }

        /// <summary>
        /// 跑马灯定时器Tick：文字超出可视宽度时自动向左滚动
        /// </summary>
        private void MarqueeTimer_Tick(object? sender, EventArgs e)
        {
            if (running_status_label == null || _marqueePaused) return;

            string fullText = _marqueeFullText;
            if (string.IsNullOrEmpty(fullText))
            {
                fullText = running_status_label.Text;
                if (string.IsNullOrEmpty(fullText)) return;
                _marqueeFullText = fullText;
            }

            int visibleWidth = running_status_label.ClientSize.Width - running_status_label.Margin.Horizontal;
            if (visibleWidth <= 0) return;

            bool textChanged = running_status_label.Text != fullText;
            bool sizeChanged = visibleWidth != _marqueeVisibleWidth;

            if (textChanged || sizeChanged || _marqueeTextWidth <= 0)
            {
                using var g = running_status_label.CreateGraphics();
                var textSize = g.MeasureString(fullText.TrimStart(), running_status_label.Font);
                _marqueeTextWidth = textSize.Width;
                _marqueeVisibleWidth = visibleWidth;
                _marqueeOverflow = _marqueeTextWidth > visibleWidth;
                _marqueeOffset = 0;
                running_status_label.Text = fullText;
            }

            if (!_marqueeOverflow)
            {
                if (running_status_label.Padding.Left != 0)
                    running_status_label.Padding = new Padding(0);
                _marqueeOffset = 0;
                return;
            }

            _marqueeOffset += 2;
            int gap = 40;
            int totalLoop = (int)_marqueeTextWidth + gap;

            if (_marqueeOffset > totalLoop)
                _marqueeOffset = 0;

            int negPad = -_marqueeOffset;
            if (running_status_label.Padding.Left != negPad)
                running_status_label.Padding = new Padding(negPad, 0, 0, 0);
        }

        /// <summary>
        /// 创建图标按钮（用于状态栏左侧图标区域）
        /// </summary>
        private Button CreateIconButton(string text, Color foreColor, int size)
        {
            return new Button
            {
                Text = text,
                Width = size,
                Height = size,
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 0 },
                BackColor = ColorTranslator.FromHtml(STATUS_BAR_BG),
                ForeColor = foreColor,
                Font = new Font("Segoe UI Emoji", 14f, FontStyle.Regular),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(0),
                UseCompatibleTextRendering = true
            };
        }
        // 日志面板默认隐藏，点击状态栏展开
        // 修复：日志面板应该占据 notebook_container 区域，不推动状态栏
        private void CreateLogPanel()
        {
            Color bgColor = ColorTranslator.FromHtml(STATUS_BAR_BG);

            // full_log_view 容器 - 使用 Fill 而不是 Bottom，这样它会占据 notebook_container 的区域
            full_log_view = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = bgColor,
                Visible = false,
                Padding = new Padding(6, 4, 6, 4)
            };
            log_frame = new GroupBox
            {
                Text = "  运行日志（自动滚动，区分颜色）  ",
                Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular),
                Dock = DockStyle.Fill,
                Padding = new Padding(6)
            };
            var toolRow1 = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new Padding(4, 2, 0, 2),
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight
            };

            var chkAutoScroll = new CheckBox
            {
                Text = "自动滚动",
                Checked = true,
                AutoSize = true,
                Margin = new Padding(4, 4, 0, 0)
            };
            chkAutoScroll.CheckedChanged += (s, e) => auto_scroll = chkAutoScroll.Checked;

            toolRow1.Controls.Add(chkAutoScroll);
            var toolRow2 = new Panel { Height = 22, Dock = DockStyle.Top };

            var lblLegend = new Label { Text = "  状态图例：", ForeColor = ColorTranslator.FromHtml("#555555"), AutoSize = true, Location = new Point(0, 3) };
            var lblInfo = new Label { Text = "● 信息", ForeColor = ColorTranslator.FromHtml("#555555"), AutoSize = true, Location = new Point(70, 3) };
            var lblSuccess = new Label { Text = "● 成功", ForeColor = ColorTranslator.FromHtml("#2E7D32"), AutoSize = true, Location = new Point(130, 3) };
            var lblWarn = new Label { Text = "● 警告", ForeColor = ColorTranslator.FromHtml("#EF6C00"), AutoSize = true, Location = new Point(190, 3) };
            var lblError = new Label { Text = "● 报错", ForeColor = ColorTranslator.FromHtml("#C62828"), AutoSize = true, Location = new Point(250, 3) };

            toolRow2.Controls.Add(lblLegend);
            toolRow2.Controls.Add(lblInfo);
            toolRow2.Controls.Add(lblSuccess);
            toolRow2.Controls.Add(lblWarn);
            toolRow2.Controls.Add(lblError);
            output_text = new RichTextBox
            {
                Name = "output_text",
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 10f, FontStyle.Regular),
                ReadOnly = true,
                WordWrap = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White
            };
            var logContextMenu = new ContextMenuStrip();
            logContextMenu.Items.Add("复制选中内容").Click += (s, e) => CopySelectedLog();
            logContextMenu.Items.Add("复制全部日志").Click += (s, e) => CopyAllLog();
            logContextMenu.Items.Add(new ToolStripSeparator());
            logContextMenu.Items.Add("全选").Click += (s, e) => SelectAllLog();
            logContextMenu.Items.Add(new ToolStripSeparator());
            logContextMenu.Items.Add("清空日志").Click += (s, e) => ClearLog();
            logContextMenu.Items.Add("导出日志").Click += (s, e) => ExportLog();
            output_text.ContextMenuStrip = logContextMenu;

            log_frame.Controls.Add(output_text);
            log_frame.Controls.Add(toolRow2);
            log_frame.Controls.Add(toolRow1);

            full_log_view.Controls.Add(log_frame);

            // 注意：full_log_view 初始不可见，添加到 this.Controls
            // 使用 DockStyle.Fill，展开时覆盖 notebook_container 区域，不推动状态栏
            this.Controls.Add(full_log_view);
        }
        // 优化：懒加载Tab，仅初始化主页Tab，其他Tab在首次切换时创建
        private void CreateTabs()
        {
            notebook_container = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(6, 4, 6, 4)
            };

            notebook = new TabControl
            {
                Name = "notebook",
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular)
            };

            var tab0 = new TabPage("  主页  ");
            notebook.TabPages.Add(tab0);

            // Tab 1-6: 仅创建TabPage占位，内容懒加载
            var tab1 = new TabPage("  虚拟机列表  ");
            notebook.TabPages.Add(tab1);
            var tab2 = new TabPage("  主机列表  ");
            notebook.TabPages.Add(tab2);
            var tab3 = new TabPage("  存储列表  ");
            notebook.TabPages.Add(tab3);
            var tab4 = new TabPage("  网络列表  ");
            notebook.TabPages.Add(tab4);
            var tab5 = new TabPage("  编辑列表  ");
            notebook.TabPages.Add(tab5);
            var tab6 = new TabPage("  虚拟机模板  ");
            notebook.TabPages.Add(tab6);

            var appContext = new Dictionary<string, object>
            {
                { "status_bar", status_bar! },
                { "notebook", notebook },
                { "notebook_container", notebook_container! },
                { "full_log_view", full_log_view! },
                { "output_text", output_text! },
                { "auto_scroll_var", new Action<bool>(v => auto_scroll = v) },
                { "help_btn", btn_help! },
                { "running_status_label", running_status_label! },
                { "tabs", new List<object>() },
                { "tab_refresh_callbacks", new Dictionary<string, Action>() }
            };
            _appContext = appContext;

            // 仅初始化主页Tab（其他Tab懒加载）
            home_tab = new HomeTab(tab0, this, appContext);
            // 注意：ovf_tool_tab 将在懒加载时设置
            try
            {
                appContext["_notify_ovf_tab_cache_refreshed"] = new Action(() =>
                {
                    try
                    {
                        ovf_tool_tab?.ReloadFromEnvCache();
                    }
                    catch { }
                });
            }
            catch { }

            // 更新 app_context 中的 tabs 列表
            var tabsList = appContext["tabs"] as List<object>;
            tabsList?.Add(home_tab!);

            // Tab切换自动刷新 + 懒加载
            notebook.SelectedIndexChanged += OnTabChanged;

            // 将notebook添加到container
            notebook_container.Controls.Add(notebook);
            
            // 注意：Dock 布局顺序很重要！
            // Windows Forms 的 Dock 是逆序处理的——最后添加的控件最先获得布局空间
            // 所以要先添加 Fill（notebook_container），再添加 Bottom（status_bar）
            // 这样 status_bar 会先占据底部，notebook_container 自动收缩避开
            this.Controls.Add(notebook_container);
            this.Controls.Add(status_bar);

            notebook.SelectedIndex = 0;
        }

        // 获取编辑列表Tab的当前数据（用于部署时读取）
        public List<Dictionary<string, string>> GetCsvEditorData()
        {
            return csv_edit_tab?.GetCurrentData() ?? new List<Dictionary<string, string>>();
        }

        // 获取编辑列表Tab的当前CSV路径（用于部署时读取）
        public string GetCsvEditorPath()
        {
            return csv_edit_tab?.CurrentCsvPath ?? "";
        }

        // 懒加载其他Tab（首次切换时创建）
        private bool _tabsInitialized = false;
        private bool _tabsInitializing = false;
        private void EnsureTabsInitialized()
        {
            if (_tabsInitialized || _tabsInitializing) return;
            _tabsInitializing = true;

            // Windows Forms 控件必须在 UI 线程创建，使用 BeginInvoke 异步执行不阻塞
            this.BeginInvoke(new Action(() =>
            {
                try
                {
                    var tab1 = notebook.TabPages[1]; // 虚拟机列表
                    var tab2 = notebook.TabPages[2]; // 主机列表
                    var tab3 = notebook.TabPages[3]; // 存储列表
                    var tab4 = notebook.TabPages[4]; // 网络列表
                    var tab5 = notebook.TabPages[5]; // 编辑列表
                    var tab6 = notebook.TabPages[6]; // 虚拟机模板

                    vm_list_tab = new ResourceQueryTab(tab1, this, this, "vm");
                    host_list_tab = new ResourceQueryTab(tab2, this, this, "host");
                    datastore_list_tab = new ResourceQueryTab(tab3, this, this, "datastore");
                    network_list_tab = new ResourceQueryTab(tab4, this, this, "network");
                    csv_edit_tab = new CsvEditorTab(tab5, this, this);
                    ovf_tool_tab = new OVFToolTab(tab6, this, this);

                    // 通知ovf_tab
                    try { ovf_tool_tab?.ReloadFromEnvCache(); } catch { }

                    // 注册所有子Tab到 appContext["tabs"]，供 HomeTab 同步连接状态
                    var tabsList = _appContext?["tabs"] as List<object>;
                    if (tabsList != null)
                    {
                        if (vm_list_tab != null) tabsList.Add(vm_list_tab);
                        if (host_list_tab != null) tabsList.Add(host_list_tab);
                        if (datastore_list_tab != null) tabsList.Add(datastore_list_tab);
                        if (network_list_tab != null) tabsList.Add(network_list_tab);
                        if (csv_edit_tab != null) tabsList.Add(csv_edit_tab);
                        if (ovf_tool_tab != null) tabsList.Add(ovf_tool_tab);
                    }

                    _tabsInitialized = true;

                    // 创建完成后，触发当前选中Tab的自动查询
                    TriggerAutoQueryForCurrentTab();
                }
                catch (Exception)
                {
                    _tabsInitializing = false;
                }
            }));
        }

        // 触发当前选中Tab的自动查询
        private void TriggerAutoQueryForCurrentTab()
        {
            if (notebook == null) return;
            var selectedTab = notebook.SelectedTab;
            if (selectedTab == null) return;

            if (selectedTab.Text.Contains("虚拟机列表") && vm_list_tab != null)
            {
                vm_list_tab.OnTabActivated();
            }
            else if (selectedTab.Text.Contains("主机列表") && host_list_tab != null)
            {
                host_list_tab.OnTabActivated();
            }
            else if (selectedTab.Text.Contains("存储列表") && datastore_list_tab != null)
            {
                datastore_list_tab.OnTabActivated();
            }
            else if (selectedTab.Text.Contains("网络列表") && network_list_tab != null)
            {
                network_list_tab.OnTabActivated();
            }
        }
        // 优化：首次切换到非主页Tab时触发懒加载
        private void OnTabChanged(object? sender, EventArgs e)
        {
            if (notebook == null) return;

            var selectedTab = notebook.SelectedTab;
            if (selectedTab == null) return;

            // 首次切换到非主页Tab时触发懒加载
            if (notebook.SelectedIndex > 0 && !_tabsInitialized)
            {
                EnsureTabsInitialized();
                return; // 等待懒加载完成后再触发自动查询
            }

            if (selectedTab.Text.Contains("虚拟机列表") && vm_list_tab != null)
            {
                vm_list_tab.OnTabActivated();
            }
            else if (selectedTab.Text.Contains("主机列表") && host_list_tab != null)
            {
                host_list_tab.OnTabActivated();
            }
            else if (selectedTab.Text.Contains("存储列表") && datastore_list_tab != null)
            {
                datastore_list_tab.OnTabActivated();
            }
            else if (selectedTab.Text.Contains("网络列表") && network_list_tab != null)
            {
                network_list_tab.OnTabActivated();
            }
        }
        // 1. 点击status_label：如果已连接vCenter，打开浏览器访问vCenter URL
        // 2. 点击running_status_label或status_bar空白处：切换日志面板显示/隐藏
        // 注意：status_bar的Click事件仅处理空白区域点击，子控件点击已通过各自事件处理
        private void StatusBar_Click(object? sender, EventArgs e)
        {
            // 仅当点击的是status_bar本身（非子控件）时才触发
            // 子控件（status_label、running_status_label）有各自的点击处理
            ToggleLogPanel();
        }

        private void StatusIcon_Click(object? sender, EventArgs e)
        {
            // 点击连接状态图标：切换日志面板
            ToggleLogPanel();
        }

        /// <summary>
        /// 点击 Web 图标：打开 vCenter Web 管理页面
        /// </summary>
        private void BtnWeb_Click(object? sender, EventArgs e)
        {
            if (!SessionManager.Instance.IsConnected)
            {
                status_bar_tool_tip?.Show("状态未连接", btn_web, 0, -30, 2000);
                return;
            }

            try
            {
                string? vcUrl = SessionManager.Instance.CurrentVc;
                if (!string.IsNullOrEmpty(vcUrl))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = $"https://{vcUrl}",
                        UseShellExecute = true
                    });
                }
            }
            catch { }
        }

        private void BtnExtensions_Click(object? sender, EventArgs e)
        {
            using var dlg = new ExtensionManagerDialog();
            dlg.StartPosition = FormStartPosition.CenterParent;
            dlg.ShowDialog(this);
        }

        /// <summary>
        /// 点击 CMD 图标：使用 root 账号 SSH 连接到 VCSA
        /// </summary>
        private void BtnCmd_Click(object? sender, EventArgs e)
        {
            if (!SessionManager.Instance.IsConnected)
            {
                status_bar_tool_tip?.Show("状态未连接", btn_cmd, 0, -30, 2000);
                return;
            }

            string? vcHost = SessionManager.Instance.CurrentVc;
            if (string.IsNullOrEmpty(vcHost))
            {
                status_bar_tool_tip?.Show("状态未连接", btn_ps, 0, -30, 2000);
                return;
            }

            string mobaPath = FindMobaXterm();
            if (string.IsNullOrEmpty(mobaPath))
            {
                MessageBox.Show("未找到 MobaXterm，请确认已安装\n\n默认查找路径：\nC:\\Program Files (x86)\\Mobatek\\MobaXterm\\MobaXterm.exe\nC:\\Program Files\\Mobatek\\MobaXterm\\MobaXterm.exe",
                    "未找到 MobaXterm", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = mobaPath,
                    Arguments = $"-newtab \"ssh root@{vcHost}\"",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
                AppendLog($"[{DateTime.Now:HH:mm:ss}] [info] 已启动 MobaXterm SSH 连接: root@{vcHost}{Environment.NewLine}", "info");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"SSH 连接启动失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 点击 PS 图标：打开 PowerShell 窗口，自动加载 PowerCLI 并连接 vCenter
        /// </summary>
        private void BtnPs_Click(object? sender, EventArgs e)
        {
            if (!SessionManager.Instance.IsConnected)
            {
                status_bar_tool_tip?.Show("状态未连接", btn_ps, 0, -30, 2000);
                return;
            }

            string? vcHost = SessionManager.Instance.CurrentVc;
            if (string.IsNullOrEmpty(vcHost))
            {
                status_bar_tool_tip?.Show("状态未连接", btn_ps, 0, -30, 2000);
                return;
            }

            // 获取当前连接凭据
            var cred = SessionManager.Instance.CurrentCredentials;
            if (cred == null)
            {
                status_bar_tool_tip?.Show("状态未连接", btn_ps, 0, -30, 2000);
                return;
            }

            // 构建 PowerShell 启动脚本：导入 PowerCLI → 配置证书策略 → 连接 vCenter
            string vc = SessionManager.EscapePsString(vcHost);
            string user = SessionManager.EscapePsString(cred.Value.Username);
            string pass = SessionManager.EscapePsString(cred.Value.Password);

            string psScript =
                $"Set-Location D:\\; " +
                $"Import-Module VMware.VimAutomation.Core -ErrorAction SilentlyContinue; " +
                $"Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null; " +
                $"Connect-VIServer -Server {vc} -User {user} -Password {pass} -ErrorAction Stop | Out-Null; " +
                $"Write-Host 'PowerCLI 已加载，已连接到 vCenter: {vcHost}' -ForegroundColor Green; " +
                $"Write-Host '提示: 此为独立会话，可自由执行 PowerCLI 命令' -ForegroundColor Yellow";

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoExit -Command \"{psScript}\"",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
                AppendLog($"[{DateTime.Now:HH:mm:ss}] [info] 已打开 PowerShell 窗口（PowerCLI + 已连接 {vcHost}）{Environment.NewLine}", "info");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"PowerShell 启动失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 查找 MobaXterm 安装路径
        /// </summary>
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

        private void RunningStatusLabel_Click(object? sender, EventArgs e)
        {
            ToggleLogPanel();
        }

        // 切换日志面板显示/隐藏
        // - 展开：隐藏 notebook，显示 full_log_view（占满整个窗口）
        // - 收起：隐藏 full_log_view，显示 notebook
        // 修复：日志面板展开时占据 notebook_container 区域，不推动状态栏
        private void ToggleLogPanel()
        {
            if (full_log_view == null || notebook_container == null) return;

            log_panel_expanded = !log_panel_expanded;

            if (log_panel_expanded)
            {
                // 展开：隐藏 notebook_container，显示 full_log_view
                // 两者都使用 DockStyle.Fill，所以会占据同一区域
                notebook_container.Visible = false;
                full_log_view.Visible = true;
                // 确保 full_log_view 在最上层
                full_log_view.BringToFront();
                // 强制刷新 RichTextBox 渲染，并滚动到底部，避免展开时内容空白
                if (output_text != null)
                {
                    output_text.SelectionStart = output_text.TextLength;
                    output_text.ScrollToCaret();
                    output_text.Invalidate();
                    output_text.Refresh();
                }
                UpdateRunningStatus("💡 日志面板已展开 - 再次点击收起");
            }
            else
            {
                // 收起：隐藏 full_log_view，显示 notebook_container
                full_log_view.Visible = false;
                notebook_container.Visible = true;
                // 确保 notebook_container 在最上层
                notebook_container.BringToFront();
                UpdateRunningStatus("💡 就绪 - 点击此处展开完整日志");
            }
        }

        private void BtnHelp_Click(object? sender, EventArgs e)
        {
            try
            {
                string helpPath = CoreUtils.GetHelpFilePath();
                if (!File.Exists(helpPath))
                    return;

                string url = new Uri(helpPath).AbsoluteUri;

                string[] browsers = {
                    @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                    @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
                    @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                    @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
                };

                foreach (var browser in browsers)
                {
                    if (File.Exists(browser))
                    {
                        try
                        {
                            var psi = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = browser,
                                Arguments = $"--app=\"{url}\"",
                                UseShellExecute = false
                            };
                            System.Diagnostics.Process.Start(psi);

                            _ = System.Threading.Tasks.Task.Run(() =>
                            {
                                System.Threading.Thread.Sleep(4000);

                                bool hasWindow = false;
                                foreach (var name in new[] { "msedge", "chrome" })
                                {
                                    foreach (var p in System.Diagnostics.Process.GetProcessesByName(name))
                                    {
                                        try { if (p.MainWindowHandle != IntPtr.Zero) { hasWindow = true; break; } } catch { }
                                    }
                                    if (hasWindow) break;
                                }

                                if (!hasWindow)
                                {
                                    try
                                    {
                                        var psiFallback = new System.Diagnostics.ProcessStartInfo
                                        {
                                            FileName = browser,
                                            Arguments = $"\"{helpPath}\"",
                                            UseShellExecute = false
                                        };
                                        System.Diagnostics.Process.Start(psiFallback);
                                    }
                                    catch { }
                                }
                            });

                            return;
                        }
                        catch
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = browser,
                                Arguments = $"\"{helpPath}\"",
                                UseShellExecute = false
                            });
                            return;
                        }
                    }
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = helpPath,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void ClearLog()
        {
            if (output_text != null)
            {
                output_text.Clear();
            }
            // 重置运行状态标签
            if (running_status_label != null)
            {
                running_status_label.Text = "💡 就绪 - 点击此处展开完整日志";
            }
        }

        private void ExportLog()
        {
            if (output_text == null) return;

            string defaultName = $"vmops-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
            var dlg = new SaveFileDialog
            {
                Filter = "文本文件|*.txt|日志文件|*.log",
                Title = "导出日志",
                FileName = defaultName
            };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    File.WriteAllText(dlg.FileName, output_text.Text);
                    ToastUtils.ShowToast(this, "日志导出成功");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
        /// <summary>
        /// 向主日志区写入一行带颜色的日志。
        /// 调用方负责拼接完整文本（时间戳、[前缀]、emoji、换行），本方法只做颜色着色 + 状态栏跑马灯更新。
        /// </summary>
        /// <param name="text">完整日志文本，如 "[13:05:50] [查询] ⟳ 开始查询 网络列表…\n"</param>
        /// <param name="level">
        ///   日志级别，支持英文和中文两种写法（自动兼容）：
        ///   <list type="bullet">
        ///     <item>"info" / "信息" / ""(默认) —— 深灰 #555555 —— 普通信息</item>
        ///     <item>"success" / "成功" —— 绿色 #2E7D32 —— 操作成功</item>
        ///     <item>"warn" / "警告" —— 橙色 #EF6C00 —— 警告</item>
        ///     <item>"error" / "错误" —— 红色 #C62828 —— 错误/失败</item>
        ///     <item>"connecting" / "查询" / "phase" —— 蓝色 #1565C0 —— 进行中</item>
        ///     <item>"debug" / "调试" —— 浅灰 #9E9E9E —— 调试日志</item>
        ///   </list>
        /// </param>
        public void AppendLog(string text, string level = "info")
        {
            if (output_text == null) return;

            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => AppendLog(text, level)));
                return;
            }

            while (_logQueue.Count >= LOG_QUEUE_MAX && _logQueue.TryDequeue(out _)) { }
            _logQueue.Enqueue((text, level));

            if (_logFlushTimer == null)
            {
                _logFlushTimer = new System.Windows.Forms.Timer { Interval = LOG_FLUSH_INTERVAL_MS };
                _logFlushTimer.Tick += (s, e) => FlushLogQueue();
                _logFlushTimer.Start();
            }
            else
            {
                _logFlushTimer.Stop();
                _logFlushTimer.Start();
            }
        }

        private Color ResolveLogColor(string level)
        {
            string key = (level ?? "").Trim();
            return key switch
            {
                "success" => ColorTranslator.FromHtml("#2E7D32"),
                "成功" => ColorTranslator.FromHtml("#2E7D32"),
                "warn" => ColorTranslator.FromHtml("#EF6C00"),
                "警告" => ColorTranslator.FromHtml("#EF6C00"),
                "error" => ColorTranslator.FromHtml("#C62828"),
                "错误" => ColorTranslator.FromHtml("#C62828"),
                "connecting" => ColorTranslator.FromHtml("#1565C0"),
                "查询" => ColorTranslator.FromHtml("#1565C0"),
                "phase" => ColorTranslator.FromHtml("#1565C0"),
                "debug" => ColorTranslator.FromHtml("#9E9E9E"),
                "调试" => ColorTranslator.FromHtml("#9E9E9E"),
                _ => ColorTranslator.FromHtml("#555555")
            };
        }

        private void FlushLogQueue()
        {
            if (output_text == null || _logQueue.IsEmpty) return;

            bool isFirst = true;
            string? lastCleanedText = null;
            while (_logQueue.TryDequeue(out var item))
            {
                int start = output_text.TextLength;
                output_text.AppendText(item.text);
                output_text.Select(start, item.text.Length);
                output_text.SelectionColor = ResolveLogColor(item.level);

                if (!isFirst) continue;
                isFirst = false;
                if (auto_scroll)
                {
                    output_text.SelectionStart = output_text.TextLength;
                    output_text.ScrollToCaret();
                }
                string cleanedText = item.text.Replace("\n", " ").Replace("\r", "").Trim();
                if (!string.IsNullOrEmpty(cleanedText))
                    lastCleanedText = cleanedText;
            }

            if (auto_scroll)
            {
                output_text.SelectionStart = output_text.TextLength;
                output_text.ScrollToCaret();
            }

            if (running_status_label != null && lastCleanedText != null)
            {
                running_status_label.Text = $"💡 {lastCleanedText}";
                _marqueeFullText = running_status_label.Text;
                _marqueeOffset = 0;
            }
        }

        // 公共方法：更新状态栏（更新左侧连接状态图标）
        public void UpdateStatus(string text, Color color)
        {
            if (btn_status_icon == null) return;

            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateStatus(text, color)));
                return;
            }

            btn_status_icon.ForeColor = color;
            status_bar_tool_tip?.SetToolTip(btn_status_icon, text);
        }

        public void UpdateRunningStatus(string text)
        {
            if (running_status_label == null) return;

            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateRunningStatus(text)));
                return;
            }

            running_status_label.Text = text;
        }

        // 公共方法：清空日志（供HomeTab调用）
        public void ClearLogPublic()
        {
            ClearLog();
        }

        // 公共方法：导出日志（供HomeTab调用）
        public void ExportLogPublic()
        {
            ExportLog();
        }
        private void CopySelectedLog()
        {
            if (output_text != null && !string.IsNullOrEmpty(output_text.SelectedText))
            {
                Clipboard.SetText(output_text.SelectedText);
            }
        }

        private void CopyAllLog()
        {
            if (output_text != null && !string.IsNullOrEmpty(output_text.Text))
            {
                Clipboard.SetText(output_text.Text);
            }
        }

        private void SelectAllLog()
        {
            if (output_text != null)
            {
                output_text.SelectAll();
            }
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.Text = $"VmOps.NET - 虚拟化运维管理工具 v{AppVersion.VERSION_STR}";
            int width = WINDOW_BASE_WIDTH;
            int height = WINDOW_BASE_HEIGHT;
            this.ClientSize = new Size(width, height);
            this.MinimumSize = new Size(width, height);

            this.BackColor = ColorTranslator.FromHtml(STATUS_BAR_BG);

            LoadIcon();
            SetupFonts();
            CenterWindow(width, height);
            // 1. status_bar.pack(side=tk.BOTTOM, fill=tk.X)
            CreateStatusBar();
            // 2. full_log_view 创建但不显示
            CreateLogPanel();
            // 3. notebook.pack(expand=True, fill='both', padx=px(6), pady=px(4))
            CreateTabs();

            this.ResumeLayout(false);

            _ = CheckForUpdatesSilentAsync();
        }

        private bool _updateCheckedThisSession = false;

        private async Task CheckForUpdatesSilentAsync()
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(3000);
                var info = await UpdateUtils.CheckForUpdatesAsync(AppVersion.VERSION_STR);
                if (info != null && info.IsNewer && !_updateCheckedThisSession)
                {
                    _updateCheckedThisSession = true;
                    this.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            using var dlg = new UpdateDialog(info, AppVersion.VERSION_STR);
                            dlg.ShowDialog(this);
                        }
                        catch (System.Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Update] 弹窗异常: {ex.Message}");
                        }
                    }));
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Update] 静默检查异常: {ex.Message}");
            }
        }

        private async void BtnCheckUpdate_Click(object? sender, EventArgs e)
        {
            if (btn_check_update == null) return;
            btn_check_update.Enabled = false;
            btn_check_update.Text = "检查中...";

            try
            {
                var info = await UpdateUtils.CheckForUpdatesAsync(AppVersion.VERSION_STR);
                if (info == null)
                {
                    MessageBox.Show("无法获取版本信息，请检查网络连接后重试。", "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (info.IsNewer)
                {
                    _updateCheckedThisSession = true;
                    using var dlg = new UpdateDialog(info, AppVersion.VERSION_STR);
                    dlg.ShowDialog(this);
                }
                else
                {
                    MessageBox.Show($"当前已是最新版本！\n\n当前版本：{AppVersion.VERSION_STR}\n最新版本：{info.Version}", "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"检查更新失败：{ex.Message}", "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btn_check_update.Enabled = true;
                btn_check_update.Text = "检查更新";
            }
        }

        private Task? _cleanupTask;

        public Task WaitForCleanup() => _cleanupTask ?? Task.CompletedTask;

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);

            _marqueeTimer?.Stop();
            _marqueeTimer?.Dispose();

            if (_logFlushTimer != null)
            {
                _logFlushTimer.Stop();
                _logFlushTimer.Dispose();
                _logFlushTimer = null;
            }
            FlushLogQueue();

            _cleanupTask = Task.Run(() =>
            {
                try
                {
                    home_tab?.OnClosing();
                    vm_list_tab?.OnClosing();
                    host_list_tab?.OnClosing();
                    datastore_list_tab?.OnClosing();
                    network_list_tab?.OnClosing();
                    csv_edit_tab?.OnClosing();
                    ovf_tool_tab?.OnClosing();

                    SessionManager.Instance.Dispose();
                }
                catch { }
            });
        }
    }
}