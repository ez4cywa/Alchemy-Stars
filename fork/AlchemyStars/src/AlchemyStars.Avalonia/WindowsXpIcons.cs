using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using IconPath = Avalonia.Controls.Shapes.Path;

namespace AlchemyStars.Avalonia;

/// <summary>Original, layered Luna-style artwork. Each icon occupies a fixed 24-unit canvas.</summary>
internal static class WindowsXpIcons
{
    private static readonly IBrush Blue = Paint("#b7e5ff", "#3e83d0");
    private static readonly IBrush Gold = Paint("#fff5b4", "#efb342");
    private static readonly IBrush Green = Paint("#c6efa0", "#45922e");
    private static readonly IBrush Silver = Paint("#ffffff", "#aab8cb");
    private static readonly IBrush Red = Paint("#ffb0a0", "#d33b27");
    private static readonly IBrush Paper = Paint("#ffffff", "#e0e8f3");
    private const string Outline = "#345278";

    public static Canvas Create(string glyph)
    {
        var c = new Canvas { Width = 24, Height = 24, IsHitTestVisible = false };
        void Shape(string data, IBrush? fill, string stroke = Outline, double width = 0.85) => c.Children.Add(new IconPath
        {
            Data = Geometry.Parse(data), Fill = fill, Stroke = new SolidColorBrush(Color.Parse(stroke)),
            StrokeThickness = width, StrokeJoin = PenLineJoin.Round, StrokeLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        });
        void Line(string data, string color = "#ffffff", double width = 1) => Shape(data, null, color, width);
        void Folder()
        {
            Shape("M3,7 L3,4.5 L9,4.5 L11,7 L20,7 L20,19 L3,19 Z", Gold, "#9c7428");
            Shape("M3,10 L21,9 L19,20 L3,20 Z", Gold, "#9c7428");
            Line("M4.5,11 L19,10.5", "#fffbd8");
        }
        void Page()
        {
            Shape("M5,3 L15,3 L20,8 L20,21 L5,21 Z", Paper);
            Shape("M15,3 L15,8 L20,8 Z", Blue);
            Line("M8,11 L15,11 M8,14 L15,14 M8,17 L12,17", "#8ba5c3");
        }
        void Disk()
        {
            Shape("M3,3 L18,3 L21,6 L21,21 L3,21 Z", Blue);
            Shape("M6,3 L17,3 L17,10 L6,10 Z", Silver);
            Shape("M13,4.5 L15,4.5 L15,8.5 L13,8.5 Z", new SolidColorBrush(Color.Parse("#46618b")));
            Shape("M6,13 L18,13 L18,21 L6,21 Z", Paper);
            Line("M8,16 L16,16 M8,18 L14,18", "#a4b4c8");
        }
        void Monitor()
        {
            Shape("M9,17 L15,17 L16,20 L19,21 L5,21 L8,20 Z", Silver);
            Shape("M3,3 L21,3 L21,17 L3,17 Z", Silver);
            Shape("M5,5 L19,5 L19,14 L5,14 Z", Blue);
            Line("M6,6 L17,6", "#e5f6ff");
        }
        void Arrow(bool up, double x = 12)
        {
            var d = up
                ? FormattableString.Invariant($"M{x},3 L{x+6},10 L{x+2.5},10 L{x+2.5},20 L{x-2.5},20 L{x-2.5},10 L{x-6},10 Z")
                : FormattableString.Invariant($"M{x-2.5},3 L{x+2.5},3 L{x+2.5},13 L{x+6},13 L{x},20 L{x-6},13 L{x-2.5},13 Z");
            Shape(d, Green, "#3b7529");
            Line(up ? $"M{x},5 L{x-3.5},9" : $"M{x-1},5 L{x-1},13", "#e3ffcb");
        }
        void Orb(string data, IBrush color) => Shape(data, color);

        switch (glyph)
        {
            case "about":
            case "notification":
                Shape("M12,3 A9,9 0 1,0 12,21 A9,9 0 1,0 12,3 Z", glyph == "about" ? Blue : Gold);
                Line("M6,8 Q9,3 15,5", "#ffffff", 1.3);
                Line(glyph == "about" ? "M12,7 L12,7.3 M11,10 L12,10 L12,17 M10,17 L14,17" : "M12,7 L12,13 M12,17 L12,17.3", glyph == "about" ? "#ffffff" : "#654600", 2);
                break;
            case "add":
                Shape("M9,3 L15,3 L15,9 L21,9 L21,15 L15,15 L15,21 L9,21 L9,15 L3,15 L3,9 L9,9 Z", Green, "#3b7529");
                Line("M10,4 L14,4 M4,10 L10,10");
                break;
            case "animation-library":
                Shape("M3,4 L21,4 L21,20 L3,20 Z", Blue);
                Shape("M7,5 L17,5 L17,19 L7,19 Z", Paper);
                Line("M4.5,7 L5.5,7 M4.5,11 L5.5,11 M4.5,15 L5.5,15 M18.5,7 L19.5,7 M18.5,11 L19.5,11 M18.5,15 L19.5,15", "#ffffff", 1.5);
                Shape("M10,8 L16,12 L10,16 Z", Green, "#3b7529");
                break;
            case "animation-layers":
                Shape("M3,5 L18,5 L18,9 L3,9 Z", Blue);
                Shape("M5,11 L21,11 L21,15 L5,15 Z", Gold, "#9c7428");
                Shape("M3,17 L16,17 L16,21 L3,21 Z", Green, "#3b7529");
                Line("M5,6 L15,6 M7,12 L18,12 M5,18 L13,18");
                break;
            case "batch-processing":
                foreach (var (x,y,paint) in new (int,int,IBrush)[] { (3,3,Blue),(13,3,Gold),(3,13,Green),(13,13,Silver) })
                {
                    Shape($"M{x},{y} L{x+8},{y} L{x+8},{y+8} L{x},{y+8} Z", paint);
                    Line($"M{x+1},{y+1} L{x+6},{y+1}");
                }
                break;
            case "camera-view":
                Shape("M3,7 L7,7 L9,4 L15,4 L17,7 L21,7 L21,20 L3,20 Z", Silver);
                Shape("M12,8 A5,5 0 1,0 12,18 A5,5 0 1,0 12,8 Z", Blue);
                Shape("M12,10 A3,3 0 1,0 12,16 A3,3 0 1,0 12,10 Z", Paint("#254675", "#69abdf"));
                Line("M10,12 L11,11", "#ffffff", 1.5);
                Shape("M4.5,8 L7,8 L7,10 L4.5,10 Z", Gold, "#9c7428");
                break;
            case "cast-preview":
                Monitor();
                Shape("M10,7 L16,10 L10,13 Z", Green, "#3b7529");
                break;
            case "delete":
                Shape("M6,7 L19,7 L17,21 L8,21 Z", Silver);
                Shape("M5,5 L10,5 L10,3 L15,3 L15,5 L20,5 L20,8 L5,8 Z", Blue);
                Line("M10,10 L10.5,18 M13,10 L13,18 M16,10 L15.5,18", "#778caa");
                break;
            case "dual-wield":
                Shape("M4,3 L9,3 L9,13 L7,15 L7,20 L4,20 L4,14 L3,12 Z", Blue);
                Shape("M15,3 L20,3 L21,12 L20,14 L20,20 L17,20 L17,15 L15,13 Z", Silver);
                Shape("M3,12 L10,12 L10,15 L3,15 Z", Gold, "#9c7428");
                Shape("M14,12 L21,12 L21,15 L14,15 Z", Gold, "#9c7428");
                Line("M5,4 L5,10 M17,4 L17,10");
                break;
            case "export-animation":
                Shape("M3,16 L8,16 L8,19 L16,19 L16,16 L21,16 L21,21 L3,21 Z", Silver);
                Shape("M9,3 L15,3 L15,10 L19,10 L12,17 L5,10 L9,10 Z", Green, "#3b7529");
                Line("M10,4 L10,10");
                break;
            case "fit-view":
                Shape("M3,3 L21,3 L21,21 L3,21 Z", Silver);
                Shape("M7,7 L17,7 L17,17 L7,17 Z", Blue);
                Line("M5,9 L5,5 L9,5 M15,5 L19,5 L19,9 M19,15 L19,19 L15,19 M9,19 L5,19 L5,15", "#1e4c86", 1.4);
                break;
            case "hand-pose":
                Shape("M6,12 L6,7 Q6,5 8,6 L8,4 Q9,2 10,4 L10,8 L10,4 Q11,2 12,4 L12,8 L12,5 Q13,3 14,5 L14,10 L16,8 Q18,7 18,9 L17,15 Q16,19 12,20 L8,20 Q5,18 4,15 L3,12 Q3,10 5,11 Z", Paint("#fff0ce", "#d9a269"), "#96703d");
                Shape("M7,19 L14,19 L14,21 L7,21 Z", Blue);
                Line("M8,11 L8,15 M11,11 L11,15", "#b98251");
                break;
            case "import-assets":
                Folder();
                Shape("M13,3 L17,3 L17,10 L20,10 L15,15 L10,10 L13,10 Z", Green, "#3b7529");
                break;
            case "inverse-kinematics":
                Shape("M5,6 L8,4 L19,17 L16,20 Z", Silver);
                Orb("M6,3 A3,3 0 1,0 6,9 A3,3 0 1,0 6,3 Z", Blue);
                Orb("M12,9 A3,3 0 1,0 12,15 A3,3 0 1,0 12,9 Z", Gold);
                Orb("M18,15 A3,3 0 1,0 18,21 A3,3 0 1,0 18,15 Z", Green);
                Line("M5,5 L6,4 M11,11 L12,10 M17,17 L18,16");
                break;
            case "language":
                Shape("M12,3 A9,9 0 1,0 12,21 A9,9 0 1,0 12,3 Z", Blue);
                Shape("M5,7 L8,4 L11,4 L10,7 L13,9 L10,12 L7,11 L6,15 L4,12 Z M15,12 L20,10 L20,15 L17,19 L15,17 Z", Green, "#457b38", .6);
                Line("M6,7 Q10,3 15,5", "#d8f3ff", 1.2);
                break;
            case "model-parts":
                Shape("M3,6 L9,3 L15,6 L9,9 Z M3,6 L9,9 L9,16 L3,13 Z", Gold, "#9c7428");
                Shape("M9,9 L15,6 L15,13 L9,16 Z", Paint("#e6b54f", "#ba812b"), "#9c7428");
                Shape("M11,13 L16,10 L21,13 L16,16 Z M11,13 L16,16 L16,21 L11,18 Z", Blue);
                Shape("M16,16 L21,13 L21,18 L16,21 Z", Paint("#6da4d6", "#4273aa"));
                break;
            case "move-up": Arrow(true); break;
            case "move-down": Arrow(false); break;
            case "play":
                Shape("M6,3 L21,12 L6,21 Z", Green, "#3b7529");
                Line("M7.5,5 L7.5,17", "#e3ffcb");
                break;
            case "pause":
                Shape("M5,3 L10,3 L10,21 L5,21 Z M14,3 L19,3 L19,21 L14,21 Z", Blue);
                Line("M6,4 L6,19 M15,4 L15,19");
                break;
            case "next-frame":
                Shape("M4,4 L16,12 L4,20 Z M18,4 L21,4 L21,20 L18,20 Z", Blue);
                Line("M5,6 L5,16");
                break;
            case "previous-frame":
                Shape("M20,4 L8,12 L20,20 Z M3,4 L6,4 L6,20 L3,20 Z", Blue);
                Line("M19,6 L11,12");
                break;
            case "output-naming":
                Page();
                Shape("M4,18 L16,7 L19,10 L7,21 L3,21 Z", Gold, "#9c7428");
                Shape("M16,7 L18,5 L21,8 L19,10 Z", Red, "#9e382b");
                Line("M6,18 L16,9", "#fff7bd");
                break;
            case "output-settings":
                Shape("M3,3 L21,3 L21,21 L3,21 Z", Silver);
                Line("M7,6 L7,18 M12,6 L12,18 M17,6 L17,18", "#607590", 1.5);
                Shape("M5,8 L9,8 L9,11 L5,11 Z", Blue);
                Shape("M10,14 L14,14 L14,17 L10,17 Z", Green);
                Shape("M15,6 L19,6 L19,9 L15,9 Z", Gold);
                break;
            case "project-workspace":
                Folder();
                Shape("M9,12 L18,12 L18,19 L9,19 Z", Paper);
                Shape("M9,12 L18,12 L18,14 L9,14 Z", Blue);
                Line("M11,16 L16,16", "#7a92b3");
                break;
            case "restore-layout":
                Shape("M7,5 A8,8 0 1,1 4,16 L8,14 A4,4 0 1,0 9,9 L12,12 L3,12 L3,3 Z", Green, "#3b7529");
                Line("M11,5 Q19,4 20,12", "#d8f7c7");
                break;
            case "save": Disk(); break;
            case "save-as":
                Disk();
                Shape("M12,18 L19,11 L21,13 L14,20 L11,21 Z", Gold, "#9c7428");
                Line("M13,18 L19,12", "#fffad8");
                break;
            case "timeline-playback":
                Shape("M3,4 L21,4 L21,20 L3,20 Z", Paper);
                Shape("M3,4 L21,4 L21,8 L3,8 Z", Blue);
                Line("M7,5 L7,7 M12,5 L12,7 M17,5 L17,7", "#ffffff");
                Shape("M6,11 L10,11 L10,17 L6,17 Z", Gold);
                Shape("M13,10 L19,14 L13,18 Z", Green, "#3b7529");
                break;
            case "weapon-follow":
                Shape("M6,7 C17,5 8,19 18,16 L16,13 L21,15 L19,21 L18,18 C6,21 14,8 6,10 Z", Green, "#3b7529");
                Shape("M5,3 A4,4 0 1,0 5,11 A4,4 0 1,0 5,3 Z", Blue);
                Line("M4,5 L5,4");
                break;
            case "weapon-processing-mode":
                Line("M12,7 L12,12 M6,17 L6,12 L18,12 L18,17", "#536c8c", 2);
                Shape("M8,3 L16,3 L16,8 L8,8 Z", Blue);
                Shape("M3,16 L9,16 L9,21 L3,21 Z", Gold);
                Shape("M15,16 L21,16 L21,21 L15,21 Z", Green);
                break;
            case "zoom-in":
            case "zoom-out":
                Shape("M14,13 L21,19 L19,21 L12,14 Z", Gold, "#9c7428");
                Shape("M9,3 A7,7 0 1,0 9,17 A7,7 0 1,0 9,3 Z", Silver);
                Shape("M9,5 A5,5 0 1,0 9,15 A5,5 0 1,0 9,5 Z", Blue);
                Line("M6,10 L12,10", "#214f83", 1.5);
                if (glyph == "zoom-in") Line("M9,7 L9,13", "#214f83", 1.5);
                Line("M6,7 L8,6", "#ffffff");
                break;
            default: throw new ArgumentOutOfRangeException(nameof(glyph), glyph, "XP icon has not been drawn.");
        }
        return c;
    }

    private static LinearGradientBrush Paint(string top, string bottom) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0.8, 1, RelativeUnit.Relative),
        GradientStops = new GradientStops { new(Color.Parse(top), 0), new(Color.Parse(bottom), 1) },
    };
}
