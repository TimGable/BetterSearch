using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BetterSearch;

public static class FileIconProvider
{
    // SHGetFileInfo returns the same icon Windows Explorer uses for folders and file extensions.
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiSmallIcon = 0x000000001;
    private const uint ShgfiUseFileAttributes = 0x000000010;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeNormal = 0x00000080;

    private static readonly ConcurrentDictionary<string, ImageSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? GetIcon(string extension, bool isDirectory)
    {
        // Cache by type instead of by full path so thousands of files do not cause thousands of shell calls.
        var key = isDirectory ? "<folder>" : string.IsNullOrWhiteSpace(extension) ? "<file>" : extension.TrimStart('.');
        return IconCache.GetOrAdd(key, _ => LoadShellIcon(extension, isDirectory));
    }

    private static ImageSource? LoadShellIcon(string extension, bool isDirectory)
    {
        var attributes = isDirectory ? FileAttributeDirectory : FileAttributeNormal;
        // The shell can resolve an icon from attributes alone, so the file does not need to actually exist.
        var lookupPath = isDirectory
            ? "folder"
            : string.IsNullOrWhiteSpace(extension)
                ? "file"
                : $".{extension.TrimStart('.')}";

        var result = SHGetFileInfo(
            lookupPath,
            attributes,
            out var fileInfo,
            (uint)Marshal.SizeOf<SHFILEINFO>(),
            ShgfiIcon | ShgfiSmallIcon | ShgfiUseFileAttributes);

        if (result == IntPtr.Zero || fileInfo.hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                fileInfo.hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(16, 16));
            // Freeze the image so WPF can safely reuse it across bindings.
            source.Freeze();
            return source;
        }
        finally
        {
            // SHGetFileInfo hands back a native icon handle; WPF copies it, but we still own the handle.
            DestroyIcon(fileInfo.hIcon);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        out SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }
}
