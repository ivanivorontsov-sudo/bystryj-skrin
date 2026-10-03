using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace BystryjSkrin
{
    internal static class Program
    {
        private const string Id = "BystryjSkrin-3f6c1d2a";
        private static TrayApp app;

        internal sealed class Options
        {
            public string ConfigDir;
            public bool Capture, Quit, Autostart, Settings;
            public string CaptureRect, Out, Format, Marker;
            public string Error;
        }

        internal static Options ParseArgs(string[] args)
        {
            var o = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                Func<string> next = () => i + 1 < args.Length ? args[++i] : null;
                switch (a)
                {
                    case "--config-dir": o.ConfigDir = next(); break;
                    case "--capture": o.Capture = true; break;
                    case "--quit": o.Quit = true; break;
                    case "--settings": o.Settings = true; break;
                    case "--autostart": o.Autostart = true; break;
                    case "--capture-rect": o.CaptureRect = next(); break;
                    case "--out": o.Out = next(); break;
                    case "--format": o.Format = next(); break;
                    case "--saved-marker": o.Marker = next(); break;
                    default: o.Error = "Неизвестный параметр: " + args[i]; break;
                }
            }
            return o;
        }

        private static string ResolveConfigDir(string requested)
        {
            foreach (string dir in new[]
            {
                requested,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BystryjSkrin"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BystryjSkrin"),
                Path.Combine(Path.GetTempPath(), "BystryjSkrin"),
            })
            {
                if (string.IsNullOrEmpty(dir)) continue;
                try { Directory.CreateDirectory(dir); return dir; } catch { }
            }
            return Path.GetTempPath();
        }

        [STAThread]
        private static int Main(string[] args)
        {
            Native.EnsureDpiAwareness(); // на случай, если манифест не применился
            Options opt = ParseArgs(args);
            string configDir = ResolveConfigDir(opt.ConfigDir);
            Log.FilePath = Path.Combine(configDir, "log.txt");

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                if (app != null) app.ReportCrash("интерфейс", e.Exception); else Log.Error("ThreadException", e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Error("UnhandledException", e.ExceptionObject as Exception);

            if (opt.Error != null)
            {
                Console(opt.Error);
                return 2;
            }
            if (opt.CaptureRect != null) return Headless(opt, configDir);

            if (opt.Quit) { Signal("quit"); return 0; }

            bool createdNew;
            using (var mutex = new Mutex(true, @"Local\" + Id, out createdNew))
            {
                if (!createdNew)
                {
                    // Уже запущено: просим работающий экземпляр сделать скриншот или показать подсказку
                    if (!Signal(opt.Capture ? "capture" : opt.Settings ? "settings" : "show"))
                        Dialogs.Warn("«Быстрый скрин» уже запущен, но не отвечает (возможно, он запущен от имени администратора).\n\nЗакройте его через значок в трее или Диспетчер задач и запустите снова.");
                    return 0;
                }

                TrayApp.LastSavedMarker = opt.Marker;
                using (var evCapture = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + Id + "-capture"))
                using (var evShow = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + Id + "-show"))
                using (var evQuit = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + Id + "-quit"))
                using (var evSettings = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + Id + "-settings"))
                {
                    try
                    {
                        app = new TrayApp(configDir, opt.Capture, opt.Settings);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Запуск", ex);
                        Dialogs.Error("«Быстрый скрин» не смог запуститься: " + ErrorText.Friendly(ex) + "\n\nЖурнал: " + Log.FilePath);
                        return 1;
                    }
                    var waiter = new Thread(() =>
                    {
                        var handles = new WaitHandle[] { evCapture, evShow, evQuit, evSettings };
                        while (true)
                        {
                            int i;
                            try { i = WaitHandle.WaitAny(handles); } catch { return; }
                            if (i == 0) app.Post(() => app.StartCapture(150));
                            else if (i == 1) app.Post(() => Dialogs.Info("«Быстрый скрин» уже работает — его значок в трее (возле часов; может быть спрятан под стрелкой «^»).\n\nСкриншот: " + "сочетание клавиш из меню значка или щелчок по значку."));
                            else if (i == 3) app.Post(app.OpenSettings);
                            else { app.Post(app.ExitThread); return; }
                        }
                    }) { IsBackground = true, Name = "signals" };
                    waiter.Start();
                    Application.Run(app);
                }
            }
            return 0;
        }

        private static bool Signal(string name)
        {
            try
            {
                using (var ev = EventWaitHandle.OpenExisting(@"Local\" + Id + "-" + name)) ev.Set();
                return true;
            }
            catch (Exception ex) { Log.Error("Сигнал " + name, ex); return false; }
        }

        private static void Console(string text)
        {
            try { if (Native.AttachConsole(-1)) System.Console.WriteLine(text); } catch { }
            Log.Write(text);
        }

        /// <summary>--capture-rect x,y,w,h [--out папка] [--format png|jpg]: снимок области без окна (для тестов и скриптов).</summary>
        private static int Headless(Options opt, string configDir)
        {
            Rectangle rect;
            if (!RectMath.TryParse(opt.CaptureRect, out rect)) { Console("Неверный --capture-rect, нужно x,y,ширина,высота"); return 2; }
            AppSettings s = AppSettings.Load(Path.Combine(configDir, "settings.ini"));
            if (!string.IsNullOrEmpty(opt.Out)) s.SaveFolder = opt.Out;
            if (opt.Format != null) s.Format = opt.Format.Equals("jpg", StringComparison.OrdinalIgnoreCase) ? ImageFormatKind.Jpg : ImageFormatKind.Png;
            try
            {
                Rectangle virt;
                using (Bitmap shot = ScreenCapture.CaptureVirtualScreen(out virt))
                {
                    Rectangle snap = RectMath.Clamp(RectMath.ScreenToSnapshot(rect, virt), new Rectangle(0, 0, shot.Width, shot.Height));
                    if (snap.IsEmpty) { Console("Область вне экрана. Экран: " + virt); return 3; }
                    using (Bitmap img = ScreenCapture.Crop(shot, snap))
                    {
                        byte[] data = SaveService.Encode(img, s.Format, s.JpegQuality);
                        SaveOutcome o = SaveService.Save(data, s.Format, s.SaveFolder, SaveService.FallbackFolders(s.SaveFolder), DateTime.Now);
                        if (!o.Saved) { Console("Не сохранено: " + ErrorText.Friendly(o.FinalError)); return 4; }
                        if (opt.Marker != null) File.WriteAllText(opt.Marker, o.Path);
                        Console((o.FellBack ? "Сохранено в запасную папку: " : "Сохранено: ") + o.Path);
                        return o.FellBack ? 5 : 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Console("Ошибка: " + (ex is CaptureException ? ex.Message : ErrorText.Friendly(ex)));
                Log.Error("Headless", ex);
                return 3;
            }
        }
    }
}
