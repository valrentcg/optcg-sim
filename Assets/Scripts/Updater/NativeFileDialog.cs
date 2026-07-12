// One Piece TCG - Native "Open File" dialog via a direct Win32 call (comdlg32.dll), rather
// than a third-party plugin. This project ships Windows-only today (Velopack + GitHub
// Releases; see [[optcg-sim-velopack]]), so a P/Invoke wrapper is fewer moving parts than
// vendoring a cross-platform file-browser plugin the project doesn't otherwise need.
// If WebGL/Mac/Linux support ever comes back, this is the file to replace/branch.

using System;
using System.Runtime.InteropServices;

public static class NativeFileDialog
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private class OpenFileName
    {
        public int lStructSize = Marshal.SizeOf(typeof(OpenFileName));
        public IntPtr hwndOwner = IntPtr.Zero;
        public IntPtr hInstance = IntPtr.Zero;
        public string lpstrFilter;
        public string lpstrCustomFilter = null;
        public int nMaxCustFilter = 0;
        public int nFilterIndex = 0;
        public string lpstrFile;
        public int nMaxFile = 260;
        public string lpstrFileTitle;
        public int nMaxFileTitle = 64;
        public string lpstrInitialDir;
        public string lpstrTitle;
        public int Flags;
        public short nFileOffset = 0;
        public short nFileExtension = 0;
        public string lpstrDefExt = null;
        public IntPtr lCustData = IntPtr.Zero;
        public IntPtr lpfnHook = IntPtr.Zero;
        public string lpTemplateName = null;
        public IntPtr pvReserved = IntPtr.Zero;
        public int dwReserved = 0;
        public int FlagsEx = 0;
    }

    [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetOpenFileName([In, Out] OpenFileName ofn);

    private const int OFN_FILEMUSTEXIST = 0x00001000;
    private const int OFN_PATHMUSTEXIST = 0x00000800;
    private const int OFN_NOCHANGEDIR = 0x00000008;

    /// <summary>Shows a native modal "Open File" dialog filtered to one extension. Blocks the
    /// calling (main) thread until the user picks a file or cancels — that's expected/normal
    /// for a native file dialog, same as any desktop app. Returns the chosen absolute path,
    /// or null if the user canceled or the dialog failed.</summary>
    public static string OpenFile(string title, string filterLabel, string filterExt)
    {
        var ofn = new OpenFileName
        {
            lpstrFilter = $"{filterLabel}\0*.{filterExt}\0All Files\0*.*\0\0",
            lpstrFile = new string('\0', 260),
            lpstrFileTitle = new string('\0', 64),
            lpstrTitle = title,
            Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR,
        };
        return GetOpenFileName(ofn) ? ofn.lpstrFile.TrimEnd('\0') : null;
    }
}
