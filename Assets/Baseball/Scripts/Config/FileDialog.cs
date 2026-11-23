// Diálogo nativo de Windows para abrir o guardar archivos (imágenes de anuncios y la
// configuración del estadio). En otros sistemas devuelve null.

using System;
using System.Runtime.InteropServices;

namespace Baseball.Config
{
    public static class FileDialog
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        class OpenFileName
        {
            public int structSize = Marshal.SizeOf(typeof(OpenFileName));
            public IntPtr dlgOwner = IntPtr.Zero;
            public IntPtr instance = IntPtr.Zero;
            public string filter;
            public string customFilter;
            public int maxCustFilter;
            public int filterIndex;
            public string file;
            public int maxFile;
            public string fileTitle;
            public int maxFileTitle;
            public string initialDir;
            public string title;
            public int flags;
            public short fileOffset;
            public short fileExtension;
            public string defExt;
            public IntPtr custData = IntPtr.Zero;
            public IntPtr hook = IntPtr.Zero;
            public string templateName;
            public IntPtr reservedPtr = IntPtr.Zero;
            public int reservedInt;
            public int flagsEx;
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("Comdlg32.dll", CharSet = CharSet.Unicode)] static extern bool GetOpenFileName([In, Out] OpenFileName ofn);
        [DllImport("Comdlg32.dll", CharSet = CharSet.Unicode)] static extern bool GetSaveFileName([In, Out] OpenFileName ofn);
#endif

        const int OFN_FILEMUSTEXIST = 0x1000, OFN_PATHMUSTEXIST = 0x800, OFN_OVERWRITEPROMPT = 0x2, OFN_NOCHANGEDIR = 0x8;

        // filter: pares "Descripción", "*.png;*.jpg"
        public static string Open(string title, params string[] filter) => Show(title, null, false, filter);
        public static string Save(string title, string defaultName, params string[] filter) => Show(title, defaultName, true, filter);

        static string Show(string title, string defaultName, bool save, string[] filter)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var ofn = new OpenFileName
            {
                filter = string.Join("\0", filter) + "\0\0",
                file = (defaultName ?? "") + new string('\0', 1024 - (defaultName ?? "").Length),
                maxFile = 1024,
                fileTitle = new string('\0', 256), maxFileTitle = 256,
                initialDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                title = title,
                defExt = save ? "json" : null,
                flags = OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | (save ? OFN_OVERWRITEPROMPT : OFN_FILEMUSTEXIST),
            };
            bool ok = save ? GetSaveFileName(ofn) : GetOpenFileName(ofn);
            return ok ? ofn.file.TrimEnd('\0') : null;
#else
            return null;
#endif
        }
    }
}
