using System.Runtime.InteropServices;
using SkiaSharp;
using StarXelem.Constants;

namespace StarXelem.Services.Scan;

/// <summary>
/// Opérations d'image partagées par la reconnaissance de signature (OCR et gabarits). Toutes travaillent en
/// BGRA8888 opaque et renvoient un nouveau bitmap indépendant de la source.
/// </summary>
public static class ScanImageOps
{
    /// <summary>Agrandissement bicubique (High) : mesuré 2,5× plus rapide côté reconnaissance qu'un bilinéaire (image plus nette), pour +15 ms de resize.</summary>
    public static SKBitmap Scale(SKBitmap image, double scale)
    {
        if (Math.Abs(scale - 1.0) < 0.001) return image.Copy();
        var info = new SKImageInfo(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale)), SKColorType.Bgra8888, SKAlphaType.Opaque);
        return image.Resize(info, SKFilterQuality.High) ?? image.Copy();
    }

#pragma warning disable CS0618 // SKBitmapResizeMethod est obsolète mais reste le seul accès à Lanczos3 en SkiaSharp 2.88.
    public static SKBitmap ScaleLanczos(SKBitmap image, double scale)
    {
        if (Math.Abs(scale - 1.0) < 0.001) return image.Copy();
        var info = new SKImageInfo(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale)), SKColorType.Bgra8888, SKAlphaType.Opaque);
        return image.Resize(info, SKBitmapResizeMethod.Lanczos3) ?? image.Copy();
    }
#pragma warning restore CS0618

    public static SKBitmap Crop(SKBitmap source, SKRectI rect)
    {
        var dst = new SKBitmap(rect.Width, rect.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        if (!source.ExtractSubset(dst, rect))
        {
            dst.Dispose();
            return source.Copy();
        }
        // ExtractSubset partage les pixels de la source : on copie pour obtenir un bitmap indépendant (et ses Bytes contigus).
        var copy = dst.Copy();
        dst.Dispose();
        return copy;
    }

    /// <summary>Niveaux de gris à partir du seul canal vert : supprime la frange chromatique rouge/bleue du texte HUD.</summary>
    public static SKBitmap GreenChannel(SKBitmap source)
    {
        var dst = new SKBitmap(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var src = source.Bytes;
        var pixels = new byte[src.Length];
        for (var i = 0; i + 3 < src.Length; i += 4)
        {
            var g = src[i + 1];
            pixels[i] = g;
            pixels[i + 1] = g;
            pixels[i + 2] = g;
            pixels[i + 3] = 255;
        }
        Marshal.Copy(pixels, 0, dst.GetPixels(), pixels.Length);
        return dst;
    }

    /// <summary>
    /// Étire le contraste (percentiles 1–99 %) puis applique un gamma sombre (<see cref="ScanConstants.OcrGamma"/>),
    /// par tuiles de <paramref name="tileSize"/> px (0 = toute l'image). Attend une image en niveaux de gris
    /// (les trois canaux égaux) ; renvoie de même.
    /// </summary>
    public static SKBitmap NormalizeContrast(SKBitmap gray, int tileSize)
    {
        var width = gray.Width;
        var height = gray.Height;
        var src = gray.Bytes;
        var dst = new byte[src.Length];
        var tile = tileSize <= 0 ? Math.Max(width, height) : tileSize;
        var histogram = new int[256];
        var lut = new byte[256];

        for (var ty = 0; ty < height; ty += tile)
        {
            for (var tx = 0; tx < width; tx += tile)
            {
                var x2 = Math.Min(width, tx + tile);
                var y2 = Math.Min(height, ty + tile);
                Array.Clear(histogram);
                for (var y = ty; y < y2; y++)
                {
                    var row = y * width * 4;
                    for (var x = tx; x < x2; x++) histogram[src[row + x * 4 + 1]]++;
                }

                var total = (x2 - tx) * (y2 - ty);
                var lo = Percentile(histogram, total, 0.01);
                var hi = Percentile(histogram, total, 0.99);
                var range = hi - lo;
                if (range < ScanConstants.OcrNormalizeMinRange)
                {
                    Array.Clear(lut);
                }
                else
                {
                    for (var v = 0; v < 256; v++)
                    {
                        var n = Math.Clamp((v - lo) / (double)range, 0, 1);
                        lut[v] = (byte)(Math.Pow(n, ScanConstants.OcrGamma) * 255);
                    }
                }

                for (var y = ty; y < y2; y++)
                {
                    var row = y * width * 4;
                    for (var x = tx; x < x2; x++)
                    {
                        var i = row + x * 4;
                        var g = lut[src[i + 1]];
                        dst[i] = g;
                        dst[i + 1] = g;
                        dst[i + 2] = g;
                        dst[i + 3] = 255;
                    }
                }
            }
        }

        var result = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        Marshal.Copy(dst, 0, result.GetPixels(), dst.Length);
        return result;
    }

    private static int Percentile(int[] histogram, int total, double p)
    {
        var target = (int)(total * p);
        var cumulative = 0;
        for (var v = 0; v < 256; v++)
        {
            cumulative += histogram[v];
            if (cumulative > target) return v;
        }
        return 255;
    }
}
