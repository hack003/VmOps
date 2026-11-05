// ============================================================================
// DpiUtils.cs - DPI 缩放工具（硬编码尺寸适配）
// ============================================================================
// 职责：96DPI 基准值 → 按当前屏幕 DPI 换算实际像素
// 核心方法：
//   Px(value96) → 96DPI 下写的尺寸值 → 返回当前 DPI 下的实际像素
//   EnableDpiAware() → 程序入口设置 DPI 感知
//
// 约束（Rule 14）：
//   - 仅用于状态栏手工布局、分割线、ListView 列宽、TableLayoutPanel 行列尺寸
//   - Label/TextBox/Button/Panel 等普通控件禁止调用
//   - 传入参数必须是硬编码常量，禁止运行时变量
// ============================================================================
using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace VMwareOps.Utils
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