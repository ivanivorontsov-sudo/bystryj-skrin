using System;
using System.IO;
using System.Text;

namespace BystryjSkrin
{
    internal static class Log
    {
        private static readonly object Sync = new object();
        public static string FilePath;

        public static void Write(string message)
        {
            try
            {
                if (string.IsNullOrEmpty(FilePath)) return;
                lock (Sync)
                {
                    var fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > 512 * 1024) fi.Delete();
                    File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }

        public static void Error(string where, Exception ex) { Write("ОШИБКА [" + where + "]: " + ex); }
    }
}
