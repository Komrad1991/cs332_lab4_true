using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using AvImage = Avalonia.Controls.Image;
using ImgColor = SixLabors.ImageSharp.Color;
using ImgPointF = SixLabors.ImageSharp.PointF;
using ImgSizeF = SixLabors.ImageSharp.SizeF;

namespace PolygonApp;

public enum ToolMode
{
    Build,
    Translate,
    RotateAroundPoint,
    RotateAroundCenter,
    ScaleAroundPoint,
    ScaleAroundCenter,
    EdgeIntersection,
    PointInPolygon,
    PointVsEdge
}

public class MainWindow : Window
{
    private readonly List<Poly> _polys = new();
    private Poly? _buildPoly;
    private ToolMode _mode = ToolMode.Build;

    // ВАЖНО: поверхность для мыши — Border, а не Image.
    private readonly Border _canvasHost = new();
    private readonly AvImage _canvasImage = new();
    private readonly TextBlock _status = new();
    private readonly TextBox _tbDx = new() { Text = "60" };
    private readonly TextBox _tbDy = new() { Text = "40" };
    private readonly TextBox _tbAngle = new() { Text = "45" };
    private readonly TextBox _tbScale = new() { Text = "1.5" };

    private Vec2 _mouse;
    private readonly List<Vec2> _interPts = new();
    private Vec2? _interResult;
    private bool _interDone;

    private Vec2? _pipPoint;
    private string _pipResult = "";

    private Poly? _pvePoly;
    private int _pveIdx = -1;
    private Vec2? _pvePoint;
    private string _pveResult = "";

    private WriteableBitmap? _bitmap;
    private int _bmpW, _bmpH;

    public MainWindow()
    {
        Title = "Аффинные преобразования полигонов (Avalonia + SixLabors)";
        Width = 1280;
        Height = 820;
        BuildUi();

        // Первый рендер после того, как Avalonia измерит контролы.
        _canvasHost.SizeChanged += (_, _) => Redraw();
        _canvasHost.Loaded += (_, _) => Redraw();
        Opened += (_, _) => Redraw();
    }

    // ---------------- UI ----------------
    private void BuildUi()
    {
        var side = new StackPanel { Width = 300, Spacing = 4, Margin = new Thickness(10) };

        AddRadio(side, "Построение полигона", ToolMode.Build, true);
        AddRadio(side, "Смещение (dx, dy)", ToolMode.Translate);
        AddRadio(side, "Поворот вокруг точки", ToolMode.RotateAroundPoint);
        AddRadio(side, "Поворот вокруг центра", ToolMode.RotateAroundCenter);
        AddRadio(side, "Масштаб отн. точки", ToolMode.ScaleAroundPoint);
        AddRadio(side, "Масштаб отн. центра", ToolMode.ScaleAroundCenter);
        AddRadio(side, "Пересечение 2 рёбер", ToolMode.EdgeIntersection);
        AddRadio(side, "Точка в полигоне", ToolMode.PointInPolygon);
        AddRadio(side, "Точка vs ребро", ToolMode.PointVsEdge);

        side.Children.Add(new TextBlock
        {
            Text = "Параметры:",
            FontWeight = Avalonia.Media.FontWeight.Bold,
            Margin = new Thickness(0, 8, 0, 4)
        });
        side.Children.Add(MakeRow("dx:", _tbDx));
        side.Children.Add(MakeRow("dy:", _tbDy));
        side.Children.Add(MakeRow("Угол (°):", _tbAngle));
        side.Children.Add(MakeRow("Масштаб:", _tbScale));

        var btnApply = new Button { Content = "Применить преобразование", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        btnApply.Click += (_, _) => ApplyTransformFromButton();
        side.Children.Add(btnApply);

        var btnFinish = new Button { Content = "Завершить полигон", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        btnFinish.Click += (_, _) => FinishBuild();
        side.Children.Add(btnFinish);

        var btnClear = new Button { Content = "Очистить сцену", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        btnClear.Click += (_, _) => ClearAll();
        side.Children.Add(btnClear);

        _status.Text = "Готово. ЛКМ — добавить точку, ПКМ — завершить полигон.";
        _status.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        _status.Margin = new Thickness(10);
        _status.FontFamily = new Avalonia.Media.FontFamily("Consolas, monospace");
        _status.FontSize = 12;

        // Image заполняет всю область Border и не мешает приёму событий.
        _canvasImage.Stretch = Avalonia.Media.Stretch.Fill;
        _canvasImage.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        _canvasImage.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        _canvasImage.IsHitTestVisible = false; // события ловит Border

        // Border всегда растягивается, у него есть фон — значит он hit-testable.
        _canvasHost.Background = Avalonia.Media.Brushes.White;
        _canvasHost.Child = _canvasImage;
        _canvasHost.ClipToBounds = true;
        _canvasHost.Focusable = true;
        _canvasHost.PointerPressed += Canvas_PointerPressed;
        _canvasHost.PointerMoved += Canvas_PointerMoved;

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowDefinitions = new RowDefinitions("*,Auto"),
        };
        Grid.SetColumn(side, 0); Grid.SetRow(side, 0); Grid.SetRowSpan(side, 2);
        grid.Children.Add(side);

        Grid.SetColumn(_canvasHost, 1); Grid.SetRow(_canvasHost, 0);
        grid.Children.Add(_canvasHost);

        Grid.SetColumn(_status, 1); Grid.SetRow(_status, 1);
        grid.Children.Add(_status);

        Content = grid;
    }

    private Control MakeRow(string label, TextBox tb)
    {
        var sp = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
        sp.Children.Add(new TextBlock
        {
            Text = label,
            Width = 90,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });
        tb.Width = 160;
        sp.Children.Add(tb);
        return sp;
    }

    private void AddRadio(Control parent, string text, ToolMode mode, bool isChecked = false)
    {
        var rb = new RadioButton { Content = text, IsChecked = isChecked, GroupName = "tool" };
        rb.IsCheckedChanged += (_, _) =>
        {
            if (rb.IsChecked != true) return;
            _mode = mode;
            ResetTransient();
            _status.Text = "Режим: " + text;
            Redraw();
        };
        ((StackPanel)parent).Children.Add(rb);
    }

    // ---------------- Служебные ----------------
    private void ResetTransient()
    {
        _interPts.Clear();
        _interResult = null;
        _interDone = false;
        _pipPoint = null;
        _pipResult = "";
        _pvePoly = null;
        _pveIdx = -1;
        _pvePoint = null;
        _pveResult = "";
    }

    private void ClearAll()
    {
        _polys.Clear();
        _buildPoly = null;
        ResetTransient();
        _status.Text = "Сцена очищена.";
        Redraw();
    }

    private Poly? CurrentPoly() => _polys.Count > 0 ? _polys[^1] : null;

    private void FinishBuild()
    {
        if (_buildPoly is { Vertices.Count: > 0 })
        {
            _polys.Add(_buildPoly);
            int n = _buildPoly.Vertices.Count;
            string kind = n == 1 ? "точка" : n == 2 ? "ребро" : "полигон";
            _status.Text = $"Добавлено: {kind} ({n} верш.).";
        }
        _buildPoly = null;
        Redraw();
    }

    // ---------------- Преобразования ----------------
    private void ApplyTransformFromButton()
    {
        var poly = CurrentPoly();
        if (poly == null) { _status.Text = "Нет полигона для преобразования."; return; }

        try
        {
            switch (_mode)
            {
                case ToolMode.Translate:
                    {
                        double dx = Parse(_tbDx.Text);
                        double dy = Parse(_tbDy.Text);
                        poly.Apply(Mat3.Translation(dx, dy));
                        _status.Text = $"Смещение на ({dx}, {dy}).";
                        break;
                    }
                case ToolMode.RotateAroundCenter:
                    {
                        double deg = Parse(_tbAngle.Text);
                        poly.Apply(Mat3.Rotation(deg * Math.PI / 180.0, poly.Centroid()));
                        _status.Text = $"Поворот на {deg:F2}° вокруг центра.";
                        break;
                    }
                case ToolMode.ScaleAroundCenter:
                    {
                        double k = Parse(_tbScale.Text);
                        poly.Apply(Mat3.Scale(k, k, poly.Centroid()));
                        _status.Text = $"Масштаб {k} относительно центра.";
                        break;
                    }
                default:
                    _status.Text = "Кликните точку-центр на холсте.";
                    return;
            }
        }
        catch (FormatException)
        {
            _status.Text = "Некорректный параметр.";
            return;
        }
        Redraw();
    }

    private static double Parse(string? s) =>
        double.Parse(s ?? "0", CultureInfo.InvariantCulture);

    private void ApplyRotationAround(Vec2 center)
    {
        var poly = CurrentPoly();
        if (poly == null) { _status.Text = "Нет полигона."; return; }
        try
        {
            double deg = Parse(_tbAngle.Text);
            poly.Apply(Mat3.Rotation(deg * Math.PI / 180.0, center));
            _status.Text = $"Поворот на {deg:F2}° вокруг ({center.X:F0}; {center.Y:F0}).";
        }
        catch { _status.Text = "Некорректный угол."; }
    }

    private void ApplyScaleAround(Vec2 center)
    {
        var poly = CurrentPoly();
        if (poly == null) { _status.Text = "Нет полигона."; return; }
        try
        {
            double k = Parse(_tbScale.Text);
            poly.Apply(Mat3.Scale(k, k, center));
            _status.Text = $"Масштаб {k} относительно ({center.X:F0}; {center.Y:F0}).";
        }
        catch { _status.Text = "Некорректный масштаб."; }
    }

    // ---------------- Мышь ----------------
    private void Canvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        var pt = e.GetPosition(_canvasHost);
        _mouse = new Vec2(pt.X, pt.Y);

        if (_mode == ToolMode.Build && _buildPoly is { Vertices.Count: > 0 })
            Redraw();
        else if (_mode == ToolMode.EdgeIntersection && _interPts.Count == 3)
            Redraw();
    }

    private void Canvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pt = e.GetPosition(_canvasHost);
        var p = new Vec2(pt.X, pt.Y);
        var props = e.GetCurrentPoint(_canvasHost).Properties;

        switch (_mode)
        {
            case ToolMode.Build: HandleBuildClick(p, props.IsRightButtonPressed); break;
            case ToolMode.RotateAroundPoint: ApplyRotationAround(p); break;
            case ToolMode.ScaleAroundPoint: ApplyScaleAround(p); break;
            case ToolMode.EdgeIntersection: HandleEdgeIntersection(p); break;
            case ToolMode.PointInPolygon: HandlePointInPolygon(p); break;
            case ToolMode.PointVsEdge: HandlePointVsEdge(p); break;
            default:
                _status.Text = "Используйте кнопку 'Применить преобразование'.";
                break;
        }
        Redraw();
    }

    private void HandleBuildClick(Vec2 p, bool isRight)
    {
        if (isRight) { FinishBuild(); return; }
        _buildPoly ??= new Poly();
        _buildPoly.Vertices.Add(p);
        _status.Text = $"Точек в текущем полигоне: {_buildPoly.Vertices.Count} (ПКМ — завершить).";
    }

    private void HandleEdgeIntersection(Vec2 p)
    {
        if (_interDone)
        {
            _interPts.Clear();
            _interResult = null;
            _interDone = false;
        }
        _interPts.Add(p);

        if (_interPts.Count == 4)
        {
            _interResult = GeomAlgorithms.SegmentIntersection(_interPts[0], _interPts[1], _interPts[2], _interPts[3]);
            _interDone = true;
            _status.Text = _interResult.HasValue
                ? $"Пересечение: ({_interResult.Value.X:F1}; {_interResult.Value.Y:F1}). Следующий клик — новое построение."
                : "Отрезки не пересекаются. Следующий клик — новое построение.";
        }
        else
        {
            _status.Text = $"Точка {_interPts.Count} из 4. Первое ребро: A1→A2, второе: B1→B2.";
        }
    }

    private void HandlePointInPolygon(Vec2 p)
    {
        _pipPoint = p;
        if (_polys.Count == 0) { _pipResult = "Нет полигонов."; _status.Text = _pipResult; return; }

        var parts = new List<string>();
        for (int i = 0; i < _polys.Count; i++)
        {
            var poly = _polys[i];
            if (poly.Vertices.Count < 3)
            {
                parts.Add($"#{i}: не полигон ({poly.Vertices.Count} верш.)");
                continue;
            }
            bool inAny = poly.ContainsPoint(p);
            bool inConv = poly.ContainsPointConvex(p);
            string shape = poly.IsConvex() ? "выпуклый" : "невыпуклый";
            parts.Add($"#{i} ({shape}): внутри={inAny}, convex-тест={inConv}");
        }
        _pipResult = string.Join(" | ", parts);
        _status.Text = _pipResult;
    }

    private void HandlePointVsEdge(Vec2 p)
    {
        if (_pvePoly == null)
        {
            double best = double.MaxValue;
            Poly? bp = null; int bi = -1;
            foreach (var poly in _polys)
            {
                if (poly.Vertices.Count < 2) continue;
                for (int i = 0; i < poly.Vertices.Count; i++)
                {
                    var a = poly.Vertices[i];
                    var b = poly.Vertices[(i + 1) % poly.Vertices.Count];
                    double d = GeomAlgorithms.PointSegmentDist(p, a, b);
                    if (d < best) { best = d; bp = poly; bi = i; }
                }
            }
            if (bp == null) { _status.Text = "Нет рёбер для анализа."; return; }
            _pvePoly = bp;
            _pveIdx = bi;
            _status.Text = $"Ребро выбрано (расст. {best:F1}). Кликните точку для классификации.";
            return;
        }

        var A = _pvePoly.Vertices[_pveIdx];
        var B = _pvePoly.Vertices[(_pveIdx + 1) % _pvePoly.Vertices.Count];
        int side = GeomAlgorithms.SideOfEdge(A, B, p);
        string sideName = side > 0 ? "СПРАВА" : side < 0 ? "СЛЕВА" : "на прямой";
        _pvePoint = p;
        _pveResult = $"Точка {sideName} от ребра";
        _status.Text = _pveResult + ". Кликните ещё раз, чтобы выбрать ребро заново.";
        _pvePoly = null;
        _pveIdx = -1;
    }

    // ---------------- Рендер через SixLabors ----------------
    private void Redraw()
    {
        int w = (int)Math.Round(_canvasHost.Bounds.Width);
        int h = (int)Math.Round(_canvasHost.Bounds.Height);
        if (w <= 0 || h <= 0) return;

        EnsureBitmap(w, h);

        using var image = new SixLabors.ImageSharp.Image<Rgba32>(w, h, new Rgba32(255, 255, 255));

        image.Mutate(ctx =>
        {
            for (int i = 0; i < _polys.Count; i++)
                DrawPoly(ctx, _polys[i],
                    i == _polys.Count - 1 ? ImgColor.SteelBlue : ImgColor.LightSteelBlue,
                    i == _polys.Count - 1);

            if (_buildPoly is { Vertices.Count: > 0 })
            {
                DrawPoly(ctx, _buildPoly, ImgColor.OrangeRed, false);

                var last = _buildPoly.Vertices[^1];
                ctx.DrawLine(Pens.Solid(ImgColor.LightGray, 1f),
                             last.ToPointF(), _mouse.ToPointF());
            }

            if (_mode == ToolMode.EdgeIntersection)
                DrawIntersection(ctx);

            if (_mode == ToolMode.PointInPolygon && _pipPoint.HasValue)
            {
                var p = _pipPoint.Value;
                ctx.Fill(ImgColor.Magenta, Circle(p, 5f));
                ctx.DrawText(_pipResult, GetFont(12), ImgColor.Black,
                             new ImgPointF((float)p.X + 8, (float)p.Y + 8));
            }

            if (_mode == ToolMode.PointVsEdge)
            {
                if (_pvePoly != null && _pveIdx >= 0)
                {
                    var a = _pvePoly.Vertices[_pveIdx];
                    var b = _pvePoly.Vertices[(_pveIdx + 1) % _pvePoly.Vertices.Count];
                    ctx.DrawLine(Pens.Solid(ImgColor.Red, 3f), a.ToPointF(), b.ToPointF());
                }
                if (_pvePoint.HasValue)
                {
                    var p = _pvePoint.Value;
                    ctx.Fill(ImgColor.Magenta, Circle(p, 5f));
                    ctx.DrawText(_pveResult, GetFont(12), ImgColor.Black,
                                 new ImgPointF((float)p.X + 8, (float)p.Y + 8));
                }
            }
        });

        byte[] pixels = new byte[w * h * 4];
        image.CopyPixelDataTo(pixels);

        using (var fb = _bitmap!.Lock())
        {
            for (int y = 0; y < h; y++)
            {
                IntPtr dst = IntPtr.Add(fb.Address, y * fb.RowBytes);
                Marshal.Copy(pixels, y * w * 4, dst, w * 4);
            }
        }

        _canvasImage.InvalidateVisual();
    }

    private void EnsureBitmap(int w, int h)
    {
        if (_bitmap == null || _bmpW != w || _bmpH != h)
        {
            _bitmap = new WriteableBitmap(
                new PixelSize(w, h),
                new Avalonia.Vector(96, 96),
                Avalonia.Platform.PixelFormat.Bgra8888,
                Avalonia.Platform.AlphaFormat.Unpremul);
            _bmpW = w; _bmpH = h;
            _canvasImage.Source = _bitmap;
        }
    }

    private static EllipsePolygon Circle(Vec2 c, float radius) =>
        new EllipsePolygon(
            new ImgPointF((float)c.X, (float)c.Y),
            new ImgSizeF(radius * 2f, radius * 2f));

    private static void DrawPoly(IImageProcessingContext ctx, Poly poly, ImgColor color, bool highlight)
    {
        var pts = poly.Vertices;
        if (pts.Count == 0) return;

        var pen = Pens.Solid(color, highlight ? 3f : 2f);

        if (pts.Count == 1)
        {
            ctx.Fill(color, Circle(pts[0], 5f));
            return;
        }

        for (int i = 0; i < pts.Count - 1; i++)
            ctx.DrawLine(pen, pts[i].ToPointF(), pts[i + 1].ToPointF());

        if (pts.Count >= 3)
            ctx.DrawLine(pen, pts[^1].ToPointF(), pts[0].ToPointF());

        foreach (var v in pts)
            ctx.Fill(ImgColor.DarkBlue, Circle(v, 3f));
    }

    private void DrawIntersection(IImageProcessingContext ctx)
    {
        if (_interPts.Count >= 2)
            ctx.DrawLine(Pens.Solid(ImgColor.Blue, 2f),
                         _interPts[0].ToPointF(), _interPts[1].ToPointF());

        if (_interPts.Count == 3)
            ctx.DrawLine(Pens.Solid(ImgColor.Green, 2f),
                         _interPts[2].ToPointF(), _mouse.ToPointF());

        if (_interPts.Count == 4)
            ctx.DrawLine(Pens.Solid(ImgColor.Green, 2f),
                         _interPts[2].ToPointF(), _interPts[3].ToPointF());

        foreach (var p in _interPts)
            ctx.Fill(ImgColor.DarkBlue, Circle(p, 4f));

        if (_interPts.Count == 3)
        {
            var inter = GeomAlgorithms.SegmentIntersection(_interPts[0], _interPts[1], _interPts[2], _mouse);
            if (inter.HasValue)
                ctx.Fill(ImgColor.Red, Circle(inter.Value, 6f));
        }
        else if (_interResult.HasValue)
        {
            ctx.Fill(ImgColor.Red, Circle(_interResult.Value, 6f));
        }
    }

    private static Font GetFont(float size) => SystemFonts.CreateFont("Arial", size);
}