// ============================================================================
// HomeTab.cs - 应用首页 Tab
// ============================================================================
// 布局结构：
//   ✅ 顶部三列 TableLayoutPanel：vCenter 连接配置 | 批量部署管理 | 功能菜单入口
//   ✅ 底部（占满剩余空间）：使用提示 / 快速帮助文本
//
// 功能：
//   连接 / 断开 vCenter、启动 CSV 编辑器、打开部署面板、跳转到各查询 Tab
// ============================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using VirtualizationOpsTool.Core;
using VirtualizationOpsTool.Utils;

namespace VirtualizationOpsTool.UI
{
    public class HomeTab
    {
        private readonly TabPage _parent;
        private readonly Form _root;
        private readonly Dictionary<string, object> _appContext;
        private Label? _statusLabel;
        private Panel? _statusBar;
        private RichTextBox? _outputText;
        private TabControl? _notebook;
        private Panel? _notebookContainer;
        private Panel? _fullLogView;
        private Label? _runningStatusLabel;
        private ComboBox? _aliasCombo;
        private ComboBox? _vcCombo;
        private ComboBox? _domainCombo;
        private ComboBox? _userCombo;
        private TextBox? _txtPassword;
        private Button? _connectDisconnectButton;
        private Button? _resetMemoryButton;
        private Button? _btnDeploy;
        private Button? _btnCancelTask;
        private List<Button> _deployButtons = new List<Button>();
        private List<(string key, Button btn)> _menuButtons = new List<(string, Button)>();
        private bool _isConnected;
        private Dictionary<string, Dictionary<string, string>> _savedCredentials = new Dictionary<string, Dictionary<string, string>>();
        private string? _lastVc;
        private string? _lastVcServer;

        public HomeTab(TabPage parent, Form root, Dictionary<string, object> appContext)
        {
            _parent = parent;
            _root = root;
            _appContext = appContext;

            ExtractContext();
            BuildUI();
            // BindGlobalButtons(); // 已移至 MainForm.cs 统一处理，避免重复绑定

            // 订阅 SessionManager 初始化日志事件
            SessionManager.Instance.InitializationLog += (msg, level) => LogMessage(level ?? "info", msg ?? "");

            // 优化：将耗时操作移到后台异步执行，避免阻塞UI启动
            // 1. 延迟加载凭据（后台线程）
            Task.Run(() => LoadCredentials());
            // 2. 延迟初始化PowerShell引擎（后台线程，不阻塞UI）
            Task.Run(() => SessionManager.Instance.EnsureInitialized());
            // 3. 延迟绑定右键菜单（窗口显示后执行）
            if (_root.IsHandleCreated)
            {
                _root.BeginInvoke(new Action(() => BindInputContextMenus()));
            }
            else
            {
                _root.Shown += (s, e) => BindInputContextMenus();
            }

            if (_root.IsHandleCreated)
            {
                _root.BeginInvoke(new Action(() => EnableConnectButton()));
            }
            else
            {
                _root.Shown += (s, e) =>
                {
                    _root.BeginInvoke(new Action(() => EnableConnectButton()));
                };
            }
        }

        private void ExtractContext()
        {
            if (_appContext.TryGetValue("status_label", out var sl)) _statusLabel = sl as Label;
            if (_appContext.TryGetValue("status_bar", out var sb)) _statusBar = sb as Panel;
            if (_appContext.TryGetValue("output_text", out var ot)) _outputText = ot as RichTextBox;
            if (_appContext.TryGetValue("notebook", out var nb)) _notebook = nb as TabControl;
            if (_appContext.TryGetValue("notebook_container", out var nc)) _notebookContainer = nc as Panel;
            if (_appContext.TryGetValue("full_log_view", out var flv)) _fullLogView = flv as Panel;
            if (_appContext.TryGetValue("running_status_label", out var rsl)) _runningStatusLabel = rsl as Label;
        }
        private void BuildUI()
        {
            var frame = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(4),
                ColumnCount = 1,
                RowCount = 2
            };
            frame.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            frame.RowStyles.Add(new RowStyle(SizeType.Percent, 40F));
            frame.RowStyles.Add(new RowStyle(SizeType.Percent, 60F));

            // =========== 顶部三列：连接参数 | 部署管理 | 功能菜单 ===========
            var topRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Padding = new Padding(4)
            };

            // 设置列权重：40% | 20% | 40%
            topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            topRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            // 左列 40%：连接参数
            var connectFrame = new GroupBox
            {
                Text = "  连接参数  ",
                Font = new Font("Microsoft YaHei", 10, FontStyle.Bold),
                ForeColor = ColorTranslator.FromHtml("#1565C0"),
                FlatStyle = FlatStyle.Standard,
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };

            BuildConnectFrame(connectFrame);

            // 中列 20%：部署管理
            var deployFrame = new GroupBox
            {
                Text = "  部署管理  ", Font = new Font("Microsoft YaHei", 10, FontStyle.Bold),
                ForeColor = ColorTranslator.FromHtml("#2E7D32"),
                FlatStyle = FlatStyle.Standard,
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };

            BuildDeployFrame(deployFrame);

            // 右列 40%：功能菜单
            var menuFrame = new GroupBox
            {
                Text = "  功能菜单  ", Font = new Font("Microsoft YaHei", 10, FontStyle.Bold),
                ForeColor = ColorTranslator.FromHtml("#2E7D32"),
                FlatStyle = FlatStyle.Standard,
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };

            BuildMenuFrame(menuFrame);

            topRow.Controls.Add(connectFrame, 0, 0);
            topRow.Controls.Add(deployFrame, 1, 0);
            topRow.Controls.Add(menuFrame, 2, 0);

            // =========== 底部：使用提示（占满剩余空间）===========
            var tipFrame = new GroupBox
            {
                Text = "  使用提示  ",
                Font = new Font("Microsoft YaHei", 9), ForeColor = ColorTranslator.FromHtml("#757575"),
                FlatStyle = FlatStyle.Standard,
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };

            BuildTipFrame(tipFrame);

            frame.Controls.Add(topRow, 0, 0);
            frame.Controls.Add(tipFrame, 0, 1);
            _parent.Controls.Add(frame);
        }
        private void BuildConnectFrame(GroupBox connectFrame)
        {
            // ===== 主布局：3列 x 4行（纯输入区，无按钮） =====
            // 列0: 标签(固定90px) | 列1: 输入框(弹性百分比) | 列2: 间隔(10px)
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 4, Padding = new Padding(0, 10, 0, 10), AutoSize = false
            };

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 10));

            for (int i = 0; i < 4; i++)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiUtils.Px(30)));
            }

            // ===== 第0行：连接别名 =====
            var lblAlias = new Label
            {
                Text = "连接别名:",
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 0, 2, 0),
                Margin = Padding.Empty,
                AutoSize = false,
                AutoEllipsis = false,
                Width = 88,
                Height = 22,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            layout.Controls.Add(lblAlias, 0, 0);

            _aliasCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDown,
                DropDownHeight = 200,
                Margin = Padding.Empty,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _aliasCombo.SelectedIndexChanged += (s, e) => OnAliasSelected();
            layout.Controls.Add(_aliasCombo, 1, 0);

            // ===== 第1行：连接地址 =====
            var lblVc = new Label
            {
                Text = "连接地址:",
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 0, 2, 0),
                Margin = Padding.Empty,
                AutoSize = false,
                AutoEllipsis = false,
                Width = 88,
                Height = 22,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            layout.Controls.Add(lblVc, 0, 1);

            _vcCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDown,
                Margin = Padding.Empty,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _vcCombo.SelectedIndexChanged += (s, e) => OnVcSelected();
            layout.Controls.Add(_vcCombo, 1, 1);

            // ===== 第2行：用户域 =====
            var lblDomain = new Label
            {
                Text = "用户域:",
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 0, 2, 0),
                Margin = Padding.Empty,
                AutoSize = false,
                AutoEllipsis = false,
                Width = 88,
                Height = 22,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            layout.Controls.Add(lblDomain, 0, 2);

            _domainCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDown,
                Margin = Padding.Empty,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _domainCombo.Items.AddRange(new object[] { "vsphere.local", "local.net" });
            if (string.IsNullOrEmpty(_domainCombo.Text))
                _domainCombo.Text = "vsphere.local";
            _domainCombo.SelectedIndexChanged += (s, e) => UpdateUserVar();
            _domainCombo.TextChanged += (s, e) => UpdateUserVar();
            layout.Controls.Add(_domainCombo, 1, 2);

            // ===== 第3行：用户名 =====
            var lblUser = new Label
            {
                Text = "用户名:",
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 0, 2, 0),
                Margin = Padding.Empty,
                AutoSize = false,
                AutoEllipsis = false,
                Width = 88,
                Height = 22,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            layout.Controls.Add(lblUser, 0, 3);

            _userCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDown,
                Margin = Padding.Empty,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _userCombo.Items.AddRange(new object[] { "root", "administrator" });
            if (string.IsNullOrEmpty(_userCombo.Text))
                _userCombo.Text = "administrator";
            _userCombo.SelectedIndexChanged += (s, e) => UpdateUserVar();
            _userCombo.TextChanged += (s, e) => UpdateUserVar();
            layout.Controls.Add(_userCombo, 1, 3);

            // ===== 右侧按钮面板（独立 Dock=Right） =====
            var btnPanel = new Panel
            {
                Dock = DockStyle.Right,
                Width = 150,
                Padding = new Padding(0, 10, 0, 10),
                Margin = Padding.Empty
            };

            var btnLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(0),
                AutoSize = false
            };
            btnLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            btnLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiUtils.Px(42)));
            btnLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiUtils.Px(42)));

            _connectDisconnectButton = new Button
            {
                Text = "开始连接",
                Font = new Font("Microsoft YaHei", 12, FontStyle.Bold),
                BackColor = Color.White,
                ForeColor = ColorTranslator.FromHtml("#333333"),
                FlatStyle = FlatStyle.Standard,
                Margin = Padding.Empty,
                Size = new Size(130, 40),
                Anchor = AnchorStyles.Top
            };
            _connectDisconnectButton.Click += async (s, e) => await ToggleConnection();
            btnLayout.Controls.Add(_connectDisconnectButton, 0, 0);

            _resetMemoryButton = new Button
            {
                Text = "重置记忆",
                Font = new Font("Microsoft YaHei", 12, FontStyle.Bold),
                BackColor = Color.White,
                ForeColor = ColorTranslator.FromHtml("#333333"),
                FlatStyle = FlatStyle.Standard,
                Margin = Padding.Empty,
                Size = new Size(130, 40),
                Anchor = AnchorStyles.Top
            };
            _resetMemoryButton.Click += (s, e) => ResetMemory();
            btnLayout.Controls.Add(_resetMemoryButton, 0, 1);

            btnPanel.Controls.Add(btnLayout);

            // 密码输入框（隐藏，仅用于存储密码）
            _txtPassword = new TextBox
            {
                Visible = false,
                Parent = connectFrame
            };

            // 先加按钮面板(Dock=Right)，再加主layout(Dock=Fill)
            connectFrame.Controls.Add(layout);
            connectFrame.Controls.Add(btnPanel);
        }
        private void BuildDeployFrame(GroupBox deployFrame)
        {
            // 使用 TableLayoutPanel 实现按钮居中和统一尺寸
            var deployLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(10, 10, 10, 10),
                AutoSize = false
            };
            deployLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            deployLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiUtils.Px(42)));
            deployLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiUtils.Px(42)));

            // 执行部署按钮
            _btnDeploy = new Button
            {
                Text = "执行部署",
                Font = new Font("Microsoft YaHei", 12, FontStyle.Bold),
                BackColor = Color.White,
                ForeColor = ColorTranslator.FromHtml("#333333"),
                FlatStyle = FlatStyle.Standard,
                Enabled = false,
                Margin = Padding.Empty,
                Size = new Size(130, 40),
                Anchor = AnchorStyles.Top
            };
            _btnDeploy.Click += (s, e) => ExecuteDeploy();
            deployLayout.Controls.Add(_btnDeploy, 0, 0);
            _deployButtons.Add(_btnDeploy);

            // 终止任务按钮
            _btnCancelTask = new Button
            {
                Text = "终止任务",
                Font = new Font("Microsoft YaHei", 12, FontStyle.Bold),
                BackColor = Color.White,
                ForeColor = ColorTranslator.FromHtml("#C62828"),
                FlatStyle = FlatStyle.Standard,
                Enabled = false,
                Margin = Padding.Empty,
                Size = new Size(130, 40),
                Anchor = AnchorStyles.Top
            };
            _btnCancelTask.Click += (s, e) => CancelCurrentTask();
            deployLayout.Controls.Add(_btnCancelTask, 0, 1);
            _deployButtons.Add(_btnCancelTask);

            deployFrame.Controls.Add(deployLayout);
        }
        private void BuildMenuFrame(GroupBox menuFrame)
        {
            var btnGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 2,
                Padding = new Padding(4)
            };

            // 设置列和行权重
            for (int c = 0; c < 3; c++)
                btnGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            for (int r = 0; r < 2; r++)
                btnGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            var menuButtons = new[]
            {
                ("虚拟机列表", "vm_list", "#1976D2"),
                ("主机列表", "host_list", "#5E35B1"),
                ("存储列表", "ds_list", "#EF6C00"),
                ("网络列表", "net_list", "#00838F"),
                ("编辑列表", "csv_editor", "#6D4C41"),
                ("虚拟机模板", "vm_template", "#00897B"),
            };

            for (int i = 0; i < menuButtons.Length; i++)
            {
                var (label, key, color) = menuButtons[i];
                int r = i / 3;
                int c = i % 3;

                var btn = new Button
                {
                    Text = label,
                    Font = new Font("Microsoft YaHei", 10, FontStyle.Bold),
                    BackColor = ColorTranslator.FromHtml(color),
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand,
                    Margin = new Padding(4), Dock = DockStyle.Fill
                };
                btn.Click += (s, e) => NavigateTo(key);

                btnGrid.Controls.Add(btn, c, r);
                _menuButtons.Add((key, btn));
            }

            menuFrame.Controls.Add(btnGrid);
        }
        private void BuildTipFrame(GroupBox tipFrame)
        {
            var tips = new[]
            {
                "① 连接成功后，在资源查询页执行查询操作，系统会自动缓存资源信息，后续切换 Tab 速度更快。",
                "② 点击上方功能菜单按钮，可直接跳转到对应功能页，并自动刷新内容。",
                "③ 顶部右侧的『帮助文档』『关于』『设置』按钮，可随时查阅帮助或调整参数。",
                "④ 点击底部状态栏，可展开/收起完整运行日志面板。",
                "⑤ 成功连接后，系统会记忆凭证（密码加密存储），下次连接时可快速选择历史 连接地址或连接别名自动填充。",
                "⑥ 在虚拟机/主机/存储/网络列表中，支持右键菜单进行批量操作（开机、关机、重启、快照等）。",
                "⑦ 列表页支持按列头排序和条件过滤，过滤后仍可正常排序，数据操作互不干扰。",
                "⑧ 『执行部署』功能会读取 CSV 模板文件批量创建虚拟机，部署前请确保模板格式正确。",
                "⑨ 若连接超时或异常，可点击『重置记忆』清除缓存密码后重新连接，或检查 PowerShell/PowerCLI 环境。",
                "⑩ 点击功能菜单中的『编辑列表』可在线编辑 CSV 部署模板文件。"
            };

            var flowPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                AutoScroll = false,
                WrapContents = false
            };

            foreach (var tip in tips)
            {
                var lbl = new Label
                {
                    Text = tip,
                    ForeColor = ColorTranslator.FromHtml("#555555"),
                    AutoSize = true,
                    Margin = new Padding(2)
                };
                flowPanel.Controls.Add(lbl);
            }

            tipFrame.Controls.Add(flowPanel);
        }
        private void NavigateTo(string key)
        {
            var mapping = new Dictionary<string, int>
            {
                { "vm_list", 1 },
                { "host_list", 2 },
                { "ds_list", 3 },
                { "net_list", 4 },
                { "csv_editor", 5 },
                { "vm_template", 6 },
            };

            if (!mapping.TryGetValue(key, out int idx)) return;
            if (_notebook == null) return;
            var keyTitleMap = new Dictionary<string, string>
            {
                { "vm_list", "虚拟机列表" },
                { "host_list", "主机列表" },
                { "ds_list", "存储列表" },
                { "net_list", "网络列表" },
                { "csv_editor", "编辑列表" },
                { "vm_template", "虚拟机模板" },
            };

            if (keyTitleMap.TryGetValue(key, out string title))
            {
                LogMessage("信息", $"跳转到【{title}】页面");
            }

            _notebook.SelectedIndex = idx;
        }
        private async Task ToggleConnection()
        {
            if (_connectDisconnectButton == null) return;

            if (_isConnected)
            {
                // 断开连接
                await Disconnect();
            }
            else
            {
                await ConnectWithPasswordPrompt();
            }
        }

        /// <summary>
        /// </summary>
        private void ShowPasswordDialog()
        {
            if (_txtPassword == null) return;
            
            using (var dialog = new Form())
            {
                dialog.Text = "密码";
                dialog.Icon = CoreUtils.GetAppIcon();
                dialog.Width = 300;
                dialog.Height = 150;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.AutoScaleMode = AutoScaleMode.Dpi;
                dialog.AutoScaleDimensions = new SizeF(96F, 96F);
                dialog.Font = new Font("Microsoft YaHei", 9, FontStyle.Regular);

                var lblPrompt = new Label
                {
                    Text = "请输入密码:", Location = new Point(20, 20),
                    AutoSize = true
                };
                dialog.Controls.Add(lblPrompt);

                var txtPassword = new TextBox
                {
                    Location = new Point(20, 50),
                    Width = 240,
                    PasswordChar = '*',
                    Text = _txtPassword.Text
                };
                dialog.Controls.Add(txtPassword);

                var btnOk = new Button
                {
                    Text = "确定",
                    DialogResult = DialogResult.OK,
                    Location = new Point(80, 85),
                    Width = 80
                };
                dialog.Controls.Add(btnOk);

                var btnCancel = new Button
                {
                    Text = "取消",
                    DialogResult = DialogResult.Cancel,
                    Location = new Point(170, 85),
                    Width = 80
                };
                dialog.Controls.Add(btnCancel);

                dialog.AcceptButton = btnOk;
                dialog.CancelButton = btnCancel;

                if (dialog.ShowDialog(this._root) == DialogResult.OK)
                {
                    _txtPassword.Text = txtPassword.Text;
                    UpdatePasswordLabel();
                }
            }
        }

        /// <summary>
        /// 更新密码标签状态（已废弃，密码行已从UI移除）
        /// </summary>
        private void UpdatePasswordLabel()
        {
            // 密码行已从UI移除，此方法保留仅为兼容旧代码调用
        }

        /// <summary>
        /// </summary>
        private async Task ConnectWithPasswordPrompt()
        {
            if (_connectDisconnectButton == null || _vcCombo == null || _userCombo == null || _txtPassword == null) return;

            string vc = _vcCombo.Text.Trim();
            string domain = _domainCombo?.Text.Trim() ?? "";

            if (string.IsNullOrEmpty(vc))
            {
                LogMessage("错误", "请填写VC地址");
                return;
            }

            // 先检查凭证：若 连接地址和用户域匹配且有有效密码，直接复用；否则仍弹出密码框
            bool hasCachedPassword = false;
            if (_savedCredentials.TryGetValue(vc, out var cred))
            {
                if (cred.TryGetValue("domain", out var credDomain) && credDomain == domain)
                {
                    if (cred.TryGetValue("password", out var savedPw) && !string.IsNullOrEmpty(savedPw))
                    {
                        // 凭证中有有效密码 → 直接复用连接，不弹框
                        _txtPassword.Text = savedPw;
                        hasCachedPassword = true;
                        LogMessage("信息", $"使用缓存凭证连接到 {vc}");
                    }
                }
            }

            // 如果没有缓存密码，弹出密码输入框
            if (!hasCachedPassword)
            {
                ShowPasswordDialog();
                
                if (string.IsNullOrEmpty(_txtPassword.Text))
                {
                    LogMessage("警告", "密码为空，不执行连接");
                    return;
                }
            }

            // 执行连接
            await Connect();
        }

        private async Task Connect()
        {
            if (_connectDisconnectButton == null || _vcCombo == null || _userCombo == null || _txtPassword == null) return;

            string vc = _vcCombo.Text.Trim();
            string user = _userCombo.Text.Trim();
            string pw = _txtPassword.Text;
            string domain = _domainCombo?.Text.Trim() ?? "";

            if (string.IsNullOrEmpty(vc) || string.IsNullOrEmpty(user))
            {
                LogMessage("错误", "请填写VC地址和用户名");
                return;
            }

            if (string.IsNullOrEmpty(pw))
            {
                LogMessage("错误", "密码为空，请先输入密码");
                return;
            }

            _connectDisconnectButton.Enabled = false;
            _connectDisconnectButton.Text = "连接中...";
            SetBusinessButtonsState(true);
            SetStatus($"正在连接到 {vc}...", "connecting");

            try
            {
                bool success = await SessionManager.Instance.ConnectAsync(vc, user, pw, string.IsNullOrEmpty(domain) ? null : domain,
                    (msg, level) => LogMessage(level ?? "信息", msg ?? ""));

                if (success)
                {
                    _isConnected = true;
                    _lastVc = vc;
                    _lastVcServer = vc;

                    _connectDisconnectButton.Text = "断开连接";
                    _connectDisconnectButton.Enabled = true;

                    SetStatus($"已连接至 {vc}", "success");
                    LogMessage("success", $"✓ 成功连接到 {vc}");

                    SyncConnectionStateToOtherTabs(true);
                    SaveCredentials(vc, user, domain);
                }
                else
                {
                    _connectDisconnectButton.Text = "开始连接";
                    _connectDisconnectButton.Enabled = true;
                    SetBusinessButtonsState(false);
                    SetStatus("连接失败", "error");
                    LogMessage("error", "连接失败，请检查地址、用户名和密码");
                }
            }
            catch (Exception ex)
            {
                _connectDisconnectButton.Text = "开始连接";
                _connectDisconnectButton.Enabled = true;
                SetBusinessButtonsState(false);
                SetStatus("连接异常", "error");
                LogMessage("error", $"连接异常: {ex.Message}");
            }
        }

        private async Task Disconnect()
        {
            if (_connectDisconnectButton == null) return;

            _connectDisconnectButton.Enabled = false;
            _connectDisconnectButton.Text = "断开中...";
            LogMessage("信息", "正在断开连接...");

            try
            {
                await SessionManager.Instance.DisconnectAsync();
                _isConnected = false;

                _connectDisconnectButton.Text = "开始连接";
                _connectDisconnectButton.Enabled = true;

                SetStatus("未连接 vCenter", "info");
                LogMessage("信息", "已断开连接");

                SetBusinessButtonsState(false);
                SyncConnectionStateToOtherTabs(false);
            }
            catch (Exception ex)
            {
                _connectDisconnectButton.Text = "开始连接";
                _connectDisconnectButton.Enabled = true;
                LogMessage("error", $"断开连接异常: {ex.Message}");
            }
        }
        private void OnAliasSelected()
        {
            if (_aliasCombo == null) return;

            string alias = _aliasCombo.Text.Trim();
            if (string.IsNullOrEmpty(alias)) return;

            foreach (var kvp in _savedCredentials)
            {
                var vc = kvp.Key;
                var cred = kvp.Value;
                if (cred.TryGetValue("alias", out var credAlias) && credAlias == alias)
                {
                    if (_vcCombo != null) _vcCombo.Text = vc;
                    if (_domainCombo != null && cred.TryGetValue("domain", out var domain))
                        _domainCombo.Text = domain;
                    if (_userCombo != null && cred.TryGetValue("user", out var user))
                        _userCombo.Text = user;
                    if (_txtPassword != null && cred.TryGetValue("password", out var pw))
                        _txtPassword.Text = pw;
                    LogMessage("信息", $"已选择别名「{alias}」→ {vc}");
                    UpdatePasswordLabel();
                    break;
                }
            }
        }
        private void OnVcSelected()
        {
            if (_vcCombo == null) return;

            string vc = _vcCombo.Text.Trim();
            if (string.IsNullOrEmpty(vc)) return;

            if (_savedCredentials.TryGetValue(vc, out var cred))
            {
                if (cred.TryGetValue("alias", out var alias) && !string.IsNullOrEmpty(alias))
                    _aliasCombo.Text = alias;
                if (cred.TryGetValue("domain", out var domain))
                    _domainCombo.Text = domain;
                if (cred.TryGetValue("user", out var user))
                    _userCombo.Text = user;
                if (cred.TryGetValue("password", out var pw))
                    _txtPassword.Text = pw;
                UpdatePasswordLabel();
            }
        }
        private void UpdateUserVar()
        {
            // 根据用户域和用户名自动更新显示
        }
        private string GetCredentialsPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "VMTool", "credentials.json");
        }
        private void LoadCredentials()
        {
            try
            {
                string credPath = GetCredentialsPath();
                if (!File.Exists(credPath)) return;

                string json = File.ReadAllText(credPath);
                var encoded = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(json);
                if (encoded == null) return;

                var vcHosts = new List<string>();
                var aliases = new List<string>();

                foreach (var kvp in encoded)
                {
                    string vc = kvp.Key;
                    if (vc == "_aliases") continue;

                    var cred = kvp.Value;
                    var decodedCred = new Dictionary<string, string>();

                    if (cred.TryGetValue("domain", out var domain))
                        decodedCred["domain"] = domain;
                    if (cred.TryGetValue("user", out var user))
                        decodedCred["user"] = user;
                    if (cred.TryGetValue("password", out var pw) && !string.IsNullOrEmpty(pw))
                    {
                        string decrypted = PasswordCrypto.Decrypt(pw);
                        decodedCred["password"] = decrypted ?? "";
                    }
                    if (cred.TryGetValue("alias", out var alias))
                        decodedCred["alias"] = alias;

                    _savedCredentials[vc] = decodedCred;
                    vcHosts.Add(vc);
                    if (!string.IsNullOrEmpty(alias))
                        aliases.Add(alias);
                }

                // 先填充下拉框
                if (vcHosts.Count > 0)
                {
                    _vcCombo.Items.AddRange(vcHosts.ToArray());
                }
                if (aliases.Count > 0)
                {
                    _aliasCombo.Items.AddRange(aliases.ToArray());
                }

                // 再设置默认值
                if (vcHosts.Count > 0)
                {
                    _vcCombo.Text = vcHosts[0];

                    if (_savedCredentials.TryGetValue(vcHosts[0], out var firstCred))
                    {
                        if (firstCred.TryGetValue("domain", out var domain))
                            _domainCombo.Text = domain;
                        if (firstCred.TryGetValue("user", out var user))
                            _userCombo.Text = user;
                        if (firstCred.TryGetValue("password", out var pw))
                            _txtPassword.Text = pw;
                        if (firstCred.TryGetValue("alias", out var alias))
                            _aliasCombo.Text = alias;
                    }
                    
                    // 更新密码标签状态
                    UpdatePasswordLabel();
                }
            }
            catch
            {
                _savedCredentials = new Dictionary<string, Dictionary<string, string>>();
            }
        }
        private void SaveCredentials(string vc, string user, string domain)
        {
            try
            {
                string credPath = GetCredentialsPath();
                string dir = Path.GetDirectoryName(credPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var encoded = new Dictionary<string, Dictionary<string, string>>();

                if (File.Exists(credPath))
                {
                    string json = File.ReadAllText(credPath);
                    encoded = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(json)
                              ?? new Dictionary<string, Dictionary<string, string>>();
                }

                string pw = _txtPassword?.Text ?? "";
                string alias = _aliasCombo?.Text ?? "";

                encoded[vc] = new Dictionary<string, string>
                {
                    { "domain", domain },
                    { "user", user },
                    { "password", PasswordCrypto.Encrypt(pw) },
                    { "alias", alias }
                };

                string outJson = JsonConvert.SerializeObject(encoded, Formatting.Indented);
                File.WriteAllText(credPath, outJson);

                // 更新内存中的凭证缓存
                var decodedCred = new Dictionary<string, string>
                {
                    { "domain", domain },
                    { "user", user },
                    { "password", pw },
                    { "alias", alias }
                };
                _savedCredentials[vc] = decodedCred;

                // 更新下拉框选项
                if (_vcCombo != null && !_vcCombo.Items.Contains(vc))
                {
                    _vcCombo.Items.Add(vc);
                }
                if (_aliasCombo != null && !string.IsNullOrEmpty(alias) && !_aliasCombo.Items.Contains(alias))
                {
                    _aliasCombo.Items.Add(alias);
                }

                LogMessage("信息", $"凭证已保存: {vc} (别名: {alias})");
            }
            catch (Exception ex)
            {
                LogMessage("error", $"保存凭证失败: {ex.Message}");
            }
        }
        private void ResetMemory()
        {
            string vcKey = (_vcCombo?.Text ?? "").Trim();
            if (string.IsNullOrEmpty(vcKey))
            {
                LogMessage("警告", "请先在首页选择或填写要清除的 vCenter 地址");
                return;
            }

            string aliasHint = "";
            if (_savedCredentials.TryGetValue(vcKey, out var cred) &&
                cred.TryGetValue("alias", out var a) && !string.IsNullOrEmpty(a))
                aliasHint = $"（别名：{a}）";

            var result = MessageBox.Show(
                $"确定要清除 vCenter「{vcKey}」{aliasHint} 的记忆凭证吗？\n其他 vCenter 的记忆不受影响。",
                "确认清除", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result != DialogResult.Yes) return;

            try
            {
                string credPath = GetCredentialsPath();
                string removedAlias = null;

                if (File.Exists(credPath))
                {
                    string json = File.ReadAllText(credPath);
                    var encoded = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(json);
                    if (encoded != null && encoded.ContainsKey(vcKey))
                    {
                        if (encoded[vcKey].TryGetValue("alias", out var al))
                            removedAlias = al;
                        encoded.Remove(vcKey);

                        string dir = Path.GetDirectoryName(credPath);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);
                        File.WriteAllText(credPath, JsonConvert.SerializeObject(encoded, Formatting.Indented));
                    }
                }

                _savedCredentials.Remove(vcKey);

                if (_vcCombo != null)
                {
                    _vcCombo.Items.Remove(vcKey);
                    _vcCombo.Text = "";
                }
                if (_aliasCombo != null)
                {
                    if (!string.IsNullOrEmpty(removedAlias))
                        _aliasCombo.Items.Remove(removedAlias);
                    _aliasCombo.Text = "";
                }
                if (_domainCombo != null) _domainCombo.Text = "vsphere.local";
                if (_userCombo != null) _userCombo.Text = "administrator";
                if (_txtPassword != null) _txtPassword.Text = "";

                LogMessage("success", $"已清除 vCenter「{vcKey}」的记忆凭证");
            }
            catch (Exception ex)
            {
                LogMessage("error", $"重置记忆失败: {ex.Message}");
            }
        }

        private void EnableConnectButton()
        {
            if (_connectDisconnectButton != null && _root.InvokeRequired)
            {
                _root.Invoke(new Action(EnableConnectButton));
                return;
            }

            if (_connectDisconnectButton != null)
                _connectDisconnectButton.Enabled = true;
        }
        private void SyncConnectionStateToOtherTabs(bool connected)
        {
            try
            {
                if (!_appContext.TryGetValue("tabs", out var tabsObj)) return;
                var tabs = tabsObj as List<object>;
                if (tabs == null) return;

                foreach (var tab in tabs)
                {
                    if (tab == this) continue;

                    try
                    {
                        var tabType = tab.GetType();
                        var connProp = tabType.GetProperty("ConnectedToVc");
                        if (connProp != null)
                            connProp.SetValue(tab, connected);
                    }
                    catch { }

                    if (!connected)
                    {
                        try
                        {
                            var tabType = tab.GetType();
                            var method = tabType.GetMethod("ClearAllCache");
                            if (method != null)
                                method.Invoke(tab, null);
                        }
                        catch { }
                    }

                    try
                    {
                        var tabType = tab.GetType();
                        var method = tabType.GetMethod("SetBusinessButtonsState");
                        if (method != null)
                            method.Invoke(tab, new object[] { connected });
                    }
                    catch { }

                    // 同步 OVF Tool 执行按钮状态
                    try
                    {
                        var tabType = tab.GetType();
                        var method = tabType.GetMethod("UpdateExecuteButtonState");
                        if (method != null)
                            method.Invoke(tab, null);
                    }
                    catch { }
                }
            }
            catch { }
        }
        private void SetBusinessButtonsState(bool enabled)
        {
            var state = enabled ? true : false;

            if (_aliasCombo != null) _aliasCombo.Enabled = !enabled;
            if (_vcCombo != null) _vcCombo.Enabled = !enabled;
            if (_domainCombo != null) _domainCombo.Enabled = !enabled;
            if (_userCombo != null) _userCombo.Enabled = !enabled;

            // 部署管理按钮
            foreach (var btn in _deployButtons)
            {
                btn.Enabled = enabled;
                if (!enabled)
                    btn.BackColor = ColorTranslator.FromHtml("#9E9E9E");
                else
                    btn.BackColor = Color.White;
            }

            // 功能菜单按钮保持启用（未连接也可点击，但会提示先连接）
            foreach (var (_, btn) in _menuButtons)
            {
                btn.Enabled = true;
            }
        }
        private void SetStatus(string statusText, string level = "info")
        {
            var colorMap = new Dictionary<string, Color>
            {
                { "info", ColorTranslator.FromHtml("#888888") },
                { "connecting", ColorTranslator.FromHtml("#1565C0") },
                { "success", ColorTranslator.FromHtml("#2E7D32") },
                { "warn", ColorTranslator.FromHtml("#EF6C00") },
                { "error", ColorTranslator.FromHtml("#C62828") },
            };

            Color color = colorMap.TryGetValue(level, out var c) ? c : ColorTranslator.FromHtml("#555555");

            // 通过 MainForm.UpdateStatus 更新状态栏图标
            if (_root is MainForm mainForm)
            {
                mainForm.UpdateStatus(statusText, color);
            }

            // 更新状态栏背景色
            if (_statusBar != null)
            {
                if (level == "success")
                    _statusBar.BackColor = ColorTranslator.FromHtml("#F5F7FA");
                else if (level == "error")
                    _statusBar.BackColor = ColorTranslator.FromHtml("#C62828");
                else
                    _statusBar.BackColor = ColorTranslator.FromHtml("#F5F7FA");
            }
        }

        private void BindGlobalButtons()
        {
            if (_appContext.TryGetValue("help_btn", out var helpBtn))
                (helpBtn as Button).Click += (s, e) => ShowHelp();
        }

        private void ShowHelp()
        {
            try
            {
                string helpPath = CoreUtils.GetHelpFilePath();
                if (File.Exists(helpPath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = helpPath,
                        UseShellExecute = true
                    });
                }
            }
            catch { }
        }

        private void ExecuteDeploy()
        {
            if (!SessionManager.Instance.IsConnected)
            {
                LogMessage("错误", "尚未连接到 vCenter Server，请先连接成功后再执行批量部署。");
                MessageBox.Show("尚未连接到 vCenter Server\n请先点击「开始连接」，连接成功后再执行批量部署。", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var deployManager = new Core.DeployManager(_root, LogMessage);
            Task.Run(async () =>
            {
                try
                {
                    _root.Invoke(new Action(() =>
                    {
                        _btnDeploy.Enabled = false;
                        _btnCancelTask.Enabled = true;
                        _btnCancelTask.Text = "终止任务";
                        LogMessage("信息", "开始执行批量部署...");
                    }));

                    // 优先从编辑器内存获取数据，避免未保存的修改丢失
                    // 必须在 UI 线程读取 DataGridView 数据，后台线程访问会返回空
                    List<Dictionary<string, string>>? memoryData = null;
                    if (_root is MainForm mainForm)
                    {
                        _root.Invoke(new Action(() =>
                        {
                            memoryData = mainForm.GetCsvEditorData();
                        }));
                    }

                    // 过滤内存数据中的全空行
                    if (memoryData != null && memoryData.Count > 0)
                    {
                        memoryData = memoryData.Where(row => row.Values.Any(v => !string.IsNullOrEmpty(v))).ToList();
                    }

                    if (memoryData != null && memoryData.Count > 0)
                    {
                        _root.Invoke(new Action(() =>
                        {
                            LogMessage("信息", "使用编辑器内存数据执行批量部署");
                        }));
                        await deployManager.DeployFromCsvAsync(vmListData: memoryData);
                    }
                    else
                    {
                        _root.Invoke(new Action(() =>
                        {
                            _btnDeploy.Enabled = true;
                            _btnCancelTask.Enabled = false;
                            LogMessage("warn", "获取编辑器内存数据失败或为空，已取消部署");
                            MessageBox.Show(
                                "未获取到「编辑列表」中的有效部署数据。\n\n" +
                                "请确认编辑列表中有数据，且 Name 和 IP 列已填写。",
                                "部署数据缺失",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
                        }));
                        return;
                    }

                    _root.Invoke(new Action(() =>
                    {
                        _btnDeploy.Enabled = true;
                        _btnCancelTask.Enabled = false;
                        LogMessage("信息", "批量部署任务完成");
                    }));
                }
                catch (Exception ex)
                {
                    _root.Invoke(new Action(() =>
                    {
                        _btnDeploy.Enabled = true;
                        _btnCancelTask.Enabled = false;
                        LogMessage("错误", $"部署过程中出错: {ex.Message}");
                    }));
                }
            });
        }
        public void LogMessage(string level, string message)
        {
            // 中文 → 英文 level 归一化（文本里的 [level] 也统一用英文，和 ResourceQueryTab 保持一致）
            string mfLevel = level switch
            {
                "信息" => "info",
                "成功" => "success",
                "警告" => "warn",
                "错误" => "error",
                _ => level ?? "info"
            };
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string text = $"[{timestamp}] [{mfLevel}] {message}\n";

            if (_root is MainForm mf)
            {
                // HomeTab._outputText 是 MainForm.output_text 的引用（通过 _appContext 传入），
                // 所以统一走 mf.AppendLog 写 output_text，同时它还会更新状态栏跑马灯。
                // 直接写 _outputText.AppendText 会导致和 mf.AppendLog 双写同一个控件而重复。
                mf.AppendLog(text, mfLevel);
            }
            else if (_outputText != null)
            {
                // fallback: 非 MainForm 宿主时直接写（实际不会触发）
                if (_root.InvokeRequired)
                {
                    _root.Invoke(new Action(() => LogMessage(level, message)));
                    return;
                }

                Color color = level switch
                {
                    "success" => ColorTranslator.FromHtml("#2E7D32"),
                    "warn" => ColorTranslator.FromHtml("#EF6C00"),
                    "error" => ColorTranslator.FromHtml("#C62828"),
                    "phase" => ColorTranslator.FromHtml("#1565C0"),
                    _ => ColorTranslator.FromHtml("#555555")
                };

                int start = _outputText.TextLength;
                _outputText.AppendText(text);
                _outputText.Select(start, text.Length);
                _outputText.SelectionColor = color;
                _outputText.SelectionStart = _outputText.TextLength;
                _outputText.ScrollToCaret();
            }
        }
        private void CreateCsvTemplate()
        {
            LogMessage("信息", "创建CSV模板功能开发中...");
        }
        private void BindInputContextMenus()
        {
            if (_parent == null) return;
            BindContextMenuRecursive(_parent);
        }

        private void BindContextMenuRecursive(Control parent)
        {
            foreach (Control ctrl in parent.Controls)
            {
                if (ctrl is ComboBox || ctrl is TextBox)
                {
                    AttachInputContextMenu(ctrl);
                }
                if (ctrl.HasChildren)
                {
                    BindContextMenuRecursive(ctrl);
                }
            }
        }

        private void AttachInputContextMenu(Control ctrl)
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("剪切").Click += (s, e) => CutText(ctrl);
            menu.Items.Add("复制").Click += (s, e) => CopyText(ctrl);
            menu.Items.Add("粘贴").Click += (s, e) => PasteText(ctrl);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("复制全部").Click += (s, e) => CopyAllText(ctrl);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("全选").Click += (s, e) => SelectAllText(ctrl);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("清空").Click += (s, e) => ClearText(ctrl);
            ctrl.ContextMenuStrip = menu;
        }

        private void CutText(Control ctrl)
        {
            if (ctrl is TextBox tb && !string.IsNullOrEmpty(tb.SelectedText))
            {
                Clipboard.SetText(tb.SelectedText);
                tb.SelectedText = "";
            }
            else if (ctrl is ComboBox cmb && !string.IsNullOrEmpty(cmb.SelectedText))
            {
                Clipboard.SetText(cmb.SelectedText);
                int start = cmb.SelectionStart;
                cmb.Text = cmb.Text.Remove(start, cmb.SelectionLength);
                cmb.SelectionStart = start;
            }
        }

        private void CopyText(Control ctrl)
        {
            if (ctrl is TextBox tb && !string.IsNullOrEmpty(tb.SelectedText))
            {
                Clipboard.SetText(tb.SelectedText);
            }
            else if (ctrl is ComboBox cmb && !string.IsNullOrEmpty(cmb.SelectedText))
            {
                Clipboard.SetText(cmb.SelectedText);
            }
        }

        private void CopyAllText(Control ctrl)
        {
            if (ctrl is TextBox tb && !string.IsNullOrEmpty(tb.Text))
            {
                Clipboard.SetText(tb.Text);
            }
            else if (ctrl is ComboBox cmb && !string.IsNullOrEmpty(cmb.Text))
            {
                Clipboard.SetText(cmb.Text);
            }
        }

        private void PasteText(Control ctrl)
        {
            string clipText = Clipboard.GetText();
            if (string.IsNullOrEmpty(clipText)) return;

            if (ctrl is TextBox tb)
            {
                if (!string.IsNullOrEmpty(tb.SelectedText))
                    tb.SelectedText = clipText;
                else
                {
                    int pos = tb.SelectionStart;
                    tb.Text = tb.Text.Insert(pos, clipText);
                    tb.SelectionStart = pos + clipText.Length;
                }
            }
            else if (ctrl is ComboBox cmb)
            {
                if (!string.IsNullOrEmpty(cmb.SelectedText))
                {
                    int start = cmb.SelectionStart;
                    cmb.Text = cmb.Text.Remove(start, cmb.SelectionLength).Insert(start, clipText);
                    cmb.SelectionStart = start + clipText.Length;
                }
                else
                {
                    int pos = cmb.SelectionStart;
                    cmb.Text = cmb.Text.Insert(pos, clipText);
                    cmb.SelectionStart = pos + clipText.Length;
                }
            }
        }

        private void SelectAllText(Control ctrl)
        {
            if (ctrl is TextBox tb)
                tb.SelectAll();
            else if (ctrl is ComboBox cmb)
            {
                cmb.SelectAll();
                cmb.Focus();
            }
        }

        private void ClearText(Control ctrl)
        {
            if (ctrl is TextBox tb)
                tb.Text = "";
            else if (ctrl is ComboBox cmb)
                cmb.Text = "";
        }

        // 窗口关闭时调用
        public void OnClosing()
        {
            // 窗口关闭时的清理逻辑
        }

        /// <summary>
        /// 终止当前正在执行的任务（部署或查询）
        /// </summary>
        private void CancelCurrentTask()
        {
            if (SessionManager.Instance.IsTaskRunning)
            {
                var result = MessageBox.Show(
                    "确定要终止当前正在执行的任务吗？\n\n这可能会导致正在进行的操作中断。",
                    "确认终止",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (result == DialogResult.Yes)
                {
                    LogMessage("warn", "正在请求终止任务...");
                    SessionManager.Instance.CancelCurrentTask();
                    
                    // 禁用终止按钮，防止重复点击
                    if (_btnCancelTask != null)
                    {
                        _btnCancelTask.Enabled = false;
                        _btnCancelTask.Text = "终止中...";
                    }
                }
            }
            else
            {
                LogMessage("info", "当前没有正在执行的任务");
            }
        }


    }
}