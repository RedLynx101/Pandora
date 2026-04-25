using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CustomFences.App;

public static class FileIconService
{
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;
    private const uint ShgfiUseFileAttributes = 0x000000010;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeNormal = 0x00000080;
    private static readonly Guid ShellItemImageFactoryGuid = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    public static ImageSource? GetIcon(string path)
    {
        var highResolutionIcon = TryGetHighResolutionIcon(path, 128);
        if (highResolutionIcon is not null)
        {
            return highResolutionIcon;
        }

        var attributes = Directory.Exists(path) ? FileAttributeDirectory : FileAttributeNormal;
        var flags = ShgfiIcon | ShgfiLargeIcon;
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            flags |= ShgfiUseFileAttributes;
        }

        var info = new ShFileInfo();
        var result = SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), flags);
        if (result == IntPtr.Zero || info.IconHandle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(
                info.IconHandle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(64, 64));
            image.Freeze();
            return image;
        }
        finally
        {
            DestroyIcon(info.IconHandle);
        }
    }

    private static ImageSource? TryGetHighResolutionIcon(string path, int size)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return null;
        }

        IShellItemImageFactory? factory = null;
        IntPtr bitmapHandle = IntPtr.Zero;
        try
        {
            var factoryGuid = ShellItemImageFactoryGuid;
            var result = SHCreateItemFromParsingName(path, IntPtr.Zero, ref factoryGuid, out factory);
            if (result != 0 || factory is null)
            {
                return null;
            }

            var getImageResult = factory.GetImage(
                new NativeSize { Width = size, Height = size },
                ShellItemImageFactoryFlags.IconOnly | ShellItemImageFactoryFlags.BiggerSizeOk | ShellItemImageFactoryFlags.ScaleUp,
                out bitmapHandle);

            if (getImageResult != 0 || bitmapHandle == IntPtr.Zero)
            {
                return null;
            }

            var image = Imaging.CreateBitmapSourceFromHBitmap(
                bitmapHandle,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(size, size));
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (bitmapHandle != IntPtr.Zero)
            {
                DeleteObject(bitmapHandle);
            }

            if (factory is not null)
            {
                Marshal.ReleaseComObject(factory);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr IconHandle;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
    }

    [Flags]
    private enum ShellItemImageFactoryFlags : uint
    {
        BiggerSizeOk = 0x1,
        IconOnly = 0x4,
        ScaleUp = 0x100
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, ShellItemImageFactoryFlags flags, out IntPtr bitmapHandle);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint fileAttributes, ref ShFileInfo fileInfo, uint fileInfoSize, uint flags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        string path,
        IntPtr bindContext,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory shellItemImageFactory);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr iconHandle);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr objectHandle);
}
