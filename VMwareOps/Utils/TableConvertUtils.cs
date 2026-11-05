// ============================================================================
// TableConvertUtils.cs - PowerCLI 数据 → WinForms 表格渲染 + 导出工具
// ============================================================================
// 职责范围（全部为 static 静态方法）：
//
//   ① JSON 解析 — ParseJsonToTable()
//      将 PowerShell -Json 输出 / ExecuteQueryAsync 返回的字典列表
//      统一转换为 (columns, rows) 二维表结构
//
//   ② ListView 渲染 — BuildTreeviewColumns() / FillTreeviewRows()
//      根据二维表自动创建 ListView 列 + 填充行数据（含斑马纹）
//      无数据时 RenderEmptyLabel() 显示"无数据"占位标签
//
//   ③ 列标题中文映射 — StandardColumnMap
//      PowerCLI 英文属性名（Name / VMHost / PowerState...）
//      ↔ UI 中文列标题（名称 / 所在主机 / 电源状态...）
//
//   ④ 列宽控制 — ColumnWidthMap + AutoResizeColumns()
//      固定列宽预设（PowerState=90, IPAddress=110...）
//      + 自适应列宽（Fill / Legacy 两套算法，按窗口宽度分配）
//
//   ⑤ 数据导出 — RowsToCsvText() / RowsToTsvText()
//      二维表 → CSV / TSV 纯文本（含字段转义 EscapeCsvField）
//
//   ⑥ PowerShell 查询脚本模板 — QueryScriptVmList() / HostList() / DatastoreList() / ...
//      预定义常用 PowerCLI 查询命令（Select-Object 指定要拿哪些字段 + 计算属性）
//      UI 层直接调用拿脚本字符串丢给 Runspace 执行
//
// 调用方式：
//   var (cols, rows) = TableConvertUtils.ParseJsonToTable(jsonText);
//   TableConvertUtils.BuildTreeviewColumns(listView, cols, rows);
//   TableConvertUtils.FillTreeviewRows(listView, cols, rows);
//   TableConvertUtils.AutoResizeColumns(listView);
//
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace VMwareOps.Utils
{
    public static class TableConvertUtils
    {
        private static readonly Dictionary<string, string> StandardColumnMap = new Dictionary<string, string>
        {
            { "Name", "名称" },
            { "Id", "ID" },
            { "PowerState", "电源状态" },
            { "NumCpu", "CPU" },
            { "MemoryGB", "内存" },
            { "MemoryUsagePercent", "内存使用率(%)" },
            { "CpuUsagePercent", "CPU使用率(%)" },
            { "MemoryMB", "内存(MB)" },
            { "VMHost", "所在主机" },
            { "Host", "主机" },
            { "ResourcePool", "资源池" },
            { "IPAddress", "IP 地址" },
            { "IP Address", "IP 地址" },
            { "Datastore", "存储" },
            { "DatastoreId", "存储" },
            { "CapacityGB", "容量(GB)" },
            { "FreeSpaceGB", "空闲(GB)" },
            { "UsedPercent", "使用率(%)" },
            { "VLanId", "VLAN" },
            { "NumPorts", "端口数" },
            { "NumPortsAvailable", "可用端口" },
            { "MacAddress", "MAC 地址" },
            { "NetworkName", "网络" },
            { "Description", "描述" },
            { "Version", "版本" },
            { "Template", "模板" },
            { "Created", "创建时间" },
            { "SizeGB", "大小(GB)" },
            { "GuestId", "来宾 ID" },
            { "GuestOS", "来宾系统" },
            { "Notes", "备注" },
            { "ConfigFile", "配置文件" },
            { "Path", "路径" },
            { "HardwareVersion", "硬件版本" },
            { "Persistence", "持久化" },
            { "Filename", "文件名" },
            { "Type", "类型" },
            { "Connected", "已连接" },
            { "ConnectionState", "连接状态" },
            { "State", "状态" },
            { "Accessible", "可访问" },
        };

        /// <summary>
        /// 列宽预设固定值（单位：96DPI 像素）。
        /// 此处定义的列在 AutoResizeColumns 中会被识别为"固定列"，
        /// 直接使用本处设定的宽度（经 DpiUtils.Px 缩放），不参与自适应比例分配。
        /// </summary>
        private static readonly Dictionary<string, int> ColumnWidthMap = new Dictionary<string, int>
        {
            // 固定列宽（96DPI 像素）。定义了的列用固定值，没定义的按内容自适应。
            { "PowerState", 90 },
            { "IPAddress", 110 },
            { "IP Address", 110 },
            { "NumCpu", 60 },
            { "MemoryGB", 80 },
            { "MemoryUsagePercent", 110 },
            { "CpuUsagePercent", 100 },
        };

        public static (List<string> columns, List<List<string>> rows) ParseJsonToTable(string outputText)
        {
            var columns = new List<string>();
            var rows = new List<List<string>>();

            if (string.IsNullOrWhiteSpace(outputText))
                return (columns, rows);

            try
            {
                var jsonStart = outputText.IndexOf('[');
                var jsonEnd = outputText.LastIndexOf(']');
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    string jsonStr = outputText.Substring(jsonStart, jsonEnd - jsonStart + 1);
                    
                    // 尝试多种反序列化方式
                    try
                    {
                        // 方式1：尝试解析为 List<Dictionary<string, object>>
                        var jsonArray = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(jsonStr);
                        
                        if (jsonArray != null && jsonArray.Count > 0)
                        {
                            columns = jsonArray[0].Keys.ToList();
                            foreach (var item in jsonArray)
                            {
                                var row = new List<string>();
                                foreach (var col in columns)
                                {
                                    if (item.TryGetValue(col, out var value))
                                    {
                                        row.Add(value?.ToString() ?? "");
                                    }
                                    else
                                    {
                                        row.Add("");
                                    }
                                }
                                rows.Add(row);
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // 方式2：尝试解析为 JArray，然后扁平化处理
                        try
                        {
                            var jArray = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JArray>(jsonStr);
                            if (jArray != null && jArray.Count > 0)
                            {
                                // 获取所有可能的键
                                var allKeys = new HashSet<string>();
                                foreach (var item in jArray)
                                {
                                    if (item is Newtonsoft.Json.Linq.JObject obj)
                                    {
                                        foreach (var prop in obj.Properties())
                                        {
                                            allKeys.Add(prop.Name);
                                        }
                                    }
                                }
                                
                                columns = allKeys.ToList();
                                foreach (var item in jArray)
                                {
                                    var row = new List<string>();
                                    if (item is Newtonsoft.Json.Linq.JObject obj)
                                    {
                                        foreach (var col in columns)
                                        {
                                            var val = obj[col]?.ToString();
                                            row.Add(val ?? "");
                                        }
                                    }
                                    else
                                    {
                                        row.AddRange(columns.Select(_ => ""));
                                    }
                                    rows.Add(row);
                                }
                            }
                        }
                        catch (Exception)
                        {
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            return (columns, rows);
        }

        public static (List<string> columns, List<List<string>> rows) ParseAnyToTable(string outputText, string? hint = null)
        {
            var result = ParseJsonToTable(outputText);
            if (result.columns.Count > 0 && result.rows.Count > 0)
            {
                return result;
            }

            return ParseFormatTableToTable(outputText);
        }

        private static (List<string> columns, List<List<string>> rows) ParseFormatTableToTable(string outputText)
        {
            var columns = new List<string>();
            var rows = new List<List<string>>();

            var lines = outputText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Select(l => l.Trim())
                                  .Where(l => !string.IsNullOrEmpty(l))
                                  .ToList();

            if (lines.Count < 2)
                return (columns, rows);

            var separatorLine = lines.FirstOrDefault(l => l.Contains("---"));
            if (separatorLine == null)
                return (columns, rows);

            int sepIndex = lines.IndexOf(separatorLine);
            if (sepIndex <= 0)
                return (columns, rows);

            string headerLine = lines[sepIndex - 1];
            var headerParts = Regex.Split(headerLine, @"\s{2,}").Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            columns = headerParts;

            for (int i = sepIndex + 1; i < lines.Count; i++)
            {
                var row = new List<string>();
                var parts = Regex.Split(lines[i], @"\s{2,}").Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                for (int j = 0; j < columns.Count; j++)
                {
                    row.Add(j < parts.Count ? parts[j] : "");
                }
                rows.Add(row);
            }

            return (columns, rows);
        }

        public static void BuildTreeviewColumns(ListView tree, List<string> columns, List<List<string>> rows)
        {
            tree.Items.Clear();
            tree.Columns.Clear();

            foreach (var col in columns)
            {
                string displayName = GetColumnDisplayName(col);
                int width = GetColumnWidth(col);
                
                var columnHeader = new ColumnHeader
                {
                    Text = displayName,
                    Width = width
                };
                tree.Columns.Add(columnHeader);
            }

            tree.View = View.Details;
            tree.FullRowSelect = true;
            tree.GridLines = true;

            foreach (var row in rows)
            {
                if (row.Count == 0) continue;
                
                var item = new ListViewItem(row[0]);
                for (int i = 1; i < row.Count; i++)
                {
                    item.SubItems.Add(i < row.Count ? row[i] : "");
                }
                tree.Items.Add(item);
            }

            // 自适应列宽
            AutoResizeColumns(tree);
        }

        public static void FillTreeviewRows(ListView tree, List<string> columns, List<List<string>> rows, bool zebra = true)
        {
            tree.Items.Clear();

            foreach (var row in rows)
            {
                if (row.Count == 0) continue;

                var item = new ListViewItem(row[0]);
                for (int i = 1; i < row.Count; i++)
                {
                    item.SubItems.Add(i < row.Count ? row[i] : "");
                }
                tree.Items.Add(item);
            }
        }

        public static void RenderEmptyLabel(Control parent, string text = "无数据")
        {
            var label = new Label
            {
                Text = text,
                Font = new Font("Microsoft YaHei", 12), ForeColor = Color.Gray,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleCenter
            };
            label.Dock = DockStyle.Fill;
            parent.Controls.Add(label);
            label.BringToFront();
        }

        public static string RowsToCsvText(List<string> columns, List<List<string>> rows, string delimiter = ",")
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(delimiter, columns.Select(EscapeCsvField)));
            
            foreach (var row in rows)
            {
                sb.AppendLine(string.Join(delimiter, row.Select(EscapeCsvField)));
            }
            
            return sb.ToString();
        }

        public static string RowsToTsvText(List<string> columns, List<List<string>> rows)
        {
            return RowsToCsvText(columns, rows, "\t");
        }

        private static string EscapeCsvField(string field)
        {
            if (field.Contains(",") || field.Contains("\"") || field.Contains("\n") || field.Contains("\r"))
            {
                return "\"" + field.Replace("\"", "\"\"") + "\"";
            }
            return field;
        }

        public static List<string> ExtractHeadersFromFirstRow(List<string> rowData, List<string>? defaultHeaders = null)
        {
            if (defaultHeaders != null)
                return defaultHeaders;

            return Enumerable.Range(0, rowData.Count).Select(i => $"列{i + 1}").ToList();
        }

        private static string GetColumnDisplayName(string columnName)
        {
            if (StandardColumnMap.TryGetValue(columnName, out var displayName))
            {
                return displayName;
            }
            return columnName;
        }

        private static int GetColumnWidth(string columnName)
        {
            if (ColumnWidthMap.TryGetValue(columnName, out var width))
            {
                return width;
            }
            return 100;
        }

        /// <summary>
        /// 自适应列宽：
        /// - fillToWindow=false（旧逻辑，VM 列表用）：
        ///   有预设宽度的固定列直接使用 ColumnWidthMap 值，剩余列按内容宽度自适应，允许水平滚动
        /// - fillToWindow=true（非 VM 列表用）：
        ///   所有列按权重比例铺满窗口宽度，不区分固定/自适应，不留水平滚动条
        /// </summary>
        public static void AutoResizeColumns(ListView listView, int? overrideWidth = null, bool fillToWindow = false)
        {
            try
            {
                if (listView == null) return;
                if (listView.IsDisposed || !listView.IsHandleCreated) return;
                if (listView.Columns == null || listView.Columns.Count == 0) return;

                int clientWidth = overrideWidth ?? listView.ClientSize.Width;
                if (clientWidth <= 0) return;

                if (fillToWindow)
                {
                    AutoResizeColumnsFill(listView, clientWidth);
                    return;
                }

                AutoResizeColumnsLegacy(listView, clientWidth);
            }
            catch (Exception)
            {
            }
        }

        private static void AutoResizeColumnsFill(ListView listView, int clientWidth)
        {
            int colCount = listView.Columns.Count;

            using (var graphics = listView.CreateGraphics())
            {
                var font = listView.Font ?? Control.DefaultFont;

                for (int col = 0; col < colCount; col++)
                {
                    string headerText = listView.Columns[col].Text ?? "";
                    string lookupKey = listView.Columns[col].Tag?.ToString() ?? headerText;

                    if (ColumnWidthMap.TryGetValue(lookupKey, out int presetWidth))
                    {
                        listView.Columns[col].Width = DpiUtils.Px(presetWidth);
                    }
                    else
                    {
                        int headerWidth = (int)graphics.MeasureString(headerText, font).Width + 16;
                        int maxDataWidth = 0;
                        foreach (ListViewItem item in listView.Items)
                        {
                            if (col < item.SubItems.Count)
                            {
                                string cellText = item.SubItems[col].Text ?? "";
                                int cellWidth = (int)graphics.MeasureString(cellText, font).Width + 16;
                                if (cellWidth > maxDataWidth)
                                    maxDataWidth = cellWidth;
                            }
                        }
                        listView.Columns[col].Width = Math.Max(headerWidth, maxDataWidth);
                    }
                }
            }

            int totalW = 0;
            for (int i = 0; i < colCount; i++) totalW += listView.Columns[i].Width;
            int leftover = clientWidth - totalW - 18;
            if (leftover > 0 && colCount > 0)
            {
                listView.Columns[colCount - 1].Width += leftover;
            }
        }

        private static void AutoResizeColumnsLegacy(ListView listView, int clientWidth)
        {
            var columnNames = new string[listView.Columns.Count];
            var contentWidths = new int[listView.Columns.Count];
            var isFixed = new bool[listView.Columns.Count];
            int fixedTotalWidth = 0;
            int totalContentWidth = 0;

            using (var graphics = listView.CreateGraphics())
            {
                if (graphics == null) return;

                var font = listView.Font ?? Control.DefaultFont;

                for (int col = 0; col < listView.Columns.Count; col++)
                {
                    string headerText = listView.Columns[col].Text ?? "";
                    columnNames[col] = headerText;

                    string lookupKey = listView.Columns[col].Tag?.ToString() ?? headerText;
                    if (ColumnWidthMap.TryGetValue(lookupKey, out int presetWidth))
                    {
                        isFixed[col] = true;
                        int scaledWidth = DpiUtils.Px(presetWidth);
                        contentWidths[col] = scaledWidth;
                        fixedTotalWidth += scaledWidth;
                    }
                    else
                    {
                        isFixed[col] = false;
                        int headerWidth = (int)graphics.MeasureString(headerText, font).Width + 16;
                        int maxDataWidth = 0;
                        foreach (ListViewItem item in listView.Items)
                        {
                            if (col < item.SubItems.Count)
                            {
                                string cellText = item.SubItems[col].Text ?? "";
                                int cellWidth = (int)graphics.MeasureString(cellText, font).Width + 16;
                                if (cellWidth > maxDataWidth)
                                    maxDataWidth = cellWidth;
                            }
                        }
                        contentWidths[col] = Math.Max(headerWidth, maxDataWidth);
                        totalContentWidth += contentWidths[col];
                    }
                }
            }

            int colCount = listView.Columns.Count;
            for (int i = 0; i < colCount; i++)
            {
                listView.Columns[i].Width = contentWidths[i];
            }

            int totalW = 0;
            for (int i = 0; i < colCount; i++) totalW += listView.Columns[i].Width;
            int leftover = clientWidth - totalW - 18;
            if (leftover > 0 && colCount > 0)
            {
                listView.Columns[colCount - 1].Width += leftover;
            }
        }

        // ---------- VM 列表 ----------
        // 对应 Tab: ResourceQueryTab (VM 查询)
        // 输出字段: Name, PowerState, NumCpu, MemoryGB, VMHost, ResourcePool, Datastore, IPAddress, GuestOS, Id
        public static string QueryScriptVmList()
        {
            return @"
$ErrorActionPreference = 'Stop';
try {
    $hostMap = @{};
    Get-View -ViewType HostSystem -Property name -ErrorAction SilentlyContinue | ForEach-Object {
        $hostMap[$_.MoRef.Value] = $_.Name
    };
    $rpMap = @{};
    Get-View -ViewType ResourcePool -Property Name,Parent -ErrorAction SilentlyContinue | ForEach-Object {
        $rpMap[$_.MoRef.Value] = $_.Name
    };
    $dsMap = @{};
    Get-View -ViewType Datastore -Property Name -ErrorAction SilentlyContinue | ForEach-Object {
        $dsMap[$_.MoRef.Value] = $_.Name
    };
    Get-View -ViewType VirtualMachine -Property name,runtime.powerState,config.hardware,config.template,guest,runtime.host,ResourcePool,Datastore -ErrorAction Stop | Where-Object { -not $_.Config.Template } | ForEach-Object {
        $vmhost = '';
        if ($_.Runtime -and $_.Runtime.Host -and $hostMap.ContainsKey($_.Runtime.Host.Value)) {
            $vmhost = $hostMap[$_.Runtime.Host.Value]
        };
        $rpName = '';
        if ($_.ResourcePool -and $rpMap.ContainsKey($_.ResourcePool.Value)) {
            $rpName = $rpMap[$_.ResourcePool.Value]
        };
        $dsNames = @();
        if ($_.Datastore) {
            foreach ($ds in $_.Datastore) {
                if ($dsMap.ContainsKey($ds.Value)) { $dsNames += $dsMap[$ds.Value] }
            }
        };
        $ips = @();
        if ($_.Guest -and $_.Guest.IpAddress) {
            $ips = $_.Guest.IpAddress | Where-Object { $_ -match '^\d{1,3}(\.\d{1,3}){3}$' }
        };
        [PSCustomObject]@{
            Id           = $_.MoRef.Value;
            Name         = $_.Name;
            PowerState   = $_.Runtime.PowerState;
            NumCpu       = $_.Config.Hardware.NumCPU;
            MemoryGB     = [math]::Round($_.Config.Hardware.MemoryMB / 1024, 2);
            IPAddress    = ($ips -join ',');
            VMHost       = $vmhost;
            ResourcePool = $rpName;
            Datastore    = ($dsNames -join ',');
            GuestOS      = if($_.Guest -and $_.Guest.GuestFullName){$_.Guest.GuestFullName}else{''};
        }
    }
} catch {
    Write-Output ""[ERROR] 查询失败: $($_.Exception.Message)"";
}
";
        }

        public static string QueryScriptSingleVm(string vmIdOrName, bool useMoRef = false)
        {
            string escaped = vmIdOrName.Replace("'", "''");
            string vmQuery = useMoRef
                ? $"Get-View -Id '{escaped}' -Property Name, Runtime.PowerState, Config.Hardware.NumCPU, Config.Hardware.MemoryMB, Guest.Net, Runtime.Host, Config.GuestFullName, Guest.IpAddress, ResourcePool, Datastore -ErrorAction Stop"
                : $"Get-View -ViewType VirtualMachine -Filter @{{\"Name\" = \"{escaped}\"}} -Property Name, Runtime.PowerState, Config.Hardware.NumCPU, Config.Hardware.MemoryMB, Guest.Net, Runtime.Host, Config.GuestFullName, Guest.IpAddress, ResourcePool, Datastore -ErrorAction Stop";
            return $@"
$ErrorActionPreference = ""Stop"";
try {{
    $vm = {vmQuery};
    if ($vm.Count -eq 0 -or -not $vm) {{
        Write-Output ""[ERROR] VM not found: {escaped}"";
    }} else {{
        $vm = if ($vm -is [System.Collections.IEnumerable]) {{ $vm | Select-Object -First 1 }} else {{ $vm }};
        $numCpu = if ($vm.Config -and $vm.Config.Hardware -and $vm.Config.Hardware.NumCPU) {{ [int]$vm.Config.Hardware.NumCPU }} else {{ 0 }};
        $memoryGB = if ($vm.Config -and $vm.Config.Hardware -and $vm.Config.Hardware.MemoryMB) {{ [math]::Round([double]$vm.Config.Hardware.MemoryMB / 1024.0, 2) }} else {{ 0 }};
        $ip = """";
        if ($vm.Guest -and $vm.Guest.IpAddress -and $vm.Guest.IpAddress.Count -gt 0) {{
            $ip = ($vm.Guest.IpAddress -join "", "");
        }} elseif ($vm.Guest -and $vm.Guest.Net -and $vm.Guest.Net.Count -gt 0) {{
            $ip = (($vm.Guest.Net | ForEach-Object {{ $_.IpAddress }} | Where-Object {{ $_ -and $_ -notlike ""127.*"" }}) -join "", "");
        }}
        $hostName = """";
        if ($vm.Runtime -and $vm.Runtime.Host) {{
            try {{
                $hostObj = Get-View -Id $vm.Runtime.Host -Property Name -ErrorAction SilentlyContinue;
                if ($hostObj) {{ $hostName = $hostObj.Name }}
            }} catch {{}}
        }}
        $rpName = """";
        if ($vm.ResourcePool) {{
            try {{
                $rpObj = Get-View -Id $vm.ResourcePool -Property Name -ErrorAction SilentlyContinue;
                if ($rpObj) {{ $rpName = $rpObj.Name }}
            }} catch {{}}
        }}
        $dsNames = @();
        if ($vm.Datastore) {{
            foreach ($ds in $vm.Datastore) {{
                try {{
                    $dsObj = Get-View -Id $ds -Property Name -ErrorAction SilentlyContinue;
                    if ($dsObj) {{ $dsNames += $dsObj.Name }}
                }} catch {{}}
            }}
        }}
        $guestOs = if ($vm.Config -and $vm.Config.GuestFullName) {{ $vm.Config.GuestFullName }} else {{ """" }};
        $row = [PSCustomObject]@{{
            Id = if ($vm.MoRef) {{ $vm.MoRef.Value }} else {{ """" }};
            Name = $vm.Name;
            PowerState = if ($vm.Runtime) {{ $vm.Runtime.PowerState }} else {{ """" }};
            NumCpu = $numCpu;
            MemoryGB = $memoryGB;
            IPAddress = $ip;
            VMHost = $hostName;
            ResourcePool = $rpName;
            Datastore = ($dsNames -join "","");
            GuestOS = $guestOs;
        }};
        $row;
    }}
}} catch {{
    Write-Output ""[ERROR] 查询失败: $($_.Exception.Message)"";
}}
";
        }

        // ---------- 主机列表 ----------
        // 对应 Tab: ResourceQueryTab (主机查询)
        // 输出字段: Name, ConnectionState, NumCpu, CpuUsagePercent, MemoryGB, MemoryUsagePercent, ProductVersion, Id
        public static string QueryScriptHostList()
        {
            return @"
$ErrorActionPreference = 'Stop';
try {
    $hosts = Get-View -ViewType HostSystem -Property Name,Hardware.CpuInfo,Hardware.MemorySize,Summary.QuickStats,Runtime.ConnectionState,Config.Product.Version,Config.Product.Build -ErrorAction Stop;
        foreach ($h in $hosts) {
        $hw = $h.Hardware;
        $cpuInfo = if ($hw) { $hw.CpuInfo } else { $null };
        $numCpu = if ($cpuInfo) { $cpuInfo.NumCpuCores } else { 0 };
        $cpuTotalMhz = if ($cpuInfo -and $cpuInfo.Hz -gt 0) { [math]::Round($cpuInfo.Hz / 1000000 * $cpuInfo.NumCpuCores) } else { 0 };
        $memGB = if ($hw -and $hw.MemorySize -gt 0) { [math]::Round($hw.MemorySize / 1073741824, 2) } else { 0 };
        $cpuPct = 0; $memPct = 0;
        $qs = $h.Summary.QuickStats;
        if ($qs -and $cpuTotalMhz -gt 0) { $cpuPct = [math]::Round($qs.OverallCpuUsage / $cpuTotalMhz * 100, 1) };
        if ($qs -and $hw -and $hw.MemorySize -gt 0) { $memPct = [math]::Round(($qs.OverallMemoryUsage * 1048576) / $hw.MemorySize * 100, 1) };
        $connState = '--';
        if ($h.Runtime) { try { $connState = [string]$h.Runtime.ConnectionState } catch {} };
        $prodVer = '--';
        if ($h.Config -and $h.Config.Product) { $prodVer = ""$($h.Config.Product.Version) build-$($h.Config.Product.Build)"" };
        [PSCustomObject]@{
            Id                 = $h.MoRef.Value;
            Name               = $h.Name;
            ConnectionState    = $connState;
            NumCpu             = $numCpu;
            CpuUsagePercent    = $cpuPct;
            MemoryGB           = $memGB;
            MemoryUsagePercent = $memPct;
            ProductVersion     = $prodVer
        }
    }
} catch {
    Write-Output ""[ERROR] 查询失败: $($_.Exception.Message)"";
}
";
        }

        // ---------- 存储列表 ----------
        // 对应 Tab: ResourceQueryTab (存储查询)
        // 输出字段: Name, CapacityGB, FreeSpaceGB, UsedPercent, Type, Id
        public static string QueryScriptDatastoreList()
        {
            return @"
$ErrorActionPreference = 'Stop';
try {
    Get-View -ViewType Datastore -Property Name,Summary.Capacity,Summary.FreeSpace,Summary.Type -ErrorAction Stop | ForEach-Object {
        $capGB = [math]::Round($_.Summary.Capacity / 1GB, 2);
        $freeGB = [math]::Round($_.Summary.FreeSpace / 1GB, 2);
        $usedPct = if ($_.Summary.Capacity -gt 0) { [math]::Round(($_.Summary.Capacity - $_.Summary.FreeSpace) / $_.Summary.Capacity * 100, 1) } else { 0 };
        [PSCustomObject]@{
            Id           = $_.MoRef.Value;
            Name         = $_.Name;
            CapacityGB   = $capGB;
            FreeSpaceGB  = $freeGB;
            UsedPercent  = $usedPct;
            Type         = $_.Summary.Type;
        }
    }
} catch {
    Write-Output ""[ERROR] 查询失败: $($_.Exception.Message)"";
}
";
        }

        // ---------- 网络端口组列表（合并标准 + 分布式） ----------
        // 对应 Tab: ResourceQueryTab (网络查询)
        // 输出字段: Name, PortGroupType, VDSwitch, VLAN, NumPorts, NumPortsAvailable, Id
        public static string QueryScriptNetworkList()
        {
            return @"
$ErrorActionPreference = 'Stop';
try {
    $dvsMap = @{};
    Get-View -ViewType DistributedVirtualSwitch -Property Name,Portgroup -ErrorAction SilentlyContinue | ForEach-Object {
        foreach ($pg in $_.Portgroup) { $dvsMap[$pg.Value] = $_.Name }
    };
    Get-View -ViewType DistributedVirtualPortgroup -ErrorAction Stop | ForEach-Object {
        $vlan = '--';
        try {
            if ($_.Config.DefaultPortConfig -and $_.Config.DefaultPortConfig.Vlan) {
                $vlanObj = $_.Config.DefaultPortConfig.Vlan;
                if ($vlanObj -is [VMware.Vim.VmwareDistributedVirtualSwitchVlanIdSpec]) { $vlan = $vlanObj.VlanId }
                elseif ($vlanObj -is [VMware.Vim.VmwareDistributedVirtualSwitchTrunkVlanSpec]) { $vlan = 'Trunk' }
                elseif ($vlanObj -is [VMware.Vim.VmwareDistributedVirtualSwitchPvlanSpec]) { $vlan = 'PVLAN-' + $vlanObj.PvlanId }
            }
        } catch {};
        $dvsName = '--';
        try {
            if ($_.Key -and $dvsMap.ContainsKey($_.Key)) { $dvsName = $dvsMap[$_.Key] }
        } catch {};
        $numPorts = '--';
        try { if ($_.Config.NumPorts) { $numPorts = $_.Config.NumPorts } } catch {};
        [PSCustomObject]@{
            Id                 = $_.MoRef.Value;
            Name               = $_.Name;
            PortGroupType      = '分布式端口组';
            VDSwitch           = $dvsName;
            VLAN               = $vlan;
            NumPorts           = $numPorts;
            NumPortsAvailable  = '--';
        }
    };
    $stdPgMap = @{};
    Get-View -ViewType HostSystem -Property Name,Config.Network.Portgroup -ErrorAction SilentlyContinue | ForEach-Object {
        foreach ($pg in $_.Config.Network.Portgroup) {
            if (-not $stdPgMap.ContainsKey($pg.Spec.Name)) {
                $stdPgMap[$pg.Spec.Name] = if ($pg.Spec.VlanId -ne $null -and $pg.Spec.VlanId -ne '') { $pg.Spec.VlanId } else { '--' };
            }
        }
    };
    Get-View -ViewType Network -ErrorAction Stop | Where-Object { $_.MoRef.Type -eq 'Network' } | ForEach-Object {
        $vlan = if ($stdPgMap.ContainsKey($_.Name)) { $stdPgMap[$_.Name] } else { '--' };
        [PSCustomObject]@{
            Id                 = $_.MoRef.Value;
            Name               = $_.Name;
            PortGroupType      = '标准端口组';
            VDSwitch           = '--';
            VLAN               = $vlan;
            NumPorts           = '--';
            NumPortsAvailable  = '--'
        }
    }
} catch {
    Write-Output ""[ERROR] 查询失败: $($_.Exception.Message)"";
}
";
        }

        // ---------- 标准端口组列表 ----------
        // 对应 Tab: ResourceQueryTab (网络查询 -> 标准端口组)
        // 输出字段: Name, Type, VLanId, NumPorts, NumPortsAvailable, Id
        public static string QueryScriptNetworkStandardPg()
        {
            return @"
$ErrorActionPreference = 'Stop';
try {
    $stdPgMap = @{};
    Get-View -ViewType HostSystem -Property Name,Config.Network.Portgroup -ErrorAction SilentlyContinue | ForEach-Object {
        foreach ($pg in $_.Config.Network.Portgroup) {
            if (-not $stdPgMap.ContainsKey($pg.Spec.Name)) {
                $stdPgMap[$pg.Spec.Name] = if ($pg.Spec.VlanId -ne $null -and $pg.Spec.VlanId -ne '') { $pg.Spec.VlanId } else { '--' };
            }
        }
    };
    Get-View -ViewType Network -ErrorAction Stop | Where-Object { $_.MoRef.Type -eq 'Network' } | ForEach-Object {
        $vlan = if ($stdPgMap.ContainsKey($_.Name)) { $stdPgMap[$_.Name] } else { '--' };
        [PSCustomObject]@{
            Id                = $_.MoRef.Value;
            Name              = $_.Name;
            Type              = '标准端口组';
            VLanId            = $vlan;
            NumPorts          = '--';
            NumPortsAvailable = '--'
        }
    }
} catch {
    Write-Output ""[ERROR] 查询失败: $($_.Exception.Message)"";
}
";
        }

        // ---------- 分布式端口组列表 ----------
        // 对应 Tab: ResourceQueryTab (网络查询 -> 分布式端口组)
        // 输出字段: Name, Type, VLanId, NumPorts, NumPortsAvailable, Id
        public static string QueryScriptNetworkDistributedPg()
        {
            return @"
$ErrorActionPreference = 'Stop';
try {
    Get-View -ViewType DistributedVirtualPortgroup -ErrorAction Stop | ForEach-Object {
        $vlan = '--';
        try {
            if ($_.Config.DefaultPortConfig -and $_.Config.DefaultPortConfig.Vlan) {
                $vlanObj = $_.Config.DefaultPortConfig.Vlan;
                if ($vlanObj -is [VMware.Vim.VmwareDistributedVirtualSwitchVlanIdSpec]) { $vlan = $vlanObj.VlanId }
                elseif ($vlanObj -is [VMware.Vim.VmwareDistributedVirtualSwitchTrunkVlanSpec]) { $vlan = 'Trunk' }
                elseif ($vlanObj -is [VMware.Vim.VmwareDistributedVirtualSwitchPvlanSpec]) { $vlan = 'PVLAN-' + $vlanObj.PvlanId }
            }
        } catch {};
        [PSCustomObject]@{
            Id                = $_.MoRef.Value;
            Name              = $_.Name;
            Type              = '分布式端口组';
            VLanId            = $vlan;
            NumPorts          = if ($_.Config.NumPorts) { $_.Config.NumPorts } else { '--' };
            NumPortsAvailable = '--'
        }
    }
} catch {
    Write-Output ""[ERROR] 查询失败: $($_.Exception.Message)"";
}
";
        }

        // ---------- 分布式交换机列表 ----------
        // 对应 Tab: ResourceQueryTab (网络查询 -> 分布式交换机)
        // 输出字段: Name, Type, NumPorts, NumPortsAvailable, Id
        public static string QueryScriptNetworkVdswitch()
        {
            return @"
$ErrorActionPreference = 'Stop';
try {
    Get-View -ViewType DistributedVirtualSwitch -Property Name,NumPorts -ErrorAction Stop | ForEach-Object {
        [PSCustomObject]@{
            Id                = $_.MoRef.Value;
            Name              = $_.Name;
            Type              = '分布式交换机';
            NumPorts          = if ($_.NumPorts) { $_.NumPorts } else { '--' };
            NumPortsAvailable = '--'
        }
    }
} catch {
    Write-Output ""[ERROR] 查询失败: $($_.Exception.Message)"";
}
";
        }

        // ---------- 模板列表 ----------
        // 对应 Tab: ResourceQueryTab (模板查询)
        // 输出字段: Name, MemoryGB, NumCpu, Id
        public static string QueryScriptTemplateList()
        {
            return @"
$ErrorActionPreference = 'Stop';
try {
    Get-View -ViewType VirtualMachine -Property Name,Config.Hardware,Config.Template -ErrorAction Stop | Where-Object { $_.Config.Template -eq $true } | ForEach-Object {
        [PSCustomObject]@{
            Id        = $_.MoRef.Value;
            Name      = $_.Name;
            MemoryGB  = [math]::Round($_.Config.Hardware.MemoryMB / 1024, 2);
            NumCpu    = $_.Config.Hardware.NumCPU
        }
    }
} catch {
    Write-Output ""[ERROR] 查询失败: $($_.Exception.Message)"";
}
";
        }

        // ---------- IP 列表 ----------
        // 对应 Tab: ResourceQueryTab (IP 查询)
        // 输出字段: Name, IPAddress
        public static string QueryScriptGuestIpList()
        {
            return @"
$ErrorActionPreference = 'Stop';
try {
    Get-View -ViewType VirtualMachine -Property Name,Guest.IpAddress,Config.Template -ErrorAction Stop | Where-Object { -not $_.Config.Template -and $_.Guest.IpAddress } | ForEach-Object {
        $ips = $_.Guest.IpAddress | Where-Object { $_ -match '^\d{1,3}(\.\d{1,3}){3}$' };
        [PSCustomObject]@{
            Name      = $_.Name;
            IPAddress = ($ips -join ',')
        }
    }
} catch {
    Write-Output ""[ERROR] 查询失败: $($_.Exception.Message)"";
}
";
        }

    }
}