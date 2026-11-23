using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

using StarXelem.Models;

namespace StarXelem.Components;

/// <summary>
/// Piste de qualité de 0 à 1000 : un point par groupe de lots, dont la taille suit la quantité. La zone sous la qualité
/// minimale est hachurée et ses points grisés ; un clic sur un point utilisable le retient.
/// </summary>
public class QualityTrack : Control
{
    /// <summary>Points à afficher.</summary>
    public static readonly StyledProperty<IReadOnlyList<QualityTrackPoint>?> PointsProperty =
        AvaloniaProperty.Register<QualityTrack, IReadOnlyList<QualityTrackPoint>?>(nameof(Points));

    /// <summary>Qualité minimale exigée (0 si aucune).</summary>
    public static readonly StyledProperty<int> MinQualityProperty =
        AvaloniaProperty.Register<QualityTrack, int>(nameof(MinQuality));

    /// <summary>Qualité du point retenu.</summary>
    public static readonly StyledProperty<int?> SelectedQualityProperty =
        AvaloniaProperty.Register<QualityTrack, int?>(nameof(SelectedQuality), defaultBindingMode: BindingMode.TwoWay);

    private const double MaxQuality = 1000;
    private const double SidePadding = 14;
    private const double RailY = 40;
    private const double MinRadius = 4.5;
    private const double MaxRadius = 10;

    static QualityTrack()
    {
        AffectsRender<QualityTrack>(PointsProperty, MinQualityProperty, SelectedQualityProperty);
        AffectsMeasure<QualityTrack>(PointsProperty);
    }

    /// <summary>Initialise la piste ; la main indique que les points sont cliquables.</summary>
    public QualityTrack()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
        ClipToBounds = false;
    }

    /// <summary>Points à afficher.</summary>
    public IReadOnlyList<QualityTrackPoint>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    /// <summary>Qualité minimale exigée (0 si aucune).</summary>
    public int MinQuality
    {
        get => GetValue(MinQualityProperty);
        set => SetValue(MinQualityProperty, value);
    }

    /// <summary>Qualité du point retenu.</summary>
    public int? SelectedQuality
    {
        get => GetValue(SelectedQualityProperty);
        set => SetValue(SelectedQualityProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width;
        return new Size(width, 66);
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var position = e.GetPosition(this);
        var hit = Layout()
            .Where(entry => !entry.Point.IsExcluded)
            .Select(entry => (entry.Point, Distance: Math.Abs(entry.X - position.X)))
            .Where(entry => entry.Distance <= MaxRadius + 2)
            .OrderBy(entry => entry.Distance)
            .FirstOrDefault();
        if (hit.Point != null)
        {
            SelectedQuality = hit.Point.Quality;
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var line = Brush("Sx.LineBrush", Color.FromRgb(0x2A, 0x2F, 0x3A));
        var muted = Brush("Sx.MutedBrush", Color.FromRgb(0x8A, 0x93, 0xA5));
        var secondary = Brush("Sx.SecondaryBrush", Color.FromRgb(0xA4, 0xAC, 0xBA));
        var accent = Brush("Sx.AccentBrush", Color.FromRgb(0xE8, 0xB0, 0x4A));
        var danger = Brush("Sx.DangerSoftBrush", Color.FromArgb(0x24, 0xF2, 0x66, 0x6A));
        var background = Brush("Sx.SurfaceBrush", Color.FromRgb(0x12, 0x15, 0x1C));
        var font = new Typeface(this.TryFindResource("Sx.FontMono", ActualThemeVariant, out var mono) && mono is FontFamily family ? family : FontFamily.Default);

        // Zone exclue : hachures sous la qualité minimale.
        if (MinQuality > 0)
        {
            var end = ToX(MinQuality);
            var zone = new Rect(SidePadding, RailY - 9, Math.Max(0, end - SidePadding), 18);
            using (context.PushClip(zone))
            {
                context.FillRectangle(danger, zone);
                var hatch = new Pen(danger, 2);
                for (var x = zone.Left - zone.Height; x < zone.Right; x += 7)
                {
                    context.DrawLine(hatch, new Point(x, zone.Bottom), new Point(x + zone.Height, zone.Top));
                }
            }

            DrawText(context, $"min. {MinQuality}", font, 10, muted, new Point(end, RailY + 14), center: true);
        }

        // Rail et graduations.
        context.DrawLine(new Pen(line, 2), new Point(SidePadding, RailY), new Point(Bounds.Width - SidePadding, RailY));
        for (var quality = 0; quality <= MaxQuality; quality += 250)
        {
            var x = ToX(quality);
            context.DrawLine(new Pen(line, 1), new Point(x, RailY - 4), new Point(x, RailY + 4));
            if (MinQuality <= 0 || Math.Abs(quality - MinQuality) > 60)
            {
                DrawText(context, quality.ToString(CultureInfo.CurrentCulture), font, 10, muted, new Point(x, RailY + 14), center: true);
            }
        }

        // Points : les étiquettes alternent de hauteur quand deux points sont proches.
        var previousLabelX = double.NegativeInfinity;
        var raised = false;
        foreach (var entry in Layout())
        {
            var isSelected = !entry.Point.IsExcluded && entry.Point.Quality == SelectedQuality;
            var fill = entry.Point.IsExcluded ? line : isSelected ? accent : secondary;
            if (isSelected)
            {
                context.DrawEllipse(null, new Pen(accent, 1.5), new Point(entry.X, RailY), entry.Radius + 4, entry.Radius + 4);
            }

            context.DrawEllipse(fill, new Pen(background, 2), new Point(entry.X, RailY), entry.Radius, entry.Radius);

            raised = entry.X - previousLabelX < 70 && !raised;
            previousLabelX = entry.X;
            var labelY = raised ? 0 : 12;
            DrawText(context, entry.Point.Label, font, 11, entry.Point.IsExcluded ? muted : isSelected ? accent : secondary, new Point(entry.X, labelY), center: true);
        }
    }

    private IEnumerable<(QualityTrackPoint Point, double X, double Radius)> Layout()
    {
        var points = Points;
        if (points == null || points.Count == 0)
        {
            yield break;
        }

        var maxQuantity = points.Max(point => point.Quantity);
        foreach (var point in points.OrderBy(point => point.Quality))
        {
            var ratio = maxQuantity > 0 ? Math.Sqrt(point.Quantity / maxQuantity) : 0;
            yield return (point, ToX(point.Quality), MinRadius + (MaxRadius - MinRadius) * ratio);
        }
    }

    private double ToX(double quality) => SidePadding + (Bounds.Width - 2 * SidePadding) * Math.Clamp(quality, 0, MaxQuality) / MaxQuality;

    private IBrush Brush(string key, Color fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var resource) && resource is IBrush brush ? brush : new SolidColorBrush(fallback);

    private void DrawText(DrawingContext context, string text, Typeface typeface, double size, IBrush brush, Point origin, bool center)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, brush);
        var x = center ? origin.X - formatted.Width / 2 : origin.X;
        x = Math.Clamp(x, 0, Math.Max(0, Bounds.Width - formatted.Width));
        context.DrawText(formatted, new Point(x, origin.Y));
    }
}
