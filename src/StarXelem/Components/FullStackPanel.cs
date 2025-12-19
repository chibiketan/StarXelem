using Avalonia;
using Avalonia.Controls;

namespace StarXelem.Components;

public class FullStackPanel : StackPanel
{
   protected override Size MeasureOverride(Size availableSize)
   {
       double childWidthSum = Children.Sum(c => c.Bounds.Width);
       double childHeightMax = Children.Max(c => c.Bounds.Height);
       int childrenCount = Children.Count;

       if (childWidthSum == 0 || childHeightMax == 0)
       {
           availableSize = base.MeasureOverride(availableSize);
       }

       double targetHeight = availableSize.Height;
       double targetWidth = availableSize.Width;

       targetHeight = double.IsInfinity(targetHeight) ? Bounds.Height : targetHeight;
       targetHeight = double.Max(targetHeight, childHeightMax);

       // if (DisplayMode == DisplayMode.NoOverlap)
       // {
       //     Spacing = 0;
       //
       //     availableSize = new Size(childWidthSum, targetHeight);
       //     availableSize = base.MeasureOverride(availableSize);
       //
       //     return availableSize;
       // }

       // // If inside a ScrollView, bind MaxFitAllWidth to ScrollView.Bounds.Width:
       // if (double.IsPositiveInfinity(targetWidth))
       // {
       //     if (double.IsPositive(MaxFitAllWidth) && double.IsFinite(MaxFitAllWidth))
       //     {
       //         targetWidth = MaxFitAllWidth;
       //     }
       // }

       targetWidth = double.Min(targetWidth, childWidthSum);
       Spacing = -double.Max(0, (childWidthSum - targetWidth) / (childrenCount - 1));

       return new Size(targetWidth, targetHeight);
   }    
}