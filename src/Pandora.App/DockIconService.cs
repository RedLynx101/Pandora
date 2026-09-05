using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pandora.Core;

namespace Pandora.App;

/// <summary>Read-only local raster icons. No URI decoder, shell extraction, or external resources.</summary>
public static class DockIconService
{
    public const int MaximumFileBytes = 8 * 1024 * 1024;
    public const int MaximumDimension = 2048;
    public const long MaximumPixels = 4 * 1024 * 1024;

    public static DockIconResult Resolve(ZoneAppearance appearance, string? productIconStyle)
    {
        if (appearance.HeaderIcon == DockHeaderIcon.None) return new(null, false, false, string.Empty);
        if (appearance.HeaderIcon == DockHeaderIcon.Custom)
        {
            try { return new(LoadCustomImage(appearance.HeaderIconPath), true, false, string.Empty); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
                NotSupportedException or FormatException or InvalidOperationException or OverflowException or COMException)
            {
                return new(BrandIdentity.Image(productIconStyle, 32), true, true,
                    "Custom header icon unavailable; using Pandora. " + ex.Message);
            }
        }
        return new(BrandIdentity.Image(productIconStyle, 32), true, false, string.Empty);
    }

    private static BitmapSource LoadCustomImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal))
            throw new IOException("Choose an absolute local PNG, ICO, or JPEG file.");
        var fullPath = Path.GetFullPath(path);
        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
            throw new IOException("Network and device image paths are not supported. Choose a local file.");
        var root = Path.GetPathRoot(fullPath)!;
        if (new DriveInfo(root).DriveType == DriveType.Network)
            throw new IOException("Network images are not supported. Choose a local file.");
        // Path checks reduce accidental linked/network traversal, but are not race-proof handles.
        SafeFileTransfer.RequireOrdinaryPath(fullPath);
        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        if (extension is not (".png" or ".ico" or ".jpg" or ".jpeg"))
            throw new IOException("Supported image types are PNG, ICO, and JPEG.");
        using var file = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length is <= 0 or > MaximumFileBytes)
            throw new IOException("The image must be no larger than 8 MiB.");
        var bytes = new byte[(int)file.Length];
        file.ReadExactly(bytes);
        ValidateHeader(bytes, extension);

        // Inspect metadata before requesting pixel data; both passes use only the bounded bytes.
        using var metadataStream = new MemoryStream(bytes, writable: false);
        var metadata = Decode(metadataStream, extension, BitmapCacheOption.OnDemand);
        if (metadata.Frames.Count is < 1 or > 64) throw new IOException("The icon contains too many frames.");
        foreach (var frame in metadata.Frames)
            ValidateDimensions(frame.PixelWidth, frame.PixelHeight, extension == ".ico" ? 256 : MaximumDimension);
        using var pixelStream = new MemoryStream(bytes, writable: false);
        var decoded = Decode(pixelStream, extension, BitmapCacheOption.OnLoad);
        var image = decoded.Frames.OrderByDescending(frame => frame.PixelWidth).First();
        image.Freeze();
        return image;
    }

    private static BitmapDecoder Decode(Stream stream, string extension, BitmapCacheOption cache)
    {
        const BitmapCreateOptions options = BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile;
        return extension switch
        {
            ".png" => new PngBitmapDecoder(stream, options, cache),
            ".ico" => new IconBitmapDecoder(stream, options, cache),
            _ => new JpegBitmapDecoder(stream, options, cache)
        };
    }

    private static void ValidateHeader(byte[] bytes, string extension)
    {
        var span = bytes.AsSpan();
        if (extension == ".png")
        {
            ValidatePngHeader(span);
            return;
        }
        if (extension == ".ico")
        {
            if (bytes.Length < 6 || BinaryPrimitives.ReadUInt32LittleEndian(span[..4]) != 0x00010000)
                throw new IOException("The ICO header is invalid.");
            var count = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(4, 2));
            if (count is < 1 or > 64 || bytes.Length < 6 + count * 16) throw new IOException("The ICO frame directory is invalid.");
            // Bound embedded PNG/DIB dimensions too; directory dimensions alone are not authoritative.
            for (var index = 0; index < count; index++)
            {
                var entry = span.Slice(6 + index * 16, 16);
                var length = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(8, 4));
                var offset = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(12, 4));
                if (offset < 6 + count * 16 || length < 24 || (long)offset + length > bytes.Length)
                    throw new IOException("An ICO frame is invalid.");
                var frame = span.Slice((int)offset, (int)length);
                if (frame[0] == 137)
                {
                    ValidatePngHeader(frame);
                    ValidateDimensions(BinaryPrimitives.ReadUInt32BigEndian(frame.Slice(16, 4)), BinaryPrimitives.ReadUInt32BigEndian(frame.Slice(20, 4)), 256);
                }
                else
                {
                    if (BinaryPrimitives.ReadUInt32LittleEndian(frame[..4]) < 40) throw new IOException("Unsupported ICO bitmap header.");
                    var width = BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(4, 4));
                    var doubleHeight = BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(8, 4));
                    if (doubleHeight % 2 != 0) throw new IOException("Invalid ICO bitmap height.");
                    ValidateDimensions(width, Math.Abs((long)doubleHeight) / 2, 256);
                }
            }
            return;
        }
        if (bytes.Length < 4 || bytes[0] != 255 || bytes[1] != 216) throw new IOException("The JPEG header is invalid.");
        for (var offset = 2; offset + 3 < bytes.Length;)
        {
            if (bytes[offset++] != 255) throw new IOException("The JPEG segment is invalid.");
            while (offset < bytes.Length && bytes[offset] == 255) offset++;
            if (offset >= bytes.Length) break;
            var marker = bytes[offset++];
            if (marker is 216 or 1 or >= 208 and <= 215) continue;
            if (marker is 217 or 218 || offset + 2 > bytes.Length) break;
            var length = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(offset, 2));
            if (length < 2 || offset + length > bytes.Length) throw new IOException("The JPEG segment length is invalid.");
            if (marker is >= 192 and <= 207 && marker is not (196 or 200 or 204))
            {
                if (length < 8) break;
                ValidateDimensions(BinaryPrimitives.ReadUInt16BigEndian(span.Slice(offset + 5, 2)), BinaryPrimitives.ReadUInt16BigEndian(span.Slice(offset + 3, 2)));
                return;
            }
            offset += length;
        }
        throw new IOException("The JPEG dimensions could not be read.");
    }

    private static void ValidatePngHeader(ReadOnlySpan<byte> span)
    {
        if (span.Length < 24 || !span[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            !span.Slice(12, 4).SequenceEqual("IHDR"u8)) throw new IOException("The PNG header is invalid.");
        ValidateDimensions(BinaryPrimitives.ReadUInt32BigEndian(span.Slice(16, 4)), BinaryPrimitives.ReadUInt32BigEndian(span.Slice(20, 4)));
    }

    private static void ValidateDimensions(long width, long height, int limit = MaximumDimension)
    {
        if (width <= 0 || height <= 0 || width > limit || height > limit || width * height > MaximumPixels)
            throw new IOException($"The image dimensions must be between 1 and {limit} pixels per side (at most 4 megapixels).");
    }
}

public sealed record DockIconResult(ImageSource? Image, bool IsVisible, bool IsFallback, string StatusMessage);
