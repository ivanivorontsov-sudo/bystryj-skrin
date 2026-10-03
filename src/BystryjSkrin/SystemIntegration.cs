using System;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace BystryjSkrin
{
    /// <summary>Автозапуск только через HKCU\...\Run: без Планировщика заданий (там и возникает
    /// «Оператор или администратор отклонил запрос», 0x800710E0) и без прав администратора.</summary>
    internal static class Autostart
    {
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        public const string ValueName = "BystryjSkrin";

        public static string CommandFor(string exePath) { return "\"" + exePath + "\" --autostart"; }

        public static string ExePathFromCommand(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return null;
            cmd = cmd.Trim();
            if (cmd.StartsWith("\"", StringComparison.Ordinal))
            {
                int end = cmd.IndexOf('"', 1);
                return end > 1 ? cmd.Substring(1, end - 1) : null;
            }
            int sp = cmd.IndexOf(' ');
            return sp > 0 ? cmd.Substring(0, sp) : cmd;
        }

        public static bool IsEnabled()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue(ValueName) is string; }
            catch { return false; }
        }

        /// <summary>Отключён ли автозапуск в «Диспетчер задач → Автозагрузка» / «Параметры → Приложения → Автозагрузка».</summary>
        public static bool IsDisabledByWindows()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(ApprovedKey))
                {
                    var v = k == null ? null : k.GetValue(ValueName) as byte[];
                    return v != null && v.Length > 0 && (v[0] & 1) == 1;
                }
            }
            catch { return false; }
        }

        public static void Enable(string exePath)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                k.SetValue(ValueName, CommandFor(exePath), RegistryValueKind.String);
            // Пользователь явно включил — снимаем отметку «Отключено» из Диспетчера задач
            try { using (var k = Registry.CurrentUser.OpenSubKey(ApprovedKey, true)) if (k != null) k.DeleteValue(ValueName, false); } catch { }
        }

        public static void Disable()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true)) if (k != null) k.DeleteValue(ValueName, false);
        }

        /// <summary>Если exe перенесли — обновить путь в автозапуске.</summary>
        public static void RefreshPath(string exePath)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    string cur = k == null ? null : k.GetValue(ValueName) as string;
                    if (cur != null && !string.Equals(ExePathFromCommand(cur), exePath, StringComparison.OrdinalIgnoreCase))
                    {
                        k.SetValue(ValueName, CommandFor(exePath), RegistryValueKind.String);
                        Log.Write("Путь автозапуска обновлён: " + exePath);
                    }
                }
            }
            catch (Exception ex) { Log.Error("Обновление автозапуска", ex); }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

        public static string DownloadsFolder()
        {
            try
            {
                IntPtr p;
                if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out p) == 0)
                {
                    string s = Marshal.PtrToStringUni(p);
                    Marshal.FreeCoTaskMem(p);
                    return s;
                }
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        /// <summary>Предупреждение, если программа запущена из места, откуда автозапуск сломается.</summary>
        public static string RiskyLocationWarning(string exePath, string downloads, string temp, Func<string, bool> isRemovable)
        {
            if (string.IsNullOrEmpty(exePath)) return null;
            if (ErrorText.IsUnderAny(exePath, new[] { temp }))
                return "Программа запущена из временной папки (возможно, прямо из архива). Распакуйте BystryjSkrin.exe в постоянную папку, иначе автозапуск не сработает.";
            if (isRemovable != null && isRemovable(exePath))
                return "Программа запущена со съёмного диска. Если его отключить, автозапуск не сработает — лучше скопировать exe на компьютер.";
            if (!string.IsNullOrEmpty(downloads) && ErrorText.IsUnderAny(exePath, new[] { downloads }))
                return "Программа лежит в «Загрузках». Лучше перенести её в постоянную папку (например, C:\\Users\\<имя>\\Программы\\), чтобы её случайно не удалили при очистке загрузок.";
            return null;
        }

        public static bool IsRemovable(string path)
        {
            try { return new DriveInfo(Path.GetPathRoot(path)).DriveType == DriveType.Removable; } catch { return false; }
        }
    }

    internal static class RecycleBin
    {
        /// <summary>Перемещает файл в Корзину (можно восстановить).</summary>
        public static void Send(string path)
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
        }
    }

    internal static class Shell
    {
        public static void Open(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Error("Открытие " + target, ex); throw; }
        }

        public static void ShowInFolder(string file)
        {
            if (File.Exists(file)) Process.Start("explorer.exe", "/select,\"" + file + "\"");
            else Open(Path.GetDirectoryName(file));
        }

        public static void OpenFolder(string folder)
        {
            Directory.CreateDirectory(folder);
            Process.Start("explorer.exe", "\"" + folder + "\"");
        }
    }

    internal static class Sounds
    {
        private static byte[] shutter;

        /// <summary>Короткий синтезированный «щелчок затвора» (чтобы не зависеть от системных звуков).</summary>
        public static byte[] ShutterWav()
        {
            if (shutter != null) return shutter;
            const int rate = 22050;
            int n = rate * 12 / 100;
            var rnd = new Random(7);
            var pcm = new short[n];
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / rate;
                double env = Math.Exp(-t * 60) + 0.6 * (t > 0.05 ? Math.Exp(-(t - 0.05) * 70) : 0);
                pcm[i] = (short)(Math.Max(-1, Math.Min(1, (rnd.NextDouble() * 2 - 1) * env * 0.5)) * short.MaxValue);
            }
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + n * 2); w.Write(new[] { 'W', 'A', 'V', 'E' });
                w.Write(new[] { 'f', 'm', 't', ' ' }); w.Write(16); w.Write((short)1); w.Write((short)1);
                w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(n * 2);
                foreach (short s in pcm) w.Write(s);
                w.Flush();
                shutter = ms.ToArray();
            }
            return shutter;
        }

        public static void PlayShutter()
        {
            try { using (var p = new SoundPlayer(new MemoryStream(ShutterWav()))) p.Play(); }
            catch (Exception ex) { Log.Error("Звук", ex); }
        }
    }

    internal static class Dialogs
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

        public const string Title = "Быстрый скрин";
        private const uint MB_OK = 0, MB_YESNO = 4, MB_ICONERROR = 0x10, MB_ICONQUESTION = 0x20, MB_ICONWARNING = 0x30,
            MB_ICONINFO = 0x40, MB_TOPMOST = 0x40000, MB_SETFOREGROUND = 0x10000;

        public static void Info(string text, IntPtr owner = default(IntPtr)) { MessageBoxW(owner, text, Title, MB_OK | MB_ICONINFO | MB_TOPMOST | MB_SETFOREGROUND); }
        public static void Warn(string text, IntPtr owner = default(IntPtr)) { MessageBoxW(owner, text, Title, MB_OK | MB_ICONWARNING | MB_TOPMOST | MB_SETFOREGROUND); }
        public static void Error(string text, IntPtr owner = default(IntPtr)) { MessageBoxW(owner, text, Title, MB_OK | MB_ICONERROR | MB_TOPMOST | MB_SETFOREGROUND); }
        public static bool Ask(string text, IntPtr owner = default(IntPtr))
        {
            return MessageBoxW(owner, text, Title, MB_YESNO | MB_ICONQUESTION | MB_TOPMOST | MB_SETFOREGROUND) == 6;
        }
    }
}
