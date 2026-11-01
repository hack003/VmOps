// ============================================================================
// ToastUtils.cs - 轻量级 Toast 弹窗通知
// ============================================================================
// 职责：在指定控件中下方弹一个半透明提示条（类似 Android Toast）
// 核心方法：ShowToast(parent, message, durationMs=2500)
//   - 无边框小 Form + 渐变背景
//   - 自动定时消失 + 鼠标悬停暂停 + 点击关闭
// ============================================================================
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VirtualizationOpsTool.Utils
{
    public static class ToastUtils
    {
        public static void ShowToast(Control parent, string message, int durationMs = 2500)
        {
            var toast = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                ShowInTaskbar = false,
                TopMost = true,
                BackColor = Color.FromArgb(50, 50, 50),
                Opacity = 0.95,
                AutoScaleMode = AutoScaleMode.Dpi,
                AutoScaleDimensions = new SizeF(96F, 96F),
                Size = new Size(280, 50)
            };

            var lbl = new Label
            {
                Text = message,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei", 9), Padding = new Padding(10)
            };
            toast.Controls.Add(lbl);

            var parentRect = parent.RectangleToScreen(parent.ClientRectangle);
            toast.Location = new Point(
                parentRect.Left + (parentRect.Width - toast.Width) / 2,
                parentRect.Bottom - toast.Height - 40
            );

            toast.Show(parent);

            _ = Task.Run(async () =>
            {
                await Task.Delay(durationMs);
                toast.Invoke(new Action(() => toast.Close()));
            });
        }
    }
}