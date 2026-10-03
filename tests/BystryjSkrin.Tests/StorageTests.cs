using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Xunit;

namespace BystryjSkrin.Tests
{
    public class SettingsTests
    {
        [Fact]
        public void Defaults_AreSensible()
        {
            var s = AppSettings.CreateDefault(@"C:\Users\I\Pictures\Скриншоты");
            Assert.Equal(ImageFormatKind.Png, s.Format);
            Assert.False(s.PlaySound);           // звук выключен по умолчанию
            Assert.True(s.ShowNotification);
            Assert.Equal(Hotkey.DefaultCapture, s.CaptureHotkey);
            Assert.Equal(Hotkey.DefaultUndo, s.UndoHotkey);
            Assert.EndsWith(@"Pictures\Скриншоты", AppSettings.DefaultFolder().Replace("Изображения", "Pictures"), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SerializeParse_RoundTrip()
        {
            using (var d = new TempDir())
            {
                var s = AppSettings.CreateDefault(d.Sub("def"));
                s.SaveFolder = d.Sub("Мои скрины");
                s.Format = ImageFormatKind.Jpg; s.JpegQuality = 77; s.CopyToClipboard = false; s.PlaySound = true; s.ShowGuides = false;
                s.CaptureHotkey = new Hotkey(Mods.Ctrl | Mods.Alt, Keys.S); s.UndoHotkeyEnabled = false;
                var p = AppSettings.Parse(s.Serialize(), d.Sub("def"));
                Assert.Empty(p.LoadWarnings);
                Assert.Equal(s.SaveFolder, p.SaveFolder);
                Assert.Equal(ImageFormatKind.Jpg, p.Format); Assert.Equal(77, p.JpegQuality);
                Assert.False(p.CopyToClipboard); Assert.True(p.PlaySound); Assert.False(p.ShowGuides);
                Assert.Equal(s.CaptureHotkey, p.CaptureHotkey); Assert.False(p.UndoHotkeyEnabled);
            }
        }

        [Fact]
        public void Parse_GarbageFallsBackWithWarnings()
        {
            string def = Path.Combine(Path.GetTempPath(), "def-folder");
            var p = AppSettings.Parse("мусор без равно\nFormat=bmp\nJpegQuality=abc\nCaptureHotkey=Ctrl+\nSaveFolder=relative\\path\n", def);
            Assert.Equal(ImageFormatKind.Png, p.Format);
            Assert.Equal(90, p.JpegQuality);
            Assert.Equal(Hotkey.DefaultCapture, p.CaptureHotkey);
            Assert.Equal(def, p.SaveFolder);
            Assert.True(p.LoadWarnings.Count >= 4);
        }

        [Fact]
        public void Sanitize_SystemFolderAndQuality()
        {
            string def = Path.Combine(Path.GetTempPath(), "def-folder");
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var p = AppSettings.Parse("SaveFolder=" + win + "\\Temp\nJpegQuality=500\n", def);
            Assert.Equal(def, p.SaveFolder);
            Assert.Equal(100, p.JpegQuality);
        }

        [Fact]
        public void Sanitize_DangerousHotkeyAndSameUndo()
        {
            var p = AppSettings.Parse("CaptureHotkey=Ctrl+C\nUndoHotkey=Ctrl+Shift+S\n", Path.GetTempPath() + "x");
            Assert.Equal(Hotkey.DefaultCapture, p.CaptureHotkey);
            Assert.NotEqual(p.CaptureHotkey, p.UndoHotkey);
        }

        [Fact]
        public void Validate_ReportsAllProblems()
        {
            var s = AppSettings.CreateDefault(@"C:\Users\I\Pictures\Скриншоты");
            s.SaveFolder = "";
            s.JpegQuality = 5;
            s.CaptureHotkey = new Hotkey(Mods.Win | Mods.Shift, Keys.S);
            s.UndoHotkey = s.CaptureHotkey;
            var e = s.Validate();
            Assert.Contains(e, x => x.StartsWith("Папка"));
            Assert.Contains(e, x => x.Contains("JPG"));
            Assert.Contains(e, x => x.Contains("Ножниц"));
        }

        [Fact]
        public void Validate_SameHotkeysRejected()
        {
            var s = AppSettings.CreateDefault(Path.Combine(Path.GetTempPath(), "ok"));
            s.UndoHotkey = s.CaptureHotkey;
            Assert.Contains(s.Validate(), x => x.Contains("различаться"));
            s.UndoHotkeyEnabled = false;
            Assert.Empty(s.Validate());
        }

        [Fact]
        public void SaveLoad_AtomicAndMissingAndCorrupted()
        {
            using (var d = new TempDir())
            {
                string path = Path.Combine(d.Sub("cfg"), "settings.ini");
                var missing = AppSettings.Load(path, d.Sub("def"));
                Assert.Equal(d.Sub("def"), missing.SaveFolder);

                var s = AppSettings.CreateDefault(d.Sub("def"));
                s.JpegQuality = 55;
                s.Save(path);
                s.JpegQuality = 66;
                s.Save(path); // повторное сохранение через замену
                Assert.False(File.Exists(path + ".tmp"));
                Assert.Equal(66, AppSettings.Load(path, d.Sub("def")).JpegQuality);

                File.WriteAllBytes(path, new byte[] { 0, 0, 1, 2, 0 });
                var broken = AppSettings.Load(path, d.Sub("def"));
                Assert.Equal(90, broken.JpegQuality);
                Assert.NotEmpty(broken.LoadWarnings);
                Assert.True(File.Exists(path + ".broken"));
            }
        }
    }

    public class FolderRulesTests
    {
        [Fact]
        public void Rejects_SystemFolders()
        {
            Assert.NotNull(FolderRules.Validate(Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
            Assert.NotNull(FolderRules.Validate(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers")));
            Assert.NotNull(FolderRules.Validate(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "MyApp")));
            Assert.NotNull(FolderRules.Validate(@"C:\$Recycle.Bin\S-1-5"));
            Assert.NotNull(FolderRules.Validate(@"D:\System Volume Information"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(@"C:\")]
        [InlineData(@"D:\")]
        [InlineData(@"relative\folder")]
        [InlineData(@"\no-drive")]
        [InlineData(@"C:folder")]
        [InlineData(@"C:\bad|name")]
        [InlineData(@"C:\what?")]
        [InlineData(@"C:\a:b")]
        [InlineData(@"C:\Users\I\dot.\x")]
        [InlineData(@"\\?\C:\x")]
        public void Rejects_InvalidPaths(string p) { Assert.NotNull(FolderRules.Validate(p)); }

        [Theory]
        [InlineData(@"C:\Users\Иван\Pictures\Скриншоты")]
        [InlineData(@"D:\Скрины")]
        [InlineData(@"\\server\share\screens")]
        [InlineData(@"C:\Users\Иван\OneDrive\Изображения\Скриншоты")]
        public void Accepts_NormalPaths(string p) { Assert.Null(FolderRules.Validate(p)); }

        [Fact]
        public void Rejects_TooLong() { Assert.NotNull(FolderRules.Validate(@"C:\" + new string('a', 300))); }

        [Fact]
        public void ProbeWrite_CreatesFolderAndLeavesNoTrash()
        {
            using (var d = new TempDir())
            {
                string f = Path.Combine(d.Path, "новая", "папка");
                Assert.Null(FolderRules.ProbeWrite(f));
                Assert.True(Directory.Exists(f));
                Assert.Empty(Directory.GetFiles(f));
            }
        }

        [Fact]
        public void ProbeWrite_FailsWhenPathIsAFile()
        {
            using (var d = new TempDir())
            {
                string file = d.Sub("not-a-folder");
                File.WriteAllText(file, "x");
                Assert.NotNull(FolderRules.ProbeWrite(Path.Combine(file, "sub")));
            }
        }
    }

    public class ErrorTextTests
    {
        private static IOException Io(int win32) { var e = new IOException("x"); typeof(Exception).GetProperty("HResult").SetValue(e, unchecked((int)0x80070000) | win32); return e; }

        [Fact] public void DiskFull() { Assert.Contains("место", ErrorText.Friendly(Io(112))); Assert.True(ErrorText.IsDiskFull(Io(39))); }
        [Fact] public void AccessDenied() { Assert.Contains("прав", ErrorText.Friendly(new UnauthorizedAccessException())); }
        [Fact] public void OperatorError() { Assert.Contains("Оператор или администратор отклонил запрос", ErrorText.Friendly(Io(4320))); }
        [Fact] public void UsbUnplugged() { Assert.Contains("флешка", ErrorText.Friendly(Io(21))); Assert.Contains("диск", ErrorText.Friendly(new DirectoryNotFoundException())); }
        [Fact] public void Cloud() { Assert.Contains("OneDrive", ErrorText.Friendly(Io(0x16A))); }
        [Fact] public void Unknown_IsStillFriendly() { Assert.StartsWith("непредвиденная ошибка", ErrorText.Friendly(new InvalidOperationException("boom"))); Assert.NotNull(ErrorText.Friendly(null)); }

        [Fact]
        public void Cfa_Detection()
        {
            var roots = new[] { @"C:\Users\I\Pictures", @"C:\Users\I\OneDrive\Документы" };
            var denied = new UnauthorizedAccessException();
            Assert.True(ErrorText.LooksLikeCfaBlock(denied, @"C:\Users\I\Pictures\Скриншоты", null, roots));
            Assert.True(ErrorText.LooksLikeCfaBlock(denied, @"C:\Users\I\OneDrive\Документы\a", true, roots));
            Assert.False(ErrorText.LooksLikeCfaBlock(denied, @"C:\Users\I\Pictures\Скриншоты", false, roots));
            Assert.False(ErrorText.LooksLikeCfaBlock(denied, @"D:\Скрины", true, roots));
            Assert.False(ErrorText.LooksLikeCfaBlock(Io(112), @"C:\Users\I\Pictures\Скриншоты", true, roots));
            Assert.Contains("программ-шантажистов", ErrorText.CfaHint);
        }
    }

    public class SaveServiceTests
    {
        private static readonly DateTime T = new DateTime(2026, 10, 3, 14, 25, 7);

        private static Bitmap Sample(int w = 40, int h = 30)
        {
            var b = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(b)) { g.Clear(Color.Red); g.FillRectangle(Brushes.Blue, 0, 0, w / 2, h); }
            return b;
        }

        [Fact]
        public void Encode_PngAndJpg_Decodable()
        {
            using (var b = Sample())
            {
                foreach (var f in new[] { ImageFormatKind.Png, ImageFormatKind.Jpg })
                {
                    byte[] data = SaveService.Encode(b, f, 90);
                    using (var ms = new MemoryStream(data))
                    using (var img = Image.FromStream(ms))
                    {
                        Assert.Equal(40, img.Width); Assert.Equal(30, img.Height);
                        Assert.Equal(f == ImageFormatKind.Png ? ImageFormat.Png.Guid : ImageFormat.Jpeg.Guid, img.RawFormat.Guid);
                    }
                }
            }
        }

        [Fact]
        public void Save_ToPrimary_NoOverwrite()
        {
            using (var d = new TempDir())
            {
                string folder = d.Sub("Скриншоты");
                var o1 = SaveService.Save(new byte[] { 1 }, ImageFormatKind.Png, folder, null, T);
                var o2 = SaveService.Save(new byte[] { 2 }, ImageFormatKind.Png, folder, null, T);
                Assert.True(o1.Saved && o2.Saved);
                Assert.False(o1.FellBack);
                Assert.Equal("Скриншот 2026-10-03 14-25-07.png", Path.GetFileName(o1.Path));
                Assert.Equal("Скриншот 2026-10-03 14-25-07 (2).png", Path.GetFileName(o2.Path));
                Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(o1.Path));
            }
        }

        [Fact]
        public void Save_PrimaryUnavailable_FallsBack_ShotNotLost()
        {
            using (var d = new TempDir())
            {
                string blocker = d.Sub("флешка");
                File.WriteAllText(blocker, "это файл, а не папка"); // папку создать нельзя
                string primary = Path.Combine(blocker, "Скриншоты");
                string fallback = d.Sub("запасная");
                var o = SaveService.Save(new byte[] { 9, 9 }, ImageFormatKind.Png, primary, new[] { fallback }, T, (e, f) => false);
                Assert.True(o.Saved);
                Assert.True(o.FellBack);
                Assert.NotNull(o.PrimaryError);
                Assert.StartsWith(fallback, o.Path);
                Assert.Equal(new byte[] { 9, 9 }, File.ReadAllBytes(o.Path));
            }
        }

        [Fact]
        public void Save_InvalidPrimary_FallsBack()
        {
            using (var d = new TempDir())
            {
                var o = SaveService.Save(new byte[] { 1 }, ImageFormatKind.Jpg, Environment.GetFolderPath(Environment.SpecialFolder.Windows), new[] { d.Sub("ok") }, T, (e, f) => false);
                Assert.True(o.Saved && o.FellBack);
                Assert.EndsWith(".jpg", o.Path);
            }
        }

        [Fact]
        public void Save_CfaSuspected_IsReported()
        {
            using (var d = new TempDir())
            {
                string blocker = d.Sub("blk"); File.WriteAllText(blocker, "x");
                var o = SaveService.Save(new byte[] { 1 }, ImageFormatKind.Png, Path.Combine(blocker, "x"), new[] { d.Sub("ok") }, T, (e, f) => true);
                Assert.True(o.CfaSuspected);
                Assert.True(o.Saved);
            }
        }

        [Fact]
        public void Save_AllFail_ReportsError()
        {
            using (var d = new TempDir())
            {
                string blocker = d.Sub("blk"); File.WriteAllText(blocker, "x");
                var o = SaveService.Save(new byte[] { 1 }, ImageFormatKind.Png, Path.Combine(blocker, "a"), new[] { Path.Combine(blocker, "b"), "" }, T, (e, f) => false);
                Assert.False(o.Saved);
                Assert.NotNull(o.FinalError);
                Assert.Equal(3, o.Attempts.Count);
            }
        }

        [Fact]
        public void FallbackFolders_ExcludePrimary_AndAvoidProtectedOnes()
        {
            var list = SaveService.FallbackFolders(AppSettings.DefaultFolder());
            Assert.DoesNotContain(list, x => string.Equals(x, AppSettings.DefaultFolder(), StringComparison.OrdinalIgnoreCase));
            // хотя бы одна запасная папка вне «Изображений»/«Документов» (их блокирует защита от шантажистов)
            Assert.Contains(list, x => !ErrorText.IsUnderAny(x, ErrorText.CfaProtectedRoots()));
            Assert.All(list, x => Assert.Null(FolderRules.Validate(x)));
        }
    }
}
