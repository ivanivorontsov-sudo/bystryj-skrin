using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace BystryjSkrin
{
    internal sealed class CaptureException : Exception
    {
        public CaptureException(string message, Exception inner = null) : base(message, inner) { }
    }

    internal static class ScreenCapture
    {
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
        [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr h);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool GetUserObjectInformation(IntPtr h, int index, StringBuilder info, int len, out int needed);

        public const string SecureDesktopMessage =
            "Сейчас открыт защищённый экран Windows (блокировка, Ctrl+Alt+Del или запрос администратора UAC) — его снимать нельзя. Повторите после него.";

        /// <summary>true, если сейчас активен защищённый рабочий стол (Winlogon / UAC) или экран заблокирован.</summary>
        public static bool IsSecureDesktopActive()
        {
            IntPtr h = IntPtr.Zero;
            try
            {
                h = OpenInputDesktop(0, false, 0x0100 /*DESKTOP_SWITCHDESKTOP*/);
                if (h == IntPtr.Zero) return true;
                var sb = new StringBuilder(256);
                int needed;
                if (GetUserObjectInformation(h, 2 /*UOI_NAME*/, sb, sb.Capacity * 2, out needed))
                    return !string.Equals(sb.ToString(), "Default", StringComparison.OrdinalIgnoreCase);
                return false;
            }
            catch { return false; }
            finally { if (h != IntPtr.Zero) CloseDesktop(h); }
        }

        /// <summary>Снимок всего виртуального экрана (все мониторы, физические пиксели).</summary>
        public static Bitmap CaptureVirtualScreen(out Rectangle bounds)
        {
            bounds = Native.VirtualScreen();
            if (bounds.Width <= 0 || bounds.Height <= 0)
                throw new CaptureException("Не удалось определить размер экрана.");
            if (IsSecureDesktopActive()) throw new CaptureException(SecureDesktopMessage);

            Exception last = null;
            foreach (int rop in new[] { Native.SRCCOPY | Native.CAPTUREBLT, Native.SRCCOPY })
            {
                try
                {
                    Bitmap b = GdiCapture(bounds, rop);
                    if (b != null) return b;
                }
                catch (OutOfMemoryException ex) { last = ex; break; }
                catch (Exception ex) { last = ex; Log.Error("BitBlt", ex); }
            }
            try
            {
                var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
                using (var g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
                return bmp;
            }
            catch (Exception ex) { last = ex; Log.Error("CopyFromScreen", ex); }

            if (IsSecureDesktopActive()) throw new CaptureException(SecureDesktopMessage, last);
            if (last is OutOfMemoryException)
                throw new CaptureException("Не хватило памяти для снимка всех мониторов. Закройте тяжёлые программы и повторите.", last);
            throw new CaptureException("Windows не дала снять экран: " + ErrorText.Friendly(last), last);
        }

        private static Bitmap GdiCapture(Rectangle b, int rop)
        {
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "GetDC");
            IntPtr mem = IntPtr.Zero, hbmp = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                mem = Native.CreateCompatibleDC(screen);
                hbmp = Native.CreateCompatibleBitmap(screen, b.Width, b.Height);
                if (mem == IntPtr.Zero || hbmp == IntPtr.Zero) throw new OutOfMemoryException("CreateCompatibleBitmap");
                old = Native.SelectObject(mem, hbmp);
                if (!Native.BitBlt(mem, 0, 0, b.Width, b.Height, screen, b.X, b.Y, rop))
                {
                    Log.Write("BitBlt вернул ошибку " + Marshal.GetLastWin32Error() + " (rop=" + rop.ToString("X") + ")");
                    return null;
                }
                Native.SelectObject(mem, old); old = IntPtr.Zero;
                return Image.FromHbitmap(hbmp);
            }
            finally
            {
                if (old != IntPtr.Zero) Native.SelectObject(mem, old);
                if (hbmp != IntPtr.Zero) Native.DeleteObject(hbmp);
                if (mem != IntPtr.Zero) Native.DeleteDC(mem);
                Native.ReleaseDC(IntPtr.Zero, screen);
            }
        }

        /// <summary>Вырезает область из снимка (координаты снимка). Результат — 24 бита без прозрачности.</summary>
        public static Bitmap Crop(Bitmap shot, Rectangle r)
        {
            Rectangle c = RectMath.Clamp(r, new Rectangle(0, 0, shot.Width, shot.Height));
            if (c.IsEmpty) throw new ArgumentException("Область вне экрана");
            try { return shot.Clone(c, PixelFormat.Format24bppRgb); }
            catch (Exception ex) { Log.Error("Clone", ex); }
            var res = new Bitmap(c.Width, c.Height, PixelFormat.Format24bppRgb);
            try
            {
                using (var g = Graphics.FromImage(res))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(shot, new Rectangle(0, 0, c.Width, c.Height), c, GraphicsUnit.Pixel);
                }
                return res;
            }
            catch { res.Dispose(); throw; }
        }
    }
}
