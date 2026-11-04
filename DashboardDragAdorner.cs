using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace StreamlootsManager;

internal sealed class DashboardDragAdorner : Adorner
{
    private VisualCollection? _visuals;
    private readonly Image _preview;
    private readonly double _width;
    private readonly double _height;
    private Point _pointer;

    public DashboardDragAdorner(UIElement adornedElement, BitmapSource snapshot, double width, double height)
        : base(adornedElement)
    {
        _visuals = new VisualCollection(this);
        _width = width;
        _height = height;
        IsHitTestVisible = false;
        _preview = new Image
        {
            Source = snapshot,
            Width = width,
            Height = height,
            Opacity = 0.82,
            IsHitTestVisible = false,
            Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(93, 110, 255),
                BlurRadius = 18,
                ShadowDepth = 5,
                Opacity = 0.32
            }
        };
        _visuals.Add(_preview);
    }

    public void Move(Point pointer)
    {
        _pointer = pointer;
        InvalidateArrange();
    }

    protected override int VisualChildrenCount => _visuals?.Count ?? 0;

    protected override Visual GetVisualChild(int index)
    {
        if (_visuals is null || index < 0 || index >= _visuals.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        return _visuals[index];
    }

    protected override Size MeasureOverride(Size constraint)
    {
        _preview.Measure(new Size(_width, _height));
        var adornedSize = AdornedElement.RenderSize;
        var width = double.IsFinite(constraint.Width) ? constraint.Width : Math.Max(_width, adornedSize.Width);
        var height = double.IsFinite(constraint.Height) ? constraint.Height : Math.Max(_height, adornedSize.Height);
        return new Size(Math.Max(0, width), Math.Max(0, height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _preview.Arrange(new Rect(_pointer.X + 16, _pointer.Y + 14, _width, _height));
        return finalSize;
    }
}
