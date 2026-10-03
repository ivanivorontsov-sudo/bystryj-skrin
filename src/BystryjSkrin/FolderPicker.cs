using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BystryjSkrin
{
    /// <summary>Современный выбор папки (IFileOpenDialog) с запасным вариантом и проверкой выбранного пути.</summary>
    internal static class FolderPicker
    {
        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")] private class FileOpenDialogCoClass { }

        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint c, IntPtr spec);
            void SetFileTypeIndex(uint i);
            void GetFileTypeIndex(out uint i);
            void Advise(IntPtr sink, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint fos);
            void GetOptions(out uint fos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem psi);
            void GetCurrentSelection(out IShellItem psi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void GetResult(out IShellItem psi);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
            void GetAttributes(uint mask, out uint attribs);
            void Compare(IShellItem psi, uint hint, out int order);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

        private const uint FOS_PICKFOLDERS = 0x20, FOS_FORCEFILESYSTEM = 0x40, FOS_PATHMUSTEXIST = 0x800, FOS_NOCHANGEDIR = 0x8;
        private const uint SIGDN_FILESYSPATH = 0x80058000;
        private const int ERROR_CANCELLED_HR = unchecked((int)0x800704C7);

        private static string ShowModern(IWin32Window owner, string initial)
        {
            var dlg = (IFileDialog)new FileOpenDialogCoClass();
            try
            {
                dlg.SetOptions(FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST | FOS_NOCHANGEDIR);
                dlg.SetTitle("Куда сохранять скриншоты");
                dlg.SetOkButtonLabel("Выбрать папку");
                string start = initial;
                while (!string.IsNullOrEmpty(start) && !Directory.Exists(start)) start = Path.GetDirectoryName(start);
                if (!string.IsNullOrEmpty(start))
                {
                    try { IShellItem si; SHCreateItemFromParsingName(start, IntPtr.Zero, typeof(IShellItem).GUID, out si); dlg.SetFolder(si); } catch { }
                }
                int hr = dlg.Show(owner == null ? IntPtr.Zero : owner.Handle);
                if (hr == ERROR_CANCELLED_HR) return null;
                if (hr != 0) Marshal.ThrowExceptionForHR(hr);
                IShellItem res; dlg.GetResult(out res);
                string path; res.GetDisplayName(SIGDN_FILESYSPATH, out path);
                return path;
            }
            finally { Marshal.FinalReleaseComObject(dlg); }
        }

        private static string ShowClassic(IWin32Window owner, string initial)
        {
            using (var fb = new FolderBrowserDialog { Description = "Куда сохранять скриншоты", ShowNewFolderButton = true })
            {
                if (Directory.Exists(initial)) fb.SelectedPath = initial;
                return fb.ShowDialog(owner) == DialogResult.OK ? fb.SelectedPath : null;
            }
        }

        /// <summary>Выбор папки. Системные/недоступные папки не принимаются — пользователь выбирает снова.</summary>
        public static string Pick(IWin32Window owner, string initial)
        {
            while (true)
            {
                string path;
                try { path = ShowModern(owner, initial); }
                catch (Exception ex) { Log.Error("IFileOpenDialog", ex); path = ShowClassic(owner, initial); }
                if (path == null) return null;

                string err = FolderRules.Validate(path);
                if (err != null)
                {
                    Dialogs.Warn("Эта папка не подходит: " + err + "\n\nВыберите другую папку.", owner == null ? IntPtr.Zero : owner.Handle);
                    initial = AppSettings.DefaultFolder();
                    continue;
                }
                Exception probe = FolderRules.ProbeWrite(path);
                if (probe != null)
                {
                    string msg = "В папку «" + path + "» не получается записать: " + ErrorText.Friendly(probe);
                    if (ErrorText.LooksLikeCfaBlock(probe, path)) msg += "\n\n" + ErrorText.CfaHint;
                    Dialogs.Warn(msg + "\n\nВыберите другую папку.", owner == null ? IntPtr.Zero : owner.Handle);
                    initial = path;
                    continue;
                }
                return FolderRules.Normalize(path);
            }
        }
    }
}
