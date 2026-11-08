using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using VMwareOps.Core;
using VMwareOps.Utils;

namespace VMwareOps.UI
{
    public class ExtensionManagerDialog : Form
    {
        private ListView? _listView;
        private ContextMenuStrip? _ctxMenu;
        private ToolStripMenuItem? _miInstall;
        private ToolStripMenuItem? _miUpdate;
        private ToolStripSeparator? _sep1;
        private ToolStripMenuItem? _miUninstall;
        private ToolStripMenuItem? _miRefresh;
        private Label? _lblStatus;

        private string _selectedId = "";
        private string _selectedName = "";
        private bool _selectedInstalled;

        public ExtensionManagerDialog()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            BuildUI();
            LoadAll();
        }

        private void BuildUI()
        {
            Text = "扩展管理";
            Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Icon = CoreUtils.GetAppIcon();
            BackColor = Color.FromArgb(250, 250, 252);
            Size = new Size(640, 460);

            var titleLabel = new Label
            {
                Text = "扩展管理",
                Font = new Font("Microsoft YaHei", 12f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(14, 10)
            };
            Controls.Add(titleLabel);

            var pathLabel = new Label
            {
                Text = "扩展目录: " + Path.Combine(CoreUtils.GetUserDataDir(), "webserver"),
                Font = new Font("Microsoft YaHei", 8f),
                ForeColor = Color.Gray,
                AutoSize = true,
                Location = new Point(14, 34)
            };
            Controls.Add(pathLabel);

            _listView = new ListView
            {
                Location = new Point(12, 58),
                Size = new Size(600, 350),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                CheckBoxes = true,
                Font = new Font("Microsoft YaHei", 9f),
                BackColor = Color.White,
                BorderStyle = BorderStyle.None,
                MultiSelect = false
            };
            _listView.Columns.Add("扩展名称", 180);
            _listView.Columns.Add("ID", 120);
            _listView.Columns.Add("版本", 70);
            _listView.Columns.Add("最新", 70);
            _listView.Columns.Add("状态", 90);
            _listView.Columns.Add("安装时间", 120);
            _listView.SelectedIndexChanged += ListView_SelectedIndexChanged;
            _listView.ItemCheck += ListView_ItemCheck;
            Controls.Add(_listView);

            BuildContextMenu();
            _listView.ContextMenuStrip = _ctxMenu;

            _lblStatus = new Label
            {
                Text = "正在从远程仓库加载扩展列表...",
                Font = new Font("Microsoft YaHei", 8f),
                ForeColor = Color.FromArgb(120, 120, 120),
                AutoSize = true,
                Location = new Point(12, 416)
            };
            Controls.Add(_lblStatus);
        }

        private void BuildContextMenu()
        {
            _ctxMenu = new ContextMenuStrip
            {
                Font = new Font("Microsoft YaHei", 9f)
            };

            _miInstall = new ToolStripMenuItem("安装扩展", null, MiInstall_Click);
            _miUpdate = new ToolStripMenuItem("检查更新", null, MiUpdate_Click);
            _miRefresh = new ToolStripMenuItem("刷新列表", null, MiRefresh_Click);
            _sep1 = new ToolStripSeparator();
            _miUninstall = new ToolStripMenuItem("卸载扩展", null, MiUninstall_Click)
            {
                ForeColor = Color.FromArgb(211, 47, 47)
            };

            _ctxMenu.Opening += (s, e) =>
            {
                _miInstall!.Visible = !_selectedInstalled;
                _miUpdate!.Visible = _selectedInstalled;
                _miUninstall!.Visible = _selectedInstalled;
                _sep1!.Visible = _selectedInstalled;
            };

            _ctxMenu.Items.Add(_miInstall);
            _ctxMenu.Items.Add(_miUpdate);
            _ctxMenu.Items.Add(_sep1);
            _ctxMenu.Items.Add(_miUninstall);
            _ctxMenu.Items.Add(new ToolStripSeparator());
            _ctxMenu.Items.Add(_miRefresh);
        }

        private async void LoadAll()
        {
            try
            {
                var market = await ExtensionManager.Instance.FetchMarketAsync();
                var installed = ExtensionManager.Instance.GetInstalled();
                RenderList(market, installed);
            }
            catch (Exception ex)
            {
                _lblStatus!.Text = "加载远程列表失败: " + ex.Message + "（仅显示本地）";
                RenderList(new List<RemoteExtension>(), ExtensionManager.Instance.GetInstalled());
            }
        }

        private void RenderList(List<RemoteExtension> market, List<InstalledExtension> installed)
        {
            if (_listView == null) return;
            _listView.Items.Clear();

            var installedById = installed.ToDictionary(i => i.Id);
            var marketById = market.ToDictionary(m => m.Id);
            var allIds = new HashSet<string>(installedById.Keys.Concat(marketById.Keys));

            if (allIds.Count == 0)
            {
                var empty = new ListViewItem("（远程仓库和本地都没有扩展）");
                empty.SubItems.Add("");
                empty.SubItems.Add("");
                empty.SubItems.Add("");
                empty.SubItems.Add("");
                empty.SubItems.Add("");
                empty.Tag = "";
                _listView.Items.Add(empty);
                _lblStatus!.Text = "远程仓库为空或无法连接";
                return;
            }

            int enabledCount = 0;
            int updateCount = 0;

            foreach (var id in allIds)
            {
                InstalledExtension? ext = installedById.ContainsKey(id) ? installedById[id] : null;
                RemoteExtension? remote = marketById.ContainsKey(id) ? marketById[id] : null;

                var name = ext?.Name ?? remote?.Name ?? id;
                var installedVersion = ext?.Version ?? "";
                var latestVersion = remote?.Version ?? installedVersion;
                var hasUpdate = ext != null && remote != null && remote.Version != ext.Version;
                var isInstalled = ext != null;

                var item = new ListViewItem(name)
                {
                    Tag = id,
                    Checked = ext?.Enabled ?? false
                };

                item.SubItems.Add(id);
                item.SubItems.Add(isInstalled ? installedVersion : "—");

                if (hasUpdate)
                {
                    item.SubItems.Add(latestVersion + " ↑");
                    item.SubItems[3].ForeColor = Color.FromArgb(245, 124, 0);
                    updateCount++;
                }
                else
                {
                    item.SubItems.Add(latestVersion);
                }

                if (!isInstalled)
                {
                    item.SubItems.Add("○ 未安装");
                    item.ForeColor = Color.FromArgb(150, 150, 150);
                    item.UseItemStyleForSubItems = true;
                }
                else
                {
                    item.SubItems.Add(ext!.Enabled ? "✓ 已启用" : "○ 已禁用");
                    if (ext.Enabled) enabledCount++;
                }

                item.SubItems.Add(isInstalled ? ext!.InstalledAt.ToString("yyyy-MM-dd HH:mm") : "—");

                if (!isInstalled) item.ForeColor = Color.FromArgb(150, 150, 150);

                _listView.Items.Add(item);
            }

            _lblStatus!.Text = "共 " + allIds.Count + " 个扩展 | 已安装 " + installed.Count + " 个 | "
                + enabledCount + " 个已启用" + (updateCount > 0 ? " | " + updateCount + " 个可更新 ↑" : "");
        }

        private void ListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_listView == null || _listView.SelectedItems.Count == 0)
            {
                _selectedId = "";
                _selectedName = "";
                _selectedInstalled = false;
                return;
            }
            var item = _listView.SelectedItems[0];
            _selectedId = item.Tag as string ?? "";
            _selectedName = item.Text;
            _selectedInstalled = ExtensionManager.Instance.GetExtension(_selectedId) != null;
        }

        private void ListView_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_listView == null) return;
            var item = _listView.Items[e.Index];
            var id = item.Tag as string;
            if (string.IsNullOrEmpty(id)) return;

            var installed = ExtensionManager.Instance.GetExtension(id);
            if (installed == null)
            {
                e.NewValue = CheckState.Unchecked;
                return;
            }

            var newEnabled = e.NewValue == CheckState.Checked;
            ExtensionManager.Instance.SetEnabled(id, newEnabled);
            item.SubItems[4].Text = newEnabled ? "✓ 已启用" : "○ 已禁用";
            _lblStatus!.Text = "扩展 " + id + (newEnabled ? " 已启用" : " 已禁用");
        }

        private async void MiRefresh_Click(object sender, EventArgs e)
        {
            _lblStatus!.Text = "正在从远程仓库拉取扩展列表...";
            await ExtensionManager.Instance.FetchMarketAsync(true);
            LoadAll();
        }

        private async void MiInstall_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedId)) return;

            _lblStatus!.Text = "正在从远程仓库获取扩展 " + _selectedName + "...";

            try
            {
                var market = await ExtensionManager.Instance.FetchMarketAsync(true);
                var remote = market.FirstOrDefault(m => m.Id == _selectedId);

                if (remote == null)
                {
                    MessageBox.Show(this, "远程仓库未找到该扩展的下载地址", "安装失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    _lblStatus.Text = "未找到远程扩展";
                    return;
                }

                _lblStatus.Text = "正在下载安装 " + remote.Name + " v" + remote.Version + "...";
                _miInstall!.Enabled = false;

                var result = await ExtensionManager.Instance.InstallFromMarketAsync(remote);
                if (result.Success)
                {
                    _lblStatus.Text = "扩展 " + result.Manifest?.Name + " v" + result.Manifest?.Version + " 安装成功";
                }
                else
                {
                    MessageBox.Show(this, "安装失败: " + result.Error, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    _lblStatus.Text = "安装失败";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "安装失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _lblStatus.Text = "安装失败";
            }
            finally
            {
                _miInstall.Enabled = true;
                LoadAll();
            }
        }

        private async void MiUpdate_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedId)) return;
            var ext = ExtensionManager.Instance.GetExtension(_selectedId);
            if (ext == null) return;

            _lblStatus!.Text = "正在检查 " + _selectedName + " 的更新...";
            _miUpdate!.Enabled = false;

            try
            {
                var market = await ExtensionManager.Instance.FetchMarketAsync(true);
                var remote = market.FirstOrDefault(m => m.Id == _selectedId);

                if (remote == null)
                {
                    MessageBox.Show(this, "远程仓库未找到该扩展", "检查更新",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    _lblStatus.Text = "未找到远程扩展";
                    return;
                }

                if (remote.Version == ext.Version)
                {
                    _lblStatus.Text = _selectedName + " 已是最新 v" + ext.Version;
                    return;
                }

                var proceed = MessageBox.Show(this,
                    "发现新版本: " + remote.Version + "\n当前版本: " + ext.Version + "\n\n是否更新？",
                    "检查更新", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (proceed != DialogResult.Yes) return;

                _lblStatus.Text = "正在更新到 v" + remote.Version + "...";
                var result = await ExtensionManager.Instance.InstallFromMarketAsync(remote);

                if (result.Success)
                    _lblStatus.Text = "已更新到 v" + result.Manifest?.Version;
                else
                {
                    MessageBox.Show(this, "更新失败: " + result.Error, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    _lblStatus.Text = "更新失败";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "检查更新失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _lblStatus.Text = "检查更新失败";
            }
            finally
            {
                _miUpdate.Enabled = true;
                LoadAll();
            }
        }

        private void MiUninstall_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedId)) return;

            var result = MessageBox.Show(
                this,
                "确定要卸载扩展「" + _selectedName + "」吗？\n此操作会删除扩展文件，不可恢复。",
                "确认卸载",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result != DialogResult.Yes) return;

            if (ExtensionManager.Instance.Uninstall(_selectedId))
            {
                LoadAll();
                _lblStatus!.Text = "扩展已卸载";
            }
        }
    }
}