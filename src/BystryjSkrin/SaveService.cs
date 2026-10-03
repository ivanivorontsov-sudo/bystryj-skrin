using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace BystryjSkrin
{
    internal sealed class SaveOutcome
    {
        public string Path;
        public string Folder;
        public string PrimaryFolder;
        public bool FellBack;
        public Exception PrimaryError;
        public bool CfaSuspected;
        public Exception FinalError;
        public readonly List<string> Attempts = new List<string>();
        public bool Saved { get { return Path != null; } }
    }

    internal static class SaveService
    {
        /// <summary>Запасные папки: сначала «Изображения\Скриншоты», затем папки вне защиты Defender.</summary>
        public static List<string> FallbackFolders(string primary)
        {
            var list = new List<string>();
            Action<Func<string>> add = f =>
            {
                try
                {
                    string p = f();
                    if (string.IsNullOrEmpty(p)) return;
                    if (primary != null && string.Equals(p.TrimEnd('\\'), primary.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return;
                    if (list.Any(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase))) return;
                    list.Add(p);
                }
                catch { }
            };
            add(AppSettings.DefaultFolder);
            add(() => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Скриншоты"));
            add(() => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BystryjSkrin", "Скриншоты"));
            add(() => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BystryjSkrin-Скриншоты"));
            return list;
        }

        public static byte[] Encode(Bitmap image, ImageFormatKind format, int jpegQuality)
        {
            using (var ms = new MemoryStream())
            {
                if (format == ImageFormatKind.Jpg)
                {
                    ImageCodecInfo codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                    using (var ep = new EncoderParameters(1))
                    {
                        ep.Param[0] = new EncoderParameter(Encoder.Quality, (long)Math.Max(1, Math.Min(100, jpegQuality)));
                        image.Save(ms, codec, ep);
                    }
                }
                else image.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        /// <summary>Пишет байты в новый файл (не перезаписывая существующие). Возвращает путь.</summary>
        public static string WriteNew(string folder, DateTime time, ImageFormatKind format, byte[] data)
        {
            Directory.CreateDirectory(folder);
            for (int attempt = 0; attempt < 50; attempt++)
            {
                string path = FileNaming.UniquePath(folder, time, format);
                FileStream fs;
                try { fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
                catch (IOException ex) when (ErrorText.Win32Code(ex) == ErrorText.ERROR_FILE_EXISTS) { continue; } // гонка с другим процессом
                try
                {
                    using (fs)
                    {
                        fs.Write(data, 0, data.Length);
                        fs.Flush(true);
                    }
                    return path;
                }
                catch
                {
                    try { File.Delete(path); } catch { } // не оставляем обрезанный файл (например, при нехватке места)
                    throw;
                }
            }
            throw new IOException("Не удалось подобрать свободное имя файла");
        }

        /// <summary>Сохраняет в основную папку, при ошибке — по очереди в запасные. Снимок не теряется.</summary>
        public static SaveOutcome Save(byte[] data, ImageFormatKind format, string primaryFolder, IEnumerable<string> fallbacks, DateTime time,
                                       Func<Exception, string, bool> cfaDetector = null)
        {
            var o = new SaveOutcome { PrimaryFolder = primaryFolder };
            var folders = new List<string>();
            if (!string.IsNullOrWhiteSpace(primaryFolder)) folders.Add(primaryFolder);
            if (fallbacks != null) folders.AddRange(fallbacks);
            if (cfaDetector == null) cfaDetector = ErrorText.LooksLikeCfaBlock;

            for (int i = 0; i < folders.Count; i++)
            {
                string folder = folders[i];
                try
                {
                    string err = FolderRules.Validate(folder);
                    if (err != null) throw new IOException("Папка не подходит: " + err);
                    o.Path = WriteNew(folder, time, format, data);
                    o.Folder = folder;
                    o.FellBack = i > 0;
                    return o;
                }
                catch (Exception ex)
                {
                    o.Attempts.Add(folder + ": " + ex.Message);
                    Log.Error("Сохранение в «" + folder + "»", ex);
                    if (i == 0)
                    {
                        o.PrimaryError = ex;
                        try { o.CfaSuspected = cfaDetector(ex, folder); } catch { }
                    }
                    o.FinalError = ex;
                }
            }
            return o;
        }
    }

    internal sealed class UndoHistory
    {
        private readonly List<string> items = new List<string>();
        public const int Max = 100;
        public int Count { get { return items.Count; } }

        public void Push(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            items.Remove(path);
            items.Add(path);
            if (items.Count > Max) items.RemoveAt(0);
        }

        public void Remove(string path) { items.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)); }

        /// <summary>Достаёт последний ещё существующий файл (удалённые вручную пропускаются).</summary>
        public string TakeLast(Func<string, bool> exists)
        {
            while (items.Count > 0)
            {
                string p = items[items.Count - 1];
                items.RemoveAt(items.Count - 1);
                if (exists(p)) return p;
            }
            return null;
        }

        public string PeekLast(Func<string, bool> exists)
        {
            for (int i = items.Count - 1; i >= 0; i--) if (exists(items[i])) return items[i];
            return null;
        }
    }

    /// <summary>Не даёт открыть второе выделение, пока открыто первое (повторное нажатие игнорируется).</summary>
    internal sealed class CaptureGate
    {
        private bool busy;
        public bool IsBusy { get { return busy; } }
        public bool TryEnter() { if (busy) return false; busy = true; return true; }
        public void Exit() { busy = false; }
    }
}
