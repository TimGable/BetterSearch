using System.IO;
using System.Windows.Media;

namespace BetterSearch;

public sealed class FileSearchResult
{
    public FileSearchResult(string path, bool isDirectory)
    {
        Path = path;
        IsDirectory = isDirectory;
        // Normalize the path once when the item enters the index, so searching and binding stay simple.
        Name = System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(Name))
        {
            Name = path;
        }

        Folder = isDirectory ? System.IO.Path.GetDirectoryName(path.TrimEnd('\\')) ?? path : System.IO.Path.GetDirectoryName(path) ?? string.Empty;
        Extension = isDirectory ? string.Empty : System.IO.Path.GetExtension(path).TrimStart('.');
        // Keep the displayed type short, matching the compact layout of the results table.
        Type = isDirectory ? "Folder" : string.IsNullOrWhiteSpace(Extension) ? "File" : Extension.ToUpperInvariant();
        Drive = System.IO.Path.GetPathRoot(path) ?? string.Empty;
        IconSource = FileIconProvider.GetIcon(Extension, isDirectory);
        DisplayFolder = string.IsNullOrWhiteSpace(Folder) ? Path : Folder;
    }

    public string Name { get; }

    public string Path { get; }

    public string Folder { get; }

    public string Directory => Folder;

    public string DisplayFolder { get; }

    public string Extension { get; }

    public string Type { get; }

    public string Drive { get; }

    public bool IsDirectory { get; }

    public ImageSource? IconSource { get; }
}
