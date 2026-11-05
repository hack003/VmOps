// ============================================================================
// UpdateDialog.cs - 版本更新弹窗（含自动下载）
// ============================================================================
// 内嵌 WebBrowser 渲染 Markdown 更新日志
// package_url 存在时显示「立即下载并更新」按钮 + 进度条
// 下载完成 → 启动 update_helper.exe → 主程序退出 → helper 替换并重启
// ============================================================================
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using VMwareOps.Utils;

namespace VMwareOps.UI
{
    public class UpdateDialog : Form
    {
        private readonly UpdateUtils.UpdateInfo _info;
        private readonly string _currentVersion;

        private Button? _btnAutoUpdate;
        private Button? _btnClose;
        private ProgressBar? _progressBar;
        private Label? _lblProgress;
        private Panel? _panelProgress;

        private bool _isDownloading;

        public UpdateDialog(UpdateUtils.UpdateInfo info, string currentVersion)
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            _info = info;
            _currentVersion = currentVersion;
            BuildUI();
        }

        private void BuildUI()
        {
            Text = "版本更新";
            Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Icon = CoreUtils.GetAppIcon();
            BackColor = Color.FromArgb(245, 245, 245);

            Size = new Size(600, 540);
            MinimumSize = new Size(600, 540);

            var main = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(DpiUtils.Px(16)),
                RowCount = 3,
                ColumnCount = 1
            };
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiUtils.Px(44)));
            main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiUtils.Px(44)));

            Controls.Add(main);

            // --- 顶部标题区：图标 + 标题 + 版本号 一行 ---
            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 1,
                ColumnCount = 3
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, DpiUtils.Px(36)));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var pic = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = CoreUtils.GetAppIcon()?.ToBitmap(),
                Margin = new Padding(0, 0, DpiUtils.Px(6), 0)
            };
            header.Controls.Add(pic, 0, 0);

            var lblTitle = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei", 12f, FontStyle.Bold),
                ForeColor = ColorTranslator.FromHtml("#1a73e8"),
                Text = "发现新版本"
            };
            header.Controls.Add(lblTitle, 1, 0);

            var lblVersion = new Label
            {
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular),
                ForeColor = ColorTranslator.FromHtml("#666666"),
                Text = $"当前 {_currentVersion}  →  新版 {_info.Version}",
                Margin = new Padding(8, 0, 0, 0)
            };
            header.Controls.Add(lblVersion, 2, 0);
            main.Controls.Add(header, 0, 0);

            // --- 更新日志（WebBrowser） ---
            var browser = new WebBrowser
            {
                Dock = DockStyle.Fill,
                ScriptErrorsSuppressed = true,
                ScrollBarsEnabled = true,
                Margin = new Padding(0, 8, 0, 0)
            };
            browser.DocumentText = BuildHtml(MarkdownToHtml(_info.Changelog));
            main.Controls.Add(browser, 0, 1);

            // --- 进度条面板（嵌入 WebBrowser 底部，下载时才显示） ---
            _panelProgress = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = DpiUtils.Px(48),
                Visible = false,
                BackColor = Color.FromArgb(245, 245, 245)
            };

            _progressBar = new ProgressBar
            {
                Location = new Point(0, DpiUtils.Px(4)),
                Size = new Size(DpiUtils.Px(560), DpiUtils.Px(22)),
                Style = ProgressBarStyle.Continuous,
                Margin = Padding.Empty
            };

            _lblProgress = new Label
            {
                Location = new Point(0, DpiUtils.Px(26)),
                Size = new Size(DpiUtils.Px(560), DpiUtils.Px(18)),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei", 8.5f)
            };

            _panelProgress.Controls.Add(_progressBar);
            _panelProgress.Controls.Add(_lblProgress);
            browser.Controls.Add(_panelProgress);

            // --- 底部按钮区 ---
            var btnBar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 1,
                ColumnCount = 3,
                Margin = new Padding(0, 0, 0, 0)
            };
            btnBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            btnBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, DpiUtils.Px(110)));
            btnBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, DpiUtils.Px(110)));

            _btnAutoUpdate = new Button
            {
                Text = "确认更新",
                Size = new Size(DpiUtils.Px(110), DpiUtils.Px(32)),
                FlatStyle = FlatStyle.Standard,
                BackColor = ColorTranslator.FromHtml("#1a73e8"),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Margin = new Padding(0, 0, DpiUtils.Px(8), 0),
                Enabled = !string.IsNullOrEmpty(_info.PackageUrl)
            };
            _btnAutoUpdate.Click += BtnAutoUpdate_Click;
            if (string.IsNullOrEmpty(_info.PackageUrl))
                _btnAutoUpdate.Text = "自动更新暂不可用";

            _btnClose = new Button
            {
                Text = "取消更新",
                Size = new Size(DpiUtils.Px(110), DpiUtils.Px(32)),
                FlatStyle = FlatStyle.Standard,
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                DialogResult = DialogResult.OK,
                Margin = Padding.Empty
            };

            btnBar.Controls.Add(new Panel { Dock = DockStyle.Fill }, 0, 0);
            btnBar.Controls.Add(_btnAutoUpdate, 1, 0);
            btnBar.Controls.Add(_btnClose, 2, 0);
            main.Controls.Add(btnBar, 0, 2);
        }

        private async void BtnAutoUpdate_Click(object? sender, EventArgs e)
        {
            if (_isDownloading || _btnAutoUpdate == null || _panelProgress == null) return;

            string newExePath = Path.Combine(Path.GetTempPath(), $"vmops_update_{Guid.NewGuid():N}.exe");

            _isDownloading = true;
            _btnAutoUpdate.Enabled = false;
            _btnAutoUpdate.Text = "下载中...";
            _btnClose.Enabled = false;
            _panelProgress.Visible = true;
            _progressBar!.Value = 0;
            _lblProgress!.Text = "准备下载...";

            var progress = new Progress<double>(pct =>
            {
                if (_progressBar != null && _lblProgress != null)
                {
                    if (pct >= 0)
                    {
                        _progressBar.Value = (int)pct;
                        _lblProgress.Text = $"下载中... {(int)pct}%";
                    }
                    else
                    {
                        _lblProgress.Text = "下载中...";
                    }
                }
            });

            bool ok = await UpdateUtils.DownloadPackageAsync(_info, newExePath, progress);

            if (!ok || !File.Exists(newExePath))
            {
                MessageBox.Show("下载失败，请检查网络连接或手动前往下载页下载。",
                    "更新失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                try { if (File.Exists(newExePath)) File.Delete(newExePath); } catch { }
                _isDownloading = false;
                _btnAutoUpdate.Enabled = true;
                _btnAutoUpdate.Text = "确认更新";
                _btnClose.Enabled = true;
                _panelProgress.Visible = false;
                return;
            }

            _progressBar.Value = 100;
            _lblProgress.Text = "下载完成，正在启动更新程序...";

            bool launched = UpdateUtils.RunUpdateHelper(newExePath);

            if (!launched)
            {
                MessageBox.Show(
                    "下载完成但无法启动更新程序。\n" +
                    "可能是 update_helper.exe 缺失或被杀毒软件拦截。\n\n" +
                    "请手动前往下载页下载新版本 exe，覆盖旧 exe 后重启程序。",
                    "自动更新失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _isDownloading = false;
                _btnAutoUpdate.Enabled = true;
                _btnAutoUpdate.Text = "确认更新";
                _btnClose.Enabled = true;
                _panelProgress.Visible = false;
                return;
            }

            _btnAutoUpdate.Enabled = false;
            _btnClose.Enabled = false;
            _lblProgress.Text = "更新程序已启动，即将退出并自动重启...";
            DialogResult = DialogResult.OK;
            Close();

            await Task.Delay(200);
            Environment.Exit(0);
        }

        private static string MarkdownToHtml(string md)
        {
            if (string.IsNullOrWhiteSpace(md))
                return "<p style='color:#888'>无更新说明</p>";

            var lines = md.Replace("\r\n", "\n").Split('\n');
            var html = new System.Text.StringBuilder();
            bool inList = false;

            foreach (var raw in lines)
            {
                var line = raw.TrimEnd();

                if (line.StartsWith("### "))
                {
                    if (inList) { html.Append("</ul>"); inList = false; }
                    html.Append("<h4 style='color:#1a73e8;margin:14px 0 8px 0;font-size:14px;font-weight:bold;border-left:3px solid #1a73e8;padding-left:8px;'>");
                    html.Append(System.Security.SecurityElement.Escape(line.Substring(4).Trim()));
                    html.Append("</h4>");
                    continue;
                }

                if (line.StartsWith("## "))
                {
                    if (inList) { html.Append("</ul>"); inList = false; }
                    html.Append("<h3 style='color:#1a73e8;margin:16px 0 8px 0;font-size:16px;font-weight:bold;'>");
                    html.Append(System.Security.SecurityElement.Escape(line.Substring(3).Trim()));
                    html.Append("</h3>");
                    continue;
                }

                if (line.StartsWith("- ") || line.StartsWith("* "))
                {
                    if (!inList) { html.Append("<ul style='margin:6px 0 6px 18px;padding:0;'>"); inList = true; }
                    html.Append("<li style='margin:3px 0;line-height:1.6;'>");
                    html.Append(InlineFormat(System.Security.SecurityElement.Escape(line.Substring(2).Trim())));
                    html.Append("</li>");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    if (inList) { html.Append("</ul>"); inList = false; }
                    html.Append("<div style='height:6px'></div>");
                    continue;
                }

                if (inList) { html.Append("</ul>"); inList = false; }
                html.Append("<p style='margin:6px 0;line-height:1.7;'>");
                html.Append(InlineFormat(System.Security.SecurityElement.Escape(line)));
                html.Append("</p>");
            }

            if (inList) html.Append("</ul>");
            return html.ToString();
        }

        private static string InlineFormat(string text)
        {
            text = System.Text.RegularExpressions.Regex.Replace(
                text, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
            text = System.Text.RegularExpressions.Regex.Replace(
                text, @"`([^`]+)`",
                "<span style='background:#f0f0f0;color:#d63384;padding:1px 6px;border-radius:3px;font-family:Consolas,monospace;font-size:13px'>$1</span>");
            return text;
        }

        private static string BuildHtml(string bodyHtml)
        {
            return
                "<!DOCTYPE html><html><head><meta charset='UTF-8'>" +
                "<style>" +
                "body{font-family:'Microsoft YaHei','Segoe UI',sans-serif;background:#fff;color:#333;padding:12px 16px;margin:0;font-size:14px;line-height:1.8;}" +
                "</style></head><body>" + bodyHtml + "</body></html>";
        }
    }
}