// ============================================================================
// CsvEditorTab.cs - CSV 部署列表编辑器 Tab
// ============================================================================
// 职责：在应用内嵌一个 CSV 表格编辑器，供用户填写批量部署参数
//
// 核心功能：
//   ✅ DataGridView 渲染 — 可编辑表格，列头为部署模板字段（VM名/IP/CPU/内存等）
//   ✅ 加载 / 保存 CSV — UTF-8 with BOM 编码读写
//   ✅ 模板下载 — 提供空模板供新用户参考
//   ✅ 数据校验 — 部署前基础字段检查
//
// 调用方：部署管理 Tab → 「编辑列表」按钮
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using VMwareOps.Core;
using VMwareOps.Utils;

namespace VMwareOps.UI
{
    public class CsvEditorTab
    {
        private readonly TabPage _parent;
        private readonly Form _root;
        private readonly Control _app;

        private DataGridView? _grid;
        private Button? _btnOpen;
        private Button? _btnSave;
        private Button? _btnAddRow;
        private Button? _btnDeleteRow;
        private Button? _btnReset;
        private Button? _btnSaveAs;
        private Label? _dirtyLabel;
        private Label? _statusLabel;

        private string _currentPath = "";
        private bool _dirty = false;
        private List<string> _columns = new List<string> { "Name", "IP", "Mask", "Gateway", "CPU", "Memory", "Template", "Vlan", "Host", "DiskCapacityGB", "Datastore" };
        private List<List<string>> _rows = new List<List<string>>();

        private static readonly HashSet<string> IgnoreColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Rule" };

        private static readonly Dictionary<string, int> ColumnWidthMap = new Dictionary<string, int>();

        public CsvEditorTab(TabPage parent, Form root, Control app)
        {
            _parent = parent;
            _root = root;
            _app = app;
            _currentPath = GetDefaultCsvPath();
            BuildUI();
            LoadCsv(_currentPath);
        }

        private string GetDefaultCsvPath()
        {
            return Path.Combine(CoreUtils.GetUserDataDir(), "deploy_vm_list.csv");
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
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = Padding.Empty,
                Margin = new Padding(6, 6, 6, 2)
            };

            _btnOpen = new Button { Text = "导入", FlatStyle = FlatStyle.Flat, Margin = new Padding(2), AutoSize = true, MinimumSize = new Size(60, 0) };
            _btnOpen.Click += BtnImport_Click;
            toolbar.Controls.Add(_btnOpen);

            _btnSave = new Button { Text = "保存", FlatStyle = FlatStyle.Flat, Margin = new Padding(2), AutoSize = true, MinimumSize = new Size(60, 0) };
            _btnSave.Click += BtnSave_Click;
            toolbar.Controls.Add(_btnSave);

            var btnEdit = new Button { Text = "编辑", FlatStyle = FlatStyle.Flat, Margin = new Padding(2), AutoSize = true, MinimumSize = new Size(60, 0) };
            btnEdit.Click += BtnEditExternal_Click;
            toolbar.Controls.Add(btnEdit);

            _btnAddRow = new Button { Text = "新增行", FlatStyle = FlatStyle.Flat, Margin = new Padding(2), AutoSize = true, MinimumSize = new Size(70, 0) };
            _btnAddRow.Click += BtnAddRow_Click;
            toolbar.Controls.Add(_btnAddRow);

            _btnDeleteRow = new Button { Text = "删除选中行", FlatStyle = FlatStyle.Flat, Margin = new Padding(2), AutoSize = true, MinimumSize = new Size(90, 0) };
            _btnDeleteRow.Click += BtnDeleteRow_Click;
            toolbar.Controls.Add(_btnDeleteRow);

            _btnReset = new Button { Text = "重置为模板", FlatStyle = FlatStyle.Flat, Margin = new Padding(2), AutoSize = true, MinimumSize = new Size(90, 0) };
            _btnReset.Click += BtnReset_Click;
            toolbar.Controls.Add(_btnReset);

            _btnSaveAs = new Button { Text = "另存为", FlatStyle = FlatStyle.Flat, Margin = new Padding(2), AutoSize = true, MinimumSize = new Size(80, 0) };
            _btnSaveAs.Click += BtnSaveAs_Click;
            toolbar.Controls.Add(_btnSaveAs);

            _dirtyLabel = new Label { Text = "", AutoSize = true, ForeColor = Color.FromArgb(239, 108, 0), Margin = new Padding(4) };
            toolbar.Controls.Add(_dirtyLabel);

            _statusLabel = new Label { Text = "提示：双击单元格直接编辑，Enter 确认，Esc 取消；Delete 删除选中行", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(85, 85, 85), AutoSize = true, Padding = new Padding(0, 4, 0, 4) };

            var gridContainer = new Panel { Dock = DockStyle.Fill };

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AllowUserToResizeColumns = true,
                ReadOnly = false,
                MultiSelect = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Microsoft YaHei", 9),
                BackgroundColor = SystemColors.Window,
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    Font = new Font("Microsoft YaHei", 9, FontStyle.Bold),
                    BackColor = Color.FromArgb(240, 240, 240)
                },
                EnableHeadersVisualStyles = false
            };
            _grid.CellValueChanged += Grid_CellValueChanged;
            _grid.KeyDown += Grid_KeyDown;
            _grid.ColumnWidthChanged += Grid_ColumnWidthChanged;
            _grid.SizeChanged += Grid_SizeChanged;
            gridContainer.Controls.Add(_grid);

            frame.Controls.Add(toolbar);
            frame.SetRow(toolbar, 0);
            frame.Controls.Add(_statusLabel);
            frame.SetRow(_statusLabel, 1);
            frame.Controls.Add(gridContainer);
            frame.SetRow(gridContainer, 2);

            _parent.Controls.Add(frame);

            UpdatePathLabel();
        }

        private void BtnImport_Click(object? sender, EventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "CSV 文件|*.csv", Title = "导入 CSV 文件" };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                ColumnWidthMap.Clear();
                LoadCsv(dlg.FileName);
                UpdatePathLabel();
                if (_statusLabel != null)
                {
                    _statusLabel.Text = $"📥 已导入：{Path.GetFileName(dlg.FileName)}（保存始终写入默认文件）";
                    _statusLabel.ForeColor = Color.FromArgb(21, 101, 192);
                }
            }
        }

        private void BtnSave_Click(object? sender, EventArgs e)
        {
            SaveCsv();
        }

        private void BtnEditExternal_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_currentPath) || !File.Exists(_currentPath))
            {
                ToastUtils.ShowToast(_root, "请先打开或保存一个 CSV 文件");
                return;
            }

            // 如果程序内有未保存的修改，先提示用户保存
            if (_dirty)
            {
                var result = MessageBox.Show(
                    "当前有未保存的修改，是否先保存到文件？\n\n选择「是」将先保存当前修改，再用外部编辑器打开。\n选择「否」将直接打开外部编辑器，但程序内的修改会丢失。",
                    "提示",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Cancel) return;
                if (result == DialogResult.Yes)
                {
                    SaveCsv();
                }
            }

            try
            {
                // 记录修改时间，用于检测文件是否被保存
                var originalMtime = File.GetLastWriteTime(_currentPath);
                Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _currentPath,
                    UseShellExecute = true
                });
                if (_statusLabel != null)
                {
                    _statusLabel.Text = " 已在外部编辑器中打开，等待保存后自动加载…";
                    _statusLabel.ForeColor = Color.FromArgb(21, 101, 192);
                }
                var watchThread = new System.Threading.Thread(() => WatchExternalEdit(originalMtime))
                {
                    IsBackground = true
                };
                watchThread.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开文件：\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void WatchExternalEdit(DateTime originalMtime)
        {
            try
            {
                while (true)
                {
                    System.Threading.Thread.Sleep(500);
                    if (!File.Exists(_currentPath)) continue;

                    var currentMtime = File.GetLastWriteTime(_currentPath);
                    if (currentMtime > originalMtime)
                    {
                        _root.Invoke(new Action(() =>
                        {
                            try
                            {
                                ReloadAfterExternalEdit();
                            }
                            catch
                            {
                            }
                        }));
                        break;
                    }
                }
            }
            catch
            {
            }
        }

        private void ReloadAfterExternalEdit()
        {
            LoadCsv(_currentPath, silent: true);
            _dirty = false;
            UpdatePathLabel();
            if (_statusLabel != null)
            {
                _statusLabel.Text = $"✅ 已从外部编辑器重新加载：{Path.GetFileName(_currentPath)}";
                _statusLabel.ForeColor = Color.FromArgb(46, 125, 50);
            }
        }

        private void BtnAddRow_Click(object? sender, EventArgs e)
        {
            if (_grid == null) return;

            var values = new object[_grid.Columns.Count];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = "";
            }
            int rowIndex = _grid.Rows.Add(values);
            _grid.ClearSelection();
            _grid.Rows[rowIndex].Selected = true;
            MarkDirty();
        }

        private void BtnDeleteRow_Click(object? sender, EventArgs e)
        {
            if (_grid == null) return;

            var selectedRows = _grid.SelectedRows.Cast<DataGridViewRow>().OrderByDescending(r => r.Index).ToList();
            foreach (var row in selectedRows)
            {
                _grid.Rows.Remove(row);
            }
            MarkDirty();
        }

        private void BtnReset_Click(object? sender, EventArgs e)
        {
            if (MessageBox.Show("确定要重置为模板吗？当前修改将丢失。", "确认", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                _grid?.Rows.Clear();
                _dirty = false;
                UpdatePathLabel();
            }
        }

        private void BtnSaveAs_Click(object? sender, EventArgs e)
        {
            if (_grid == null || _grid.Rows.Count == 0)
            {
                ToastUtils.ShowToast(_root, "当前没有可保存的数据");
                return;
            }

            string vcIp = SessionManager.Instance.CurrentVc ?? "unknown";
            string safeVc = vcIp.Replace(":", "-").Replace("/", "-");

            using var dlg = new SaveFileDialog
            {
                Filter = "CSV 文件|*.csv",
                Title = "另存为 CSV 文件",
                FileName = $"VmOps-{safeVc}.csv"
            };

            if (dlg.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    SaveCsvToPath(dlg.FileName);
                    _dirty = false;
                    UpdatePathLabel();
                    if (_statusLabel != null)
                    {
                        _statusLabel.Text = $"✅ 已另存为备份：{Path.GetFileName(dlg.FileName)}（当前编辑文件不变）";
                        _statusLabel.ForeColor = Color.FromArgb(46, 125, 50);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"保存失败：\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void Grid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
        {
            MarkDirty();
        }

        private void Grid_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                BtnDeleteRow_Click(null, EventArgs.Empty);
                e.Handled = true;
            }
        }

        private void Grid_ColumnWidthChanged(object? sender, DataGridViewColumnEventArgs e)
        {
            if (e.Column is DataGridViewColumn col && !string.IsNullOrEmpty(col.Name))
            {
                ColumnWidthMap[col.Name] = col.Width;
            }
        }

        private void Grid_SizeChanged(object? sender, EventArgs e)
        {
            ApplyAutoResizeWithMemory();
        }

        private void ApplyAutoResizeWithMemory()
        {
            if (_grid == null || _grid.Columns.Count == 0) return;

            _grid.SuspendLayout();

            var savedWidths = new Dictionary<string, int>();
            foreach (DataGridViewColumn col in _grid.Columns)
            {
                if (ColumnWidthMap.TryGetValue(col.Name, out var w))
                {
                    savedWidths[col.Name] = w;
                }
            }

            _grid.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells);

            foreach (var kvp in savedWidths)
            {
                if (_grid.Columns.Contains(kvp.Key))
                {
                    _grid.Columns[kvp.Key].Width = kvp.Value;
                }
            }

            _grid.ResumeLayout();
        }

        private void LoadCsv(string filePath, bool silent = false)
        {
            // 先读取到临时变量，成功后再替换，避免读取失败时数据丢失
            var newColumns = new List<string>(_columns);
            var newRows = new List<List<string>>();

            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                try
                {
                    // 先读取原始字节，再根据编码解码
                    byte[] fileBytes;
                    using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        fileBytes = new byte[fs.Length];
                        fs.Read(fileBytes, 0, (int)fs.Length);
                    }

                    // 检测 BOM
                    System.Text.Encoding detectedEncoding = DetectEncoding(fileBytes);

                    // 用检测到的编码将字节转为字符串
                    string fileContent = detectedEncoding.GetString(fileBytes);
                    // 去除 UTF-8 BOM（\uFEFF）污染，避免首列表头被污染导致字段名不匹配
                    if (fileContent.Length > 0 && fileContent[0] == '\uFEFF')
                        fileContent = fileContent.Substring(1);

                    // 按行解析 CSV
                    var lines = fileContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                                           .Where(l => !string.IsNullOrWhiteSpace(l))
                                           .ToList();

                    if (lines.Count > 0)
                    {
                        var rawColumns = new List<string>(lines[0].Split(','));
                        for (int i = 0; i < rawColumns.Count; i++)
                            rawColumns[i] = rawColumns[i].Trim();

                        var keepIdx = new List<int>();
                        for (int i = 0; i < rawColumns.Count; i++)
                        {
                            if (!IgnoreColumns.Contains(rawColumns[i]))
                                keepIdx.Add(i);
                        }
                        newColumns = keepIdx.Select(i => rawColumns[i]).ToList();

                        for (int i = 1; i < lines.Count; i++)
                        {
                            var fields = lines[i].Split(',');
                            var row = new List<string>();
                            foreach (int ci in keepIdx)
                            {
                                row.Add(ci < fields.Length ? fields[ci] : "");
                            }
                            newRows.Add(row);
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!silent)
                    {
                        MessageBox.Show($"加载 CSV 失败: {ex.Message}\n\n当前数据保持不变。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    return; // 读取失败，保持原有数据不变
                }
            }

            // 读取成功，替换数据
            _columns = newColumns;
            _rows = newRows;
            UpdateGrid();
            _dirty = false;
            UpdatePathLabel();
        }

        /// <summary>
        /// 检测文件编码：优先 BOM，无 BOM 时通过内容特征判断 UTF-8 还是 GBK
        /// </summary>
        private System.Text.Encoding DetectEncoding(byte[] bytes)
        {
            if (bytes.Length == 0)
                return System.Text.Encoding.UTF8;

            // 检测 UTF-8 BOM
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return System.Text.Encoding.UTF8;

            // 检测 UTF-16 LE BOM
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return System.Text.Encoding.Unicode;

            // 检测 UTF-16 BE BOM
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return System.Text.Encoding.BigEndianUnicode;

            // 无 BOM 时，通过字节特征判断 UTF-8 还是 GBK
            if (IsValidUtf8(bytes))
                return System.Text.Encoding.UTF8;

            // 不是有效 UTF-8，按系统默认编码（GBK）处理
            return System.Text.Encoding.Default;
        }

        /// <summary>
        /// 判断字节数组是否为有效的 UTF-8 编码
        /// </summary>
        private bool IsValidUtf8(byte[] bytes)
        {
            int i = 0;
            while (i < bytes.Length)
            {
                byte b = bytes[i];

                // ASCII (0x00-0x7F)
                if (b <= 0x7F)
                {
                    i++;
                    continue;
                }

                // 多字节 UTF-8 序列
                int continuationBytes = 0;
                if ((b & 0xE0) == 0xC0) continuationBytes = 1;      // 2-byte sequence
                else if ((b & 0xF0) == 0xE0) continuationBytes = 2; // 3-byte sequence
                else if ((b & 0xF8) == 0xF0) continuationBytes = 3; // 4-byte sequence
                else return false; // Invalid leading byte

                // 检查后续字节
                for (int j = 1; j <= continuationBytes; j++)
                {
                    if (i + j >= bytes.Length) return false;
                    if ((bytes[i + j] & 0xC0) != 0x80) return false;
                }

                i += 1 + continuationBytes;
            }
            return true;
        }

        private void SaveCsv()
        {
            if (string.IsNullOrEmpty(_currentPath))
            {
                var dlg = new SaveFileDialog { Filter = "CSV 文件|*.csv", Title = "保存 CSV 文件" };
                if (dlg.ShowDialog() != DialogResult.OK) return;
                _currentPath = dlg.FileName;
            }

            try
            {
                _grid?.EndEdit();

                // 保存为 UTF-8 with BOM，确保 Excel 能正确识别中文编码
                using var writer = new StreamWriter(_currentPath, false, new System.Text.UTF8Encoding(true));

                // 从 _grid 读取表头（支持用户调整列顺序）
                var headers = new List<string>();
                foreach (DataGridViewColumn col in _grid.Columns)
                    headers.Add(col.Name);
                writer.WriteLine(string.Join(",", headers));

                // 从 _grid 读取数据行
                foreach (DataGridViewRow gridRow in _grid.Rows)
                {
                    if (gridRow.IsNewRow) continue;
                    var values = new List<string>();
                    for (int i = 0; i < _grid.Columns.Count; i++)
                    {
                        values.Add(gridRow.Cells[i].Value?.ToString() ?? "");
                    }
                    writer.WriteLine(string.Join(",", values));
                }

                _dirty = false;
                UpdatePathLabel();
                ApplyAutoResizeWithMemory();
                ToastUtils.ShowToast(_root, "保存成功");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SaveCsvToPath(string path)
        {
            _grid?.EndEdit();

            // 保存为 UTF-8 with BOM，确保 Excel 能正确识别中文编码
            using var writer = new StreamWriter(path, false, new System.Text.UTF8Encoding(true));

            // 从 _grid 读取表头
            var headers = new List<string>();
            foreach (DataGridViewColumn col in _grid.Columns)
                headers.Add(col.Name);
            writer.WriteLine(string.Join(",", headers));

            // 从 _grid 读取数据行
            foreach (DataGridViewRow gridRow in _grid.Rows)
            {
                if (gridRow.IsNewRow) continue;
                var values = new List<string>();
                for (int i = 0; i < _grid.Columns.Count; i++)
                {
                    values.Add(gridRow.Cells[i].Value?.ToString() ?? "");
                }
                writer.WriteLine(string.Join(",", values));
            }
        }

        private void UpdateGrid()
        {
            if (_grid == null) return;

            _grid.SuspendLayout();
            _grid.Columns.Clear();
            _grid.Rows.Clear();

            foreach (var col in _columns)
            {
                _grid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    HeaderText = col,
                    Name = col,
                    MinimumWidth = 50,
                    SortMode = DataGridViewColumnSortMode.NotSortable
                });
            }

            foreach (var row in _rows)
            {
                if (row.Count == 0) continue;
                var cells = new List<object>();
                for (int i = 0; i < _columns.Count; i++)
                {
                    cells.Add(i < row.Count ? row[i] : "");
                }
                _grid.Rows.Add(cells.ToArray());
            }

            _grid.ResumeLayout();

            ApplyAutoResizeWithMemory();
        }

        private void UpdatePathLabel()
        {
            if (_dirtyLabel != null)
            {
                _dirtyLabel.Text = _dirty ? "  * 未保存" : "  ✓ 已保存";
            }
        }

        private void MarkDirty()
        {
            _dirty = true;
            UpdatePathLabel();
        }

        /// <summary>
        /// 获取当前打开的 CSV 文件路径（用于部署时读取）
        /// </summary>
        public string CurrentCsvPath => _currentPath;

        /// <summary>
        /// 从 _grid 读取当前所有数据行（用于保存和导出）
        /// </summary>
        private List<List<string>> GetGridData()
        {
            var result = new List<List<string>>();
            if (_grid == null) return result;

            foreach (DataGridViewRow gridRow in _grid.Rows)
            {
                if (gridRow.IsNewRow) continue;
                var row = new List<string>();
                for (int i = 0; i < _grid.Columns.Count; i++)
                {
                    row.Add(gridRow.Cells[i].Value?.ToString() ?? "");
                }
                result.Add(row);
            }
            return result;
        }

        /// <summary>
        /// 获取当前编辑的 CSV 数据（用于部署时直接读取内存数据）
        /// 以 DataGridView 为唯一数据源，强制提交当前编辑并过滤全空行
        /// </summary>
        public List<Dictionary<string, string>> GetCurrentData()
        {
            var result = new List<Dictionary<string, string>>();
            if (_grid == null) return result;

            _grid.EndEdit();

            foreach (DataGridViewRow gridRow in _grid.Rows)
            {
                if (gridRow.IsNewRow) continue;

                var dict = new Dictionary<string, string>();
                bool hasAnyValue = false;

                for (int i = 0; i < _grid.Columns.Count; i++)
                {
                    string key = _grid.Columns[i].Name;
                    string value = gridRow.Cells[i].Value?.ToString()?.Trim() ?? "";
                    dict[key] = value;
                    if (!string.IsNullOrEmpty(value)) hasAnyValue = true;
                }

                // 过滤全空行，同时过滤关键字段（Name/IP）为空的行
                // 避免 DataGridView 中不完整的行被当成有效数据，导致跳过 CSV 文件回退
                bool hasKeyFields = !string.IsNullOrEmpty(dict.GetValueOrDefault("Name"))
                                 && !string.IsNullOrEmpty(dict.GetValueOrDefault("IP"));
                if (hasAnyValue && hasKeyFields) result.Add(dict);
            }

            return result;
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