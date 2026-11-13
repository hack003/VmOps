// ============================================================================
// LoggerUtils.cs - UI 日志输出工具（RichTextBox 写消息）
// ============================================================================
// 职责：向窗口底部/侧边 RichTextBox 输出彩色分级日志
//   ✅ 分级输出：info / success / warning / error / debug（不同颜色）
//   ✅ 时间戳格式化 GetTimestamp()
//   ✅ 自动滚动到底部 auto_scroll 控制
//   ✅ 递归查找 RichTextBox 控件 GetOutputWidget()
//   ✅ 辅助格式化：MakeStageHeader / MakeVmHeader / MakeVmStep / LogProgress
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace VmOps.Utils
{
    public static class LoggerUtils
    {
        public const string TIMESTAMP_FORMAT = "HH:mm:ss";
        public const int STAGE_HEADER_WIDTH = 60;
        public const char STAGE_HEADER_CHAR = '\u2550';
        public const char VM_HEADER_CHAR = '\u2500';
        public const char PROGRESS_BAR_FILLED = '\u2588';
        public const char PROGRESS_BAR_EMPTY = '\u2591';
        public const string INDENT_STEP = "  ";

        private static readonly object _logLock = new object();

        public static string GetTimestamp()
        {
            return DateTime.Now.ToString(TIMESTAMP_FORMAT);
        }

        public static void WriteToOutput(Control app, string text, string? level = null)
        {
            if (app == null) return;

            var appendFn = GetAppendLogMethod(app);
            if (appendFn != null)
            {
                try
                {
                    appendFn(text, level ?? "info");
                    return;
                }
                catch { }
            }

            app.Invoke(new Action(() =>
            {
                var outputWidget = GetOutputWidget(app);
                if (outputWidget == null) return;

                try
                {
                    bool wasDisabled = false;
                    try
                    {
                        if (outputWidget.ReadOnly)
                        {
                            outputWidget.ReadOnly = false;
                            wasDisabled = true;
                        }
                    }
                    catch { }

                    outputWidget.AppendText(text);

                    if (wasDisabled)
                    {
                        try { outputWidget.ReadOnly = true; } catch { }
                    }

                    outputWidget.SelectionStart = outputWidget.Text.Length;
                    outputWidget.ScrollToCaret();
                }
                catch { }
            }));
        }

        private static Action<string, string>? GetAppendLogMethod(Control app)
        {
            var method = app.GetType().GetMethod("AppendLog");
            if (method != null)
            {
                return (text, level) => method.Invoke(app, new object[] { text, level });
            }
            return null;
        }

        private static RichTextBox? GetOutputWidget(Control app)
        {
            foreach (Control control in GetAllControls(app))
            {
                if (control is RichTextBox rtb && control.Name == "output_text")
                {
                    return rtb;
                }
            }
            return null;
        }

        private static IEnumerable<Control> GetAllControls(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                yield return control;
                foreach (var child in GetAllControls(control))
                {
                    yield return child;
                }
            }
        }

        public static void LogMessage(Control app, string? level, string message)
        {
            string timestamp = GetTimestamp();
            string prefix = level != null ? $"[{level}] " : "";
            string formattedMessage = $"[{timestamp}] {prefix}{message}{Environment.NewLine}";
            WriteToOutput(app, formattedMessage, level);
        }

        public static string MakeStageHeader(string title)
        {
            int padding = (STAGE_HEADER_WIDTH - title.Length - 4) / 2;
            if (padding < 0) padding = 0;
            string line = new string(STAGE_HEADER_CHAR, padding);
            return $"{Environment.NewLine}{line}\u2550\u2550 {title} \u2550\u2550{line}{Environment.NewLine}";
        }

        public static void LogStageHeader(Control app, string title)
        {
            WriteToOutput(app, MakeStageHeader(title));
        }

        public static string MakeVmHeader(string vmName)
        {
            return $"{Environment.NewLine}{new string(VM_HEADER_CHAR, 40)}{Environment.NewLine}  ▶ {vmName}{Environment.NewLine}{new string(VM_HEADER_CHAR, 40)}{Environment.NewLine}";
        }

        public static void LogVmHeader(Control app, string vmName)
        {
            WriteToOutput(app, MakeVmHeader(vmName));
        }

        public static void LogVmStep(Control app, string stepName)
        {
            WriteToOutput(app, $"{INDENT_STEP}● {stepName}{Environment.NewLine}");
        }

        public static void LogVmResult(Control app, string result)
        {
            WriteToOutput(app, $"{INDENT_STEP}✓ {result}{Environment.NewLine}", "success");
        }

        public static void LogProgress(Control app, int current, int total, string message)
        {
            string bar = RenderProgressBar(current, total, 30);
            WriteToOutput(app, $"{bar} [{current}/{total}] {message}{Environment.NewLine}");
        }

        public static string RenderProgressBar(int current, int total, int width)
        {
            if (total <= 0) return "";
            double ratio = (double)current / total;
            int filled = (int)Math.Round(ratio * width);
            int empty = width - filled;
            return new string(PROGRESS_BAR_FILLED, filled) + new string(PROGRESS_BAR_EMPTY, empty);
        }

        public static void LogConnectSuccess(Control app, string? detailLines, string? vcHost = null, string? userDomain = null)
        {
            string msg = "✓ 连接成功";
            if (vcHost != null) msg += $" - {vcHost}";
            if (userDomain != null) msg += $" ({userDomain})";
            msg += Environment.NewLine;
            if (detailLines != null) msg += detailLines + Environment.NewLine;
            WriteToOutput(app, msg, "success");
        }

        public static void LogConnectError(Control app, string friendlyMsg)
        {
            WriteToOutput(app, $"✗ 连接失败: {friendlyMsg}{Environment.NewLine}", "error");
        }

        public static void LogCommandTitle(Control app, string title)
        {
            WriteToOutput(app, $"{Environment.NewLine}═══ {title} ═══{Environment.NewLine}");
        }

        public static void LogPsOutput(Control app, string output)
        {
            if (string.IsNullOrWhiteSpace(output)) return;
            string filtered = FilterPowershellOutput(output);
            WriteToOutput(app, filtered);
        }

        public static string FilterPowershellOutput(string output)
        {
            if (string.IsNullOrEmpty(output)) return "";
            output = Regex.Replace(output, @"\x1B\[[0-9;]*[a-zA-Z]", "");
            output = Regex.Replace(output, @"[\r\n]+", Environment.NewLine);
            return output;
        }

        public static string FormatDuration(double seconds)
        {
            if (seconds < 60)
                return $"{seconds:F1} 秒";
            if (seconds < 3600)
                return $"{(int)(seconds / 60)} 分 {seconds % 60:F1} 秒";
            return $"{(int)(seconds / 3600)} 时 {(int)((seconds % 3600) / 60)} 分 {seconds % 60:F1} 秒";
        }

        public static void LogVmListPreview(Control app, List<string> vmNames, int maxPreview = 10)
        {
            if (vmNames == null || vmNames.Count == 0) return;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"待操作虚拟机列表 (共 {vmNames.Count} 台):");
            int count = Math.Min(vmNames.Count, maxPreview);
            for (int i = 0; i < count; i++)
            {
                sb.AppendLine($"  {i + 1}. {vmNames[i]}");
            }
            if (vmNames.Count > maxPreview)
            {
                sb.AppendLine($"  ... 及其他 {vmNames.Count - maxPreview} 台)");
            }
            WriteToOutput(app, sb.ToString());
        }

        public static void LogSummaryTable(Control app, string title, Dictionary<string, string> data)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"{Environment.NewLine}═══ {title} ═══");
            foreach (var kvp in data)
            {
                sb.AppendLine($"  {kvp.Key}: {kvp.Value}");
            }
            sb.AppendLine();
            WriteToOutput(app, sb.ToString(), "success");
        }

        public static List<Dictionary<string, object>>? ParsePsTable(string outputText)
        {
            if (string.IsNullOrWhiteSpace(outputText)) return null;

            try
            {
                var jsonStart = outputText.IndexOf('[');
                var jsonEnd = outputText.LastIndexOf(']');
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    string jsonStr = outputText.Substring(jsonStart, jsonEnd - jsonStart + 1);
                    var list = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(jsonStr);
                    return list;
                }
            }
            catch { }

            var lines = outputText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Select(l => l.Trim())
                                  .Where(l => !string.IsNullOrEmpty(l))
                                  .ToList();

            if (lines.Count < 2) return null;

            var results = new List<Dictionary<string, object>>();
            foreach (var line in lines.Skip(1))
            {
                var row = new Dictionary<string, object>();
                row["line"] = line;
                results.Add(row);
            }
            return results;
        }
    }
}