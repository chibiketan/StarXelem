using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace StarXelem.Components;

/// <summary>
/// Conteneur à deux angles coupés (haut droit et bas gauche), brique visuelle de la nouvelle charte.
/// Le contenu est découpé selon la même forme, pour ne jamais déborder sur les angles.
/// </summary>
public class NotchedBorder : Decorator
{
    /// <summary>Longueur de la coupe des angles, en pixels.</summary>
    public static readonly StyledProperty<double> CutSizeProperty =
        AvaloniaProperty.Register<NotchedBorder, double>(nameof(CutSize), 11d);

    /// <summary>Remplissage de la forme.</summary>
    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        Border.BackgroundProperty.AddOwner<NotchedBorder>();

    /// <summary>Couleur du contour de la forme.</summary>
    public static readonly StyledProperty<IBrush?> BorderBrushProperty =
        Border.BorderBrushProperty.AddOwner<NotchedBorder>();

    /// <summary>Épaisseur du contour, en pixels. Zéro pour ne dessiner aucun contour.</summary>
    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<NotchedBorder, double>(nameof(StrokeThickness));

    static NotchedBorder()
    {
        AffectsRender<NotchedBorder>(BackgroundProperty, BorderBrushProperty, StrokeThicknessProperty, CutSizeProperty);
        AffectsArrange<NotchedBorder>(CutSizeProperty);
    }

    /// <summary>Longueur de la coupe des angles, en pixels.</summary>
    public double CutSize
    {
        get => GetValue(CutSizeProperty);
        set => SetValue(CutSizeProperty, value);
    }

    /// <summary>Remplissage de la forme.</summary>
    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>Couleur du contour de la forme.</summary>
    public IBrush? BorderBrush
    {
        get => GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    /// <summary>Épaisseur du contour, en pixels.</summary>
    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        var thickness = StrokeThickness;
        var pen = thickness > 0 && BorderBrush != null ? new Pen(BorderBrush, thickness) : null;
        context.DrawGeometry(Background, pen, CreateShape(size, thickness / 2));
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var arranged = base.ArrangeOverride(finalSize);
        Clip = CreateShape(arranged, 0);
        return arranged;
    }

    private StreamGeometry CreateShape(Size size, double inset)
    {
        var cut = Math.Min(CutSize, Math.Min(size.Width, size.Height) / 2);
        var left = inset;
        var top = inset;
        var right = Math.Max(left, size.Width - inset);
        var bottom = Math.Max(top, size.Height - inset);

        var geometry = new StreamGeometry();
        using var geometryContext = geometry.Open();
        geometryContext.BeginFigure(new Point(left, top), true);
        geometryContext.LineTo(new Point(right - cut, top));
        geometryContext.LineTo(new Point(right, top + cut));
        geometryContext.LineTo(new Point(right, bottom));
        geometryContext.LineTo(new Point(left + cut, bottom));
        geometryContext.LineTo(new Point(left, bottom - cut));
        geometryContext.EndFigure(true);
        return geometry;
    }
}
