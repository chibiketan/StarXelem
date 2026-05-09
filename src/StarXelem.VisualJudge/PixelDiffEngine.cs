using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace StarXelem.VisualJudge;

public static class PixelDiffEngine
{
    public static async Task<double> SimilarityPercentAsync(string pathA, string pathB)
    {
        using var imgA = await Image.LoadAsync<Rgba32>(pathA);
        using var imgB = await Image.LoadAsync<Rgba32>(pathB);

        if (imgA.Width != imgB.Width || imgA.Height != imgB.Height)
            throw new ArgumentException("Images must have the same dimensions for pixel comparison.");

        long totalPixels = (long)imgA.Width * imgA.Height;
        long matchingPixels = 0;

        Rgba32[] pixelsA = new Rgba32[totalPixels];
        Rgba32[] pixelsB = new Rgba32[totalPixels];
        imgA.CopyPixelDataTo(pixelsA);
        imgB.CopyPixelDataTo(pixelsB);

        for (int i = 0; i < totalPixels; i++)
        {
            var pa = pixelsA[i];
            var pb = pixelsB[i];

            double dist = Math.Sqrt(
                (pa.R - pb.R) * (pa.R - pb.R) +
                (pa.G - pb.G) * (pa.G - pb.G) +
                (pa.B - pb.B) * (pa.B - pb.B));

            if (dist <= 30.0)
                matchingPixels++;
        }

        return (matchingPixels / (double)totalPixels) * 100.0;
    }

    public static async Task<string> GenerateHeatmapAsync(string pathA, string pathB, string outputPath)
    {
        using var imgA = await Image.LoadAsync<Rgba32>(pathA);
        using var imgB = await Image.LoadAsync<Rgba32>(pathB);

        if (imgA.Width != imgB.Width || imgA.Height != imgB.Height)
            throw new ArgumentException("Images must have the same dimensions for heatmap generation.");

        long totalPixels = (long)imgA.Width * imgA.Height;

        Rgba32[] pixelsA = new Rgba32[totalPixels];
        Rgba32[] pixelsB = new Rgba32[totalPixels];
        imgA.CopyPixelDataTo(pixelsA);
        imgB.CopyPixelDataTo(pixelsB);

        using var heatmap = new Image<Rgba32>(imgA.Width, imgA.Height);

        heatmap.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                int baseIndex = y * imgA.Width;

                for (int x = 0; x < row.Length; x++)
                {
                    var pa = pixelsA[baseIndex + x];
                    var pb = pixelsB[baseIndex + x];

                    double dist = Math.Sqrt(
                        (pa.R - pb.R) * (pa.R - pb.R) +
                        (pa.G - pb.G) * (pa.G - pb.G) +
                        (pa.B - pb.B) * (pa.B - pb.B));

                    if (dist > 30.0)
                        row[x] = new Rgba32(255, 0, 0, 128);
                    else
                        row[x] = pa;
                }
            }
        });

        await heatmap.SaveAsync(outputPath);
        return outputPath;
    }
}
