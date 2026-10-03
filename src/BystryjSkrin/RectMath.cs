using System;
using System.Drawing;
using System.Globalization;

namespace BystryjSkrin
{
    internal static class RectMath
    {
        /// <summary>Прямоугольник по двум углам в любом порядке (тянуть можно в любую сторону).
        /// Конечная точка включается: от (0,0) до (9,9) — это 10×10.</summary>
        public static Rectangle FromPoints(Point a, Point b)
        {
            int x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y);
            return new Rectangle(x, y, Math.Abs(a.X - b.X) + 1, Math.Abs(a.Y - b.Y) + 1);
        }

        /// <summary>Нормализует прямоугольник с отрицательной шириной/высотой.</summary>
        public static Rectangle Normalize(Rectangle r)
        {
            int x = r.Width < 0 ? r.X + r.Width : r.X;
            int y = r.Height < 0 ? r.Y + r.Height : r.Y;
            return new Rectangle(x, y, Math.Abs(r.Width), Math.Abs(r.Height));
        }

        public static Rectangle Clamp(Rectangle r, Rectangle bounds)
        {
            Rectangle n = Normalize(r);
            n.Intersect(bounds);
            return n.Width <= 0 || n.Height <= 0 ? Rectangle.Empty : n;
        }

        /// <summary>Экранные (физические, могут быть отрицательными) координаты → координаты снимка всего виртуального экрана.</summary>
        public static Rectangle ScreenToSnapshot(Rectangle screenRect, Rectangle virtualScreen)
        {
            return new Rectangle(screenRect.X - virtualScreen.X, screenRect.Y - virtualScreen.Y, screenRect.Width, screenRect.Height);
        }

        public static Rectangle SnapshotToScreen(Rectangle snapRect, Rectangle virtualScreen)
        {
            return new Rectangle(snapRect.X + virtualScreen.X, snapRect.Y + virtualScreen.Y, snapRect.Width, snapRect.Height);
        }

        /// <summary>Масштабирование размеров интерфейса под DPI монитора (96 = 100%).</summary>
        public static int ScaleForDpi(int value96, int dpi)
        {
            if (dpi <= 0) dpi = 96;
            return (int)Math.Round(value96 * dpi / 96.0, MidpointRounding.AwayFromZero);
        }

        /// <summary>Логические (DIP) координаты → физические пиксели.</summary>
        public static Rectangle LogicalToPhysical(Rectangle logical, int dpi)
        {
            return new Rectangle(ScaleForDpi(logical.X, dpi), ScaleForDpi(logical.Y, dpi),
                                 ScaleForDpi(logical.Width, dpi), ScaleForDpi(logical.Height, dpi));
        }

        /// <summary>Случайный щелчок или слишком маленькое выделение (меньше min×min) — не сохраняем.</summary>
        public static bool IsTooSmall(Rectangle r, int min)
        {
            return r.IsEmpty || r.Width < min || r.Height < min;
        }

        public static string SizeLabel(Rectangle r)
        {
            return r.Width.ToString(CultureInfo.InvariantCulture) + " × " + r.Height.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Разбор «x,y,w,h» для --capture-rect.</summary>
        public static bool TryParse(string s, out Rectangle r)
        {
            r = Rectangle.Empty;
            if (string.IsNullOrWhiteSpace(s)) return false;
            string[] p = s.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length != 4) return false;
            int[] v = new int[4];
            for (int i = 0; i < 4; i++)
                if (!int.TryParse(p[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v[i])) return false;
            if (v[2] <= 0 || v[3] <= 0) return false;
            r = new Rectangle(v[0], v[1], v[2], v[3]);
            return true;
        }

        /// <summary>Где нарисовать подпись с размером: под выделением справа, а если не влезает — внутри/сверху.</summary>
        public static Rectangle LabelPlacement(Rectangle selection, Size label, Rectangle screen, int gap)
        {
            int x = selection.Right - label.Width;
            int y = selection.Bottom + gap;
            if (y + label.Height > screen.Bottom) y = selection.Top - gap - label.Height;
            if (y < screen.Top) y = selection.Bottom - gap - label.Height;
            if (x < screen.Left) x = screen.Left + gap;
            if (x + label.Width > screen.Right) x = screen.Right - label.Width - gap;
            if (y < screen.Top) y = screen.Top + gap;
            return new Rectangle(x, y, label.Width, label.Height);
        }
    }
}
