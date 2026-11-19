// ============================================================================
// [UTF-8 无 BOM] SettingsDialog.cs - 全局设置对话框
// ============================================================================

//
// UI 编码规范见 MainForm.cs 头部注释
// ============================================================================
// 布局：上下两大区，Footer + 底部按钮
//
//   Form (ClientSize 540×560)
//     └─ root (TableLayoutPanel, Dock=Fill, Padding 18/14/18/12)
//          row0: Percent=40  → GroupBox "程序设置" (Dock=Fill)
//               └─ 内部 TableLayoutPanel (Dock=Fill)
//                    3 行 Percent 100 等分
//                    row0: FlowLayoutPanel(水平) [RadioButton] [RadioButton]
//                    row1: CheckBox
//                    row2: CheckBox
//          row1: Percent=60  → GroupBox "路径设置" (Dock=Fill)
//               └─ 内部 TableLayoutPanel (Dock=Fill)
//                    5 行 Percent 100 等分
//                    3 列: Abs(100) + Percent(100) + Abs(60)
//                    每行: Label(Dock=Fill, 垂直居中) | TextBox(Anchor L|R, 垂直居中) | Button(AutoSize, 垂直居中)
//          row2: AutoSize   → Footer Label
//          row3: Absolute=44 → FlowLayoutPanel 水平居中放 保存/取消（与工具内其他对话框一致）
//
// 所有容器都有确定宽高 → Percent 正确计算 → 无循环依赖
// ============================================================================
using System;
using System.Drawing;
using System.Windows.Forms;
using VmOps.Utils;

namespace VmOps.UI
{
    public class SettingsDialog : Form
    {
        private RadioButton? _rbSingleInstance;
        private RadioButton? _rbMultiInstance;
        private CheckBox? _chkMinimizeOnStart;
        private CheckBox? _chkMinimizeOnClose;

        private TextBox? _txtOvfTool;
        private TextBox? _txtVmrc;
        private TextBox? _txtRdp;
        private TextBox? _txtBrowser;
        private TextBox? _txtMobaXterm;

        private readonly AppSettings _settings;

        public SettingsDialog()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            _settings = AppSettings.Instance;
            BuildUI();
            LoadValues();
        }

        private void BuildUI()
        {
            Text = "全局设置";
            Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Icon = CoreUtils.GetAppIcon();
            BackColor = ColorTranslator.FromHtml("#F5F7FA");
            ClientSize = new Size(DpiUtils.Px(540), DpiUtils.Px(440));

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(DpiUtils.Px(18), DpiUtils.Px(14), DpiUtils.Px(18), DpiUtils.Px(12)),
                RowCount = 4,
                ColumnCount = 1
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiUtils.Px(44)));
            Controls.Add(root);

            root.Controls.Add(BuildGeneralSection(), 0, 0);
            root.Controls.Add(BuildToolPathsSection(), 0, 1);
            root.Controls.Add(BuildFooter(), 0, 2);
            root.Controls.Add(BuildButtonsPanel(), 0, 3);
        }

        private GroupBox BuildGeneralSection()
        {
            var group = new GroupBox
            {
                Text = "  程序设置  ",
                Dock = DockStyle.Fill,
                Padding = new Padding(DpiUtils.Px(12), DpiUtils.Px(22), DpiUtils.Px(12), DpiUtils.Px(6))
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1
            };
            for (int i = 0; i < 3; i++)
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var radioRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0)
            };

            _rbSingleInstance = new RadioButton
            {
                Text = "单实例模式",
                AutoSize = true,
                Margin = new Padding(0, DpiUtils.Px(4), DpiUtils.Px(20), 0),
                Checked = true
            };

            _rbMultiInstance = new RadioButton
            {
                Text = "允许多开",
                AutoSize = true,
                Margin = new Padding(0, DpiUtils.Px(4), 0, 0)
            };

            radioRow.Controls.Add(_rbSingleInstance);
            radioRow.Controls.Add(_rbMultiInstance);
            layout.Controls.Add(radioRow, 0, 0);

            _chkMinimizeOnStart = new CheckBox
            {
                Text = "启动时直接最小化到系统托盘",
                Dock = DockStyle.Fill
            };
            layout.Controls.Add(_chkMinimizeOnStart, 0, 1);

            _chkMinimizeOnClose = new CheckBox
            {
                Text = "关闭窗口时最小化到系统托盘",
                Dock = DockStyle.Fill
            };
            layout.Controls.Add(_chkMinimizeOnClose, 0, 2);

            group.Controls.Add(layout);
            return group;
        }

        private GroupBox BuildToolPathsSection()
        {
            var group = new GroupBox
            {
                Text = "  路径设置  ",
                Dock = DockStyle.Fill,
                Padding = new Padding(DpiUtils.Px(12), DpiUtils.Px(22), DpiUtils.Px(12), DpiUtils.Px(6))
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 5,
                ColumnCount = 3
            };

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, DpiUtils.Px(100)));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, DpiUtils.Px(60)));

            for (int i = 0; i < 5; i++)
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            string[] labels = { "OVF Tool", "VMRC Client", "RDP 客户端", "浏览器", "MobaXterm" };
            var textboxes = new TextBox?[5];

            for (int i = 0; i < 5; i++)
            {
                var lbl = new Label
                {
                    Text = labels[i],
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Margin = new Padding(0, 0, DpiUtils.Px(8), 0)
                };
                layout.Controls.Add(lbl, 0, i);

                var txt = new TextBox
                {
                    Anchor = AnchorStyles.Left | AnchorStyles.Right,
                    Margin = new Padding(0, 0, DpiUtils.Px(6), 0)
                };
                textboxes[i] = txt;
                layout.Controls.Add(txt, 1, i);

                var btnBrowse = new Button
                {
                    Text = "浏览",
                    AutoSize = true,
                    Anchor = AnchorStyles.Left | AnchorStyles.Right
                };
                int browseIdx = i;
                btnBrowse.Click += (s, e) => BrowseExe(textboxes[browseIdx]!);
                layout.Controls.Add(btnBrowse, 2, i);
            }

            _txtOvfTool = textboxes[0];
            _txtVmrc = textboxes[1];
            _txtRdp = textboxes[2];
            _txtBrowser = textboxes[3];
            _txtMobaXterm = textboxes[4];

            group.Controls.Add(layout);
            return group;
        }

        private Label BuildFooter()
        {
            return new Label
            {
                Text = $"配置文件：{AppSettings.GetSettingsPath()}",
                ForeColor = ColorTranslator.FromHtml("#999999"),
                AutoSize = true,
                Font = new Font("Microsoft YaHei", 8f, FontStyle.Regular),
                Margin = new Padding(0, DpiUtils.Px(6), 0, 0)
            };
        }

        private FlowLayoutPanel BuildButtonsPanel()
        {
            var btnSave = new Button
            {
                Text = "保存",
                Size = new Size(DpiUtils.Px(80), DpiUtils.Px(28)),
                Font = new Font("Microsoft YaHei", 9f),
                DialogResult = DialogResult.OK
            };
            btnSave.Click += (s, e) => SaveSettings();

            var btnCancel = new Button
            {
                Text = "取消",
                Size = new Size(DpiUtils.Px(80), DpiUtils.Px(28)),
                Font = new Font("Microsoft YaHei", 9f),
                DialogResult = DialogResult.Cancel
            };

            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, DpiUtils.Px(6), 0, DpiUtils.Px(6)),
                BackColor = Color.FromArgb(240, 240, 240),
                Margin = new Padding(0)
            };
            panel.Controls.Add(btnSave);
            panel.Controls.Add(btnCancel);
            CenterFlowPanel(panel);

            AcceptButton = btnSave;
            CancelButton = btnCancel;
            return panel;
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
                panel.Padding = new Padding(half, DpiUtils.Px(6), half, DpiUtils.Px(6));
            };
        }

        private void BrowseExe(TextBox txt)
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "可执行文件|*.exe|所有文件|*.*",
                Title = "选择可执行文件"
            };

            if (!string.IsNullOrEmpty(txt.Text))
                dlg.FileName = txt.Text;

            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                txt.Text = dlg.FileName;
            }
        }

        private void LoadValues()
        {
            if (_rbSingleInstance != null && _rbMultiInstance != null)
            {
                _rbSingleInstance.Checked = _settings.SingleInstanceMode;
                _rbMultiInstance.Checked = !_settings.SingleInstanceMode;
            }

            if (_chkMinimizeOnStart != null)
                _chkMinimizeOnStart.Checked = _settings.MinimizeToTrayOnStart;

            if (_chkMinimizeOnClose != null)
                _chkMinimizeOnClose.Checked = _settings.MinimizeToTrayOnClose;

            if (_txtOvfTool != null) _txtOvfTool.Text = _settings.GetOvfToolPath() ?? "";
            if (_txtVmrc != null) _txtVmrc.Text = _settings.GetVmrcPath() ?? "";
            if (_txtRdp != null) _txtRdp.Text = _settings.GetRdpPath() ?? "";
            if (_txtBrowser != null) _txtBrowser.Text = _settings.GetBrowserPath() ?? "";
            if (_txtMobaXterm != null) _txtMobaXterm.Text = _settings.GetMobaXtermPath() ?? "";
        }

        private void SaveSettings()
        {
            if (_rbSingleInstance != null)
                _settings.SingleInstanceMode = _rbSingleInstance.Checked;

            if (_chkMinimizeOnStart != null)
                _settings.MinimizeToTrayOnStart = _chkMinimizeOnStart.Checked;

            if (_chkMinimizeOnClose != null)
                _settings.MinimizeToTrayOnClose = _chkMinimizeOnClose.Checked;

            _settings.OvfToolPath = Normalize(_txtOvfTool?.Text);
            _settings.VmrcPath = Normalize(_txtVmrc?.Text);
            _settings.RdpPath = Normalize(_txtRdp?.Text);
            _settings.BrowserPath = Normalize(_txtBrowser?.Text);
            _settings.MobaXtermPath = Normalize(_txtMobaXterm?.Text);

            _settings.Save();
        }

        private static string? Normalize(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return s!.Trim();
        }
    }
}