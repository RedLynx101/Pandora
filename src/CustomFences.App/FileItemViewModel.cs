using System;
using System.IO;
using System.Windows.Media;
using CustomFences.Core;

namespace CustomFences.App;

public sealed class FileItemViewModel
{
    public FileItemViewModel(FileSystemInfo info)
    {
        Path = info.FullName;
        DisplayName = DesktopItemCatalog.CleanDisplayName(info.Name);
        IsDirectory = info.Attributes.HasFlag(FileAttributes.Directory);
        LastWriteTime = info.LastWriteTime;
        Extension = IsDirectory ? "folder" : System.IO.Path.GetExtension(info.FullName).TrimStart('.');
        Icon = FileIconService.GetIcon(info.FullName);
    }

    public string Path { get; }
    public string DisplayName { get; }
    public bool IsDirectory { get; }
    public DateTime LastWriteTime { get; }
    public string Extension { get; }
    public ImageSource? Icon { get; }
}
