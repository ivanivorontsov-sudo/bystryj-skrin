using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace BystryjSkrin
{
    /// <summary>Полноэкранное «замороженное» затемнённое окно поверх всех мониторов для выделения области.
    /// Координаты окна = координаты снимка (физические пиксели, начало — левый верхний угол виртуального экрана).</summary>
    internal sealed class OverlayForm : Form
    {
        public const int MinSize = 5;

        private readonly Rectangle virt;
        private readonly bool guides;
        private readonly Screen[] screens;
        private IntPtr hShot, hDim, dcShot, dcDim, oldShot, oldDim;

        private bool dragging;
        private Point start, cursor;
        private Rectangle selection = Rectangle.Empty;
        private readonly List<Rectangle> painted = new List<Rectangle>();
        private string flash;
        private readonly Timer flashTimer = new Timer { Interval = 2200 };
        private Font font, fontBold;
        private int fontDpi;
        private bool finished, cancelPending;

        public event Action<Rectangle> RegionSelected;
        public event Action Cancelled;

        public OverlayForm(Bitmap shot, Rectangle virtualScreen, bool showGuides)
        {
            virt = virtualScreen;
            guides = showGuides;
            screens = Screen.AllScreens;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;
            Text = "Быстрый скрин — выделите область";
            Cursor = Cursors.Cross;
            BackColor = Color.Black;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
            Bounds = virt;

            using (var dim = new Bitmap(shot.Width, shot.Height, PixelFormat.Format32bppRgb))
            {
                using (var g = Graphics.FromImage(dim))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.DrawImage(shot, new Rectangle(0, 0, shot.Width, shot.Height), 0, 0, shot.Width, shot.Height, GraphicsUnit.Pixel);
                    g.CompositingMode = CompositingMode.SourceOver;
                    using (var b = new SolidBrush(Color.FromArgb(115, 0, 0, 0))) g.FillRectangle(b, 0, 0, shot.Width, shot.Height);
                }
                hDim = dim.GetHbitmap();
            }
            hShot = shot.GetHbitmap();
            dcShot = Native.CreateCompatibleDC(IntPtr.Zero);
            dcDim = Native.CreateCompatibleDC(IntPtr.Zero);
            oldShot = Native.SelectObject(dcShot, hShot);
            oldDim = Native.SelectObject(dcDim, hDim);

            flashTimer.Tick += (s, e) => { flashTimer.Stop(); flash = null; UpdateDecorations(); };
            cursor = ToClient(Control.MousePosition);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00000080 /*WS_EX_TOOLWINDOW*/ | 0x00000008 /*WS_EX_TOPMOST*/;
                return cp;
            }
        }

        private Point ToClient(Point screenPt) { return new Point(screenPt.X - virt.X, screenPt.Y - virt.Y); }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Точно растягиваем на весь виртуальный экран в физических пикселях (мониторы с разным DPI)
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, virt.X, virt.Y, virt.Width, virt.Height, Native.SWP_SHOWWINDOW);
            Activate();
            Native.SetForegroundWindow(Handle);
            Focus();
            cursor = ToClient(Control.MousePosition);
            Invalidate();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_DPICHANGED) { m.Result = IntPtr.Zero; return; } // размер не меняем
            base.WndProc(ref m);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        private void EnsureFont()
        {
            int dpi = Native.DpiAt(new Point(cursor.X + virt.X, cursor.Y + virt.Y));
            if (font != null && dpi == fontDpi) return;
            if (font != null) font.Dispose();
            if (fontBold != null) fontBold.Dispose();
            fontDpi = dpi;
            font = new Font("Segoe UI", RectMath.ScaleForDpi(14, dpi), FontStyle.Regular, GraphicsUnit.Pixel);
            fontBold = new Font("Segoe UI", RectMath.ScaleForDpi(14, dpi), FontStyle.Bold, GraphicsUnit.Pixel);
        }

        private int S(int v) { return RectMath.ScaleForDpi(v, fontDpi == 0 ? 96 : fontDpi); }

        private Rectangle CurrentScreen()
        {
            Point p = new Point(cursor.X + virt.X, cursor.Y + virt.Y);
            foreach (var s in screens)
                if (s.Bounds.Contains(p)) return new Rectangle(s.Bounds.X - virt.X, s.Bounds.Y - virt.Y, s.Bounds.Width, s.Bounds.Height);
            var b = Screen.FromPoint(p).Bounds;
            return new Rectangle(b.X - virt.X, b.Y - virt.Y, b.Width, b.Height);
        }

        // ---------- что и где рисуется ----------

        private Rectangle LabelRect(out string text)
        {
            text = RectMath.SizeLabel(selection);
            Size sz = TextRenderer.MeasureText(text, fontBold);
            sz = new Size(sz.Width + S(12), sz.Height + S(6));
            return RectMath.LabelPlacement(selection, sz, CurrentScreen(), S(6));
        }

        private Rectangle HintRect(out string text, out bool warning)
        {
            warning = flash != null;
            text = flash ?? "Выделите область мышью   •   Esc или правая кнопка — отмена";
            Size sz = TextRenderer.MeasureText(text, font);
            sz = new Size(sz.Width + S(28), sz.Height + S(16));
            Rectangle scr = CurrentScreen();
            return new Rectangle(scr.X + (scr.Width - sz.Width) / 2, scr.Y + S(24), sz.Width, sz.Height);
        }

        private List<Rectangle> DecorationRects()
        {
            EnsureFont();
            var list = new List<Rectangle>();
            if (!selection.IsEmpty)
            {
                Rectangle r = selection; r.Inflate(3, 3); list.Add(r);
                string t; Rectangle lr = LabelRect(out t); lr.Inflate(2, 2); list.Add(lr);
            }
            if (!dragging && guides)
            {
                list.Add(new Rectangle(0, cursor.Y - 1, ClientSize.Width, 3));
                list.Add(new Rectangle(cursor.X - 1, 0, 3, ClientSize.Height));
            }
            if (!dragging || flash != null)
            {
                string t; bool w; Rectangle hr = HintRect(out t, out w); hr.Inflate(2, 2); list.Add(hr);
            }
            return list;
        }

        private void UpdateDecorations()
        {
            foreach (var r in painted) Invalidate(r);
            painted.Clear();
            painted.AddRange(DecorationRects());
            foreach (var r in painted) Invalidate(r);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Rectangle clip = Rectangle.Intersect(e.ClipRectangle, ClientRectangle);
            if (clip.Width <= 0 || clip.Height <= 0) return;
            EnsureFont();
            IntPtr target = e.Graphics.GetHdc();
            IntPtr mem = IntPtr.Zero, bmp = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                mem = Native.CreateCompatibleDC(target);
                bmp = Native.CreateCompatibleBitmap(target, clip.Width, clip.Height);
                old = Native.SelectObject(mem, bmp);
                Native.BitBlt(mem, 0, 0, clip.Width, clip.Height, dcDim, clip.X, clip.Y, Native.SRCCOPY);
                if (!selection.IsEmpty)
                {
                    Rectangle s = Rectangle.Intersect(selection, clip);
                    if (s.Width > 0 && s.Height > 0)
                        Native.BitBlt(mem, s.X - clip.X, s.Y - clip.Y, s.Width, s.Height, dcShot, s.X, s.Y, Native.SRCCOPY);
                }
                using (var g = Graphics.FromHdc(mem))
                {
                    g.TranslateTransform(-clip.X, -clip.Y);
                    DrawDecorations(g);
                }
                Native.BitBlt(target, clip.X, clip.Y, clip.Width, clip.Height, mem, 0, 0, Native.SRCCOPY);
            }
            finally
            {
                if (old != IntPtr.Zero) Native.SelectObject(mem, old);
                if (bmp != IntPtr.Zero) Native.DeleteObject(bmp);
                if (mem != IntPtr.Zero) Native.DeleteDC(mem);
                e.Graphics.ReleaseHdc(target);
            }
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private void DrawDecorations(Graphics g)
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            if (!dragging && guides)
            {
                using (var pen = new Pen(Color.FromArgb(150, 255, 255, 255), 1))
                {
                    g.DrawLine(pen, 0, cursor.Y, ClientSize.Width, cursor.Y);
                    g.DrawLine(pen, cursor.X, 0, cursor.X, ClientSize.Height);
                }
            }
            if (!selection.IsEmpty)
            {
                using (var pen = new Pen(Color.FromArgb(255, 26, 140, 255), 1))
                    g.DrawRectangle(pen, selection.X - 1, selection.Y - 1, selection.Width + 1, selection.Height + 1);
                using (var pen = new Pen(Color.FromArgb(200, 255, 255, 255), 1) { DashStyle = DashStyle.Dash })
                    g.DrawRectangle(pen, selection.X - 2, selection.Y - 2, selection.Width + 3, selection.Height + 3);
                string t; Rectangle lr = LabelRect(out t);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Rounded(lr, S(4)))
                using (var b = new SolidBrush(Color.FromArgb(220, 20, 20, 20))) g.FillPath(b, path);
                g.SmoothingMode = SmoothingMode.None;
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(t, fontBold, Brushes.White, lr, sf);
            }
            if (!dragging || flash != null)
            {
                string t; bool warn; Rectangle hr = HintRect(out t, out warn);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Rounded(hr, S(8)))
                using (var b = new SolidBrush(warn ? Color.FromArgb(235, 150, 60, 0) : Color.FromArgb(215, 25, 25, 25))) g.FillPath(b, path);
                g.SmoothingMode = SmoothingMode.None;
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(t, font, Brushes.White, hr, sf);
            }
        }

        // ---------- ввод ----------

        private Point ClampToClient(Point p)
        {
            return new Point(Math.Max(0, Math.Min(ClientSize.Width - 1, p.X)), Math.Max(0, Math.Min(ClientSize.Height - 1, p.Y)));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            // Отмена правой/средней кнопкой — по отпусканию, чтобы отпускание не открыло контекстное меню рабочего стола
            if (e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle) { cancelPending = true; return; }
            if (e.Button != MouseButtons.Left) return;
            dragging = true;
            if (flash != null) { flash = null; flashTimer.Stop(); }
            start = ClampToClient(e.Location);
            cursor = start;
            selection = RectMath.FromPoints(start, start);
            Capture = true;
            UpdateDecorations();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            cursor = ClampToClient(e.Location);
            if (dragging) selection = RectMath.FromPoints(start, cursor);
            UpdateDecorations();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if ((e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle) && cancelPending) { Cancel(); return; }
            if (e.Button != MouseButtons.Left || !dragging) return;
            dragging = false;
            Capture = false;
            Rectangle r = RectMath.Clamp(selection, ClientRectangle);
            if (RectMath.IsTooSmall(r, MinSize))
            {
                // Случайный щелчок или крошечное выделение — не сохраняем, даём выделить заново
                selection = Rectangle.Empty;
                ShowFlash("Слишком маленькая область — выделите ещё раз (нужно хотя бы " + MinSize + "×" + MinSize + " пикселей)");
                return;
            }
            Finish(r);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (dragging && !Capture)
            {
                // Выделение прервано (например, Alt+Tab) — начинаем заново
                dragging = false;
                selection = Rectangle.Empty;
                UpdateDecorations();
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Cancel(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ShowFlash(string text)
        {
            flash = text;
            flashTimer.Stop();
            flashTimer.Start();
            UpdateDecorations();
        }

        /// <summary>Только для автотестов: имитирует выделение мышью (координаты окна/снимка).</summary>
        internal void SimulateDrag(Point a, Point b)
        {
            OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, a.X, a.Y, 0));
            OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, b.X, b.Y, 0));
            OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, b.X, b.Y, 0));
        }

        internal bool IsFinished { get { return finished; } }

        public void Cancel()
        {
            if (finished) return;
            finished = true;
            Hide();
            var h = Cancelled; if (h != null) h();
            Close();
        }

        private void Finish(Rectangle r)
        {
            if (finished) return;
            finished = true;
            Hide();
            var h = RegionSelected;
            try { if (h != null) h(r); }
            finally { Close(); }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (!finished) { finished = true; var h = Cancelled; if (h != null) h(); }
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (dcShot != IntPtr.Zero) { Native.SelectObject(dcShot, oldShot); Native.DeleteDC(dcShot); dcShot = IntPtr.Zero; }
            if (dcDim != IntPtr.Zero) { Native.SelectObject(dcDim, oldDim); Native.DeleteDC(dcDim); dcDim = IntPtr.Zero; }
            if (hShot != IntPtr.Zero) { Native.DeleteObject(hShot); hShot = IntPtr.Zero; }
            if (hDim != IntPtr.Zero) { Native.DeleteObject(hDim); hDim = IntPtr.Zero; }
            if (disposing)
            {
                flashTimer.Dispose();
                if (font != null) font.Dispose();
                if (fontBold != null) fontBold.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
