using System;
using System.IO;
using System.Windows.Media;
using CustomFences.Core;

namespace CustomFences.App;

public sealed class FileItemViewModel
{
    public FileItemViewModel(FileSystemInfo info)
        : this(info.FullName, DesktopItemCatalog.CleanDisplayName(info.Name), info.Attributes.HasFlag(FileAttributes.Directory), info.LastWriteTime)
    {
    }

    public FileItemViewModel(string path, string? displayName = null)
        : this(
            path,
            displayName ?? DesktopItemCatalog.CleanDisplayName(System.IO.Path.GetFileName(path)),
            Directory.Exists(path),
            File.Exists(path) ? File.GetLastWriteTime(path) : DateTime.MinValue)
    {
    }

    private FileItemViewModel(string path, string displayName, bool isDirectory, DateTime lastWriteTime)
    {
        Path = WorkspaceLayoutService.NormalizePath(path);
        DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? DesktopItemCatalog.CleanDisplayName(System.IO.Path.GetFileName(path))
            : displayName;
        IsDirectory = isDirectory;
        LastWriteTime = lastWriteTime;
        Extension = IsDirectory ? "folder" : System.IO.Path.GetExtension(Path).TrimStart('.');
        Icon = FileIconService.GetIcon(Path);
    }

    public string Path { get; }
    public string DisplayName { get; }
    public bool IsDirectory { get; }
    public DateTime LastWriteTime { get; }
    public string Extension { get; }
    public ImageSource? Icon { get; }
}
