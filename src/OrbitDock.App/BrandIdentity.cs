using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace OrbitDock.App;

/// <summary>Product-facing identity; legacy storage and interoperability names intentionally remain stable.</summary>
public static class BrandIdentity
{
    public const string Name = "Pandora";

    public static string AssetStem(string? style) => style switch
    {
        "Selene" => "Pandora-Selene",
        "Aster" => "Pandora-Aster",
        _ => "Pandora"
    };

    public static string IconPath(string? style) => Path.Combine(AppContext.BaseDirectory, "Assets", "Brand", AssetStem(style) + ".ico");

    public static BitmapImage? Image(string? style, int size = 128)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Brand", AssetStem(style) + $"-{size}.png");
        if (!File.Exists(path)) return null;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
