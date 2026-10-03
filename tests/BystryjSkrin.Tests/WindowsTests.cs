using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Xunit;

namespace BystryjSkrin.Tests
{
    /// <summary>Тесты, которым нужен настоящий Windows (запускаются в CI на windows-latest).</summary>
    public class WindowsTests
    {
        private static Bitmap Pattern(int w, int h)
        {
            var b = new Bitmap(w, h, PixelFormat.Format32bppRgb);
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) b.SetPixel(x, y, Color.FromArgb(x % 256, y % 256, (x + y) % 256));
            return b;
        }

        [Fact]
        public void Crop_TakesExactPixels()
        {
            using (var shot = Pattern(300, 200))
            using (var c = ScreenCapture.Crop(shot, new Rectangle(10, 20, 50, 40)))
            {
                Assert.Equal(50, c.Width); Assert.Equal(40, c.Height);
                Assert.Equal(PixelFormat.Format24bppRgb, c.PixelFormat);
                Assert.Equal(Color.FromArgb(10, 20, 30).ToArgb(), c.GetPixel(0, 0).ToArgb());
                Assert.Equal(Color.FromArgb(59, 59, 118).ToArgb(), c.GetPixel(49, 39).ToArgb());
            }
        }

        [Fact]
        public void Crop_ClampsToSnapshot_AndRejectsOutside()
        {
            using (var shot = Pattern(100, 100))
            {
                using (var c = ScreenCapture.Crop(shot, new Rectangle(90, 90, 50, 50))) { Assert.Equal(10, c.Width); Assert.Equal(10, c.Height); }
                Assert.Throws<ArgumentException>(() => ScreenCapture.Crop(shot, new Rectangle(500, 500, 10, 10)));
            }
        }

        [Fact]
        public void CaptureVirtualScreen_MatchesVirtualScreenSize()
        {
            Rectangle virt;
            using (var b = ScreenCapture.CaptureVirtualScreen(out virt))
            {
                Assert.Equal(Native.VirtualScreen(), virt);
                Assert.Equal(virt.Width, b.Width);
                Assert.Equal(virt.Height, b.Height);
            }
        }

        [Fact]
        public void Overlay_TinyDragIsIgnored_RealDragSelects()
        {
            Sta.Run(() =>
            {
                using (var shot = Pattern(400, 300))
                {
                    var virt = new Rectangle(-200, -50, 400, 300); // как будто монитор слева/сверху
                    using (var ov = new OverlayForm(shot, virt, true))
                    {
                        Rectangle? got = null;
                        ov.RegionSelected += r => got = r;
                        ov.SimulateDrag(new Point(10, 10), new Point(12, 13)); // случайный щелчок
                        Assert.Null(got);
                        Assert.False(ov.IsFinished);
                        ov.SimulateDrag(new Point(150, 120), new Point(20, 30)); // тянем влево-вверх
                        Assert.Equal(new Rectangle(20, 30, 131, 91), got);
                        Assert.True(ov.IsFinished);
                    }
                }
            });
        }

        [Fact]
        public void Overlay_CancelRaisesCancelled()
        {
            Sta.Run(() =>
            {
                using (var shot = Pattern(100, 100))
                using (var ov = new OverlayForm(shot, new Rectangle(0, 0, 100, 100), false))
                {
                    bool cancelled = false, selected = false;
                    ov.Cancelled += () => cancelled = true;
                    ov.RegionSelected += r => selected = true;
                    ov.Cancel();
                    ov.Cancel(); // повторно — без ошибок
                    Assert.True(cancelled);
                    Assert.False(selected);
                }
            });
        }

        [Fact]
        public void RecycleBin_MovesFileAway()
        {
            using (var d = new TempDir())
            {
                string f = d.Sub("Скриншот тест.png");
                File.WriteAllBytes(f, new byte[] { 1, 2, 3 });
                RecycleBin.Send(f);
                Assert.False(File.Exists(f));
            }
        }

        [Fact]
        public void SecureDesktop_NotActiveDuringTests() { Assert.False(ScreenCapture.IsSecureDesktopActive()); }

        [Fact]
        public void HotkeyManager_RegistersAndDetectsConflict()
        {
            Sta.Run(() =>
            {
                var hk = new Hotkey(Mods.Ctrl | Mods.Alt | Mods.Shift, System.Windows.Forms.Keys.F11);
                using (var a = new HotkeyManager())
                using (var b = new HotkeyManager())
                {
                    Assert.True(a.Register(HotkeyManager.CaptureId, hk));
                    Assert.False(b.CanRegister(hk)); // уже занято «другой программой»
                    a.UnregisterAll();
                    Assert.True(b.CanRegister(hk));
                }
            });
        }
    }
}
