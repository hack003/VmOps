// ============================================================================
// [UTF-8 无 BOM] DpiUtils.cs - DPI 缩放工具
// ============================================================================
//
// DPI 编码规范（全项目遵循）
// ──────────────────────────────────────────────────────────────────────────
//
// 【双层缩放机制】
//   第一层：WinForms 原生 → AutoScaleMode.Dpi 自动处理控件大小/字体
//   第二层：DpiUtils.Px() → 补充处理 WinForms 覆盖不到的手工布局值
//
// 【EnableDpiAware()】
//   · 必须在 Program.Main() 中、第一个 Form 创建之前调用
//   · 按系统版本自动选择 PerMonitorV2 / PerMonitor / System DPI 感知
//   · 只调一次，进程级设置，无需后续重复调用
//
// 【Px(int value96) 合法使用场景】← 只允许以下 5 类
//   1. TableLayoutPanel 的 RowStyle / ColumnStyle 的 Absolute 值
//        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiUtils.Px(44)));
//   2. Padding / Margin 的固定像素值
//        Padding = new Padding(DpiUtils.Px(18), DpiUtils.Px(14), ...);
//   3. 窗体 / 控件的固定 Size / Location（手工布局的弹窗、对话框）
//        ClientSize = new Size(DpiUtils.Px(540), DpiUtils.Px(440));
//   4. ListView / DataGridView 的列宽（固定宽度列）
//        listView.Columns[col].Width = DpiUtils.Px(presetWidth);
//   5. 分割线 / 边框 / 工具条的固定高度
//        Height = DpiUtils.Px(28);
//
// 【Px() 禁止使用场景】
//   · Label / TextBox / Button / CheckBox / RadioButton —— AutoScaleMode.Dpi 已自动缩放
//   · PictureBox / ProgressBar / ComboBox 等标准控件 —— 同上
//   · Dock=Fill 或 Dock=Top/Bottom/Left/Right 的控件 —— 由布局引擎决定尺寸
//   · 运行时动态计算值（MeasureString 结果、用户输入、数据库值等）
//        ❌ DpiUtils.Px(graphics.MeasureString(text, font).Width)
//        ❌ DpiUtils.Px(userInputSize)
//     允许：硬编码常量、const 字段、static readonly 字典/数组取值
//        ✅ DpiUtils.Px(44)
//        ✅ DpiUtils.Px(presetWidth)   // presetWidth 来自 static readonly Dictionary
//
// 【96DPI 基准约定】
//   · Px() 的入参含义是 "96DPI（100% 缩放）下的像素值"
//   · 所有源码中的尺寸数字，写的就是 96DPI 下的视觉大小
//   · 例如 Px(100) 在 150% 缩放屏上返回 156，在 125% 屏上返回 125
//
// 【实现说明】
//   · GetScaleRatio() 首次调用时读取 GDI+ LOGPIXELSX 并缓存
//   · 多显示器场景：按主显示器 DPI 统一缩放，不做 per-monitor 动态切换
//
// ============================================================================
using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace VmOps.Utils
{
    /// <summary>
    /// DPI 工具类 - 负责启用进程级 DPI 感知
    /// 实际控件缩放由 WinForms 系统原生 AutoScaleMode.Dpi 处理
    /// </summary>
    public static class DpiUtils
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(int dpiFlag);

        [DllImport("shcore.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwareness(int awareness);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        private const int LOGPIXELSX = 88;
        private const int DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;
        private const int PROCESS_PER_MONITOR_DPI_AWARE = 2;

        private static float? _scaleRatio;

        /// <summary>
        /// 获取当前 DPI 相对于 96DPI 的缩放比例（缓存）
        /// </summary>
        private static float GetScaleRatio()
        {
            if (_scaleRatio.HasValue) return _scaleRatio.Value;

            using (var g = Graphics.FromHwnd(IntPtr.Zero))
            {
                var hdc = g.GetHdc();
                try
                {
                    int dpi = GetDeviceCaps(hdc, LOGPIXELSX);
                    _scaleRatio = dpi / 96f;
                }
                finally
                {
                    g.ReleaseHdc(hdc);
                }
            }
            return _scaleRatio.Value;
        }

        /// <summary>
        /// 将 96DPI 下的像素值转换为当前 DPI 下的像素值
        /// 仅允许传入硬编码常量，禁止运行时变量
        /// </summary>
        public static int Px(int value96)
        {
            return (int)Math.Round(value96 * GetScaleRatio());
        }

        /// <summary>
        /// 启用进程级 DPI 感知（PerMonitorV2）
        /// 必须在第一个窗口创建之前调用
        /// </summary>
        public static void EnableDpiAware()
        {
            try
            {
                // Windows 10 1703+ (10.0.15063): 使用 PerMonitorV2
                if (Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 15063)
                {
                    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
                }
                // Windows 8.1+: 使用 PerMonitor DPI 感知
                else if (Environment.OSVersion.Version.Major >= 6 && Environment.OSVersion.Version.Minor >= 3)
                {
                    SetProcessDpiAwareness(PROCESS_PER_MONITOR_DPI_AWARE);
                }
                // Windows 7/8: 使用系统 DPI 感知
                else
                {
                    SetProcessDPIAware();
                }
            }
            catch
            {
                // 降级处理：尝试最基本的 DPI 感知
                try
                {
                    SetProcessDPIAware();
                }
                catch { }
            }
        }
    }
}