using Avalonia;
using SkiaSharp;
using StarXelem.Data;

namespace StarXelem.Models;

/// <summary>Image capturée + origine du rectangle capturé en coordonnées écran (pixels physiques).</summary>
public sealed class CapturedFrame : IDisposable
{
    public SKBitmap Bitmap { get; }
    public int ScreenX { get; }
    public int ScreenY { get; }

    public CapturedFrame(SKBitmap bitmap, int screenX, int screenY)
    {
        Bitmap = bitmap;
        ScreenX = screenX;
        ScreenY = screenY;
    }

    public void Dispose() => Bitmap.Dispose();
}

/// <summary>Nombre lu par l'OCR et sa boîte en coordonnées écran.</summary>
public sealed record SignatureCandidate(int Value, string RawText, PixelRect ScreenBounds);

/// <summary>Un candidat de signature détecté à l'écran et les lignes de la base qui lui correspondent.</summary>
public sealed record SignatureMatch(SignatureCandidate Candidate, IReadOnlyList<MineralSignatureEntity> Rows);
