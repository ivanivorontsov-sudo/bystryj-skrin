using System;
using System.IO;
using System.Threading;

namespace BystryjSkrin.Tests
{
    internal sealed class TempDir : IDisposable
    {
        public readonly string Path;
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bs-test-" + Guid.NewGuid().ToString("N").Substring(0, 10));
            Directory.CreateDirectory(Path);
        }
        public string Sub(string name) { return System.IO.Path.Combine(Path, name); }
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }

    internal static class Sta
    {
        public static void Run(Action a)
        {
            Exception err = null;
            var t = new Thread(() => { try { a(); } catch (Exception ex) { err = ex; } });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (err != null) throw new Exception("STA: " + err.Message, err);
        }
    }
}
