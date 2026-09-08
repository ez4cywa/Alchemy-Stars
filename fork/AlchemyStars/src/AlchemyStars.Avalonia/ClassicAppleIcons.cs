using Avalonia.Controls;
using Avalonia.Media;
using IconPath = Avalonia.Controls.Shapes.Path;

namespace AlchemyStars.Avalonia;

/// <summary>Original graphite symbols, drawn on an optically inset 24-unit canvas.</summary>
internal static class ClassicAppleIcons
{
    public static Control Create(string glyph, IBrush? brush)
    {
        var canvas = new Canvas { Width = 24, Height = 24, IsHitTestVisible = false };
        var ink = brush ?? Brushes.DimGray;
        void Line(string data, double width = 1.65) => canvas.Children.Add(new IconPath
        {
            Data = Geometry.Parse(data), Stroke = ink, StrokeThickness = width,
            StrokeJoin = PenLineJoin.Round, StrokeLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        });
        void Solid(string data) => canvas.Children.Add(new IconPath
        {
            Data = Geometry.Parse(data), Fill = ink, IsHitTestVisible = false,
        });
        void Circle(double x, double y, double radius) => Line(FormattableString.Invariant(
            $"M{x},{y-radius} A{radius},{radius} 0 1,0 {x},{y+radius} A{radius},{radius} 0 1,0 {x},{y-radius} Z"));
        void Folder() => Line("M3,8 V6 Q3,4.5 4.5,4.5 H9 L11,7 H19.5 Q21,7 21,8.5 V18 Q21,19.5 19.5,19.5 H4.5 Q3,19.5 3,18 Z M3,9 H21");
        void Page() => Line("M14,3.5 H6 Q4.5,3.5 4.5,5 V19 Q4.5,20.5 6,20.5 H18 Q19.5,20.5 19.5,19 V9 Z M14,3.5 V9 H19.5");
        switch (glyph)
        {
            case "about":
                Circle(12, 12, 9); Line("M12,10.5 V17 M10.5,17 H13.5"); Solid("M12,6 A1,1 0 1,0 12,8 A1,1 0 1,0 12,6 Z"); break;
            case "notification":
                Line("M5,16.5 H19 L17.5,14 V10 Q17.5,5.5 12,5.5 Q6.5,5.5 6.5,10 V14 Z M10,19 Q12,21 14,19 M12,3 V5.5"); break;
            case "add": Line("M12,4.5 V19.5 M4.5,12 H19.5", 1.9); break;
            case "animation-library":
                Line("M5,4 H19 Q20.5,4 20.5,5.5 V18.5 Q20.5,20 19,20 H5 Q3.5,20 3.5,18.5 V5.5 Q3.5,4 5,4 Z M7,4 V20 M17,4 V20 M3.5,8 H7 M3.5,12 H7 M3.5,16 H7 M17,8 H20.5 M17,12 H20.5 M17,16 H20.5", 1.4);
                Solid("M10,8.5 L15,12 L10,15.5 Z"); break;
            case "animation-layers": Line("M3,7 L12,3 L21,7 L12,11 Z M3,12 L12,16 L21,12 M3,17 L12,21 L21,17"); break;
            case "batch-processing":
                Line("M4,4 H9 V9 H4 Z M15,4 H20 V9 H15 Z M4,15 H9 V20 H4 Z M15,15 H20 V20 H15 Z", 1.55); break;
            case "camera-view":
                Line("M4,7 H8 L9.5,4.5 H14.5 L16,7 H20 Q21,7 21,8.5 V18 Q21,19.5 19.5,19.5 H4.5 Q3,19.5 3,18 V8.5 Q3,7 4,7 Z"); Circle(12, 13, 4); Solid("M18,8.5 H19.5 V10 H18 Z"); break;
            case "cast-preview":
                Line("M4,4 H20 Q21,4 21,5 V16 Q21,17 20,17 H4 Q3,17 3,16 V5 Q3,4 4,4 Z M9,21 H15 M12,17 V21"); Solid("M10,7 L16,10.5 L10,14 Z"); break;
            case "delete":
                Line("M4.5,6.5 H19.5 M9,6.5 V3.5 H15 V6.5 M6.5,6.5 L7.5,20 H16.5 L17.5,6.5 M10,10 V16.5 M14,10 V16.5"); break;
            case "dual-wield":
                Line("M4.5,3.5 L8,6 V14 H4.5 Z M3,15 H9.5 M6.25,15 V20.5 M15.5,6 L19,3.5 V14 H15.5 Z M14,15 H20.5 M17.25,15 V20.5"); break;
            case "export-animation":
                Line("M4,14.5 V19.5 H20 V14.5 M12,3 V15 M7.5,10.5 L12,15 L16.5,10.5"); break;
            case "fit-view":
                Line("M3.5,9 V3.5 H9 M15,3.5 H20.5 V9 M20.5,15 V20.5 H15 M9,20.5 H3.5 V15 M8,8 H16 V16 H8 Z"); break;
            case "hand-pose":
                Line("M7.5,12 V6 Q7.5,3.5 10,5.5 V11 M10,6 V4 Q11.5,2 13,4 V11 M13,6 Q15.5,4 15.5,6.5 V12 M15.5,9 Q18,7.5 18,10 V15 Q18,19.5 14.5,21 H9.5 Q7,19.5 5.5,16 L3.5,12.5 Q3,10.5 5,10.5 L7.5,13", 1.5); break;
            case "import-assets":
                Folder(); Line("M12,10.5 V17 M9,14 L12,17 L15,14", 1.5); break;
            case "inverse-kinematics":
                Circle(5.5, 5.5, 2.5); Circle(17.5, 10.5, 2.5); Circle(8, 19, 2.5); Line("M8,6.5 L15,9.5 M15.5,12.5 L10,17"); break;
            case "language":
                Circle(12, 12, 9); Line("M3,12 H21 M12,3 C6.5,8 6.5,16 12,21 C17.5,16 17.5,8 12,3 M4.7,7 H19.3 M4.7,17 H19.3", 1.35); break;
            case "model-parts":
                Line("M3,6 L8,3 L13,6 L8,9 Z M3,6 V12 L8,15 L13,12 V6 M8,9 V15 M11,15 L16,12 L21,15 L16,18 Z M11,15 V19 L16,22 L21,19 V15 M16,18 V22", 1.45); break;
            case "move-up": Line("M12,20 V4 M6,10 L12,4 L18,10", 1.9); break;
            case "move-down": Line("M12,4 V20 M6,14 L12,20 L18,14", 1.9); break;
            case "play": Solid("M7,4.5 Q7,3.5 8,4 L20,11 Q21.5,12 20,13 L8,20 Q7,20.5 7,19.5 Z"); break;
            case "pause": Solid("M6,4 H9 Q10,4 10,5 V19 Q10,20 9,20 H6 Q5,20 5,19 V5 Q5,4 6,4 Z M15,4 H18 Q19,4 19,5 V19 Q19,20 18,20 H15 Q14,20 14,19 V5 Q14,4 15,4 Z"); break;
            case "next-frame": Solid("M4.5,5 L15.5,12 L4.5,19 Z M18,5 H20 V19 H18 Z"); break;
            case "previous-frame": Solid("M19.5,5 L8.5,12 L19.5,19 Z M4,5 H6 V19 H4 Z"); break;
            case "output-naming":
                Page(); Line("M8,13 H16 M12,13 V18 M10,18 H14", 1.55); break;
            case "output-settings":
                Line("M6,3.5 V7 M6,12 V20.5 M12,3.5 V13 M12,18 V20.5 M18,3.5 V5 M18,10 V20.5 M3.5,7 H8.5 V12 H3.5 Z M9.5,13 H14.5 V18 H9.5 Z M15.5,5 H20.5 V10 H15.5 Z", 1.55); break;
            case "project-workspace":
                Folder(); Line("M7,12 H17 V17 H7 Z M10,12 V17", 1.4); break;
            case "restore-layout":
                Line("M4,9 A8.5,8.5 0 1,1 5,17 M4,4 V9 H9 M12,7 V12 L16,14"); break;
            case "save":
                Line("M4,3.5 H17 L20.5,7 V20.5 H3.5 V4 Q3.5,3.5 4,3.5 Z M7,3.5 V9 H16 V3.5 M7,20.5 V14 H17 V20.5 M13,5.5 V7", 1.55); break;
            case "save-as":
                Line("M10,20.5 H3.5 V3.5 H17 L20.5,7 V10 M7,3.5 V9 H16 V3.5 M13,5.5 V7 M12,18 L18.5,11.5 L21,14 L14.5,20.5 L11,21.5 Z M17,13 L19.5,15.5", 1.55); break;
            case "timeline-playback":
                Line("M4,4 H20 V20 H4 Z M4,8 H20 M8,4 V6 M12,4 V6 M16,4 V6 M7,11 V17", 1.5); Solid("M11,11 L17,14.5 L11,18 Z"); break;
            case "weapon-follow":
                Circle(5.5, 5.5, 2.5); Line("M9,5.5 H13 Q17,5.5 17,9 Q17,12.5 13,12.5 H10 Q6,12.5 6,16 Q6,19.5 10,19.5 H20 M16.5,16 L20,19.5 L16.5,23", 1.5); break;
            case "weapon-processing-mode":
                Line("M8.5,3 H15.5 V8 H8.5 Z M12,8 V12 M6,16 V12 H18 V16 M3,16 H9 V21 H3 Z M15,16 H21 V21 H15 Z", 1.55); break;
            case "zoom-in":
            case "zoom-out":
                Circle(10, 10, 6.5); Line("M15,15 L21,21 M7,10 H13"); if (glyph == "zoom-in") Line("M10,7 V13"); break;
            default: throw new ArgumentOutOfRangeException(nameof(glyph), glyph, "Classic Apple icon has not been drawn.");
        }
        return canvas;
    }
}
