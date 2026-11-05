// ============================================================================
// ResourceQueryTab.Query.cs - 查询引擎模块
// ============================================================================
// 职责范围：
//   ✅ 数据查询逻辑（DoQuery / QueryWorker）
//   ✅ 查询结果渲染（ApplyResult / RebuildListView）
//   ✅ 搜索与过滤（ApplySearch / ApplySearchWithoutRebuild）
//   ✅ 列表排序（ListView_ColumnClick）
//   ✅ 状态更新（UpdateStatus / LogMessage）
//   ✅ 缓存管理（SyncToUnifiedCache）
//
// 开发规范：
//   查询相关优化 → 仅修改此文件
//   新增数据展示方式 → 在此文件添加方法
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using VMwareOps.Core;
using VMwareOps.Utils;

namespace VMwareOps.UI
{
    public partial class ResourceQueryTab
    {
        private void DoQuery()
        {
            string cacheKey = CurrentCacheKey();
            
            string command = QueryBuilder.GetJsonQueryScript(cacheKey);
            
            if (string.IsNullOrEmpty(command))
            {
                UpdateStatus($"⚠ {_title}：查询脚本未定义", ColorTranslator.FromHtml("#C62828"));
                return;
            }

            string subtypeLabel = GetSubtypeDisplayName(cacheKey);
            string logTitle = string.IsNullOrEmpty(subtypeLabel) ? _title : $"{_title} - {subtypeLabel}";

            _queryStartTime = DateTime.Now;
            _columnsAutoResized = false;

            _refreshBtn.Enabled = false;
            UpdateStatus($"⟳ 正在查询 {logTitle}…", ColorTranslator.FromHtml("#1565C0"));
            LogMessage("connecting", $"⟳ 开始查询 {logTitle}…");

            Task.Run(() => QueryWorker(command));
        }

        private async void QueryWorker(string command)
        {
            try
            {
                var psDataList = await SessionManager.Instance.ExecuteQueryAsync(
                    command, 
                    (msg, level) => LogMessage(level ?? "info", msg ?? ""), 
                    _timeoutSec
                );

                if (psDataList != null && psDataList.Count > 0)
                {
                    var columns = psDataList[0].Keys.ToList();
                    foreach (var d in psDataList)
                    {
                        foreach (var k in d.Keys)
                        {
                            if (!columns.Contains(k, StringComparer.OrdinalIgnoreCase))
                                columns.Add(k);
                        }
                    }
                    var rows = psDataList.Select(d => columns.Select(c =>
                        d.TryGetValue(c, out var v) ? (v?.ToString() ?? "") : "").ToList()).ToList();

                    int idColIdx = columns.FindIndex(c => c.Equals("Id", StringComparison.OrdinalIgnoreCase));
                    if (idColIdx > 0)
                    {
                        string idCol = columns[idColIdx];
                        columns.RemoveAt(idColIdx);
                        columns.Insert(0, idCol);
                        foreach (var row in rows)
                        {
                            if (idColIdx < row.Count)
                            {
                                var idVal = row[idColIdx];
                                row.RemoveAt(idColIdx);
                                row.Insert(0, idVal);
                            }
                        }
                    }

                    QueueRender(columns, rows);
                    SyncToUnifiedCache(columns, rows);
                    AutoSaveToFileCache(columns, rows);

                    int elapsedMs = (int)(DateTime.Now - _queryStartTime).TotalMilliseconds;
                    QueueStatus($"✓ {_title}：共 {rows.Count} 条 ({elapsedMs}ms)", ColorTranslator.FromHtml("#2E7D32"));
                    LogMessage("success", $"✓ 查询完成: {rows.Count} 条记录, 耗时 {elapsedMs}ms");
                }
                else
                {
                    QueueRender(new List<string>(), new List<List<string>>());
                    SyncToUnifiedCache(new List<string>(), new List<List<string>>());
                    AutoSaveToFileCache(new List<string>(), new List<List<string>>());

                    QueueStatus($"⚠ {_title}：未查询到数据", ColorTranslator.FromHtml("#EF6C00"));
                    LogMessage("warn", "⚠ 查询返回空集合");
                }
                QueueFinish();
            }
            catch (Exception ex)
            {
                QueueStatus($"✗ {_title}：查询失败 - {ex.Message}", ColorTranslator.FromHtml("#C62828"));
                LogMessage("error", $"✗ 查询异常: {ex.Message}");
                QueueFinish();
            }
        }

        private static readonly Dictionary<string, string> SubtypeLabelMap = new Dictionary<string, string>
        {
            { "vm", "虚拟机" },
            { "template", "模板" },
            { "network", "网络" },
            { "network_standard_pg", "标准端口组" },
            { "network_distributed_pg", "分布式端口组" },
            { "network_vdswitch", "虚拟交换机" },
            { "vm_ip", "IP查询" },
            { "host", "主机" },
            { "datastore", "存储" },
        };

        private string GetSubtypeDisplayName(string cacheKey)
        {
            if (SubtypeLabelMap.TryGetValue(cacheKey, out var label)) return label;
            return null;
        }

        private void QueueStatus(string text, Color fg)
        {
            try
            {
                if (_root.InvokeRequired)
                    _root.BeginInvoke(new Action(() => UpdateStatus(text, fg)));
                else
                    UpdateStatus(text, fg);
            }
            catch { }
        }

        private void QueueFinish()
        {
            try
            {
                if (_root.InvokeRequired)
                    _root.BeginInvoke(new Action(FinishQueryUI));
                else
                    FinishQueryUI();
            }
            catch { }
        }

        private void FinishQueryUI()
        {
            if (_root.InvokeRequired)
            {
                _root.Invoke(new Action(FinishQueryUI));
                return;
            }

            _refreshBtn.Enabled = true;
        }

        private void QueueRender(List<string> columns, List<List<string>> rows)
        {
            try
            {
                if (_root.InvokeRequired)
                    _root.BeginInvoke(new Action(() => ApplyResult(columns, rows)));
                else
                    ApplyResult(columns, rows);
            }
            catch { }
        }
        private void ApplyResult(List<string> columns, List<List<string>> rows)
        {
            _columns = columns;
            _rows = rows;

            string cacheKey = CurrentCacheKey();
            _subtypeCache[cacheKey] = new SubtypeCacheItem
            {
                Columns = columns,
                Rows = rows,
                DispName = _title,
                ElapsedMs = 0
            };

            // 有旧数据且列名/列数完全一致 → Diff 增量更新（无闪烁），否则全量重建
            bool columnsMatch = false;
            if (_listView != null && _listView.Columns.Count == columns.Count && columns.Count > 0)
            {
                columnsMatch = true;
                for (int ci = 0; ci < columns.Count; ci++)
                {
                    string existingTag = _listView.Columns[ci].Tag?.ToString() ?? "";
                    if (!string.Equals(existingTag, columns[ci], StringComparison.OrdinalIgnoreCase))
                    {
                        columnsMatch = false;
                        break;
                    }
                }
            }

            if (_listView != null && _listView.Items.Count > 0 && columnsMatch)
            {
                try
                {
                    UpdateListViewDiff(columns, rows);
                }
                catch
                {
                    RebuildListView();
                }
            }
            else
            {
                RebuildListView();
            }

            // 刷新后若搜索框有值，保持过滤状态
            if (_searchBox != null && !string.IsNullOrWhiteSpace(_searchBox.Text))
            {
                ApplySearch();
            }
        }

        private void SyncToUnifiedCache()
        {
            SyncToUnifiedCache(_columns, _rows);
        }

        private void SyncToUnifiedCache(List<string> columns, List<List<string>> rows)
        {
            _allRowValues = rows;
            _subtypeQueried.Add(CurrentCacheKey());
        }

        private void AutoSaveToFileCache(List<string> columns, List<List<string>> rows)
        {
            try
            {
                if (!SessionManager.Instance.IsConnected) return;

                string vc = SessionManager.Instance.CurrentVc ?? "";
                if (string.IsNullOrEmpty(vc)) return;

                string cacheKey = CurrentCacheKey();
                if (cacheKey == "template") return;

                var existingCache = UnifiedCache.CacheLoadResources(vc) ?? new Dictionary<string, object>();
                int nameColIdx = columns.FindIndex(c => c.Equals("Name", StringComparison.OrdinalIgnoreCase));
                int nIdx = nameColIdx >= 0 ? nameColIdx : 0;
                var names = rows.Select(r => nIdx < r.Count ? r[nIdx].Trim() : "").Where(s => !string.IsNullOrEmpty(s)).ToList();

                switch (cacheKey)
                {
                    case "host":
                        existingCache["host_names"] = names;
                        UnifiedCache.UpdateResourceCache(hosts: names);
                        break;
                    case "datastore":
                        existingCache["datastore_names"] = names;
                        UnifiedCache.UpdateResourceCache(datastores: names);
                        break;
                    case "network":
                        existingCache["network_names"] = names;
                        UnifiedCache.UpdateResourceCache(portGroups: names);
                        break;
                    case "vm":
                        existingCache["vm_names"] = names;
                        UnifiedCache.UpdateResourceCache(vms: names);
                        break;
                }

                existingCache["vc_host"] = vc;
                existingCache["updated_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                UnifiedCache.CacheSaveResources(vc, existingCache);
            }
            catch
            {
            }
        }
        private void RebuildListView()
        {
            if (_listView == null) return;

            var savedWidths = new Dictionary<string, int>();
            foreach (ColumnHeader ch in _listView.Columns)
            {
                string key = ch.Tag?.ToString() ?? "";
                if (!string.IsNullOrEmpty(key))
                    savedWidths[key] = ch.Width;
            }

            _listView.BeginUpdate();
            _listView.Items.Clear();
            _listView.Columns.Clear();

            foreach (var col in _columns)
            {
                string displayName = GetColumnDisplayName(col);
                int width = savedWidths.TryGetValue(col, out int w) ? w : GetColumnWidth(col);
                var ch = new ColumnHeader { Text = displayName, Width = width, Tag = col };
                _listView.Columns.Add(ch);
            }

            foreach (var row in _rows)
            {
                var paddedRow = new List<string>(row);
                while (paddedRow.Count < _columns.Count)
                    paddedRow.Add("");

                var lvi = new ListViewItem(paddedRow.Count > 0 ? paddedRow[0] : "");
                lvi.Tag = new List<string>(paddedRow);
                for (int i = 1; i < paddedRow.Count; i++)
                    lvi.SubItems.Add(paddedRow[i]);
                _listView.Items.Add(lvi);
            }

            _listView.EndUpdate();

            // 只在首次加载数据时计算列宽，后续排序/过滤不再触发
            if (!_columnsAutoResized)
            {
                bool fillToWindow = !(_queryType == "vm" && _currentSubtype == "vm");
                Utils.TableConvertUtils.AutoResizeColumns(_listView, fillToWindow: fillToWindow);
                _columnsAutoResized = true;
            }
        }

        private void UpdateListViewDiff(List<string> columns, List<List<string>> rows)
        {
            if (_listView == null || columns == null || columns.Count == 0) return;

            int colCount = columns.Count;
            const int keyCol = 0;

            List<string> PadRow(List<string> r)
            {
                var p = new List<string>(r ?? new List<string>());
                while (p.Count < colCount) p.Add("");
                if (p.Count > colCount) p.RemoveRange(colCount, p.Count - colCount);
                return p;
            }

            var newKeyMap = new Dictionary<string, List<string>>();
            foreach (var row in rows)
            {
                var padded = PadRow(row);
                string key = padded[keyCol] ?? "";
                if (!string.IsNullOrEmpty(key) && !newKeyMap.ContainsKey(key))
                    newKeyMap[key] = padded;
            }

            _listView.BeginUpdate();
            try
            {
                for (int i = _listView.Items.Count - 1; i >= 0; i--)
                {
                    var lvi = _listView.Items[i];
                    string key = "";
                    if (lvi.Tag is List<string> tr && tr.Count > keyCol) key = tr[keyCol] ?? "";
                    if (string.IsNullOrEmpty(key)) key = lvi.Text ?? "";
                    if (!string.IsNullOrEmpty(key) && !newKeyMap.ContainsKey(key))
                        _listView.Items.RemoveAt(i);
                }

                foreach (ListViewItem lvi in _listView.Items)
                {
                    string key = "";
                    if (lvi.Tag is List<string> tr0 && tr0.Count > keyCol) key = tr0[keyCol] ?? "";
                    if (string.IsNullOrEmpty(key)) key = lvi.Text ?? "";
                    if (string.IsNullOrEmpty(key) || !newKeyMap.TryGetValue(key, out var newPadded)) continue;

                    List<string>? oldTag = lvi.Tag as List<string>;
                    bool same = oldTag != null
                        && oldTag.Count == colCount
                        && newPadded.Count == colCount;
                    if (same)
                        for (int c = 0; c < colCount; c++)
                            if ((oldTag[c] ?? "") != (newPadded[c] ?? "")) { same = false; break; }

                    if (same) continue;

                    while (lvi.SubItems.Count < colCount) lvi.SubItems.Add("");
                    while (lvi.SubItems.Count > colCount) lvi.SubItems.RemoveAt(lvi.SubItems.Count - 1);

                    for (int c = 0; c < colCount; c++)
                        lvi.SubItems[c].Text = newPadded[c] ?? "";
                    lvi.Tag = new List<string>(newPadded);
                }

                var existingItems = new Dictionary<string, ListViewItem>();
                foreach (ListViewItem lvi in _listView.Items)
                {
                    string key = "";
                    if (lvi.Tag is List<string> tr2 && tr2.Count > keyCol) key = tr2[keyCol] ?? "";
                    if (string.IsNullOrEmpty(key)) key = lvi.Text ?? "";
                    if (!string.IsNullOrEmpty(key)) existingItems[key] = lvi;
                }

                int idx = 0;
                foreach (var row in rows)
                {
                    var padded = PadRow(row);
                    string key = padded[keyCol] ?? "";
                    if (!string.IsNullOrEmpty(key) && !existingItems.ContainsKey(key))
                    {
                        var lvi = new ListViewItem(padded[0]);
                        lvi.Tag = new List<string>(padded);
                        for (int c = 1; c < colCount; c++)
                            lvi.SubItems.Add(padded[c] ?? "");
                        if (idx <= _listView.Items.Count)
                            _listView.Items.Insert(idx, lvi);
                        else
                            _listView.Items.Add(lvi);
                        existingItems[key] = lvi;
                    }
                    idx++;
                }

                if (_listView.Items.Count == rows.Count)
                {
                    var reordered = new List<ListViewItem>();
                    foreach (var row in rows)
                    {
                        var padded = PadRow(row);
                        string key = padded[keyCol] ?? "";
                        if (existingItems.TryGetValue(key, out var lvi)) reordered.Add(lvi);
                    }
                    if (reordered.Count == rows.Count)
                    {
                        _listView.Items.Clear();
                        foreach (var lvi in reordered) _listView.Items.Add(lvi);
                    }
                }
            }
            finally
            {
                _listView.EndUpdate();
            }
        }

        // 禁止标签编辑（防止双击第一列进入编辑模式）
        private void ListView_BeforeLabelEdit(object? sender, LabelEditEventArgs e)
        {
            e.CancelEdit = true;
        }

        // 窗口大小变化时重新分配列宽
        private void ListView_SizeChanged(object? sender, EventArgs e)
        {
            if (_listView != null && _listView.Columns.Count > 0)
            {
                bool fillToWindow = !(_queryType == "vm" && _currentSubtype == "vm");
                Utils.TableConvertUtils.AutoResizeColumns(_listView, fillToWindow: fillToWindow);
            }
        }
        private void ListView_ColumnClick(object? sender, ColumnClickEventArgs e)
        {
            if (e.Column < 0 || e.Column >= _columns.Count) return;

            if (_sortColumn == _columns[e.Column])
                _sortAsc = !_sortAsc;
            else
            {
                _sortColumn = _columns[e.Column];
                _sortAsc = true;
            }

            int idx = e.Column;
            bool asc = _sortAsc;

            Comparison<ListViewItem> comparer = (a, b) =>
            {
                if (idx >= a.SubItems.Count || idx >= b.SubItems.Count) return 0;
                string sa = a.SubItems[idx].Text;
                string sb = b.SubItems[idx].Text;

                if (double.TryParse(sa, out double da) && double.TryParse(sb, out double db))
                    return asc ? da.CompareTo(db) : db.CompareTo(da);

                return asc ? string.Compare(sa, sb, StringComparison.OrdinalIgnoreCase)
                           : string.Compare(sb, sa, StringComparison.OrdinalIgnoreCase);
            };

            _listView.ListViewItemSorter = new ComparerWrapper(comparer);
            _listView.Sort();

            ApplySearchWithoutRebuild();
        }

        private void ApplySearchWithoutRebuild()
        {
            if (_allRowValues.Count == 0 || string.IsNullOrWhiteSpace(_searchBox?.Text))
                return;

            var keywords = _searchBox.Text.Trim().ToLowerInvariant()
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var filteredRows = _allRowValues.Where(row =>
                keywords.All(kw => row.Any(cell => cell.ToLowerInvariant().Contains(kw)))
            ).ToList();

            _rows = filteredRows;
            RebuildListView();
            RefreshStatusBar();
        }

        private void RefreshListViewFromAllRows()
        {
            if (_allRowValues.Count == 0) return;
            _rows = new List<List<string>>(_allRowValues);
            RebuildListView();
            RefreshStatusBar();
        }
        private void ApplySearch()
        {
            if (_allRowValues.Count == 0) return;

            var source = new List<List<string>>(_allRowValues);

            if (string.IsNullOrWhiteSpace(_searchBox?.Text))
            {
                _rows = source;
                RebuildListView();
                RefreshStatusBar();
                return;
            }

            var keywords = _searchBox.Text.Trim().ToLowerInvariant()
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var filteredRows = source.Where(row =>
                keywords.All(kw => row.Any(cell => cell.ToLowerInvariant().Contains(kw)))
            ).ToList();

            _rows = filteredRows;
            RebuildListView();
            RefreshStatusBar();
        }

        private void UpdateStatus(string text, Color fg)
        {
            if (_statusLabel == null) return;
            if (_statusLabel.InvokeRequired)
            {
                try { _statusLabel.Invoke(new Action(() => { _statusLabel.Text = text; _statusLabel.ForeColor = fg; })); }
                catch { }
            }
            else
            {
                _statusLabel.Text = text;
                _statusLabel.ForeColor = fg;
            }
        }

        private void RefreshSingleVm(string anyId)
        {
            if (_currentSubtype != "vm" || _listView == null) return;
            if (string.IsNullOrEmpty(anyId)) return;
            string fullMoRef = FullVmRef(anyId);
            int sepIdx = fullMoRef.IndexOf('-');
            string shortId = sepIdx >= 0 ? fullMoRef.Substring(sepIdx + 1) : anyId;
            string script = TableConvertUtils.QueryScriptSingleVm(fullMoRef, useMoRef: true);
            Task.Run(async () =>
            {
                try
                {
                    var result = await SessionManager.Instance.ExecuteQueryAsync(script, (m, l) => { });
                    if (result == null || result.Count == 0) return;
                    var dict = result[0];

                    var newRow = new List<string>
                    {
                        SafeStr(dict, "Id"),
                        SafeStr(dict, "Name"),
                        SafeStr(dict, "PowerState"),
                        SafeStr(dict, "NumCpu"),
                        SafeStr(dict, "MemoryGB"),
                        SafeStr(dict, "IPAddress"),
                        SafeStr(dict, "VMHost"),
                        SafeStr(dict, "ResourcePool"),
                        SafeStr(dict, "Datastore"),
                        SafeStr(dict, "GuestOS")
                    };

                    _root.Invoke(new Action(() =>
                    {
                        int idColIdx = _columns != null ? _columns.FindIndex(c => c.Equals("Id", StringComparison.OrdinalIgnoreCase)) : -1;
                        if (idColIdx < 0) return;

                        ListViewItem? targetItem = null;
                        int idx = -1;
                        foreach (ListViewItem item in _listView.Items)
                        {
                            idx++;
                            if (item.Tag is List<string> tagRow && idColIdx < tagRow.Count
                                && string.Equals(tagRow[idColIdx], shortId, StringComparison.OrdinalIgnoreCase))
                            {
                                targetItem = item; break;
                            }
                        }

                        int allIdx = -1;
                        for (int i = 0; i < _allRowValues.Count; i++)
                        {
                            var row = _allRowValues[i];
                            if (row.Count > idColIdx && string.Equals(row[idColIdx], shortId, StringComparison.OrdinalIgnoreCase))
                            {
                                allIdx = i; break;
                            }
                        }

                        if (targetItem != null)
                        {
                            for (int c = 0; c < newRow.Count; c++)
                            {
                                if (c == 0) targetItem.Text = newRow[c];
                                else if (c < targetItem.SubItems.Count) targetItem.SubItems[c].Text = newRow[c];
                            }
                            targetItem.Tag = new List<string>(newRow);
                        }

                        if (idx >= 0 && idx < _rows.Count)
                        {
                            _rows[idx] = newRow;
                        }

                        if (allIdx >= 0 && allIdx < _allRowValues.Count)
                        {
                            _allRowValues[allIdx] = newRow;
                        }

                        RefreshStatusBar();
                    }));
                }
                catch (Exception)
                {
                }
            });
        }

        private void RemoveVmRow(string anyId)
        {
            if (_currentSubtype != "vm" || _listView == null) return;
            if (string.IsNullOrEmpty(anyId)) return;

            string fullMoRef = FullVmRef(anyId);
            int sepIdx = fullMoRef.IndexOf('-');
            string shortId = sepIdx >= 0 ? fullMoRef.Substring(sepIdx + 1) : anyId;

            int idColIdx = _columns != null ? _columns.FindIndex(c => c.Equals("Id", StringComparison.OrdinalIgnoreCase)) : -1;
            if (idColIdx < 0) return;

            ListViewItem? targetItem = null;
            int rowIdx = -1;
            int idx = -1;
            foreach (ListViewItem item in _listView.Items)
            {
                idx++;
                if (item.Tag is List<string> tagRow && idColIdx < tagRow.Count
                    && string.Equals(tagRow[idColIdx], shortId, StringComparison.OrdinalIgnoreCase))
                {
                    targetItem = item; rowIdx = idx; break;
                }
            }

            int allIdx = -1;
            for (int i = 0; i < _allRowValues.Count; i++)
            {
                var row = _allRowValues[i];
                if (row.Count > idColIdx && string.Equals(row[idColIdx], shortId, StringComparison.OrdinalIgnoreCase))
                {
                    allIdx = i; break;
                }
            }

            if (targetItem != null) _listView.Items.Remove(targetItem);
            if (rowIdx >= 0 && rowIdx < _rows.Count) _rows.RemoveAt(rowIdx);
            if (allIdx >= 0 && allIdx < _allRowValues.Count) _allRowValues.RemoveAt(allIdx);

            RefreshStatusBar();
        }

        private static string SafeStr(Dictionary<string, object> dict, string key)
        {
            if (dict == null) return "";
            if (dict.TryGetValue(key, out var v) && v != null) return v.ToString();
            return "";
        }

        private static Dictionary<string, object>? SimpleJsonDeserialize(string json)
        {
            try
            {
                var result = new Dictionary<string, object>();
                json = json.Trim();
                if (!json.StartsWith("{") || !json.EndsWith("}")) return null;
                json = json.Substring(1, json.Length - 2);
                var pairs = new List<(string key, string val)>();
                bool inStr = false;
                bool esc = false;
                int start = 0;
                for (int i = 0; i < json.Length; i++)
                {
                    char c = json[i];
                    if (esc) { esc = false; continue; }
                    if (c == '\\') { esc = true; continue; }
                    if (c == '"') { inStr = !inStr; continue; }
                    if (c == ':' && !inStr) { start = i + 1; continue; }
                    if (c == ',' && !inStr)
                    {
                        pairs.Add(ExtractPair(json, start, i));
                        start = i + 1;
                    }
                }
                if (start < json.Length) pairs.Add(ExtractPair(json, start, json.Length));

                static (string key, string val) ExtractPair(string s, int from, int to)
                {
                    string piece = s.Substring(from, to - from).Trim();
                    int q1 = piece.IndexOf('"');
                    int q2 = piece.IndexOf('"', q1 + 1);
                    string key = q1 >= 0 ? piece.Substring(q1 + 1, q2 - q1 - 1) : piece;
                    string val = q2 >= 0 && q2 + 1 < piece.Length ? piece.Substring(q2 + 1).Trim() : "";
                    string v = val.Trim();
                    if (v.StartsWith("\"") && v.EndsWith("\"")) v = v.Substring(1, v.Length - 2);
                    return (key, v);
                }

                foreach (var p in pairs)
                    result[p.key] = p.val;

                return result;
            }
            catch { return null; }
        }

        private void RefreshStatusBar()
        {
            if (_statusLabel == null) return;

            string text;
            bool hasFilter = _searchBox != null && !string.IsNullOrWhiteSpace(_searchBox.Text);
            int total = _allRowValues.Count > 0 ? _allRowValues.Count : _rows.Count;

            if (hasFilter)
                text = $"✓ {_title}：筛选 {_rows.Count}/{total} 条";
            else
                text = $"✓ {_title}：共 {_rows.Count} 条";

            if (_listView != null && _listView.SelectedItems.Count > 0)
                text += $" | 选中 {_listView.SelectedItems.Count}";

            UpdateStatus(text, ColorTranslator.FromHtml("#2E7D32"));
        }

        /// <summary>
        /// 向主日志区输出一行日志（ResourceQueryTab 内部统一入口）。
        /// 自动拼接时间戳；中文 level 先归一化为英文（文本里的 [level] 和传给 AppendLog 的 level 都统一用英文）。
        /// </summary>
        protected void LogMessage(string level, string message)
        {
            try
            {
                if (_root is MainForm mainForm)
                {
                    // 中文 → 英文 level 归一化（文本里的 [level] 也统一用英文，和 HomeTab 保持一致）
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
                    if (mainForm.InvokeRequired)
                        mainForm.BeginInvoke(new Action(() => mainForm.AppendLog(text, mfLevel)));
                    else
                        mainForm.AppendLog(text, mfLevel);
                }
            }
            catch { }
        }

        // 自定义比较器包装类
        private class ComparerWrapper : IComparer
        {
            private readonly Comparison<ListViewItem> _comparer;
            public ComparerWrapper(Comparison<ListViewItem> comparer) => _comparer = comparer;
            public int Compare(object? x, object? y) => _comparer((ListViewItem)x!, (ListViewItem)y!);
        }
    }
}