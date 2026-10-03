using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace BystryjSkrin
{
    internal enum ImageFormatKind { Png, Jpg }

    internal sealed class AppSettings
    {
        public string SaveFolder;
        public ImageFormatKind Format = ImageFormatKind.Png;
        public int JpegQuality = 90;
        public bool CopyToClipboard = true;
        public bool ShowNotification = true;
        public bool PlaySound = false;
        public bool ShowGuides = true;
        public Hotkey CaptureHotkey = Hotkey.DefaultCapture;
        public bool UndoHotkeyEnabled = true;
        public Hotkey UndoHotkey = Hotkey.DefaultUndo;

        /// <summary>Предупреждения, появившиеся при загрузке (повреждённый файл, неверная папка и т.п.).</summary>
        public readonly List<string> LoadWarnings = new List<string>();

        public static string DefaultFolder()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Скриншоты");
        }

        public static AppSettings CreateDefault(string defaultFolder = null)
        {
            return new AppSettings { SaveFolder = defaultFolder ?? DefaultFolder() };
        }

        public AppSettings Clone()
        {
            var c = (AppSettings)MemberwiseClone();
            return c;
        }

        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Быстрый скрин — настройки. Файл можно удалить: будут настройки по умолчанию.");
            sb.AppendLine("SaveFolder=" + SaveFolder);
            sb.AppendLine("Format=" + (Format == ImageFormatKind.Jpg ? "jpg" : "png"));
            sb.AppendLine("JpegQuality=" + JpegQuality.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("CopyToClipboard=" + B(CopyToClipboard));
            sb.AppendLine("ShowNotification=" + B(ShowNotification));
            sb.AppendLine("PlaySound=" + B(PlaySound));
            sb.AppendLine("ShowGuides=" + B(ShowGuides));
            sb.AppendLine("CaptureHotkey=" + CaptureHotkey);
            sb.AppendLine("UndoHotkeyEnabled=" + B(UndoHotkeyEnabled));
            sb.AppendLine("UndoHotkey=" + UndoHotkey);
            return sb.ToString();
        }

        private static string B(bool v) { return v ? "true" : "false"; }

        private static bool TryBool(string s, out bool v)
        {
            s = (s ?? "").Trim().ToLowerInvariant();
            if (s == "true" || s == "1" || s == "yes" || s == "да") { v = true; return true; }
            if (s == "false" || s == "0" || s == "no" || s == "нет") { v = false; return true; }
            v = false; return false;
        }

        /// <summary>Разбор текста настроек. Неверные значения заменяются значениями по умолчанию с предупреждением.</summary>
        public static AppSettings Parse(string text, string defaultFolder = null)
        {
            var s = CreateDefault(defaultFolder);
            if (text == null) return s;
            foreach (string raw in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) { s.LoadWarnings.Add("Непонятная строка в настройках пропущена: " + line); continue; }
                string key = line.Substring(0, eq).Trim(), val = line.Substring(eq + 1).Trim();
                bool b; int i; Hotkey h;
                switch (key)
                {
                    case "SaveFolder": s.SaveFolder = val; break;
                    case "Format":
                        if (val.Equals("jpg", StringComparison.OrdinalIgnoreCase) || val.Equals("jpeg", StringComparison.OrdinalIgnoreCase)) s.Format = ImageFormatKind.Jpg;
                        else if (val.Equals("png", StringComparison.OrdinalIgnoreCase)) s.Format = ImageFormatKind.Png;
                        else s.LoadWarnings.Add("Неизвестный формат «" + val + "», используется PNG.");
                        break;
                    case "JpegQuality":
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) s.JpegQuality = i;
                        else s.LoadWarnings.Add("Неверное качество JPG, используется 90.");
                        break;
                    case "CopyToClipboard": if (TryBool(val, out b)) s.CopyToClipboard = b; break;
                    case "ShowNotification": if (TryBool(val, out b)) s.ShowNotification = b; break;
                    case "PlaySound": if (TryBool(val, out b)) s.PlaySound = b; break;
                    case "ShowGuides": if (TryBool(val, out b)) s.ShowGuides = b; break;
                    case "UndoHotkeyEnabled": if (TryBool(val, out b)) s.UndoHotkeyEnabled = b; break;
                    case "CaptureHotkey":
                        if (Hotkey.TryParse(val, out h)) s.CaptureHotkey = h;
                        else s.LoadWarnings.Add("Неверное сочетание «" + val + "», используется " + Hotkey.DefaultCapture + ".");
                        break;
                    case "UndoHotkey":
                        if (Hotkey.TryParse(val, out h)) s.UndoHotkey = h;
                        else s.LoadWarnings.Add("Неверное сочетание отмены «" + val + "», используется " + Hotkey.DefaultUndo + ".");
                        break;
                }
            }
            s.Sanitize(defaultFolder);
            return s;
        }

        /// <summary>Исправляет недопустимые значения (защита от ручной правки файла и ошибок).</summary>
        public void Sanitize(string defaultFolder = null)
        {
            if (JpegQuality < 30 || JpegQuality > 100)
            {
                int fixedQ = Math.Max(30, Math.Min(100, JpegQuality));
                LoadWarnings.Add("Качество JPG должно быть от 30 до 100 — установлено " + fixedQ + ".");
                JpegQuality = fixedQ;
            }
            string folderError = FolderRules.Validate(SaveFolder);
            if (folderError != null)
            {
                LoadWarnings.Add("Папка «" + SaveFolder + "» не подходит: " + folderError + " Используется папка по умолчанию.");
                SaveFolder = defaultFolder ?? DefaultFolder();
            }
            else SaveFolder = FolderRules.Normalize(SaveFolder);

            if (Hotkey.Validate(CaptureHotkey) != null)
            {
                LoadWarnings.Add("Сочетание " + CaptureHotkey + " не подходит, используется " + Hotkey.DefaultCapture + ".");
                CaptureHotkey = Hotkey.DefaultCapture;
            }
            if (Hotkey.Validate(UndoHotkey) != null || UndoHotkey == CaptureHotkey)
            {
                UndoHotkey = CaptureHotkey == Hotkey.DefaultUndo ? new Hotkey(Mods.Ctrl | Mods.Alt, System.Windows.Forms.Keys.Z) : Hotkey.DefaultUndo;
                if (UndoHotkey == CaptureHotkey) UndoHotkeyEnabled = false;
            }
        }

        /// <summary>Полная проверка перед сохранением из окна настроек. Пустой список — всё хорошо.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            string fe = FolderRules.Validate(SaveFolder);
            if (fe != null) errors.Add("Папка: " + fe);
            if (JpegQuality < 30 || JpegQuality > 100) errors.Add("Качество JPG должно быть от 30 до 100.");
            string he = Hotkey.Validate(CaptureHotkey);
            if (he != null) errors.Add("Сочетание для скриншота: " + he);
            if (UndoHotkeyEnabled)
            {
                string ue = Hotkey.Validate(UndoHotkey);
                if (ue != null) errors.Add("Сочетание для отмены: " + ue);
                else if (UndoHotkey == CaptureHotkey) errors.Add("Сочетания для скриншота и для отмены должны различаться.");
            }
            return errors;
        }

        public static AppSettings Load(string path, string defaultFolder = null)
        {
            if (!File.Exists(path)) return CreateDefault(defaultFolder);
            string text;
            try { text = File.ReadAllText(path, Encoding.UTF8); }
            catch (Exception ex)
            {
                Log.Error("Чтение настроек", ex);
                var d = CreateDefault(defaultFolder);
                d.LoadWarnings.Add("Не удалось прочитать настройки, используются настройки по умолчанию.");
                return d;
            }
            if (text.IndexOf('\0') >= 0)
            {
                // Файл испорчен (например, после сбоя питания) — сохраняем копию и начинаем заново
                try { File.Copy(path, path + ".broken", true); } catch { }
                var d = CreateDefault(defaultFolder);
                d.LoadWarnings.Add("Файл настроек был повреждён и сброшен (копия: settings.ini.broken).");
                return d;
            }
            return Parse(text, defaultFolder);
        }

        /// <summary>Атомарное сохранение: сначала во временный файл, затем замена.</summary>
        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, Serialize(), new UTF8Encoding(false));
            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, null); return; } catch (Exception ex) { Log.Error("File.Replace настроек", ex); }
                File.Copy(tmp, path, true);
                File.Delete(tmp);
            }
            else File.Move(tmp, path);
        }
    }
}
