using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace BystryjSkrin
{
    /// <summary>Понятные русские объяснения ошибок и распознавание типичных блокировок Windows.</summary>
    internal static class ErrorText
    {
        public const int ERROR_ACCESS_DENIED = 5, ERROR_SHARING_VIOLATION = 32, ERROR_HANDLE_DISK_FULL = 39,
            ERROR_FILE_EXISTS = 80, ERROR_DISK_FULL = 112, ERROR_NOT_READY = 21, ERROR_WRITE_PROTECT = 19,
            ERROR_BAD_NETPATH = 53, ERROR_NETNAME_DELETED = 64, ERROR_OPERATOR = 4320 /*0x10E0*/,
            ERROR_VIRUS_INFECTED = 225, ERROR_VIRUS_DELETED = 226, ERROR_DISK_QUOTA_EXCEEDED = 1295;

        public static int Win32Code(Exception ex)
        {
            int hr = ex == null ? 0 : ex.HResult;
            if ((hr & 0xFFFF0000) == 0x80070000) return hr & 0xFFFF;
            return 0;
        }

        public static bool IsDiskFull(Exception ex)
        {
            int c = Win32Code(ex);
            return c == ERROR_DISK_FULL || c == ERROR_HANDLE_DISK_FULL || c == ERROR_DISK_QUOTA_EXCEEDED;
        }

        public static bool IsAccessDenied(Exception ex)
        {
            return ex is UnauthorizedAccessException || Win32Code(ex) == ERROR_ACCESS_DENIED;
        }

        public static string Friendly(Exception ex)
        {
            if (ex == null) return "неизвестная ошибка.";
            int c = Win32Code(ex);
            if (IsDiskFull(ex)) return "на диске закончилось место.";
            if (c == ERROR_OPERATOR) return "Windows отклонила операцию («Оператор или администратор отклонил запрос»).";
            if (c == ERROR_WRITE_PROTECT) return "диск защищён от записи.";
            if (c == ERROR_NOT_READY) return "диск не готов (флешка или сетевой диск отключены?).";
            if (c == ERROR_BAD_NETPATH || c == ERROR_NETNAME_DELETED) return "сетевая папка недоступна.";
            if (c == ERROR_SHARING_VIOLATION) return "файл занят другой программой.";
            if (c == ERROR_VIRUS_INFECTED || c == ERROR_VIRUS_DELETED) return "запись заблокировал антивирус.";
            if (c >= 0x166 && c <= 0x18F) return "OneDrive или другое облачное хранилище не отвечает (запустите OneDrive или выберите обычную папку).";
            if (IsAccessDenied(ex)) return "нет прав на запись в папку.";
            if (ex is DriveNotFoundException) return "диск не найден (возможно, флешка отключена).";
            if (ex is DirectoryNotFoundException) return "папка недоступна (возможно, диск отключён).";
            if (ex is PathTooLongException) return "слишком длинный путь к папке.";
            if (ex is OutOfMemoryException) return "не хватило памяти для снимка.";
            if (ex is ExternalException) return "ошибка графической подсистемы Windows.";
            if (ex is IOException) return "ошибка записи на диск (" + ex.Message.TrimEnd('.') + ").";
            return "непредвиденная ошибка (" + ex.Message.TrimEnd('.') + ").";
        }

        // ---------- Контролируемый доступ к папкам (защита от программ-шантажистов) ----------

        public const string CfaAllowAppUri = "windowsdefender://allowappthroughfolder/";

        /// <summary>true — включён в режиме блокировки, false — выключен/аудит, null — неизвестно.</summary>
        public static bool? ControlledFolderAccessEnabled()
        {
            string[] keys =
            {
                @"SOFTWARE\Policies\Microsoft\Windows Defender\Windows Defender Exploit Guard\Controlled Folder Access",
                @"SOFTWARE\Microsoft\Windows Defender\Windows Defender Exploit Guard\Controlled Folder Access",
            };
            bool? result = null;
            foreach (string k in keys)
            {
                try
                {
                    using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                    using (var key = hklm.OpenSubKey(k))
                    {
                        object v = key == null ? null : key.GetValue("EnableControlledFolderAccess");
                        if (v is int)
                        {
                            int mode = (int)v;
                            if (mode == 1) return true; // блокировка
                            result = false;
                        }
                    }
                }
                catch { }
            }
            return result;
        }

        /// <summary>Папки, которые защищает контролируемый доступ (в т.ч. перенаправленные в OneDrive).</summary>
        public static IEnumerable<string> CfaProtectedRoots()
        {
            var f = new[]
            {
                Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.MyPictures, Environment.SpecialFolder.MyVideos,
                Environment.SpecialFolder.MyMusic, Environment.SpecialFolder.Desktop, Environment.SpecialFolder.Favorites,
                Environment.SpecialFolder.CommonDocuments, Environment.SpecialFolder.CommonPictures, Environment.SpecialFolder.CommonVideos,
                Environment.SpecialFolder.CommonMusic, Environment.SpecialFolder.CommonDesktopDirectory,
            };
            foreach (var sf in f)
            {
                string p = null;
                try { p = Environment.GetFolderPath(sf); } catch { }
                if (!string.IsNullOrEmpty(p)) yield return p;
            }
        }

        public static bool IsUnderAny(string path, IEnumerable<string> roots)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string p = path.TrimEnd('\\') + "\\";
            foreach (string r in roots)
                if (p.StartsWith(r.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Похоже ли, что запись заблокировал «Контролируемый доступ к папкам».</summary>
        public static bool LooksLikeCfaBlock(Exception ex, string folder, bool? cfaEnabled, IEnumerable<string> protectedRoots)
        {
            if (!IsAccessDenied(ex)) return false;
            if (cfaEnabled == false) return false;
            return IsUnderAny(folder, protectedRoots);
        }

        public static bool LooksLikeCfaBlock(Exception ex, string folder)
        {
            if (!IsAccessDenied(ex)) return false;
            return LooksLikeCfaBlock(ex, folder, ControlledFolderAccessEnabled(), CfaProtectedRoots());
        }

        public const string CfaHint =
            "Похоже, запись заблокировала защита Windows от программ-шантажистов («Контролируемый доступ к папкам»). " +
            "Чтобы сохранять в эту папку, разрешите программу: Безопасность Windows → Защита от вирусов и угроз → " +
            "Управление защитой от программ-шантажистов → «Разрешить работу приложения через контролируемый доступ к папкам» → " +
            "добавьте BystryjSkrin.exe. Или выберите папку вне «Изображений» и «Документов».";
    }
}
