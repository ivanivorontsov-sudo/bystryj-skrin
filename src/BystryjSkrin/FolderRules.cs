using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BystryjSkrin
{
    /// <summary>Проверка папки для сохранения: защита от системных и неверных путей.</summary>
    internal static class FolderRules
    {
        private static List<string> systemRootsOverride;

        /// <summary>Только для тестов.</summary>
        internal static void OverrideSystemRoots(IEnumerable<string> roots)
        {
            systemRootsOverride = roots == null ? null : roots.ToList();
        }

        public static IEnumerable<string> SystemRoots()
        {
            if (systemRootsOverride != null) return systemRootsOverride;
            var list = new List<string>();
            Action<string> add = p => { if (!string.IsNullOrEmpty(p)) list.Add(p); };
            add(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            add(Environment.GetFolderPath(Environment.SpecialFolder.System));
            add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            add(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)); // ProgramData
            add(Environment.GetEnvironmentVariable("ProgramW6432"));
            return list;
        }

        public static string Normalize(string path)
        {
            string full = Path.GetFullPath(path.Trim());
            string root = Path.GetPathRoot(full);
            if (full.Length > root.Length) full = full.TrimEnd('\\', '/');
            return full;
        }

        private static bool IsUnder(string path, string root)
        {
            string p = path.TrimEnd('\\') + "\\", r = root.TrimEnd('\\') + "\\";
            return p.StartsWith(r, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>null — папка подходит; иначе объяснение, почему нет.</summary>
        public static string Validate(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "папка не указана.";
            path = path.Trim();
            if (path.Length > 240) return "слишком длинный путь (больше 240 символов).";
            // Недопустимые символы (двоеточие допустимо только после буквы диска)
            char[] bad = Path.GetInvalidPathChars().Concat(new[] { '*', '?', '"', '<', '>', '|' }).ToArray();
            if (path.IndexOfAny(bad) >= 0) return "в пути есть недопустимые символы (* ? \" < > |).";
            if (path.Length > 2 && path.IndexOf(':', 2) >= 0) return "двоеточие допустимо только после буквы диска (C:\\…).";
            bool unc = path.StartsWith(@"\\", StringComparison.Ordinal);
            bool drive = path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');
            if (!unc && !drive) return "укажите полный путь, например C:\\Users\\Иван\\Pictures\\Скриншоты.";
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal))
                return "служебные пути вида \\\\?\\ не поддерживаются.";
            string full;
            try { full = Normalize(path); }
            catch (Exception) { return "путь записан неверно."; }

            string root = Path.GetPathRoot(full) ?? "";
            if (string.Equals(full.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                return "нельзя сохранять в корень диска — выберите или создайте папку.";
            foreach (string seg in path.Split('\\', '/'))
            {
                if (seg.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase) || seg.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase))
                    return "это служебная папка Windows.";
                if (seg.EndsWith(".", StringComparison.Ordinal) && seg != "." && seg.Length > 0)
                    return "имена папок не могут оканчиваться точкой.";
            }
            foreach (string sys in SystemRoots())
                if (IsUnder(full, sys)) return "это системная папка (" + sys + "). Выберите папку в своих документах или изображениях.";
            return null;
        }

        /// <summary>Пробная запись: создаёт папку (если нужно) и временный файл. null — запись возможна.</summary>
        public static Exception ProbeWrite(string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                string probe = Path.Combine(folder, ".bystryj-skrin-proba-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");
                using (var fs = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.DeleteOnClose))
                    fs.WriteByte(0);
                return null;
            }
            catch (Exception ex) { return ex; }
        }
    }
}
