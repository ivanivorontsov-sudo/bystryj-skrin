using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BystryjSkrin
{
    internal sealed class TrayApp : ApplicationContext
    {
        public const int EscId = 3;

        private readonly string configDir, settingsPath;
        private AppSettings settings;
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem miCapture, miUndo, miAutostart;
        private readonly HotkeyManager hotkeys;
        private readonly Control invoker;
        private readonly CaptureGate gate = new CaptureGate();
        private readonly UndoHistory history = new UndoHistory();
        private OverlayForm overlay;
        private ToastForm toast;
        private bool settingsOpen;
        private DateTime lastCrashToast = DateTime.MinValue;

        /// <summary>Для CI: путь последнего сохранённого файла пишется сюда.</summary>
        public static string LastSavedMarker;

        public TrayApp(string configDir, bool captureNow)
        {
            this.configDir = configDir;
            settingsPath = Path.Combine(configDir, "settings.ini");
            settings = AppSettings.Load(settingsPath);

            invoker = new Control();
            invoker.CreateControl();
            var h = invoker.Handle; // создаём окно в UI-потоке

            hotkeys = new HotkeyManager();
            hotkeys.Pressed += OnHotkey;

            menu = new ContextMenuStrip { ShowImageMargin = false };
            miCapture = new ToolStripMenuItem("Сделать скриншот", null, (s, e) => StartCapture(250)) { Font = new Font(SystemFonts.MenuFont, FontStyle.Bold) };
            miUndo = new ToolStripMenuItem("Отменить последний скриншот", null, (s, e) => UndoLast());
            miAutostart = new ToolStripMenuItem("Автозапуск с Windows", null, (s, e) => ToggleAutostart());
            menu.Items.Add(miCapture);
            menu.Items.Add(miUndo);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Открыть папку", null, (s, e) => Safe("Открыть папку", () => Shell.OpenFolder(settings.SaveFolder))));
            menu.Items.Add(new ToolStripMenuItem("Выбрать папку…", null, (s, e) => ChooseFolder()));
            menu.Items.Add(new ToolStripMenuItem("Настройки…", null, (s, e) => OpenSettings()));
            menu.Items.Add(miAutostart);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Выход", null, (s, e) => ExitThread()));
            menu.Opening += (s, e) =>
            {
                miUndo.Enabled = history.PeekLast(File.Exists) != null;
                miAutostart.Checked = Autostart.IsEnabled();
            };

            tray = new NotifyIcon { Icon = LoadAppIcon(SystemInformation.SmallIconSize.Width), ContextMenuStrip = menu, Visible = true };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) StartCapture(150); };
            UpdateHotkeyTexts();

            invoker.BeginInvoke(new Action(() => Startup(captureNow)));
        }

        public static Icon LoadAppIcon(int size)
        {
            using (var st = typeof(TrayApp).Assembly.GetManifestResourceStream("BystryjSkrin.app.ico"))
                return new Icon(st, new Size(size, size));
        }

        /// <summary>Выполнить в UI-потоке (для сигналов от второго экземпляра).</summary>
        public void Post(Action a)
        {
            try { invoker.BeginInvoke(new Action(() => Safe("команда", a))); } catch (Exception ex) { Log.Error("Post", ex); }
        }

        private void Startup(bool captureNow)
        {
            Log.Write("Запуск. Версия " + Application.ProductVersion + ", exe: " + Application.ExecutablePath);
            if (settings.LoadWarnings.Count > 0)
            {
                ShowToast("Настройки исправлены", string.Join("\n", settings.LoadWarnings), ToastKind.Warning, null, null, null, 12000);
                TrySaveSettings();
            }
            RegisterHotkeys(true);
            Autostart.RefreshPath(Application.ExecutablePath);
            if (Autostart.IsEnabled() && Autostart.IsDisabledByWindows())
                ShowToast("Автозапуск отключён в Windows", "Windows выключила автозапуск программы («Диспетчер задач → Автозагрузка»). Включите там или снимите и снова поставьте «Автозапуск с Windows» в меню.", ToastKind.Warning, null, null, null, 10000);
            CheckFolder(false);
            if (captureNow) StartCapture(300);
        }

        // ---------- горячие клавиши ----------

        private void UpdateHotkeyTexts()
        {
            miCapture.ShortcutKeyDisplayString = settings.CaptureHotkey.ToString();
            miUndo.ShortcutKeyDisplayString = settings.UndoHotkeyEnabled ? settings.UndoHotkey.ToString() : "";
            string tip = "Быстрый скрин — " + settings.CaptureHotkey + " или щелчок по значку";
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        private void RegisterHotkeys(bool interactive)
        {
            hotkeys.UnregisterAll();
            if (!hotkeys.Register(HotkeyManager.CaptureId, settings.CaptureHotkey))
            {
                Log.Write("Сочетание занято: " + settings.CaptureHotkey);
                Hotkey? alt = Hotkey.FindFree(Hotkey.CaptureAlternatives, hotkeys.CanRegister, settings.UndoHotkey);
                string busy = "Сочетание " + settings.CaptureHotkey + " уже занято другой программой, поэтому оно не сработает.";
                if (alt.HasValue && interactive &&
                    Dialogs.Ask(busy + "\n\nИспользовать вместо него " + alt.Value + "?\n\n«Да» — использовать " + alt.Value +
                                "\n«Нет» — выбрать своё сочетание в настройках.\n\nСкриншот всегда можно сделать и щелчком по значку программы в трее."))
                {
                    settings.CaptureHotkey = alt.Value;
                    TrySaveSettings();
                    hotkeys.Register(HotkeyManager.CaptureId, settings.CaptureHotkey);
                }
                else if (interactive)
                {
                    if (!alt.HasValue) Dialogs.Warn(busy + "\n\nЗадайте другое сочетание в настройках. Пока скриншот можно делать щелчком по значку в трее.");
                    invoker.BeginInvoke(new Action(OpenSettings));
                }
            }
            if (settings.UndoHotkeyEnabled && !hotkeys.Register(HotkeyManager.UndoId, settings.UndoHotkey))
            {
                Hotkey old = settings.UndoHotkey;
                Hotkey? alt = Hotkey.FindFree(Hotkey.UndoAlternatives, hotkeys.CanRegister, settings.CaptureHotkey);
                if (alt.HasValue)
                {
                    settings.UndoHotkey = alt.Value;
                    TrySaveSettings();
                    hotkeys.Register(HotkeyManager.UndoId, settings.UndoHotkey);
                    ShowToast("Сочетание отмены изменено", old + " занято другой программой, поэтому отмена последнего скриншота теперь на " + alt.Value + ". Изменить можно в настройках.", ToastKind.Warning, null, null, null, 9000);
                }
                else ShowToast("Сочетание отмены занято", old + " занято другой программой. Отменить последний скриншот можно из меню значка в трее.", ToastKind.Warning, null, null, null, 9000);
            }
            UpdateHotkeyTexts();
        }

        private void OnHotkey(int id)
        {
            Safe("горячая клавиша", () =>
            {
                if (id == HotkeyManager.CaptureId) StartCapture(0);
                else if (id == HotkeyManager.UndoId) UndoLast();
                else if (id == EscId && overlay != null) overlay.Cancel();
            });
        }

        // ---------- захват ----------

        public void StartCapture(int delayMs)
        {
            if (settingsOpen) { ShowToast("Сначала закройте настройки", "Окно настроек открыто — сохраните или закройте его, затем делайте скриншот.", ToastKind.Info, null, null, null, 4000); return; }
            if (!gate.TryEnter()) return; // выделение уже открыто — повторное нажатие игнорируем
            if (toast != null) toast.CloseSafe(); // чтобы прошлое уведомление не попало на снимок
            if (delayMs <= 0) { DoCapture(); return; }
            var t = new Timer { Interval = delayMs };
            t.Tick += (s, e) => { t.Stop(); t.Dispose(); DoCapture(); };
            t.Start();
        }

        private void DoCapture()
        {
            Bitmap shot = null;
            try
            {
                Rectangle virt;
                shot = ScreenCapture.CaptureVirtualScreen(out virt);
                var ov = new OverlayForm(shot, virt, settings.ShowGuides);
                overlay = ov;
                Bitmap captured = shot;
                ov.RegionSelected += r => Safe("сохранение", () =>
                {
                    using (Bitmap img = ScreenCapture.Crop(captured, r)) SaveImage(img);
                });
                ov.FormClosed += (s, e) =>
                {
                    hotkeys.Unregister(EscId);
                    overlay = null;
                    captured.Dispose();
                    gate.Exit();
                };
                hotkeys.Register(EscId, new Hotkey(Mods.None, Keys.Escape)); // Esc работает, даже если фокус ушёл
                ov.Show();
            }
            catch (CaptureException ex)
            {
                Log.Error("Захват", ex);
                if (shot != null) shot.Dispose();
                overlay = null; gate.Exit();
                ShowToast("Скриншот не сделан", ex.Message, ToastKind.Error, null, null, null, 9000);
            }
            catch (Exception ex)
            {
                Log.Error("Захват", ex);
                if (shot != null) shot.Dispose();
                overlay = null; gate.Exit();
                ShowToast("Скриншот не сделан", "Не удалось снять экран: " + ErrorText.Friendly(ex), ToastKind.Error, null, null, null, 9000);
            }
        }

        private void SaveImage(Bitmap img)
        {
            byte[] data;
            try { data = SaveService.Encode(img, settings.Format, settings.JpegQuality); }
            catch (Exception ex)
            {
                Log.Error("Кодирование", ex);
                bool clip = TryClipboard(img);
                ShowToast("Не удалось сохранить снимок", ErrorText.Friendly(ex) + (clip ? " Снимок скопирован в буфер обмена — вставьте его (Ctrl+V)." : ""), ToastKind.Error, img, null, null, 15000);
                return;
            }

            SaveOutcome o = SaveService.Save(data, settings.Format, settings.SaveFolder, SaveService.FallbackFolders(settings.SaveFolder), DateTime.Now);
            bool copied = false;
            if (settings.CopyToClipboard || !o.Saved) copied = TryClipboard(img);
            if (settings.PlaySound && o.Saved) Sounds.PlayShutter();

            if (!o.Saved)
            {
                ShowToast("Снимок не удалось сохранить",
                    ErrorText.Friendly(o.FinalError) + (copied ? " Снимок скопирован в буфер обмена — вставьте его (Ctrl+V), чтобы не потерять." : " Подробности в журнале."),
                    ToastKind.Error, img, new[] { new ToastAction("Выбрать папку…", ChooseFolder) }, null, 20000);
                return;
            }

            history.Push(o.Path);
            try { if (LastSavedMarker != null) File.WriteAllText(LastSavedMarker, o.Path); } catch { }
            Log.Write("Сохранено: " + o.Path + (o.FellBack ? " (запасная папка)" : ""));
            string path = o.Path;
            var actions = new System.Collections.Generic.List<ToastAction>
            {
                new ToastAction("Открыть", () => Shell.Open(path)),
                new ToastAction("Папка", () => Shell.ShowInFolder(path)),
                new ToastAction("Удалить", () => DeleteShot(path)),
            };

            if (o.FellBack)
            {
                string reason = "Папка «" + o.PrimaryFolder + "» недоступна: " + ErrorText.Friendly(o.PrimaryError);
                if (o.CfaSuspected)
                {
                    reason += "\nПохоже, мешает защита Windows от программ-шантажистов. Нажмите «Как разрешить».";
                    actions.Add(new ToastAction("Как разрешить", ShowCfaHelp));
                }
                actions.Add(new ToastAction("Выбрать папку…", ChooseFolder));
                ShowToast("Сохранено в запасную папку", reason + "\nСнимок: " + path, ToastKind.Warning, img, actions, () => Shell.ShowInFolder(path), 20000);
            }
            else if (settings.ShowNotification)
            {
                ShowToast("Сохранено: " + Path.GetFileName(path), Path.GetDirectoryName(path) + (copied ? "\nТакже скопировано в буфер обмена." : ""),
                    ToastKind.Info, img, actions, () => Shell.ShowInFolder(path), 6000);
            }
        }

        private void ShowCfaHelp()
        {
            if (Dialogs.Ask(ErrorText.CfaHint + "\n\nОткрыть нужный раздел «Безопасности Windows» сейчас?"))
            {
                try { Shell.Open(ErrorText.CfaAllowAppUri); } catch { Dialogs.Warn("Не удалось открыть «Безопасность Windows». Откройте её через Пуск."); }
            }
        }

        private bool TryClipboard(Image img)
        {
            for (int i = 0; i < 5; i++)
            {
                try { Clipboard.SetImage(img); return true; }
                catch (ExternalException) { System.Threading.Thread.Sleep(60); } // буфер занят другой программой
                catch (Exception ex) { Log.Error("Буфер обмена", ex); return false; }
            }
            Log.Write("Буфер обмена занят — не удалось скопировать");
            return false;
        }

        // ---------- отмена / удаление ----------

        private void DeleteShot(string path)
        {
            try
            {
                if (!File.Exists(path)) { history.Remove(path); ShowToast("Файл уже удалён", Path.GetFileName(path), ToastKind.Info, null, null, null, 4000); return; }
                RecycleBin.Send(path);
                history.Remove(path);
                ShowToast("Скриншот перемещён в корзину", Path.GetFileName(path) + "\nВосстановить можно из Корзины.", ToastKind.Info, null,
                    new[] { new ToastAction("Открыть корзину", () => Shell.Open("shell:RecycleBinFolder")) }, null, 6000);
            }
            catch (Exception ex)
            {
                Log.Error("Удаление", ex);
                ShowToast("Не удалось удалить", ErrorText.Friendly(ex), ToastKind.Error, null, null, null, 8000);
            }
        }

        public void UndoLast()
        {
            string path = history.TakeLast(File.Exists);
            if (path == null)
            {
                ShowToast("Нечего отменять", "С момента запуска программы новых скриншотов нет (или они уже удалены).", ToastKind.Info, null, null, null, 4000);
                return;
            }
            try
            {
                RecycleBin.Send(path);
                Log.Write("Отменён (в корзину): " + path);
                ShowToast("Последний скриншот отменён", Path.GetFileName(path) + " перемещён в корзину.", ToastKind.Info, null,
                    new[] { new ToastAction("Открыть корзину", () => Shell.Open("shell:RecycleBinFolder")) }, null, 6000);
            }
            catch (Exception ex)
            {
                history.Push(path);
                Log.Error("Отмена", ex);
                ShowToast("Не удалось отменить", ErrorText.Friendly(ex), ToastKind.Error, null, null, null, 8000);
            }
        }

        // ---------- папка / настройки / автозапуск ----------

        private void CheckFolder(bool reportSuccess)
        {
            Exception probe = FolderRules.ProbeWrite(settings.SaveFolder);
            if (probe == null) { if (reportSuccess) Log.Write("Папка доступна: " + settings.SaveFolder); return; }
            Log.Error("Проверка папки " + settings.SaveFolder, probe);
            bool cfa = ErrorText.LooksLikeCfaBlock(probe, settings.SaveFolder);
            var actions = new System.Collections.Generic.List<ToastAction>();
            if (cfa) actions.Add(new ToastAction("Как разрешить", ShowCfaHelp));
            actions.Add(new ToastAction("Выбрать папку…", ChooseFolder));
            ShowToast("Папка для скриншотов недоступна",
                "«" + settings.SaveFolder + "»: " + ErrorText.Friendly(probe) + (cfa ? " Похоже, мешает защита от программ-шантажистов." : "") +
                "\nСнимки не потеряются: они будут сохраняться в запасную папку.", ToastKind.Warning, null, actions, null, 15000);
        }

        private void ChooseFolder()
        {
            if (settingsOpen) return;
            string p = FolderPicker.Pick(null, settings.SaveFolder);
            if (p == null) return;
            settings.SaveFolder = p;
            TrySaveSettings();
            ShowToast("Папка выбрана", "Скриншоты будут сохраняться в «" + p + "».", ToastKind.Info, null,
                new[] { new ToastAction("Открыть папку", () => Shell.OpenFolder(p)) }, null, 5000);
        }

        private void OpenSettings()
        {
            if (settingsOpen || gate.IsBusy) return;
            settingsOpen = true;
            hotkeys.UnregisterAll(); // чтобы нажатое сочетание попадало в поле, а не срабатывало
            try
            {
                using (DpiScope.SystemAware()) // Windows сама масштабирует окно на мониторах с разным DPI
                using (var f = new SettingsForm(settings.Clone(), Autostart.IsEnabled(), hotkeys.CanRegister, configDir))
                {
                    if (f.ShowDialog() == DialogResult.OK && f.Result != null)
                    {
                        bool folderChanged = !string.Equals(f.Result.SaveFolder, settings.SaveFolder, StringComparison.OrdinalIgnoreCase);
                        settings = f.Result;
                        TrySaveSettings();
                        SetAutostart(f.AutostartResult, false);
                        if (folderChanged) CheckFolder(true);
                    }
                }
            }
            catch (Exception ex) { Log.Error("Настройки", ex); Dialogs.Error("Окно настроек не открылось: " + ErrorText.Friendly(ex)); }
            finally
            {
                settingsOpen = false;
                RegisterHotkeys(false); // сочетания уже проверены в окне настроек
            }
        }

        private void TrySaveSettings()
        {
            try { settings.Save(settingsPath); }
            catch (Exception ex)
            {
                Log.Error("Сохранение настроек", ex);
                ShowToast("Настройки не сохранены", "Не удалось записать настройки в «" + settingsPath + "»: " + ErrorText.Friendly(ex) + " Они будут действовать до выхода из программы.", ToastKind.Warning, null, null, null, 10000);
            }
        }

        private void ToggleAutostart() { SetAutostart(!Autostart.IsEnabled(), true); }

        private void SetAutostart(bool on, bool checkLocation)
        {
            try
            {
                if (on == Autostart.IsEnabled() && !(on && Autostart.IsDisabledByWindows())) return;
                if (on)
                {
                    string warn = checkLocation ? Autostart.RiskyLocationWarning(Application.ExecutablePath, Autostart.DownloadsFolder(), Path.GetTempPath(), Autostart.IsRemovable) : null;
                    if (warn != null && !Dialogs.Ask(warn + "\n\nВсё равно включить автозапуск?")) return;
                    Autostart.Enable(Application.ExecutablePath);
                    ShowToast("Автозапуск включён", "Программа будет запускаться вместе с Windows (без прав администратора и без Планировщика заданий).", ToastKind.Info, null, null, null, 5000);
                }
                else
                {
                    Autostart.Disable();
                    ShowToast("Автозапуск выключен", "Программа больше не будет запускаться вместе с Windows.", ToastKind.Info, null, null, null, 4000);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Автозапуск", ex);
                ShowToast("Автозапуск не изменён", ErrorText.Friendly(ex), ToastKind.Error, null, null, null, 8000);
            }
        }

        // ---------- уведомления и ошибки ----------

        private void ShowToast(string title, string text, ToastKind kind, Image thumb, System.Collections.Generic.IList<ToastAction> actions, Action defaultAction, int timeout)
        {
            try
            {
                if (toast != null) { toast.CloseSafe(); toast = null; }
                var t = new ToastForm(title, text, kind, thumb, actions, defaultAction, timeout);
                t.FormClosed += (s, e) => { if (toast == t) toast = null; };
                toast = t;
                t.Show();
            }
            catch (Exception ex)
            {
                Log.Error("Уведомление", ex);
                try { tray.ShowBalloonTip(5000, title, text, kind == ToastKind.Error ? ToolTipIcon.Error : kind == ToastKind.Warning ? ToolTipIcon.Warning : ToolTipIcon.Info); } catch { }
            }
        }

        /// <summary>Любое исключение в обработчике — понятное сообщение и запись в журнал, без падения.</summary>
        private void Safe(string what, Action a)
        {
            try { a(); }
            catch (Exception ex) { ReportCrash(what, ex); }
        }

        public void ReportCrash(string what, Exception ex)
        {
            Log.Error(what, ex);
            if ((DateTime.Now - lastCrashToast).TotalSeconds < 5) return;
            lastCrashToast = DateTime.Now;
            ShowToast("Что-то пошло не так", "Операция «" + what + "» не выполнена: " + ErrorText.Friendly(ex) + "\nПрограмма продолжает работать. Подробности — в журнале.",
                ToastKind.Error, null, new[] { new ToastAction("Открыть журнал", () => Shell.Open(Log.FilePath)) }, null, 10000);
        }

        protected override void ExitThreadCore()
        {
            try { if (overlay != null) overlay.Cancel(); } catch { }
            try { if (toast != null) toast.CloseSafe(); } catch { }
            try { hotkeys.Dispose(); } catch { }
            try { tray.Visible = false; tray.Dispose(); } catch { }
            Log.Write("Выход");
            base.ExitThreadCore();
        }
    }

    /// <summary>Временно переключает поток в режим «system aware» (смешанный режим DPI, Windows 10 1607+).</summary>
    internal sealed class DpiScope : IDisposable
    {
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
        private readonly IntPtr old;
        private readonly bool active;

        private DpiScope(IntPtr ctx)
        {
            try { old = SetThreadDpiAwarenessContext(ctx); active = old != IntPtr.Zero; } catch { active = false; }
        }

        public static DpiScope SystemAware() { return new DpiScope(new IntPtr(-2)); }

        public void Dispose()
        {
            if (active) try { SetThreadDpiAwarenessContext(old); } catch { }
        }
    }
}
