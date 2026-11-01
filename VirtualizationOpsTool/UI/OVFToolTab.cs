// ============================================================================
// OVFToolTab.cs - OVF 工具 Tab（封装 ovftool.exe 子进程）
// ============================================================================
// 三种操作模式：
//   ✅ 导出 OVF/OVA — 指定 VC + VM → 调用 OvfToolRunner 执行 ovftool
//   ✅ 导入 OVF — 指定本地 OVF + VC 目标 → 执行 ovftool import
//   ✅ OVF 转换 — OVF ↔ OVA 互转
//
// 核心功能：
//   动态表单（RadioButton 切换模式时重建字段）/ 命令预览 / 复制命令 / 环境缓存
//
// 依赖：OvfToolRunner（Core 目录，进程执行器）
// ============================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using VirtualizationOpsTool.Core;
using VirtualizationOpsTool.Utils;

namespace VirtualizationOpsTool.UI
{
    public class OVFToolTab
    {
        private readonly TabPage _parent;
        private readonly Form _root;
        private RadioButton? _rbExport;
        private RadioButton? _rbImport;
        private RadioButton? _rbConvert;
        private string _operationType = "export";

        private RadioButton? _rbVcsa;
        private RadioButton? _rbEsxi;
        private string _targetType = "vcsa";
        private FlowLayoutPanel? _targetTypeFrame;
        private Dictionary<string, Control> _currentFields = new Dictionary<string, Control>();

        // 表单容器
        private Panel? _formFrame;
        private TableLayoutPanel? _setupFrame;
        private GroupBox? _commandFrame;
        private TextBox? _commandText;
        private Panel? _optionsFrame;

        // 导出模式字段
        private ComboBox? _cmbVcsaHost;
        private ComboBox? _cmbEsxiHost;
        private ComboBox? _cmbUserDomain;
        private ComboBox? _cmbVmName;
        private ComboBox? _cmbExportPath;
        private Button? _btnBrowseExportPath;
        private CheckBox? _chkOverwrite;
        private CheckBox? _chkNoNvram;
        private CheckBox? _chkNoSslVerify;
        private CheckBox? _chkNoImageFiles;
        private TextBox? _txtOvaName;

        // 导入模式字段
        private ComboBox? _cmbOvaFile;
        private Button? _btnBrowseOva;
        private ComboBox? _cmbVcsaTargetHost;
        private TextBox? _txtImportVmName;
        private ComboBox? _cmbTargetHost;
        private ComboBox? _cmbDatastore;
        private ComboBox? _cmbTargetDomain;
        private ComboBox? _cmbDiskMode;
        private ComboBox? _cmbNetSrc;
        private ComboBox? _cmbNetDst;
        private CheckBox? _chkNetMapping;
        private CheckBox? _chkImportAsTemplate;

        // 转换模式字段
        private ComboBox? _cmbSrcType;
        private ComboBox? _cmbSrcPath;
        private Button? _btnBrowseSrc;
        private ComboBox? _cmbDstType;
        private ComboBox? _cmbDstPath;
        private Button? _btnBrowseDst;

        // 执行命令按钮引用（用于根据连接状态控制可用性）
        private Button? _btnExecute;
        private Dictionary<string, object> _envCache = new Dictionary<string, object>();

        // 虚拟机名称完整列表（用于输入筛选）
        private List<string> _vmNameFullList = new List<string>();
        // 标记是否正在从下拉列表选中（避免选中时触发筛选）
        private bool _vmNameSelecting = false;

        public OVFToolTab(TabPage parent, Form root, Control app)
        {
            _parent = parent;
            _root = root;
            BuildUI();
            InitCache();
            UpdateForm();
            // 修复：发布版本中窗口句柄可能未创建，需等待句柄创建后再调用 BeginInvoke
            if (_root.IsHandleCreated)
            {
                _root.BeginInvoke(new Action(BindInputContextMenus));
            }
            else
            {
                _root.Shown += (s, e) =>
                {
                    _root.BeginInvoke(new Action(BindInputContextMenus));
                };
            }
        }
        private void BuildUI()
        {
            var mainPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(6)
            };

            // =========== 主布局：垂直三段式（20% + 40% + 40%）==========
            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, CellBorderStyle = TableLayoutPanelCellBorderStyle.None, ColumnCount = 1, RowCount = 3,
                Padding = new Padding(0)
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 15));  // 基础设置 15%
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));  // 动态表单 45%
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));  // 命令预览 40%

            // =========== 操作类型 Panel（操作+目标同一行）==========
            var setupWrapper = new GroupBox
            {
                Text = "  操作类型  ",
                Font = new Font("Microsoft YaHei", 9, FontStyle.Bold),
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };

            _setupFrame = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Padding = new Padding(0)
            };
            _setupFrame.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
            _setupFrame.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1));
            _setupFrame.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            _setupFrame.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            setupWrapper.Controls.Add(_setupFrame);

            // 左侧：操作类型
            var operationPanel = new FlowLayoutPanel
            {
                AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = new Padding(0, 2, 0, 0)
            };

            var lblOperation = new Label
            {
                Text = "操作:",
                AutoSize = true,
                Margin = new Padding(0, 6, 8, 0)
            };
            operationPanel.Controls.Add(lblOperation);

            _rbExport = new RadioButton
            {
                Text = "导出虚拟机",
                AutoSize = true,
                Checked = true,
                Margin = new Padding(0, 4, 8, 0)
            };
            _rbExport.CheckedChanged += (s, e) => { if (_rbExport.Checked) { _operationType = "export"; UpdateForm(); } };
            operationPanel.Controls.Add(_rbExport);

            _rbImport = new RadioButton
            {
                Text = "导入虚拟机",
                AutoSize = true,
                Margin = new Padding(0, 4, 8, 0)
            };
            _rbImport.CheckedChanged += (s, e) => { if (_rbImport.Checked) { _operationType = "import"; UpdateForm(); } };
            operationPanel.Controls.Add(_rbImport);

            _rbConvert = new RadioButton
            {
                Text = "虚拟机转换",
                AutoSize = true,
                Margin = new Padding(0, 4, 8, 0)
            };
            _rbConvert.CheckedChanged += (s, e) => { if (_rbConvert.Checked) { _operationType = "convert"; UpdateForm(); } };
            operationPanel.Controls.Add(_rbConvert);

            _setupFrame.Controls.Add(operationPanel, 0, 0);

            // 中间竖线分隔
            var sepPanel = new Panel
            {
                BackColor = ColorTranslator.FromHtml("#CCCCCC"),
                Dock = DockStyle.Fill,
                Margin = Padding.Empty
            };
            _setupFrame.Controls.Add(sepPanel, 1, 0);

            // 右侧：目标类型（仅 export/import 时显示）
            var targetPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = new Padding(0, 2, 0, 0)
            };

            var lblTarget = new Label
            {
                Text = "目标:",
                AutoSize = true,
                Margin = new Padding(0, 6, 8, 0)
            };
            targetPanel.Controls.Add(lblTarget);

            _targetTypeFrame = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0)
            };

            _rbVcsa = new RadioButton
            {
                Text = "VCSA",
                AutoSize = true,
                Checked = true,
                Margin = new Padding(0, 4, 8, 0)
            };
            _rbVcsa.CheckedChanged += (s, e) => { if (_rbVcsa.Checked) { _targetType = "vcsa"; UpdateForm(); } };
            _targetTypeFrame.Controls.Add(_rbVcsa);

            _rbEsxi = new RadioButton
            {
                Text = "ESXi主机",
                AutoSize = true,
                Margin = new Padding(0, 4, 0, 0)
            };
            _rbEsxi.CheckedChanged += (s, e) => { if (_rbEsxi.Checked) { _targetType = "esxi"; UpdateForm(); } };
            _targetTypeFrame.Controls.Add(_rbEsxi);

            targetPanel.Controls.Add(_targetTypeFrame);
            _setupFrame.Controls.Add(targetPanel, 2, 0);

            mainLayout.Controls.Add(setupWrapper, 0, 0);

            // =========== 参数设置区（水平 70% 设置 + 30% 选项）==========
            var formLayout = new GroupBox
            {
                Text = "  参数设置  ",
                Font = new Font("Microsoft YaHei", 9, FontStyle.Bold),
                Dock = DockStyle.Fill,
                Padding = new Padding(4)
            };

            var formSplit = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, CellBorderStyle = TableLayoutPanelCellBorderStyle.None, ColumnCount = 2, RowCount = 1,
                Padding = new Padding(0)
            };
            formSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));  // 设置 70%
            formSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));  // 选项 30%

            _formFrame = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(4)
            };
            formSplit.Controls.Add(_formFrame, 0, 0);

            _optionsFrame = new Panel
            {
                Dock = DockStyle.Fill, Padding = new Padding(4)
            };
            formSplit.Controls.Add(_optionsFrame, 1, 0);

            formLayout.Controls.Add(formSplit);
            mainLayout.Controls.Add(formLayout, 0, 1);

            // =========== 命令预览区（底部）==========
            _commandFrame = new GroupBox
            {
                Text = "  命令预览  ",
                Font = new Font("Microsoft YaHei", 9, FontStyle.Bold),
                Dock = DockStyle.Fill,
                Padding = new Padding(0)
            };

            // 用 TableLayoutPanel 确保按钮行和文本框正确分层
            var cmdLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(6), CellBorderStyle = TableLayoutPanelCellBorderStyle.None
            };
            cmdLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiUtils.Px(28)));   // 按钮行：固定高度
            cmdLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 文本框：占据剩余空间

            // 按钮行（使用 TableLayoutPanel 确保按钮严格对齐）
            var btnPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            btnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, DpiUtils.Px(95)));
            btnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, DpiUtils.Px(95)));
            btnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, DpiUtils.Px(95)));

            var btnGenerate = new Button
            {
                Text = "生成命令",
                Size = new Size(90, 28),
                Margin = Padding.Empty,
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                FlatStyle = FlatStyle.Standard,
                TextAlign = ContentAlignment.MiddleCenter
            };
            btnGenerate.Click += (s, e) => GenerateCommand();
            btnPanel.Controls.Add(btnGenerate, 0, 0);

            var btnCopy = new Button
            {
                Text = "复制命令",
                Size = new Size(90, 28),
                Margin = Padding.Empty,
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                FlatStyle = FlatStyle.Standard,
                TextAlign = ContentAlignment.MiddleCenter
            };
            btnCopy.Click += (s, e) => CopyCommand();
            btnPanel.Controls.Add(btnCopy, 1, 0);

            _btnExecute = new Button
            {
                Text = "执行命令",
                Size = new Size(90, 28),
                Margin = Padding.Empty,
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                FlatStyle = FlatStyle.Standard,
                TextAlign = ContentAlignment.MiddleCenter,
                Enabled = SessionManager.Instance.IsConnected
            };
            _btnExecute.Click += async (s, e) => await ExecuteCommandAsync();
            btnPanel.Controls.Add(_btnExecute, 2, 0);

            cmdLayout.Controls.Add(btnPanel, 0, 0);
            _commandText = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = false,
                Font = new Font("Consolas", 10), ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle
            };

            var cmdContextMenu = new ContextMenuStrip();
            cmdContextMenu.Items.Add("复制选中内容").Click += (s, e) => CopySelectedCommand();
            cmdContextMenu.Items.Add("复制全部内容").Click += (s, e) => CopyAllCommand();
            cmdContextMenu.Items.Add(new ToolStripSeparator());
            cmdContextMenu.Items.Add("全选").Click += (s, e) => SelectAllCommand();
            cmdContextMenu.Items.Add(new ToolStripSeparator());
            cmdContextMenu.Items.Add("清空").Click += (s, e) => ClearCommand();
            _commandText.ContextMenuStrip = cmdContextMenu;

            cmdLayout.Controls.Add(_commandText, 0, 1);
            _commandFrame.Controls.Add(cmdLayout);

            mainLayout.Controls.Add(_commandFrame, 0, 2);

            mainPanel.Controls.Add(mainLayout);
            _parent.Controls.Add(mainPanel);
        }
        private void UpdateForm()
        {
            // 显示/隐藏目标类型选择
            if (_targetTypeFrame != null)
            {
                _targetTypeFrame.Visible = (_operationType == "export" || _operationType == "import");
            }

            // 清除旧表单
            _formFrame?.Controls.Clear();
            _optionsFrame?.Controls.Clear();
            _currentFields.Clear();

            // 刷新缓存
            RefreshEnvCache();

            // 根据操作类型创建表单
            switch (_operationType)
            {
                case "export":
                    CreateExportForm();
                    break;
                case "import":
                    CreateImportForm();
                    break;
                case "convert":
                    CreateConvertForm();
                    break;
            }

            // 表单创建完成后，用最新缓存填充下拉框
            ReloadFromEnvCache();
        }

        // =========== 表单布局辅助方法（TableLayoutPanel 自适应，支持 DPI 缩放）===========

        /// <summary>
        /// 创建表单布局（TableLayoutPanel，两列：标签 + 控件）
        /// </summary>
        private static TableLayoutPanel CreateFormLayout(int labelWidth = 90)
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                RowCount = 0,
                AutoSize = true
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return layout;
        }

        /// <summary>
        /// 添加一行：标签 + 控件
        /// </summary>
        private static void AddFormRow(TableLayoutPanel layout, string labelText, Control control, int controlHeight = 0)
        {
            int row = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiUtils.Px(38)));

            var lbl = new Label
            {
                Text = labelText,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 0, 2, 0),
                Margin = Padding.Empty,
                AutoSize = false,
                AutoEllipsis = false,
                Width = layout.ColumnStyles[0].Width > 1 ? (int)layout.ColumnStyles[0].Width - 2 : 88,
                Height = 22,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            layout.Controls.Add(lbl, 0, row);

            if (controlHeight > 0)
                control.Height = controlHeight;
            control.Margin = Padding.Empty;
            control.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            layout.Controls.Add(control, 1, row);
        }

        /// <summary>
        /// 添加一行：标签 + 两个控件（左右并排，如 ComboBox + Button）
        /// </summary>
        private static void AddFormRowTwoControls(TableLayoutPanel layout, string labelText, Control leftControl, Control rightControl, int rightWidth = 55)
        {
            int row = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiUtils.Px(38)));

            var lbl = new Label
            {
                Text = labelText,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 0, 2, 0),
                Margin = Padding.Empty,
                AutoSize = false,
                AutoEllipsis = false,
                Width = layout.ColumnStyles[0].Width > 1 ? (int)layout.ColumnStyles[0].Width - 2 : 88,
                Height = 22,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            layout.Controls.Add(lbl, 0, row);

            var inner = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                AutoSize = false
            };
            inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            inner.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, rightWidth));
            inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            leftControl.Margin = Padding.Empty;
            leftControl.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            inner.Controls.Add(leftControl, 0, 0);

            rightControl.Margin = Padding.Empty;
            rightControl.Size = new Size(rightWidth, 28);
            rightControl.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            inner.Controls.Add(rightControl, 1, 0);

            layout.Controls.Add(inner, 1, row);
        }

        /// <summary>
        /// 创建带两列子布局的容器（左：设置区 | 右：选项区）
        /// </summary>
        private static TableLayoutPanel CreateTwoColumnPanel(int leftPercent = 70, int rightPercent = 30)
        {
            var split = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(0)
            };
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, leftPercent));
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, rightPercent));
            split.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            return split;
        }

        /// <summary>
        /// 创建 CheckBox 列表（用于选项区）
        /// </summary>
        private static Panel CreateOptionsPanel(params CheckBox[] checkBoxes)
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
            int y = 0;
            foreach (var cb in checkBoxes)
            {
                cb.Location = new Point(0, y);
                cb.AutoSize = true;
                panel.Controls.Add(cb);
                y += 28;
            }
            return panel;
        }
        private void CreateExportForm()
        {
            var vcHostCandidates = BuildCandidatesFromAllSources("vc_host");
            var hostCandidates = BuildCandidatesFromAllSources("host");
            var vmCandidates = BuildCandidatesFromAllSources("vm");
            var domainCandidates = BuildCandidatesFromAllSources("user_domain");

            if (_targetType == "vcsa")
            {
                // ===== 连接信息（使用 TableLayoutPanel 自适应布局）=====
                var connLayout = CreateFormLayout(95);

                // VCSA IP
                _cmbVcsaHost = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
                _cmbVcsaHost.Items.AddRange(vcHostCandidates.ToArray());
                if (vcHostCandidates.Count > 0) _cmbVcsaHost.Text = vcHostCandidates[0];
                _currentFields["vcsa_host"] = _cmbVcsaHost;
                AddFormRow(connLayout, "VCSA IP:", _cmbVcsaHost);

                // 用户域
                _cmbUserDomain = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
                _cmbUserDomain.Items.AddRange(domainCandidates.ToArray());
                string defaultDomain = SessionManager.Instance.CurrentDomain
                    ?? (_envCache.TryGetValue("user_domain", out var ud) ? ud.ToString() : null)
                    ?? "vsphere.local";
                _cmbUserDomain.Text = defaultDomain;
                _currentFields["user_domain"] = _cmbUserDomain;
                AddFormRow(connLayout, "用户域:", _cmbUserDomain);

                // 虚拟机名称
                _cmbVmName = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
                _vmNameFullList = new List<string>(vmCandidates);
                _cmbVmName.Items.AddRange(vmCandidates.ToArray());
                if (vmCandidates.Count > 0) _cmbVmName.Text = vmCandidates[0];
                AttachVmNameFilter(_cmbVmName);
                _currentFields["vm_name"] = _cmbVmName;
                AddFormRow(connLayout, "虚拟机名称:", _cmbVmName);

                // 保存路径 + 浏览按钮
                _cmbExportPath = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
                _btnBrowseExportPath = new Button
                {
                    Text = "浏览",
                    FlatStyle = FlatStyle.Standard,
                    TextAlign = ContentAlignment.MiddleCenter
                };
                _btnBrowseExportPath.Click += (s, e) => BrowseExportPath();
                _currentFields["export_path"] = _cmbExportPath;
                AddFormRowTwoControls(connLayout, "保存路径:", _cmbExportPath, _btnBrowseExportPath, 55);

                // OVA名称（默认使用虚拟机名，可手动修改）
                _txtOvaName = new TextBox();
                _txtOvaName.Text = _cmbVmName?.Text ?? "";
                _currentFields["ova_name"] = _txtOvaName;
                AddFormRow(connLayout, "OVA名称:", _txtOvaName);

                // ===== 导出选项 =====
                _chkOverwrite = new CheckBox { Text = "覆盖", AutoSize = true, Checked = true };
                _chkNoNvram = new CheckBox { Text = "忽略NVRAM", AutoSize = true, Checked = true };
                _chkNoSslVerify = new CheckBox { Text = "忽略SSL", AutoSize = true, Checked = true };
                _chkNoImageFiles = new CheckBox { Text = "忽略镜像文件", AutoSize = true, Checked = true };
                var optionsPanel = CreateOptionsPanel(_chkOverwrite, _chkNoNvram, _chkNoSslVerify, _chkNoImageFiles);

                // ===== 组装到左右分栏 =====
                var split = CreateTwoColumnPanel(70, 30);
                var connWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
                connWrap.Controls.Add(connLayout);
                split.Controls.Add(connWrap, 0, 0);
                var optWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
                optWrap.Controls.Add(optionsPanel);
                split.Controls.Add(optWrap, 1, 0);

                _formFrame?.Controls.Add(split);
            }
            else
            {
                // ESXi模式
                var connLayout = CreateFormLayout(100);

                // ESXi主机IP
                _cmbEsxiHost = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
                _cmbEsxiHost.Items.AddRange(hostCandidates.ToArray());
                if (hostCandidates.Count > 0) _cmbEsxiHost.Text = hostCandidates[0];
                _currentFields["esxi_host"] = _cmbEsxiHost;
                AddFormRow(connLayout, "ESXi主机IP:", _cmbEsxiHost);

                // 虚拟机名称
                _cmbVmName = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
                _vmNameFullList = new List<string>(vmCandidates);
                _cmbVmName.Items.AddRange(vmCandidates.ToArray());
                if (vmCandidates.Count > 0) _cmbVmName.Text = vmCandidates[0];
                AttachVmNameFilter(_cmbVmName);
                _currentFields["vm_name"] = _cmbVmName;
                AddFormRow(connLayout, "虚拟机名称:", _cmbVmName);

                // 保存路径 + 浏览按钮
                _cmbExportPath = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
                _btnBrowseExportPath = new Button
                {
                    Text = "浏览",
                    FlatStyle = FlatStyle.Standard,
                    TextAlign = ContentAlignment.MiddleCenter
                };
                _btnBrowseExportPath.Click += (s, e) => BrowseExportPath();
                _currentFields["export_path"] = _cmbExportPath;
                AddFormRowTwoControls(connLayout, "保存路径:", _cmbExportPath, _btnBrowseExportPath, 55);

                // OVA名称（默认使用虚拟机名，可手动修改）
                _txtOvaName = new TextBox();
                _txtOvaName.Text = _cmbVmName?.Text ?? "";
                _currentFields["ova_name"] = _txtOvaName;
                AddFormRow(connLayout, "OVA名称:", _txtOvaName);

                // 导出选项
                _chkOverwrite = new CheckBox { Text = "覆盖", AutoSize = true, Checked = true };
                _chkNoNvram = new CheckBox { Text = "忽略NVRAM", AutoSize = true, Checked = true };
                _chkNoSslVerify = new CheckBox { Text = "忽略SSL", AutoSize = true, Checked = true };
                _chkNoImageFiles = new CheckBox { Text = "忽略镜像文件", AutoSize = true, Checked = true };
                var optionsPanel = CreateOptionsPanel(_chkOverwrite, _chkNoNvram, _chkNoSslVerify, _chkNoImageFiles);

                var split = CreateTwoColumnPanel(70, 30);
                var connWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
                connWrap.Controls.Add(connLayout);
                split.Controls.Add(connWrap, 0, 0);
                var optWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
                optWrap.Controls.Add(optionsPanel);
                split.Controls.Add(optWrap, 1, 0);

                _formFrame?.Controls.Add(split);
            }
        }
        private void CreateImportForm()
        {
            var vcHostCandidates = BuildCandidatesFromAllSources("vc_host");
            var hostCandidates = BuildCandidatesFromAllSources("host");
            var datastoreCandidates = BuildCandidatesFromAllSources("datastore");
            var domainCandidates = BuildCandidatesFromAllSources("user_domain");

            if (_targetType == "vcsa")
            {
                // ===== VCSA模式：左设置区（4行×2列双列布局） + 右选项区 =====
                var paramGrid = new TableLayoutPanel
                {
                    Dock = DockStyle.Top,
                    ColumnCount = 2,
                    RowCount = 4,
                    AutoSize = true,
                    Padding = new Padding(0),
                    Margin = new Padding(0)
                };
                paramGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
                paramGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
                for (int r = 0; r < 4; r++)
                    paramGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiUtils.Px(38)));

                // ---- 第0行左：OVA路径 + 浏览按钮 ----
                _cmbOvaFile = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
                _cmbOvaFile.TextChanged += (s, e) => OnOvaFileChange();
                _btnBrowseOva = new Button
                {
                    Text = "浏览",
                    FlatStyle = FlatStyle.Standard,
                    TextAlign = ContentAlignment.MiddleCenter
                };
                _btnBrowseOva.Click += (s, e) => BrowseOvaFile();
                var row0Left = CreateFormLayout(85);
                AddFormRowTwoControls(row0Left, "OVA路径:", _cmbOvaFile, _btnBrowseOva, 55);
                paramGrid.Controls.Add(row0Left, 0, 0);

                // ---- 第0行右：VM名称 ----
                _txtImportVmName = new TextBox();
                _currentFields["import_vm_name"] = _txtImportVmName;
                var row0Right = CreateFormLayout(85);
                AddFormRow(row0Right, "VM名称:", _txtImportVmName);
                paramGrid.Controls.Add(row0Right, 1, 0);

                // ---- 第1行左：VCSA IP ----
                _cmbVcsaTargetHost = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
                _cmbVcsaTargetHost.Items.AddRange(vcHostCandidates.ToArray());
                if (vcHostCandidates.Count > 0) _cmbVcsaTargetHost.Text = vcHostCandidates[0];
                _currentFields["vcsa_target_host"] = _cmbVcsaTargetHost;
                var row1Left = CreateFormLayout(85);
                AddFormRow(row1Left, "VCSA IP:", _cmbVcsaTargetHost);
                paramGrid.Controls.Add(row1Left, 0, 1);

                // ---- 第1行右：ESXi IP ----
                _cmbTargetHost = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
                _cmbTargetHost.Items.AddRange(hostCandidates.ToArray());
                if (hostCandidates.Count > 0) _cmbTargetHost.Text = hostCandidates[0];
                _currentFields["target_host"] = _cmbTargetHost;
                var row1Right = CreateFormLayout(85);
                AddFormRow(row1Right, "ESXi IP:", _cmbTargetHost);
                paramGrid.Controls.Add(row1Right, 1, 1);

                // ---- 第2行左：存储 ----
                _cmbDatastore = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
                _cmbDatastore.Items.AddRange(datastoreCandidates.ToArray());
                if (datastoreCandidates.Count > 0) _cmbDatastore.Text = datastoreCandidates[0];
                else _cmbDatastore.Text = "vsanDatastore";
                _currentFields["datastore"] = _cmbDatastore;
                var row2Left = CreateFormLayout(85);
                AddFormRow(row2Left, "存储:", _cmbDatastore);
                paramGrid.Controls.Add(row2Left, 0, 2);

                // ---- 第2行右：用户域 ----
                _cmbTargetDomain = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
                _cmbTargetDomain.Items.AddRange(domainCandidates.ToArray());
                string defaultTargetDomain = SessionManager.Instance.CurrentDomain
                    ?? (_envCache.TryGetValue("user_domain", out var ud2) ? ud2.ToString() : null)
                    ?? "vsphere.local";
                _cmbTargetDomain.Text = defaultTargetDomain;
                _currentFields["target_domain"] = _cmbTargetDomain;
                var row2Right = CreateFormLayout(85);
                AddFormRow(row2Right, "用户域:", _cmbTargetDomain);
                paramGrid.Controls.Add(row2Right, 1, 2);

                // ---- 第3行左：源网卡 ----
                _cmbNetSrc = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
                _cmbNetSrc.Items.AddRange(new object[] { "bridged", "nat", "Host-Only", "VMnet0", "VMnet1", "VMnet2" });
                _cmbNetSrc.Text = "bridged";
                var row3Right = CreateFormLayout(85);
                AddFormRow(row3Right, "源网卡:", _cmbNetSrc);
                paramGrid.Controls.Add(row3Right, 0, 3);

                // ---- 第3行右：目标网卡（追加行，超出4行需扩展） ----
                paramGrid.RowCount = 5;
                paramGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiUtils.Px(38)));
                _cmbNetDst = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
                var netCandidates = BuildCandidatesFromAllSources("network");
                _cmbNetDst.Items.AddRange(netCandidates.ToArray());
                if (netCandidates.Count > 0) _cmbNetDst.Text = netCandidates[0];
                else _cmbNetDst.Text = "VM Network";
                var row4Left = CreateFormLayout(85);
                AddFormRow(row4Left, "目标网卡:", _cmbNetDst);
                paramGrid.Controls.Add(row4Left, 1, 3);

                // ---- 第4行左：磁盘模式 ----
                _cmbDiskMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
                _cmbDiskMode.Items.AddRange(new object[] { "thin", "thick", "eagerZeroedThick" });
                _cmbDiskMode.Text = "thin";
                _currentFields["disk_mode"] = _cmbDiskMode;
                var row3Left = CreateFormLayout(85);
                AddFormRow(row3Left, "磁盘模式:", _cmbDiskMode);
                paramGrid.Controls.Add(row3Left, 0, 4);

                // 导入选项
                _chkNetMapping = new CheckBox { Text = "指定网卡", AutoSize = true };
                _chkNetMapping.CheckedChanged += (s, e) => UpdateNetMappingState();
                _chkImportAsTemplate = new CheckBox { Text = "导入为模板", AutoSize = true };
                var optionsPanel = CreateOptionsPanel(_chkNetMapping, _chkImportAsTemplate);

                UpdateNetMappingState();

                // 参数区直接放入 _formFrame（占外层 70%）
                var connWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6), AutoScroll = true };
                connWrap.Controls.Add(paramGrid);
                _formFrame?.Controls.Add(connWrap);

                // 选项区直接放入 _optionsFrame（占外层 30%）
                var optWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
                optWrap.Controls.Add(optionsPanel);
                _optionsFrame?.Controls.Add(optWrap);
            }
            else
            {
                // ESXi模式：单列布局
                var connLayout = CreateFormLayout(85);

                // OVA路径 + 浏览按钮
                _cmbOvaFile = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
                _cmbOvaFile.TextChanged += (s, e) => OnOvaFileChange();
                _btnBrowseOva = new Button
                {
                    Text = "浏览",
                    FlatStyle = FlatStyle.Standard,
                    TextAlign = ContentAlignment.MiddleCenter
                };
                _btnBrowseOva.Click += (s, e) => BrowseOvaFile();
                AddFormRowTwoControls(connLayout, "OVA路径:", _cmbOvaFile, _btnBrowseOva, 55);

                // ESXi IP
                _cmbTargetHost = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
                _cmbTargetHost.Items.AddRange(hostCandidates.ToArray());
                if (hostCandidates.Count > 0) _cmbTargetHost.Text = hostCandidates[0];
                _currentFields["target_host"] = _cmbTargetHost;
                AddFormRow(connLayout, "ESXi IP:", _cmbTargetHost);

                // VM名称
                _txtImportVmName = new TextBox();
                _currentFields["import_vm_name"] = _txtImportVmName;
                AddFormRow(connLayout, "VM名称:", _txtImportVmName);

                // 存储
                _cmbDatastore = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
                _cmbDatastore.Items.AddRange(datastoreCandidates.ToArray());
                if (datastoreCandidates.Count > 0) _cmbDatastore.Text = datastoreCandidates[0];
                else _cmbDatastore.Text = "vsanDatastore";
                _currentFields["datastore"] = _cmbDatastore;
                AddFormRow(connLayout, "存储:", _cmbDatastore);

                // 磁盘模式
                _cmbDiskMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
                _cmbDiskMode.Items.AddRange(new object[] { "thin", "thick", "eagerZeroedThick" });
                _cmbDiskMode.Text = "thin";
                _currentFields["disk_mode"] = _cmbDiskMode;
                AddFormRow(connLayout, "磁盘模式:", _cmbDiskMode);

                var wrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
                wrap.Controls.Add(connLayout);
                _formFrame?.Controls.Add(wrap);
            }
        }
        private void CreateConvertForm()
        {
            var connLayout = CreateFormLayout(85);

            // 源类型
            _cmbSrcType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbSrcType.Items.AddRange(new object[] { "ovf", "ova", "vmx" });
            _cmbSrcType.Text = "ovf";
            AddFormRow(connLayout, "源类型:", _cmbSrcType);

            // 源路径 + 浏览按钮
            _cmbSrcPath = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
            _btnBrowseSrc = new Button
            {
                Text = "浏览",
                FlatStyle = FlatStyle.Standard,
                TextAlign = ContentAlignment.MiddleCenter
            };
            _btnBrowseSrc.Click += (s, e) => BrowseSrcPath();
            AddFormRowTwoControls(connLayout, "源路径:", _cmbSrcPath, _btnBrowseSrc, 55);

            // 目标类型
            _cmbDstType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbDstType.Items.AddRange(new object[] { "ovf", "ova", "vmx" });
            _cmbDstType.Text = "ova";
            AddFormRow(connLayout, "目标类型:", _cmbDstType);

            // 目标路径 + 浏览按钮
            _cmbDstPath = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
            _btnBrowseDst = new Button
            {
                Text = "浏览",
                FlatStyle = FlatStyle.Standard,
                TextAlign = ContentAlignment.MiddleCenter
            };
            _btnBrowseDst.Click += (s, e) => BrowseDstPath();
            AddFormRowTwoControls(connLayout, "目标路径:", _cmbDstPath, _btnBrowseDst, 55);

            var wrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
            wrap.Controls.Add(connLayout);
            _formFrame?.Controls.Add(wrap);
        }
        private void BrowseExportPath()
        {
            var dlg = new FolderBrowserDialog();
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                if (_cmbExportPath != null)
                    _cmbExportPath.Text = dlg.SelectedPath;
            }
        }
        private void BrowseOvaFile()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "OVA/OVF 文件|*.ova;*.ovf|所有文件|*.*",
                Title = "选择 OVA/OVF 文件"
            };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                if (_cmbOvaFile != null)
                {
                    _cmbOvaFile.Text = dlg.FileName;
                    OnOvaFileChange();
                }
            }
        }
        private void BrowseSrcPath()
        {
            string ext = _cmbSrcType?.Text ?? "ovf";
            var dlg = new OpenFileDialog
            {
                Filter = $"{ext.ToUpper()} 文件|*.{ext}|所有文件|*.*",
                Title = "选择源文件"
            };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                if (_cmbSrcPath != null)
                    _cmbSrcPath.Text = dlg.FileName;
            }
        }
        private void BrowseDstPath()
        {
            string dstType = _cmbDstType?.Text ?? "ova";
            string srcPath = _cmbSrcPath?.Text ?? "";

            if (dstType == "ovf" || dstType == "ova")
            {
                string defaultName = "";
                if (!string.IsNullOrEmpty(srcPath))
                {
                    string baseName = Path.GetFileNameWithoutExtension(srcPath);
                    defaultName = $"{baseName}.{dstType}";
                }

                var dlg = new SaveFileDialog
                {
                    Filter = $"{dstType.ToUpper()} 文件|*.{dstType}",
                    DefaultExt = dstType,
                    FileName = defaultName
                };
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    if (_cmbDstPath != null)
                        _cmbDstPath.Text = dlg.FileName;
                }
            }
            else
            {
                var dlg = new FolderBrowserDialog();
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    if (_cmbDstPath != null)
                        _cmbDstPath.Text = dlg.SelectedPath;
                }
            }
        }
        private void OnOvaFileChange()
        {
            string ovaFile = _cmbOvaFile?.Text.Trim() ?? "";
            if (!string.IsNullOrEmpty(ovaFile) && _txtImportVmName != null)
            {
                string vmName = Path.GetFileNameWithoutExtension(ovaFile);
                _txtImportVmName.Text = vmName;
            }
        }
        private void UpdateNetMappingState()
        {
            bool enabled = _chkNetMapping?.Checked ?? false;
            if (_cmbNetSrc != null) _cmbNetSrc.Enabled = enabled;
            if (_cmbNetDst != null) _cmbNetDst.Enabled = enabled;
        }
        private void GenerateCommand()
        {
            try
            {
                string cmd = "";
                switch (_operationType)
                {
                    case "export":
                        cmd = BuildExportCommand();
                        break;
                    case "import":
                        cmd = BuildImportCommand();
                        break;
                    case "convert":
                        cmd = BuildConvertCommand();
                        break;
                    default:
                        cmd = "# 未知操作类型";
                        break;
                }

                if (_commandText != null)
                    _commandText.Text = cmd;

                // 每次生成命令后同步刷新执行按钮状态
                UpdateExecuteButtonState();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"生成命令时出错: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private string BuildExportCommand()
        {
            // 根据连接状态决定是否加入密码：已连接则加入，未连接则不加入
            bool isConnected = SessionManager.Instance.IsConnected;
            var cred = SessionManager.Instance.CurrentCredentials;
            string password = isConnected ? (cred?.Password ?? "") : "";

            if (_targetType == "vcsa")
            {
                string vcsaHost = _cmbVcsaHost?.Text.Trim() ?? "";
                string domain = _cmbUserDomain?.Text.Trim() ?? "";
                string userVal = !string.IsNullOrEmpty(domain) ? $"administrator@{domain}" : "";
                string vmName = _cmbVmName?.Text.Trim() ?? "";
                string exportPath = _cmbExportPath?.Text.Trim() ?? "";

                if (string.IsNullOrEmpty(vcsaHost) || string.IsNullOrEmpty(userVal) ||
                    string.IsNullOrEmpty(vmName) || string.IsNullOrEmpty(exportPath))
                {
                    return "# 请填写所有必需字段";
                }

                var cmdParts = new List<string> { "ovftool" };
                if (_chkOverwrite?.Checked == true) cmdParts.Add("-o");
                if (_chkNoNvram?.Checked == true) cmdParts.Add("--noNvramFile");
                if (_chkNoSslVerify?.Checked == true) cmdParts.Add("--noSSLVerify");
                if (_chkNoImageFiles?.Checked == true) cmdParts.Add("--noImageFiles");

                string userEncoded = Uri.EscapeDataString(userVal);
                string source;
                if (isConnected && !string.IsNullOrEmpty(password))
                {
                    string pwEncoded = Uri.EscapeDataString(password);
                    source = $"vi://{userEncoded}:{pwEncoded}@{vcsaHost}/Datacenter/vm/{vmName}";
                }
                else
                {
                    source = $"vi://{userEncoded}@{vcsaHost}/Datacenter/vm/{vmName}";
                }
                string ovaName = _txtOvaName?.Text.Trim() ?? vmName;
                if (string.IsNullOrEmpty(ovaName)) ovaName = vmName;
                string cleanPath = exportPath.TrimEnd('/', '\\');
                string fullPath = $"{cleanPath}/{ovaName}.ova";
                cmdParts.Add($"\"{source}\"");
                cmdParts.Add($"\"{fullPath}\"");

                return string.Join(" ", cmdParts);
            }
            else
            {
                string host = _cmbEsxiHost?.Text.Trim() ?? "";
                string userVal = "root";
                string vmName = _cmbVmName?.Text.Trim() ?? "";
                string exportPath = _cmbExportPath?.Text.Trim() ?? "";

                if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(vmName) || string.IsNullOrEmpty(exportPath))
                {
                    return "# 请填写所有必需字段";
                }

                var cmdParts = new List<string> { "ovftool" };
                if (_chkOverwrite?.Checked == true) cmdParts.Add("-o");
                if (_chkNoNvram?.Checked == true) cmdParts.Add("--noNvramFile");
                if (_chkNoSslVerify?.Checked == true) cmdParts.Add("--noSSLVerify");
                if (_chkNoImageFiles?.Checked == true) cmdParts.Add("--noImageFiles");

                string source;
                if (isConnected && !string.IsNullOrEmpty(password))
                {
                    string pwEncoded = Uri.EscapeDataString(password);
                    source = $"vi://{userVal}:{pwEncoded}@{host}/{vmName}";
                }
                else
                {
                    source = $"vi://{userVal}@{host}/{vmName}";
                }
                string ovaName = _txtOvaName?.Text.Trim() ?? vmName;
                if (string.IsNullOrEmpty(ovaName)) ovaName = vmName;
                string cleanPath = exportPath.TrimEnd('/', '\\');
                string fullPath = $"{cleanPath}/{ovaName}.ova";
                cmdParts.Add($"\"{source}\"");
                cmdParts.Add($"\"{fullPath}\"");

                return string.Join(" ", cmdParts);
            }
        }
        private string BuildImportCommand()
        {
            string ovaFile = _cmbOvaFile?.Text.Trim() ?? "";
            string vmName = _txtImportVmName?.Text.Trim() ?? "";
            string datastore = _cmbDatastore?.Text.Trim() ?? "";
            string diskMode = _cmbDiskMode?.Text.Trim() ?? "";

            if (string.IsNullOrEmpty(ovaFile) || string.IsNullOrEmpty(vmName) || string.IsNullOrEmpty(datastore))
            {
                return "# 请填写所有必需字段";
            }

            // 根据连接状态决定是否加入密码：已连接则加入，未连接则不加入
            bool isConnected = SessionManager.Instance.IsConnected;
            var cred = SessionManager.Instance.CurrentCredentials;
            string password = isConnected ? (cred?.Password ?? "") : "";

            var cmdParts = new List<string> { "ovftool" };
            cmdParts.Add("--overwrite");
            cmdParts.Add("--acceptAllEulas");
            cmdParts.Add($"-n={vmName}");
            cmdParts.Add($"-ds={datastore}");

            if (_targetType == "vcsa")
            {
                string vcsaHost = _cmbVcsaTargetHost?.Text.Trim() ?? "";
                string esxiHost = _cmbTargetHost?.Text.Trim() ?? "";
                string domain = _cmbTargetDomain?.Text.Trim() ?? "";
                string user = !string.IsNullOrEmpty(domain) ? $"administrator@{domain}" : "";

                if (string.IsNullOrEmpty(vcsaHost) || string.IsNullOrEmpty(esxiHost) || string.IsNullOrEmpty(user))
                {
                    return "# 请填写所有必需字段";
                }

                if (_chkNetMapping?.Checked == true)
                {
                    string netSrc = _cmbNetSrc?.Text.Trim() ?? "";
                    string netDst = _cmbNetDst?.Text.Trim() ?? "";
                    if (!string.IsNullOrEmpty(netSrc) && !string.IsNullOrEmpty(netDst))
                    {
                        cmdParts.Add($"--net:{netSrc}=\"{netDst}\"");
                    }
                }

                cmdParts.Add($"-dm={diskMode}");
                cmdParts.Add("--noSSLVerify");
                if (_chkImportAsTemplate?.Checked == true) cmdParts.Add("--importAsTemplate");

                string userEncoded = Uri.EscapeDataString(user);
                string target;
                if (isConnected && !string.IsNullOrEmpty(password))
                {
                    string pwEncoded = Uri.EscapeDataString(password);
                    target = $"\"vi://{userEncoded}:{pwEncoded}@{vcsaHost}/?ip={esxiHost}\"";
                }
                else
                {
                    target = $"\"vi://{userEncoded}@{vcsaHost}/?ip={esxiHost}\"";
                }
                cmdParts.Add($"\"{ovaFile}\"");
                cmdParts.Add(target);
            }
            else
            {
                string host = _cmbTargetHost?.Text.Trim() ?? "";
                string user = "root";
                if (string.IsNullOrEmpty(host))
                {
                    return "# 请填写所有必需字段";
                }
                cmdParts.Add($"-dm={diskMode}");
                cmdParts.Add("--noSSLVerify");
                string target;
                if (isConnected && !string.IsNullOrEmpty(password))
                {
                    string pwEncoded = Uri.EscapeDataString(password);
                    target = $"vi://{user}:{pwEncoded}@{host}";
                }
                else
                {
                    target = $"vi://{user}@{host}";
                }
                cmdParts.Add($"\"{ovaFile}\"");
                cmdParts.Add(target);
            }

            return string.Join(" ", cmdParts);
        }
        private string BuildConvertCommand()
        {
            string srcPath = _cmbSrcPath?.Text.Trim() ?? "";
            string dstPath = _cmbDstPath?.Text.Trim() ?? "";

            if (string.IsNullOrEmpty(srcPath) || string.IsNullOrEmpty(dstPath))
            {
                return "# 请填写源路径和目标路径";
            }

            return $"ovftool \"{srcPath}\" \"{dstPath}\"";
        }
        private void CopyCommand()
        {
            string command = _commandText?.Text.Trim() ?? "";
            if (!string.IsNullOrEmpty(command))
            {
                Clipboard.SetText(command);
                ShowCopySuccess();
            }
        }
        private void CopySelectedCommand()
        {
            if (_commandText != null && !string.IsNullOrEmpty(_commandText.SelectedText))
            {
                Clipboard.SetText(_commandText.SelectedText);
            }
        }

        private void CopyAllCommand()
        {
            if (_commandText != null && !string.IsNullOrEmpty(_commandText.Text))
            {
                Clipboard.SetText(_commandText.Text);
            }
        }

        private void SelectAllCommand()
        {
            if (_commandText != null)
            {
                _commandText.SelectAll();
            }
        }

        private void ClearCommand()
        {
            if (_commandText != null)
            {
                _commandText.Text = "";
            }
        }
        private void ShowCopySuccess()
        {
            var toast = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                ShowInTaskbar = false,
                TopMost = true,
                BackColor = Color.FromArgb(76, 175, 80),
                Size = new Size(200, 35)
            };

            var label = new Label
            {
                Text = "命令已复制到剪贴板！",
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei", 10), Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };
            toast.Controls.Add(label);

            int x = _root.Location.X + (_root.Width - toast.Width) / 2;
            int y = _root.Location.Y + _root.Height - 80;
            toast.Location = new Point(x, y);

            toast.Show();

            var timer = new System.Windows.Forms.Timer();
            timer.Interval = 800;
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                toast.Close();
                timer.Dispose();
            };
            timer.Start();
        }
        private void RefreshEnvCache()
        {
            try
            {
                var cache = UnifiedCache.CacheLoadResources();
                if (cache != null)
                {
                    _envCache = new Dictionary<string, object>(cache);
                }
            }
            catch
            {
                // 忽略错误
            }
        }
        private void InitCache()
        {
            RefreshEnvCache();
        }
        private List<string> BuildCandidatesFromAllSources(string fieldKind)
        {
            var result = new List<string>();
            var seen = new HashSet<string>();

            void AddIfNew(string item)
            {
                if (!string.IsNullOrEmpty(item) && !seen.Contains(item))
                {
                    seen.Add(item);
                    result.Add(item);
                }
            }

            // 从缓存读取
            if (_envCache != null)
            {
                switch (fieldKind)
                {
                    case "vc_host":
                        if (_envCache.TryGetValue("vc_host", out var vc) && vc is string vcStr && !string.IsNullOrEmpty(vcStr))
                            AddIfNew(vcStr.Trim());
                        break;
                    case "host":
                        foreach (var item in GetListFromCache("host_names")) AddIfNew(item);
                        foreach (var item in GetListFromCache("hosts")) AddIfNew(item);
                        foreach (var item in GetListFromCache("host_ips")) AddIfNew(item);
                        break;
                    case "vm":
                        foreach (var item in GetListFromCache("vm_names")) AddIfNew(item);
                        foreach (var item in GetListFromCache("vms")) AddIfNew(item);
                        break;
                    case "datastore":
                        foreach (var item in GetListFromCache("datastore_names")) AddIfNew(item);
                        foreach (var item in GetListFromCache("datastores")) AddIfNew(item);
                        break;
                    case "user_domain":
                        if (_envCache.TryGetValue("user_domain", out var ud))
                        {
                            if (ud is string udStr && !string.IsNullOrEmpty(udStr))
                            {
                                string clean = udStr.Trim();
                                if (clean.Contains("@"))
                                    clean = clean.Split('@')[1].Trim();
                                if (!string.IsNullOrEmpty(clean))
                                    AddIfNew(clean);
                            }
                        }
                        break;
                    case "network":
                        foreach (var item in GetListFromCache("network_names")) AddIfNew(item);
                        foreach (var item in GetListFromCache("portgroups")) AddIfNew(item);
                        foreach (var item in GetListFromCache("networks")) AddIfNew(item);
                        break;
                }
            }

            // 硬编码兜底
            var fallback = new Dictionary<string, List<string>>
            {
                { "datastore", new List<string> { "vsanDatastore", "datastore1" } },
                { "user_domain", new List<string> { "vsphere.local", "local.net" } },
                { "network", new List<string> { "VM Network", "Management Network" } }
            };

            if (fallback.TryGetValue(fieldKind, out var fbList))
            {
                foreach (var item in fbList)
                    AddIfNew(item);
            }

            return result;
        }

        private List<string> GetListFromCache(string key)
        {
            var result = new List<string>();
            if (_envCache.TryGetValue(key, out var val))
            {
                if (val is List<string> strList)
                {
                    foreach (var item in strList)
                    {
                        if (!string.IsNullOrEmpty(item?.Trim()))
                            result.Add(item.Trim());
                    }
                }
                else if (val is Newtonsoft.Json.Linq.JArray jArray)
                {
                    foreach (var token in jArray)
                    {
                        var str = token?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(str))
                            result.Add(str);
                    }
                }
                else if (val is string strVal && !string.IsNullOrEmpty(strVal))
                {
                    result.Add(strVal.Trim());
                }
            }
            return result;
        }

        /// <summary>
        /// </summary>
        public void ReloadFromEnvCache()
        {
            RefreshEnvCache();

            var vcHostCandidates = BuildCandidatesFromAllSources("vc_host");
            var hostCandidates = BuildCandidatesFromAllSources("host");
            var vmCandidates = BuildCandidatesFromAllSources("vm");
            var datastoreCandidates = BuildCandidatesFromAllSources("datastore");
            var domainCandidates = BuildCandidatesFromAllSources("user_domain");

            void EnsureCachedValue(ComboBox? cmb, List<string> defaultList)
            {
                if (cmb == null) return;
                var currentItems = new HashSet<string>(cmb.Items.Cast<string>());
                foreach (var item in defaultList)
                {
                    if (!currentItems.Contains(item))
                        cmb.Items.Add(item);
                }

                if (string.IsNullOrEmpty(cmb.Text) && defaultList.Count > 0)
                {
                    cmb.Text = defaultList[0];
                }
            }

            // 导出模式
            if (_cmbVcsaHost != null) EnsureCachedValue(_cmbVcsaHost, vcHostCandidates);
            if (_cmbEsxiHost != null) EnsureCachedValue(_cmbEsxiHost, hostCandidates);
            if (_cmbVmName != null) EnsureCachedValue(_cmbVmName, vmCandidates);
            if (_cmbUserDomain != null) EnsureCachedValue(_cmbUserDomain, domainCandidates);

            // 导入模式
            if (_cmbVcsaTargetHost != null) EnsureCachedValue(_cmbVcsaTargetHost, vcHostCandidates);
            if (_cmbTargetHost != null) EnsureCachedValue(_cmbTargetHost, hostCandidates);
            if (_cmbDatastore != null) EnsureCachedValue(_cmbDatastore, datastoreCandidates);
            if (_cmbTargetDomain != null) EnsureCachedValue(_cmbTargetDomain, domainCandidates);
            var networkCandidates = BuildCandidatesFromAllSources("network");
            if (_cmbNetSrc != null) EnsureCachedValue(_cmbNetSrc, networkCandidates);
            if (_cmbNetDst != null) EnsureCachedValue(_cmbNetDst, networkCandidates);
        }
        // 为所有输入框（ComboBox/TextBox）绑定右键菜单
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

        /// <summary>
        /// 执行命令（异步）
        /// </summary>
        private async Task ExecuteCommandAsync()
        {
            if (_commandText == null) return;

            string cmd = _commandText.Text.Trim();
            if (string.IsNullOrEmpty(cmd) || cmd.StartsWith("#"))
            {
                MessageBox.Show("请先生成命令", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 所有模式统一直接执行界面上生成的命令
            await ExecuteGenericAsync();
        }

        /// <summary>
        /// 执行通用命令（导出/导入/转换等模式）
        /// </summary>
        private async Task ExecuteGenericAsync()
        {
            string cmd = _commandText.Text.Trim();
            string modeName = _operationType == "export" ? "导出" : (_operationType == "import" ? "导入" : "转换");

            // 去掉开头的 "ovftool " 前缀，只保留参数部分
            // 因为 OvfToolRunner 已经知道可执行文件路径，Arguments 不应包含程序名
            string arguments = cmd;
            if (arguments.StartsWith("ovftool ", StringComparison.OrdinalIgnoreCase))
            {
                arguments = arguments.Substring("ovftool ".Length).Trim();
            }
            else if (arguments.Equals("ovftool", StringComparison.OrdinalIgnoreCase))
            {
                arguments = "";
            }

            // 清空命令预览区，同时输出到主日志
            _commandText.Text = "";
            _commandText.ReadOnly = true;
            AppendLogToMain($"[OVF{modeName}] 开始执行");

            var runner = new OvfToolRunner();
            runner.OnLogReceived += (logLine) =>
            {
                _root.BeginInvoke(new Action(() =>
                {
                    _commandText.AppendText(logLine + Environment.NewLine);
                    _commandText.SelectionStart = _commandText.Text.Length;
                    _commandText.ScrollToCaret();
                    AppendLogToMain(logLine);
                }));
            };

            try
            {
                var result = await runner.RunCommand(arguments);

                _root.Invoke(new Action(() =>
                {
                    _commandText.AppendText(Environment.NewLine);
                    if (result.IsSuccess)
                    {
                        _commandText.AppendText($"[成功] {modeName}完成，退出码: {result.ExitCode}" + Environment.NewLine);
                        AppendLogToMain($"[OVF{modeName}] 成功完成，退出码: {result.ExitCode}", "success");
                    }
                    else
                    {
                        _commandText.AppendText($"[失败] {result.ErrorMessage}，退出码: {result.ExitCode}" + Environment.NewLine);
                        AppendLogToMain($"[OVF{modeName}] 失败: {result.ErrorMessage}，退出码: {result.ExitCode}", "error");
                    }
                    _commandText.ReadOnly = false;
                }));
            }
            catch (Exception ex)
            {
                _root.Invoke(new Action(() =>
                {
                    _commandText.AppendText($"[异常] {ex.Message}" + Environment.NewLine);
                    AppendLogToMain($"[OVF{modeName}] 异常: {ex.Message}", "error");
                    _commandText.ReadOnly = false;
                }));
            }
        }

        /// <summary>
        /// 将日志同时输出到主程序日志区
        /// </summary>
        private void AppendLogToMain(string message, string level = "info")
        {
            try
            {
                if (_root is MainForm mainForm)
                {
                    mainForm.AppendLog($"[{DateTime.Now:HH:mm:ss}] [{level}] {message}{Environment.NewLine}", level);
                }
            }
            catch { }
        }

        /// <summary>
        /// 根据连接状态更新执行按钮可用性
        /// </summary>
        public void UpdateExecuteButtonState()
        {
            if (_btnExecute != null)
            {
                _btnExecute.Enabled = SessionManager.Instance.IsConnected;
            }
        }

        /// <summary>
        /// 给虚拟机名称ComboBox附加输入筛选功能
        /// </summary>
        private void AttachVmNameFilter(ComboBox cmb)
        {
            if (cmb == null) return;

            cmb.TextUpdate += (s, e) =>
            {
                if (_vmNameSelecting) return;

                string filter = cmb.Text ?? "";
                int selStart = cmb.SelectionStart;

                var filtered = string.IsNullOrEmpty(filter)
                    ? _vmNameFullList
                    : _vmNameFullList.Where(x => x.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

                cmb.Items.Clear();
                cmb.Items.AddRange(filtered.ToArray());

                cmb.Text = filter;
                cmb.SelectionStart = selStart;
                cmb.SelectionLength = 0;

                if (filtered.Count > 0 && cmb.DroppedDown == false)
                {
                    cmb.DroppedDown = true;
                }

                Cursor.Current = Cursors.Default;
            };

            cmb.SelectedIndexChanged += (s, e) =>
            {
                _vmNameSelecting = true;
            };

            cmb.DropDownClosed += (s, e) =>
            {
                _vmNameSelecting = false;
            };
        }

        /// <summary>
        /// 窗口关闭时调用，用于清理资源
        /// </summary>
        public void OnClosing()
        {
            // 窗口关闭时的清理逻辑
        }
    }
}