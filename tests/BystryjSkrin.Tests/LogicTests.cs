using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Xunit;

namespace BystryjSkrin.Tests
{
    public class FileNamingTests
    {
        private static readonly DateTime T = new DateTime(2026, 10, 3, 14, 25, 7);

        [Fact] public void BaseName_MatchesRussianPattern() { Assert.Equal("Скриншот 2026-10-03 14-25-07", FileNaming.BaseName(T)); }

        [Fact] public void Candidate_FirstHasNoNumber() { Assert.Equal("Скриншот 2026-10-03 14-25-07.png", FileNaming.Candidate(T, ImageFormatKind.Png, 1)); }

        [Fact] public void Candidate_SecondIsNumbered() { Assert.Equal("Скриншот 2026-10-03 14-25-07 (2).jpg", FileNaming.Candidate(T, ImageFormatKind.Jpg, 2)); }

        [Fact]
        public void UniquePath_NeverOverwrites()
        {
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.Combine(@"C:\x", "Скриншот 2026-10-03 14-25-07.png"),
                Path.Combine(@"C:\x", "Скриншот 2026-10-03 14-25-07 (2).png"),
            };
            string p = FileNaming.UniquePath(@"C:\x", T, ImageFormatKind.Png, existing.Contains);
            Assert.Equal(Path.Combine(@"C:\x", "Скриншот 2026-10-03 14-25-07 (3).png"), p);
        }

        [Fact]
        public void FileName_HasNoInvalidChars()
        {
            string n = FileNaming.Candidate(T, ImageFormatKind.Png, 7);
            Assert.True(n.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
        }
    }

    public class RectMathTests
    {
        [Fact]
        public void FromPoints_AnyDirection()
        {
            Assert.Equal(new Rectangle(10, 20, 91, 81), RectMath.FromPoints(new Point(100, 100), new Point(10, 20)));
            Assert.Equal(new Rectangle(10, 20, 91, 81), RectMath.FromPoints(new Point(10, 20), new Point(100, 100)));
            Assert.Equal(new Rectangle(10, 20, 91, 81), RectMath.FromPoints(new Point(10, 100), new Point(100, 20)));
        }

        [Fact] public void FromPoints_NegativeCoordinates() { Assert.Equal(new Rectangle(-1920, -100, 1921, 201), RectMath.FromPoints(new Point(0, 100), new Point(-1920, -100))); }

        [Fact] public void Normalize_NegativeSize() { Assert.Equal(new Rectangle(50, 60, 50, 40), RectMath.Normalize(new Rectangle(100, 100, -50, -40))); }

        [Fact]
        public void Clamp_ToBounds()
        {
            Assert.Equal(new Rectangle(0, 0, 50, 50), RectMath.Clamp(new Rectangle(-10, -10, 60, 60), new Rectangle(0, 0, 100, 100)));
            Assert.Equal(Rectangle.Empty, RectMath.Clamp(new Rectangle(200, 200, 10, 10), new Rectangle(0, 0, 100, 100)));
        }

        [Fact]
        public void ScreenSnapshot_RoundTrip_WithLeftMonitor()
        {
            // Второй монитор слева: виртуальный экран начинается с -1920
            var virt = new Rectangle(-1920, -200, 1920 + 2560, 1440 + 200);
            var screenRect = new Rectangle(-100, 50, 300, 200);
            Rectangle snap = RectMath.ScreenToSnapshot(screenRect, virt);
            Assert.Equal(new Rectangle(1820, 250, 300, 200), snap);
            Assert.Equal(screenRect, RectMath.SnapshotToScreen(snap, virt));
        }

        [Theory]
        [InlineData(10, 96, 10)]
        [InlineData(10, 120, 13)]
        [InlineData(10, 144, 15)]
        [InlineData(10, 192, 20)]
        [InlineData(14, 168, 25)]
        [InlineData(10, 0, 10)]
        public void ScaleForDpi(int v, int dpi, int expected) { Assert.Equal(expected, RectMath.ScaleForDpi(v, dpi)); }

        [Fact] public void LogicalToPhysical_150Percent() { Assert.Equal(new Rectangle(150, 300, 450, 150), RectMath.LogicalToPhysical(new Rectangle(100, 200, 300, 100), 144)); }

        [Theory]
        [InlineData(0, 0, true)]
        [InlineData(1, 1, true)]
        [InlineData(4, 100, true)]
        [InlineData(100, 4, true)]
        [InlineData(5, 5, false)]
        [InlineData(300, 200, false)]
        public void IsTooSmall_AccidentalClick(int w, int h, bool small) { Assert.Equal(small, RectMath.IsTooSmall(new Rectangle(10, 10, w, h), OverlayForm.MinSize)); }

        [Fact] public void SizeLabel() { Assert.Equal("300 × 200", RectMath.SizeLabel(new Rectangle(1, 2, 300, 200))); }

        [Fact]
        public void TryParse_Rect()
        {
            Rectangle r;
            Assert.True(RectMath.TryParse("10,20,300,200", out r)); Assert.Equal(new Rectangle(10, 20, 300, 200), r);
            Assert.True(RectMath.TryParse("-1920, -5, 10, 10", out r)); Assert.Equal(new Rectangle(-1920, -5, 10, 10), r);
            Assert.False(RectMath.TryParse("10,20,0,200", out r));
            Assert.False(RectMath.TryParse("10,20,300", out r));
            Assert.False(RectMath.TryParse("a,b,c,d", out r));
            Assert.False(RectMath.TryParse(null, out r));
        }

        [Fact]
        public void LabelPlacement_StaysOnScreen()
        {
            var screen = new Rectangle(0, 0, 1920, 1080);
            var label = new Size(80, 24);
            foreach (var sel in new[] { new Rectangle(100, 100, 200, 100), new Rectangle(1800, 1000, 120, 80), new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 1070, 10, 10) })
            {
                Rectangle r = RectMath.LabelPlacement(sel, label, screen, 6);
                Assert.True(screen.Contains(r), "подпись за пределами экрана для " + sel + ": " + r);
            }
        }
    }

    public class HotkeyTests
    {
        [Fact]
        public void Defaults()
        {
            Assert.Equal("Ctrl+Shift+S", Hotkey.DefaultCapture.ToString());
            Assert.Equal("Ctrl+Shift+Z", Hotkey.DefaultUndo.ToString());
            Assert.Null(Hotkey.Validate(Hotkey.DefaultCapture));
            Assert.Null(Hotkey.Validate(Hotkey.DefaultUndo));
        }

        [Theory]
        [InlineData("Ctrl+Shift+S")]
        [InlineData("Ctrl+Alt+PrintScreen")]
        [InlineData("Ctrl+Shift+1")]
        [InlineData("Alt+Shift+F12")]
        [InlineData("Ctrl+Shift+Backspace")]
        [InlineData("F9")]
        public void RoundTrip(string s)
        {
            Hotkey h;
            Assert.True(Hotkey.TryParse(s, out h));
            Assert.Equal(s, h.ToString());
        }

        [Fact]
        public void Parse_IsLenient()
        {
            Hotkey h;
            Assert.True(Hotkey.TryParse(" control + shift + s ", out h));
            Assert.Equal(Hotkey.DefaultCapture, h);
            Assert.True(Hotkey.TryParse("Ctrl+PrtSc", out h));
            Assert.Equal(Keys.PrintScreen, h.Key);
        }

        [Theory]
        [InlineData("")]
        [InlineData("Ctrl+")]
        [InlineData("Ctrl+Shift")]
        [InlineData("Hyper+S")]
        [InlineData("Ctrl+Shift+Нет")]
        [InlineData("Ctrl+Shift+123")]
        public void Parse_RejectsGarbage(string s) { Hotkey h; Assert.False(Hotkey.TryParse(s, out h)); }

        [Theory]
        [InlineData((int)(Mods.Win | Mods.Shift), Keys.S)]   // «Ножницы» Windows
        [InlineData((int)(Mods.Ctrl), Keys.C)]               // копировать
        [InlineData((int)(Mods.Ctrl), Keys.S)]               // сохранить
        [InlineData((int)(Mods.Shift), Keys.A)]              // мешает печатать
        [InlineData((int)(Mods.None), Keys.A)]
        [InlineData((int)(Mods.Alt), Keys.F4)]
        [InlineData((int)(Mods.Ctrl | Mods.Alt), Keys.Delete)]
        [InlineData((int)(Mods.Ctrl), Keys.ShiftKey)]
        [InlineData((int)(Mods.Ctrl | Mods.Shift), Keys.Escape)]
        [InlineData((int)(Mods.Win), Keys.Q)]
        public void Validate_RejectsDangerous(int m, Keys k) { Assert.NotNull(Hotkey.Validate(new Hotkey((Mods)m, k))); }

        [Theory]
        [InlineData((int)(Mods.None), Keys.F9)]
        [InlineData((int)(Mods.None), Keys.PrintScreen)]
        [InlineData((int)(Mods.Ctrl | Mods.Alt), Keys.S)]
        [InlineData((int)(Mods.Ctrl | Mods.Shift), Keys.Z)]
        public void Validate_AcceptsGood(int m, Keys k) { Assert.Null(Hotkey.Validate(new Hotkey((Mods)m, k))); }

        [Fact]
        public void FindFree_SkipsBusyAndExcluded()
        {
            var busy = new HashSet<Hotkey> { Hotkey.CaptureAlternatives[0] };
            Hotkey? r = Hotkey.FindFree(Hotkey.CaptureAlternatives, h => !busy.Contains(h), Hotkey.CaptureAlternatives[1]);
            Assert.True(r.HasValue);
            Assert.Equal(Hotkey.CaptureAlternatives[2], r.Value);
        }

        [Fact]
        public void FindFree_AllBusy_ReturnsNull()
        {
            Assert.Null(Hotkey.FindFree(Hotkey.CaptureAlternatives, h => false));
            Assert.Null(Hotkey.FindFree(Hotkey.CaptureAlternatives, h => { throw new InvalidOperationException(); }));
        }

        [Fact]
        public void Alternatives_AreAllValid_AndDistinctFromDefaults()
        {
            foreach (var h in Hotkey.CaptureAlternatives.Concat(Hotkey.UndoAlternatives))
            {
                Assert.Null(Hotkey.Validate(h));
                Assert.NotEqual(Hotkey.DefaultCapture, h);
                Assert.NotEqual(Hotkey.DefaultUndo, h);
            }
        }
    }

    public class GateAndHistoryTests
    {
        [Fact]
        public void SecondCaptureIgnoredWhileOverlayOpen()
        {
            var g = new CaptureGate();
            Assert.True(g.TryEnter());
            Assert.False(g.TryEnter());
            Assert.True(g.IsBusy);
            g.Exit();
            Assert.True(g.TryEnter());
        }

        [Fact]
        public void Undo_TakesLastExisting()
        {
            var h = new UndoHistory();
            h.Push("a"); h.Push("b"); h.Push("c");
            var exists = new HashSet<string> { "a", "b" }; // «c» пользователь уже удалил вручную
            Assert.Equal("b", h.PeekLast(exists.Contains));
            Assert.Equal("b", h.TakeLast(exists.Contains));
            Assert.Equal("a", h.TakeLast(exists.Contains));
            Assert.Null(h.TakeLast(exists.Contains));
        }

        [Fact]
        public void Undo_RemoveAndLimit()
        {
            var h = new UndoHistory();
            for (int i = 0; i < UndoHistory.Max + 20; i++) h.Push("f" + i);
            Assert.Equal(UndoHistory.Max, h.Count);
            h.Remove("F" + (UndoHistory.Max + 19));
            Assert.Equal("f" + (UndoHistory.Max + 18), h.TakeLast(p => true));
        }
    }

    public class ArgsAndAutostartTests
    {
        [Fact]
        public void ParseArgs()
        {
            var o = Program.ParseArgs(new[] { "--capture-rect", "1,2,3,4", "--out", @"C:\x", "--format", "jpg", "--config-dir", @"C:\cfg" });
            Assert.Equal("1,2,3,4", o.CaptureRect); Assert.Equal(@"C:\x", o.Out); Assert.Equal("jpg", o.Format); Assert.Equal(@"C:\cfg", o.ConfigDir);
            Assert.Null(o.Error);
            Assert.NotNull(Program.ParseArgs(new[] { "--oops" }).Error);
            Assert.True(Program.ParseArgs(new[] { "--autostart" }).Autostart);
        }

        [Fact]
        public void Autostart_CommandRoundTrip()
        {
            string exe = @"C:\Users\Иван\Программы\Быстрый скрин\BystryjSkrin.exe";
            Assert.Equal("\"" + exe + "\" --autostart", Autostart.CommandFor(exe));
            Assert.Equal(exe, Autostart.ExePathFromCommand(Autostart.CommandFor(exe)));
            Assert.Equal(@"C:\a\b.exe", Autostart.ExePathFromCommand(@"C:\a\b.exe --x"));
        }

        [Fact]
        public void Autostart_RiskyLocations()
        {
            string dl = @"C:\Users\I\Downloads", tmp = @"C:\Users\I\AppData\Local\Temp\";
            Assert.NotNull(Autostart.RiskyLocationWarning(@"C:\Users\I\AppData\Local\Temp\Rar$EX01\BystryjSkrin.exe", dl, tmp, p => false));
            Assert.NotNull(Autostart.RiskyLocationWarning(@"C:\Users\I\Downloads\BystryjSkrin.exe", dl, tmp, p => false));
            Assert.NotNull(Autostart.RiskyLocationWarning(@"E:\BystryjSkrin.exe", dl, tmp, p => true));
            Assert.Null(Autostart.RiskyLocationWarning(@"C:\Users\I\Программы\BystryjSkrin.exe", dl, tmp, p => false));
        }

        [Fact]
        public void ShutterSound_IsValidWav()
        {
            byte[] w = Sounds.ShutterWav();
            Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(w, 0, 4));
            Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(w, 8, 4));
            Assert.True(w.Length > 1000);
        }
    }
}
