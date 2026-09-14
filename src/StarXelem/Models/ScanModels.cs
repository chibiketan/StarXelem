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
/// <param name="Votes">Nombre de passes OCR ayant lu exactement cette valeur.</param>
/// <param name="ValidPasses">Nombre de passes OCR ayant produit une valeur plausible (toutes valeurs confondues).</param>
public sealed record SignatureCandidate(int Value, string RawText, PixelRect ScreenBounds, int Votes = 1, int ValidPasses = 1)
{
    /// <summary>true si la valeur vient de la reconnaissance par gabarits (candidat d'arbitrage, jamais prioritaire sur l'OCR).</summary>
    public bool FromTemplates { get; init; }

    /// <summary>Part des passes valides concordantes, 0..1.</summary>
    public double Confidence => ValidPasses == 0 ? 0 : (double)Votes / ValidPasses;
}

/// <summary>Un candidat de signature détecté à l'écran et les lignes de la base qui lui correspondent.</summary>
public sealed record SignatureMatch(SignatureCandidate Candidate, IReadOnlyList<MineralSignatureEntity> Rows);
