using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace AlchemyStars.Avalonia;

public sealed class CameraAxisGizmo : Control
{
    public static readonly StyledProperty<float> CameraYawProperty =
        AvaloniaProperty.Register<CameraAxisGizmo, float>(nameof(CameraYaw));
    public static readonly StyledProperty<float> CameraPitchProperty =
        AvaloniaProperty.Register<CameraAxisGizmo, float>(nameof(CameraPitch));
    public static readonly StyledProperty<string> SceneUpAxisProperty =
        AvaloniaProperty.Register<CameraAxisGizmo, string>(nameof(SceneUpAxis), "z");
    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<CameraAxisGizmo, IBrush?>(nameof(Background));
    public static readonly StyledProperty<IBrush?> BorderBrushProperty =
        AvaloniaProperty.Register<CameraAxisGizmo, IBrush?>(nameof(BorderBrush));

    private static readonly Color XColor = Color.Parse("#B83C3C");
    private static readonly Color YColor = Color.Parse("#2E7D32");
    private static readonly Color ZColor = Color.Parse("#315FAE");
    private Point? lastPointer;
    private bool dragged;
    private AxisEndpoint? hovered;

    static CameraAxisGizmo() => AffectsRender<CameraAxisGizmo>(
        CameraYawProperty, CameraPitchProperty, SceneUpAxisProperty,
        BackgroundProperty, BorderBrushProperty);

    public float CameraYaw { get => GetValue(CameraYawProperty); set => SetValue(CameraYawProperty, value); }
    public float CameraPitch { get => GetValue(CameraPitchProperty); set => SetValue(CameraPitchProperty, value); }
    public string SceneUpAxis { get => GetValue(SceneUpAxisProperty); set => SetValue(SceneUpAxisProperty, value); }
    public IBrush? Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    public IBrush? BorderBrush { get => GetValue(BorderBrushProperty); set => SetValue(BorderBrushProperty, value); }

    public event EventHandler<CameraOrbitEventArgs>? OrbitRequested;
    public event EventHandler<CameraAxisViewEventArgs>? AxisViewRequested;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width < 20 || Bounds.Height < 20) return;
        var center = new Point(Bounds.Width * 0.5, Bounds.Height * 0.5);
        var radius = Math.Max(8, Math.Min(Bounds.Width, Bounds.Height) * 0.5 - 1);
        context.DrawEllipse(Background, BorderBrush is null ? null : new Pen(BorderBrush, 1), center, radius, radius);

        var endpoints = GetEndpoints();
        foreach (var endpoint in endpoints.OrderBy(item => item.Depth))
        {
            var dimmed = endpoint.Sign < 0;
            var brush = new SolidColorBrush(endpoint.Color, dimmed ? 0.38 : 1);
            context.DrawLine(new Pen(brush, endpoint.Sign > 0 ? 2.2 : 1.2), center, endpoint.Point);
        }

        foreach (var endpoint in endpoints.OrderBy(item => item.Depth))
        {
            var isHovered = hovered is { } hot && hot.Axis == endpoint.Axis && hot.Sign == endpoint.Sign;
            var endpointRadius = endpoint.Sign > 0 ? 9.5 : 4.2;
            if (isHovered) endpointRadius += 2;
            var brush = new SolidColorBrush(endpoint.Color, endpoint.Sign > 0 ? 1 : 0.52);
            var outline = isHovered ? new Pen(Brushes.White, 2) : new Pen(new SolidColorBrush(endpoint.Color), 1);
            context.DrawEllipse(brush, outline, endpoint.Point, endpointRadius, endpointRadius);
            if (endpoint.Sign < 0) continue;
            var label = new FormattedText(endpoint.Axis.ToString().ToUpperInvariant(), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold, FontStretch.Normal), 10, Brushes.White);
            context.DrawText(label, new Point(endpoint.Point.X - label.Width * 0.5, endpoint.Point.Y - label.Height * 0.5));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEffectivelyEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        lastPointer = e.GetPosition(this);
        dragged = false;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        if (lastPointer is { } previous && e.Pointer.Captured == this)
        {
            var delta = position - previous;
            lastPointer = position;
            if (Math.Abs(delta.X) + Math.Abs(delta.Y) > 0.5)
            {
                dragged = true;
                OrbitRequested?.Invoke(this, new CameraOrbitEventArgs(delta.X, delta.Y));
            }
            e.Handled = true;
            return;
        }
        var next = HitTestAxis(position);
        if (next != hovered) { hovered = next; InvalidateVisual(); }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Pointer.Captured != this) return;
        if (!dragged && HitTestAxis(e.GetPosition(this)) is { } endpoint)
            AxisViewRequested?.Invoke(this, new CameraAxisViewEventArgs(endpoint.Axis, endpoint.Sign > 0));
        lastPointer = null;
        dragged = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (lastPointer is null && hovered is not null) { hovered = null; InvalidateVisual(); }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var axis = e.Key switch { Key.X => 'x', Key.Y => 'y', Key.Z => 'z', _ => '\0' };
        if (axis == '\0') return;
        AxisViewRequested?.Invoke(this, new CameraAxisViewEventArgs(axis, !e.KeyModifiers.HasFlag(KeyModifiers.Shift)));
        e.Handled = true;
    }

    private AxisEndpoint? HitTestAxis(Point point)
    {
        var matches = GetEndpoints()
            .Where(endpoint => Distance(point, endpoint.Point) <= (endpoint.Sign > 0 ? 13 : 9))
            .OrderBy(endpoint => Distance(point, endpoint.Point))
            .ThenByDescending(endpoint => endpoint.Depth)
            .ToArray();
        return matches.Length == 0 ? null : matches[0];
    }

    private AxisEndpoint[] GetEndpoints()
    {
        var center = new Point(Bounds.Width * 0.5, Bounds.Height * 0.5);
        var length = Math.Max(10, Math.Min(Bounds.Width, Bounds.Height) * 0.5 - 13);
        var camera = new PreviewCamera(CameraYaw, CameraPitch, 1);
        var (forward, right, up) = CastPreviewRenderer.ResolveOrbitBasis(SceneUpAxis, camera);
        var values = new List<AxisEndpoint>(6);
        Add('x', Vector3.UnitX, XColor);
        Add('y', Vector3.UnitY, YColor);
        Add('z', Vector3.UnitZ, ZColor);
        return values.ToArray();

        void Add(char axis, Vector3 vector, Color color)
        {
            foreach (var sign in new[] { -1, 1 })
            {
                var direction = vector * sign;
                values.Add(new AxisEndpoint(axis, sign,
                    new Point(center.X + Vector3.Dot(direction, right) * length,
                        center.Y - Vector3.Dot(direction, up) * length),
                    Vector3.Dot(direction, -forward), color));
            }
        }
    }

    private static double Distance(Point left, Point right)
    {
        var x = left.X - right.X;
        var y = left.Y - right.Y;
        return Math.Sqrt(x * x + y * y);
    }

    private readonly record struct AxisEndpoint(char Axis, int Sign, Point Point, float Depth, Color Color);
}

public sealed class CameraOrbitEventArgs(double x, double y) : EventArgs
{
    public double X { get; } = x;
    public double Y { get; } = y;
}

public sealed class CameraAxisViewEventArgs(char axis, bool positive) : EventArgs
{
    public char Axis { get; } = axis;
    public bool Positive { get; } = positive;
}
