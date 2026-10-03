using System;
using System.Globalization;
using System.IO;

namespace BystryjSkrin
{
    internal static class FileNaming
    {
        public const string Prefix = "Скриншот";

        public static string Extension(ImageFormatKind format) { return format == ImageFormatKind.Jpg ? ".jpg" : ".png"; }

        /// <summary>«Скриншот 2026-10-03 14-25-07» (без расширения).</summary>
        public static string BaseName(DateTime time)
        {
            return Prefix + " " + time.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture);
        }

        /// <summary>Имя-кандидат номер n: n=1 → «… 14-25-07.png», n=2 → «… 14-25-07 (2).png».</summary>
        public static string Candidate(DateTime time, ImageFormatKind format, int n)
        {
            return BaseName(time) + (n <= 1 ? "" : " (" + n.ToString(CultureInfo.InvariantCulture) + ")") + Extension(format);
        }

        /// <summary>Первый свободный путь в папке (существующие файлы не перезаписываются).</summary>
        public static string UniquePath(string folder, DateTime time, ImageFormatKind format, Func<string, bool> exists = null)
        {
            if (exists == null) exists = File.Exists;
            for (int n = 1; n < 100000; n++)
            {
                string p = Path.Combine(folder, Candidate(time, format, n));
                if (!exists(p)) return p;
            }
            throw new IOException("Не удалось подобрать свободное имя файла");
        }
    }
}
