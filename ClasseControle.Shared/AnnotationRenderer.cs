using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using ClasseControle.Common;

namespace ClasseControle.Shared;

/// <summary>
/// Dessine des tracés d'annotation (coordonnées normalisées) dans un Canvas, à la taille
/// d'une zone donnée. Fichier partagé : compilé dans la console ET dans l'agent, pour que
/// le professeur et l'élève voient exactement le même dessin.
/// </summary>
internal sealed class AnnotationRenderer
{
    private readonly Canvas _canvas;
    private readonly List<AnnotationStroke> _strokes = new();
    private Rect _area;

    public AnnotationRenderer(Canvas canvas) => _canvas = canvas;

    public IReadOnlyList<AnnotationStroke> Strokes => _strokes;
    public int Count => _strokes.Count;

    /// <summary>Zone (dans le Canvas) qui correspond à l'écran de l'élève.</summary>
    public void SetArea(Rect area)
    {
        if (area == _area) return;
        _area = area;
        Redraw();
    }

    /// <summary>Ajoute ou remplace (même Id) un tracé.</summary>
    public void Upsert(AnnotationStroke s)
    {
        int i = _strokes.FindIndex(x => x.Id == s.Id);
        if (i >= 0) _strokes[i] = s; else _strokes.Add(s);
        Redraw();
    }

    public void Remove(int id)
    {
        _strokes.RemoveAll(x => x.Id == id);
        Redraw();
    }

    public void Clear()
    {
        _strokes.Clear();
        Redraw();
    }

    public void Redraw()
    {
        _canvas.Children.Clear();
        if (_area.Width <= 0 || _area.Height <= 0) return;
        foreach (var s in _strokes)
            if (Build(s) is { } shape) _canvas.Children.Add(shape);
    }

    private Point P(double nx, double ny) => new(_area.X + nx * _area.Width, _area.Y + ny * _area.Height);

    private Control? Build(AnnotationStroke s)
    {
        if (s.Points.Count < 2) return null;
        IBrush brush;
        try { brush = new SolidColorBrush(Color.Parse(s.Color)); } catch { brush = Brushes.Red; }
        double w = Math.Max(1.5, s.Width * _area.Width);
        var start = P(s.Points[0], s.Points[1]);
        var end = s.Points.Count >= 4 ? P(s.Points[^2], s.Points[^1]) : start;

        switch (s.Tool)
        {
            case AnnotationTool.Pen:
            case AnnotationTool.Highlighter:
            {
                var pts = new List<Point>();
                for (int i = 0; i + 1 < s.Points.Count; i += 2) pts.Add(P(s.Points[i], s.Points[i + 1]));
                if (pts.Count == 1) pts.Add(pts[0] + new Point(0.1, 0.1)); // simple point
                bool hl = s.Tool == AnnotationTool.Highlighter;
                return new Polyline
                {
                    Points = pts, Stroke = brush, StrokeThickness = hl ? w * 4 : w,
                    StrokeLineCap = hl ? PenLineCap.Square : PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
                    Opacity = hl ? 0.35 : 1,
                };
            }
            case AnnotationTool.Rectangle:
            case AnnotationTool.Ellipse:
            {
                double x = Math.Min(start.X, end.X), y = Math.Min(start.Y, end.Y);
                Shape shape = s.Tool == AnnotationTool.Rectangle ? new Rectangle() : new Ellipse();
                shape.Width = Math.Max(1, Math.Abs(end.X - start.X));
                shape.Height = Math.Max(1, Math.Abs(end.Y - start.Y));
                shape.Stroke = brush;
                shape.StrokeThickness = w;
                Canvas.SetLeft(shape, x);
                Canvas.SetTop(shape, y);
                return shape;
            }
            case AnnotationTool.Arrow:
            {
                double dx = end.X - start.X, dy = end.Y - start.Y, len = Math.Sqrt(dx * dx + dy * dy);
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(start, false);
                    ctx.LineTo(end);
                    ctx.EndFigure(false);
                    if (len > 1)
                    {
                        double head = Math.Min(len * 0.4, w * 5 + 10), ux = dx / len, uy = dy / len;
                        var left = new Point(end.X - head * (ux * 0.87 - uy * 0.5), end.Y - head * (uy * 0.87 + ux * 0.5));
                        var right = new Point(end.X - head * (ux * 0.87 + uy * 0.5), end.Y - head * (uy * 0.87 - ux * 0.5));
                        ctx.BeginFigure(left, false);
                        ctx.LineTo(end);
                        ctx.LineTo(right);
                        ctx.EndFigure(false);
                    }
                }
                return new Avalonia.Controls.Shapes.Path
                {
                    Data = geo, Stroke = brush, StrokeThickness = w,
                    StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
                };
            }
        }
        return null;
    }
}
