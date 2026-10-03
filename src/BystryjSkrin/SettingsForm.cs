using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace BystryjSkrin
{
    /// <summary>Поле, в котором нажимают нужное сочетание клавиш.</summary>
    internal sealed class HotkeyBox : TextBox
    {
        private Hotkey value;
        public event EventHandler HotkeyChanged;

        public HotkeyBox()
        {
            ReadOnly = true;
            BackColor = SystemColors.Window;
            ShortcutsEnabled = false;
            TextAlign = HorizontalAlignment.Center;
        }

        public Hotkey Value
        {
            get { return value; }
            set { this.value = value; Text = value.IsEmpty ? "" : value.ToString().Replace("+", " + "); }
        }

        private static Mods CurrentMods(Keys modifiers)
        {
            Mods m = Mods.None;
            if ((modifiers & Keys.Control) != 0) m |= Mods.Ctrl;
            if ((modifiers & Keys.Shift) != 0) m |= Mods.Shift;
            if ((modifiers & Keys.Alt) != 0) m |= Mods.Alt;
            if ((Native.GetKeyState(Native.VK_LWIN) & 0x8000) != 0 || (Native.GetKeyState(Native.VK_RWIN) & 0x8000) != 0) m |= Mods.Win;
            return m;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            if (k == Keys.Tab && (keyData & Keys.Modifiers) == 0) return false; // Tab — переход к следующему полю
            return true;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Перехватываем Alt+… и прочее, чтобы они не уходили в меню/кнопки
            if (Focused && (keyData & Keys.KeyCode) != Keys.Tab && (keyData & Keys.KeyCode) != Keys.Escape)
            {
                if (msg.Msg == 0x0100 || msg.Msg == 0x0104) { HandleKeys(keyData); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void HandleKeys(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            Mods m = CurrentMods(keyData & Keys.Modifiers);
            if (Hotkey.IsModifierKey(k))
            {
                Text = string.Join(" + ", new[] { (m & Mods.Ctrl) != 0 ? "Ctrl" : null, (m & Mods.Alt) != 0 ? "Alt" : null, (m & Mods.Shift) != 0 ? "Shift" : null, (m & Mods.Win) != 0 ? "Win" : null }.Where(x => x != null)) + " + …";
                return;
            }
            if ((k == Keys.Back || k == Keys.Delete) && m == Mods.None) { Value = value; return; }
            Value = new Hotkey(m, k);
            var h = HotkeyChanged; if (h != null) h(this, EventArgs.Empty);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            // PrintScreen приходит только как KeyUp
            if (e.KeyCode == Keys.PrintScreen) { HandleKeys(e.KeyData); e.Handled = true; return; }
            if (Text.EndsWith("…", StringComparison.Ordinal) && e.Modifiers == Keys.None) Value = value;
            base.OnKeyUp(e);
        }

        protected override void OnLostFocus(EventArgs e) { Value = value; base.OnLostFocus(e); }
    }

    internal sealed class SettingsForm : Form
    {
        private readonly TextBox folderBox = new TextBox { Width = 330 };
        private readonly Button browse = new Button { Text = "Обзор…", AutoSize = true };
        private readonly ComboBox format = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
        private readonly NumericUpDown quality = new NumericUpDown { Minimum = 30, Maximum = 100, Width = 60 };
        private readonly HotkeyBox captureKey = new HotkeyBox { Width = 200 };
        private readonly CheckBox undoEnabled = new CheckBox { Text = "Отменить последний скриншот:", AutoSize = true };
        private readonly HotkeyBox undoKey = new HotkeyBox { Width = 200 };
        private readonly CheckBox clipboard = new CheckBox { Text = "Также копировать снимок в буфер обмена", AutoSize = true };
        private readonly CheckBox notify = new CheckBox { Text = "Показывать уведомление «Сохранено» (с кнопками «Открыть» и «Удалить»)", AutoSize = true };
        private readonly CheckBox sound = new CheckBox { Text = "Звук затвора при сохранении", AutoSize = true };
        private readonly CheckBox guides = new CheckBox { Text = "Линии-перекрестие при выделении", AutoSize = true };
        private readonly CheckBox autostart = new CheckBox { Text = "Запускать вместе с Windows", AutoSize = true };
        private readonly Label status = new Label { AutoSize = true, ForeColor = Color.Firebrick, MaximumSize = new Size(560, 0) };
        private readonly Button ok = new Button { Text = "Сохранить", AutoSize = true };
        private readonly Button cancel = new Button { Text = "Отмена", AutoSize = true, DialogResult = DialogResult.Cancel };
        private readonly Button reset = new Button { Text = "По умолчанию", AutoSize = true };
        private readonly LinkLabel logLink = new LinkLabel { Text = "Папка настроек и журнала ошибок", AutoSize = true };

        private readonly Func<Hotkey, bool> canRegister;
        private readonly string configDir;

        public AppSettings Result { get; private set; }
        public bool AutostartResult { get; private set; }

        public SettingsForm(AppSettings current, bool autostartOn, Func<Hotkey, bool> canRegister, string configDir)
        {
            this.canRegister = canRegister;
            this.configDir = configDir;
            Text = "Быстрый скрин — настройки";
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.Font;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            TopMost = true;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12);
            try { Icon = TrayApp.LoadAppIcon(32); } catch { }

            format.Items.AddRange(new object[] { "PNG", "JPG" });
            var t = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            Func<string, Label> L = s => new Label { Text = s, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 3) };
            Func<Control[], FlowLayoutPanel> Row = cs => { var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) }; f.Controls.AddRange(cs); return f; };

            t.Controls.Add(L("Папка для скриншотов:"), 0, 0); t.Controls.Add(Row(new Control[] { folderBox, browse }), 1, 0);
            t.Controls.Add(L("Формат файла:"), 0, 1);
            t.Controls.Add(Row(new Control[] { format, new Label { Text = "качество JPG:", AutoSize = true, Margin = new Padding(12, 7, 3, 3) }, quality }), 1, 1);
            t.Controls.Add(L("Сделать скриншот:"), 0, 2);
            t.Controls.Add(Row(new Control[] { captureKey, new Label { Text = "щёлкните и нажмите сочетание", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(6, 7, 3, 3) } }), 1, 2);
            undoEnabled.Margin = new Padding(3, 6, 3, 3);
            t.Controls.Add(undoEnabled, 0, 3); t.Controls.Add(undoKey, 1, 3);
            int row = 4;
            foreach (var cb in new[] { clipboard, notify, sound, guides, autostart })
            {
                cb.Margin = new Padding(3, 4, 3, 2);
                t.Controls.Add(cb, 0, row); t.SetColumnSpan(cb, 2); row++;
            }
            status.Margin = new Padding(3, 10, 3, 3);
            t.Controls.Add(status, 0, row); t.SetColumnSpan(status, 2); row++;
            logLink.Margin = new Padding(3, 6, 3, 3);
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
            buttons.Controls.AddRange(new Control[] { cancel, ok, reset });
            t.Controls.Add(logLink, 0, row); t.Controls.Add(buttons, 1, row);
            Controls.Add(t);

            AcceptButton = ok; CancelButton = cancel;
            Fill(current, autostartOn);

            browse.Click += (s, e) =>
            {
                string p = FolderPicker.Pick(this, folderBox.Text);
                if (p != null) folderBox.Text = p;
            };
            format.SelectedIndexChanged += (s, e) => quality.Enabled = format.SelectedIndex == 1;
            undoEnabled.CheckedChanged += (s, e) => undoKey.Enabled = undoEnabled.Checked;
            captureKey.HotkeyChanged += (s, e) => ShowHotkeyHint(captureKey.Value);
            undoKey.HotkeyChanged += (s, e) => ShowHotkeyHint(undoKey.Value);
            reset.Click += (s, e) => { Fill(AppSettings.CreateDefault(), autostart.Checked); status.Text = "Значения по умолчанию подставлены — нажмите «Сохранить»."; status.ForeColor = SystemColors.ControlText; };
            ok.Click += (s, e) => TrySave();
            logLink.LinkClicked += (s, e) => { try { Shell.OpenFolder(configDir); } catch { } };
        }

        private void Fill(AppSettings s, bool autostartOn)
        {
            folderBox.Text = s.SaveFolder;
            format.SelectedIndex = s.Format == ImageFormatKind.Jpg ? 1 : 0;
            quality.Value = Math.Max(30, Math.Min(100, s.JpegQuality));
            quality.Enabled = s.Format == ImageFormatKind.Jpg;
            captureKey.Value = s.CaptureHotkey;
            undoEnabled.Checked = s.UndoHotkeyEnabled;
            undoKey.Value = s.UndoHotkey;
            undoKey.Enabled = s.UndoHotkeyEnabled;
            clipboard.Checked = s.CopyToClipboard;
            notify.Checked = s.ShowNotification;
            sound.Checked = s.PlaySound;
            guides.Checked = s.ShowGuides;
            autostart.Checked = autostartOn;
        }

        private void ShowHotkeyHint(Hotkey h)
        {
            string err = Hotkey.Validate(h);
            if (err != null) { status.ForeColor = Color.Firebrick; status.Text = h + ": " + err; }
            else if (!canRegister(h)) { status.ForeColor = Color.Firebrick; status.Text = h + " уже занято другой программой — выберите другое."; }
            else { status.ForeColor = Color.DarkGreen; status.Text = h + " — свободно."; }
        }

        private void Fail(string text, Control focus)
        {
            status.ForeColor = Color.Firebrick;
            status.Text = text;
            if (focus != null) focus.Focus();
            System.Media.SystemSounds.Exclamation.Play();
        }

        private void TrySave()
        {
            var s = new AppSettings
            {
                SaveFolder = folderBox.Text.Trim(),
                Format = format.SelectedIndex == 1 ? ImageFormatKind.Jpg : ImageFormatKind.Png,
                JpegQuality = (int)quality.Value,
                CaptureHotkey = captureKey.Value,
                UndoHotkeyEnabled = undoEnabled.Checked,
                UndoHotkey = undoKey.Value,
                CopyToClipboard = clipboard.Checked,
                ShowNotification = notify.Checked,
                PlaySound = sound.Checked,
                ShowGuides = guides.Checked,
            };
            var errors = s.Validate();
            if (errors.Count > 0) { Fail(string.Join("\n", errors), errors[0].StartsWith("Папка") ? (Control)folderBox : captureKey); return; }
            s.SaveFolder = FolderRules.Normalize(s.SaveFolder);

            // Папку создаём сразу и проверяем, что в неё можно писать
            Exception probe = FolderRules.ProbeWrite(s.SaveFolder);
            if (probe != null)
            {
                string msg = "В папку «" + s.SaveFolder + "» не получается записать: " + ErrorText.Friendly(probe);
                if (ErrorText.LooksLikeCfaBlock(probe, s.SaveFolder)) msg += "\n\n" + ErrorText.CfaHint;
                msg += "\n\nВсё равно оставить эту папку? Снимки будут попадать в запасную папку, пока доступ не появится.";
                if (!Dialogs.Ask(msg, Handle)) { Fail("Выберите другую папку.", folderBox); return; }
            }

            // Свободны ли сочетания? Если нет — сразу предлагаем замену
            if (!canRegister(s.CaptureHotkey))
            {
                Hotkey? alt = Hotkey.FindFree(Hotkey.CaptureAlternatives, canRegister, s.UndoHotkey);
                if (alt.HasValue) captureKey.Value = alt.Value;
                Fail("Сочетание " + s.CaptureHotkey + " уже занято другой программой." +
                     (alt.HasValue ? " Подставлено свободное " + alt.Value + " — нажмите «Сохранить» ещё раз или задайте своё." : " Задайте другое."), captureKey);
                return;
            }
            if (s.UndoHotkeyEnabled && !canRegister(s.UndoHotkey))
            {
                Hotkey? alt = Hotkey.FindFree(Hotkey.UndoAlternatives, canRegister, s.CaptureHotkey);
                if (alt.HasValue) undoKey.Value = alt.Value;
                Fail("Сочетание " + s.UndoHotkey + " уже занято другой программой." +
                     (alt.HasValue ? " Подставлено свободное " + alt.Value + " — нажмите «Сохранить» ещё раз или задайте своё." : " Задайте другое или отключите его."), undoKey);
                return;
            }

            if (autostart.Checked)
            {
                string warn = Autostart.RiskyLocationWarning(Application.ExecutablePath, Autostart.DownloadsFolder(), Path.GetTempPath(), Autostart.IsRemovable);
                if (warn != null && !Dialogs.Ask(warn + "\n\nВсё равно включить автозапуск?", Handle)) { autostart.Checked = false; return; }
            }

            Result = s;
            AutostartResult = autostart.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
