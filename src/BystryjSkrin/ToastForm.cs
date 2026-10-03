using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BystryjSkrin
{
    internal enum ToastKind { Info, Warning, Error }

    internal sealed class ToastAction
    {
        public readonly string Text;
        public readonly Action Run;
        public ToastAction(string text, Action run) { Text = text; Run = run; }
    }

    /// <summary>Своё небольшое уведомление в углу экрана с кнопками («Открыть», «Папка», «Удалить»).
    /// Не забирает фокус, само исчезает, при наведении мыши ждёт.</summary>
    internal sealed class ToastForm : Form
    {
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private readonly string title, message;
        private readonly ToastKind kind;
        private readonly List<ToastAction> actions;
        private readonly Action defaultAction;
        private readonly Bitmap thumb;
        private readonly Timer timer = new Timer();
        private readonly int dpi;
        private readonly Font titleFont, textFont, buttonFont;
        private readonly List<Rectangle> buttonRects = new List<Rectangle>();
        private Rectangle closeRect, thumbRect, titleRect, textRect;
        private int hover = -2; // -1 = кнопка закрытия
        private readonly int timeoutMs;

        public ToastForm(string title, string message, ToastKind kind, Image thumbnail, IList<ToastAction> actions, Action defaultAction, int timeoutMs)
        {
            this.title = title; this.message = message; this.kind = kind;
            this.actions = new List<ToastAction>(actions ?? new ToastAction[0]);
            this.defaultAction = defaultAction;
            this.timeoutMs = timeoutMs;

            Point anchor = Control.MousePosition;
            Screen scr = Screen.FromPoint(anchor);
            dpi = Native.DpiAt(new Point(scr.WorkingArea.Right - 10, scr.WorkingArea.Bottom - 10));

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(32, 32, 32);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Text = "Быстрый скрин";

            titleFont = new Font("Segoe UI Semibold", S(15), FontStyle.Regular, GraphicsUnit.Pixel);
            textFont = new Font("Segoe UI", S(13), FontStyle.Regular, GraphicsUnit.Pixel);
            buttonFont = new Font("Segoe UI", S(13), FontStyle.Regular, GraphicsUnit.Pixel);

            if (thumbnail != null)
            {
                try
                {
                    int max = S(72);
                    double k = Math.Min((double)max / thumbnail.Width, (double)max / thumbnail.Height);
                    k = Math.Min(k, 1.0);
                    int w = Math.Max(1, (int)(thumbnail.Width * k)), h = Math.Max(1, (int)(thumbnail.Height * k));
                    thumb = new Bitmap(w, h);
                    using (var g = Graphics.FromImage(thumb))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(thumbnail, 0, 0, w, h);
                    }
                }
                catch (Exception ex) { Log.Error("Миниатюра", ex); thumb = null; }
            }

            DoLayout(scr.WorkingArea);
            timer.Interval = timeoutMs;
            timer.Tick += (s, e) => CloseSafe();
        }

        private int S(int v) { return RectMath.ScaleForDpi(v, dpi); }

        private void DoLayout(Rectangle work)
        {
            int width = S(380), pad = S(14);
            int x = pad;
            if (thumb != null) { thumbRect = new Rectangle(pad, pad, S(72), S(72)); x = thumbRect.Right + S(12); }
            int textW = width - x - pad - S(18);
            Size ts = TextRenderer.MeasureText(title, titleFont, new Size(textW, 1000), TextFormatFlags.WordBreak);
            titleRect = new Rectangle(x, pad - S(2), textW, ts.Height);
            Size ms = TextRenderer.MeasureText(message ?? "", textFont, new Size(textW, 1000), TextFormatFlags.WordBreak);
            textRect = new Rectangle(x, titleRect.Bottom + S(2), textW, Math.Min(ms.Height, S(140)));
            int contentBottom = Math.Max(textRect.Bottom, thumb != null ? thumbRect.Bottom : 0);
            closeRect = new Rectangle(width - S(30), S(6), S(24), S(24));

            buttonRects.Clear();
            int bx = pad, by = contentBottom + S(12), bh = S(30);
            foreach (var a in actions)
            {
                int bw = TextRenderer.MeasureText(a.Text, buttonFont).Width + S(22);
                if (bx + bw > width - pad && bx > pad) { bx = pad; by += bh + S(6); }
                buttonRects.Add(new Rectangle(bx, by, bw, bh));
                bx += bw + S(8);
            }
            int height = (actions.Count > 0 ? by + bh : contentBottom) + pad;
            Bounds = new Rectangle(work.Right - width - S(12), work.Bottom - height - S(12), width, height);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00000080 /*TOOLWINDOW*/ | 0x00000008 /*TOPMOST*/ | 0x08000000 /*NOACTIVATE*/;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int round = 2; DwmSetWindowAttribute(Handle, 33 /*DWMWA_WINDOW_CORNER_PREFERENCE*/, ref round, 4); } catch { }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (timeoutMs > 0) timer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            Color accent = kind == ToastKind.Error ? Color.FromArgb(255, 110, 110) : kind == ToastKind.Warning ? Color.FromArgb(255, 190, 60) : Color.FromArgb(90, 170, 255);
            using (var b = new SolidBrush(accent)) g.FillRectangle(b, 0, 0, S(4), Height);
            using (var p = new Pen(Color.FromArgb(70, 70, 70))) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            if (thumb != null)
            {
                int ox = thumbRect.X + (thumbRect.Width - thumb.Width) / 2, oy = thumbRect.Y + (thumbRect.Height - thumb.Height) / 2;
                g.DrawImage(thumb, ox, oy, thumb.Width, thumb.Height);
                using (var p = new Pen(Color.FromArgb(90, 90, 90))) g.DrawRectangle(p, ox - 1, oy - 1, thumb.Width + 1, thumb.Height + 1);
            }
            TextRenderer.DrawText(g, title, titleFont, titleRect, kind == ToastKind.Info ? Color.White : accent, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, message ?? "", textFont, textRect, Color.FromArgb(220, 220, 220), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            using (var hb = new SolidBrush(Color.FromArgb(hover == -1 ? 80 : 0, 255, 255, 255))) g.FillRectangle(hb, closeRect);
            TextRenderer.DrawText(g, "✕", buttonFont, closeRect, Color.FromArgb(200, 200, 200), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            for (int i = 0; i < buttonRects.Count; i++)
            {
                Rectangle r = buttonRects[i];
                using (var b = new SolidBrush(hover == i ? Color.FromArgb(78, 78, 78) : Color.FromArgb(55, 55, 55))) g.FillRectangle(b, r);
                TextRenderer.DrawText(g, actions[i].Text, buttonFont, r, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        private int HitTest(Point p)
        {
            if (closeRect.Contains(p)) return -1;
            for (int i = 0; i < buttonRects.Count; i++) if (buttonRects[i].Contains(p)) return i;
            return -2;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = HitTest(e.Location);
            if (h != hover) { hover = h; Invalidate(); }
            Cursor = h != -2 || defaultAction != null ? Cursors.Hand : Cursors.Default;
            timer.Stop(); // пока мышь над уведомлением — не исчезаем
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = -2; Invalidate();
            if (timeoutMs > 0) { timer.Interval = Math.Max(2500, timeoutMs / 2); timer.Start(); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Right) { CloseSafe(); return; }
            int h = HitTest(e.Location);
            Action run = h >= 0 ? actions[h].Run : h == -1 ? null : defaultAction;
            CloseSafe();
            if (run != null)
            {
                try { run(); }
                catch (Exception ex) { Log.Error("Кнопка уведомления", ex); Dialogs.Error("Не получилось: " + ErrorText.Friendly(ex)); }
            }
        }

        public void CloseSafe()
        {
            try { timer.Stop(); if (!IsDisposed) Close(); } catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                if (thumb != null) thumb.Dispose();
                titleFont.Dispose(); textFont.Dispose(); buttonFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
