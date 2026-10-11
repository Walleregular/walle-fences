using System.Windows.Media.Imaging;

namespace WalleFences.App;

internal static class Brand
{
    private static readonly DrawingImage Mark = BuildMark();
    internal static readonly BitmapImage WindowIcon = new(new Uri("pack://application:,,,/WalleFences;component/Assets/brand-icon.png"));
    internal static Image Logo(double size = 48) => new() { Source = Mark, Width = size, Height = size, Stretch = Stretch.Uniform };
    private static DrawingImage BuildMark()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 96, 96))));
        foreach (var (path, color) in new[] {
            ("M28,80 L40,63 L40,44 L28,55 Z M58,80 L70,63 L70,44 L58,55 Z", "#48AD82"),
            ("M8,18 Q8,10 14,13 L28,22 Q32,24 32,30 L32,84 L8,66 Z", "#A0E9C0"),
            ("M38,18 Q38,10 44,13 L58,22 Q62,24 62,30 L62,84 L38,66 Z", "#A0E9C0"),
            ("M68,18 Q68,10 74,13 L88,22 Q92,24 92,30 L92,66 L68,84 Z", "#A0E9C0") })
            group.Children.Add(new GeometryDrawing(Ui.Brush(color), null, Geometry.Parse(path)));
        group.Freeze(); var result = new DrawingImage(group); result.Freeze(); return result;
    }
    // The transparent 24-DIP canvas keeps every glyph optically centred at 16, 18 or 20 DIP.
    // Drawings retain the caller's brush; freezing them would break live theme changes.
    internal static Image Icon(string id, Brush? brush = null, double size = 18)
    {
        brush ??= Ui.Ink;
        id = id.ToLowerInvariant();
        var paths = id switch
        {
            "groups" => new[] { "M4,3 H9 Q10,3 10,4 V9 Q10,10 9,10 H4 Q3,10 3,9 V4 Q3,3 4,3 Z M15,3 H20 Q21,3 21,4 V9 Q21,10 20,10 H15 Q14,10 14,9 V4 Q14,3 15,3 Z M4,14 H9 Q10,14 10,15 V20 Q10,21 9,21 H4 Q3,21 3,20 V15 Q3,14 4,14 Z M15,14 H20 Q21,14 21,15 V20 Q21,21 20,21 H15 Q14,21 14,20 V15 Q14,14 15,14 Z" },
            "appearance" => new[] { "M12,3 C6.5,3 3,7 3,12 C3,17 7,21 12,21 H13 C15,21 16,19 15,17 C14,15 15,14 17,14 H18 C20,14 21,12 21,10 C21,6 17,3 12,3 Z" },
            "eyedropper" => new[] { "M14,4 L20,10 M16,6 L18,4 Q19.5,2.5 21,4 Q22.5,5.5 21,7 L19,9 M15,7 L4,18 V21 H7 L18,10 M7,15 L10,18" },
            "rules" => new[] { "M3,7 H21 M17,3 L21,7 L17,11 M21,17 H3 M7,13 L3,17 L7,21" },
            "set" or "sets" or "pages" => new[] { "M8,3 H19 Q21,3 21,5 V16 Q21,18 19,18 H8 Q6,18 6,16 V5 Q6,3 8,3 Z M3,7 V19 Q3,21 5,21 H17" },
            "tabs" => new[] { "M3,9 V6 Q3,4 5,4 H10 L13,9 H19 Q21,9 21,11 V18 Q21,20 19,20 H5 Q3,20 3,18 V9 Z M13,4 H17 L21,8" },
            "layers" => new[] { "M3,7 L12,3 L21,7 L12,11 L3,7 Z M3,12 L12,16 L21,12 M3,17 L12,21 L21,17" },
            "sort" => new[] { "M8,4 V20 M4,16 L8,20 L12,16 M14,5 H21 M14,10 H19 M14,15 H17" },
            "resize" => new[] { "M7,20 L20,7 M13,20 L20,13 M19,20 L20,19" },
            "snapshots" => new[] { "M5,7 H8 L10,4 H14 L16,7 H19 Q21,7 21,9 V18 Q21,20 19,20 H5 Q3,20 3,18 V9 Q3,7 5,7 Z M12,9.5 A3.5,3.5 0 1 0 12,16.5 A3.5,3.5 0 1 0 12,9.5 Z" },
            "settings" => new[] { "M3,6 H7 M11,6 H21 M3,12 H13 M17,12 H21 M3,18 H7 M11,18 H21 M9,4 A2,2 0 1 0 9,8 A2,2 0 1 0 9,4 Z M15,10 A2,2 0 1 0 15,14 A2,2 0 1 0 15,10 Z M9,16 A2,2 0 1 0 9,20 A2,2 0 1 0 9,16 Z" },
            "sidebar" or "sidebars" => new[] { "M5,3 H19 Q21,3 21,5 V19 Q21,21 19,21 H5 Q3,21 3,19 V5 Q3,3 5,3 Z M9,3 V21 M13,8 H17 M13,12 H17 M13,16 H16" },
            "sidebar-collapse" => new[] { "M5,3 H19 Q21,3 21,5 V19 Q21,21 19,21 H5 Q3,21 3,19 V5 Q3,3 5,3 Z M9,3 V21 M16,8 L12,12 L16,16" },
            "sidebar-expand" => new[] { "M5,3 H19 Q21,3 21,5 V19 Q21,21 19,21 H5 Q3,21 3,19 V5 Q3,3 5,3 Z M9,3 V21 M13,8 L17,12 L13,16" },
            "widgets" => new[] { "M4,3 H10 Q11,3 11,4 V10 Q11,11 10,11 H4 Q3,11 3,10 V4 Q3,3 4,3 Z M16,3 H20 Q21,3 21,4 V8 Q21,9 20,9 H16 Q15,9 15,8 V4 Q15,3 16,3 Z M4,15 H8 Q9,15 9,16 V20 Q9,21 8,21 H4 Q3,21 3,20 V16 Q3,15 4,15 Z M14,13 H20 Q21,13 21,14 V20 Q21,21 20,21 H14 Q13,21 13,20 V14 Q13,13 14,13 Z" },
            "shortcuts" or "keyboard" => new[] { "M5,5 H19 Q21,5 21,7 V17 Q21,19 19,19 H5 Q3,19 3,17 V7 Q3,5 5,5 Z M7,9 H8 M11.5,9 H12.5 M16,9 H17 M7,12 H8 M11.5,12 H12.5 M16,12 H17 M8,15 H16" },
            "peek" => new[] { "M3,8 V3 H8 M16,3 H21 V8 M21,16 V21 H16 M8,21 H3 V16 M7,12 C10,7 14,7 17,12 C14,17 10,17 7,12 Z" },
            "more" or "grip" => Array.Empty<string>(),
            "collapse" => new[] { "M5,4 H19 M6,15 L12,9 L18,15" },
            "expand" => new[] { "M5,4 H19 M6,10 L12,16 L18,10" },
            "add" or "plus" => new[] { "M5,12 H19 M12,5 V19" },
            "file" => new[] { "M6,3 H14 L19,8 V19 Q19,21 17,21 H6 Q4,21 4,19 V5 Q4,3 6,3 Z M14,3 V8 H19" },
            "folder" => new[] { "M3,7 V6 Q3,4 5,4 H9 L12,7 H19 Q21,7 21,9 V18 Q21,20 19,20 H5 Q3,20 3,18 V7 Z M3,10 H21" },
            "folder-add" => new[] { "M3,7 V6 Q3,4 5,4 H9 L12,7 H19 Q21,7 21,9 V18 Q21,20 19,20 H5 Q3,20 3,18 V7 Z M9,14 H15 M12,11 V17" },
            "briefcase" => new[] { "M8,7 V4 Q8,3 9,3 H15 Q16,3 16,4 V7 M4,7 H20 Q22,7 22,9 V18 Q22,20 20,20 H4 Q2,20 2,18 V9 Q2,7 4,7 Z M2,11 Q12,17 22,11 M10,13 H14 V16 H10 Z" },
            "code" => new[] { "M7,6 L2,12 L7,18 M17,6 L22,12 L17,18 M14,3 L10,21" },
            "terminal" => new[] { "M4,4 H20 Q22,4 22,6 V18 Q22,20 20,20 H4 Q2,20 2,18 V6 Q2,4 4,4 Z M6,9 L10,12 L6,15 M13,15 H18" },
            "gamepad" => new[] { "M8,7 H16 Q19,7 20,11 L22,17 Q23,21 19,20 L15,17 H9 L5,20 Q1,21 2,17 L4,11 Q5,7 8,7 Z M6,12 H10 M8,10 V14 M15,11 H15.2 M18,13 H18.2" },
            "star" or "star-filled" => new[] { "M12,2 L15,8.5 L22,9.5 L17,14.5 L18.2,21.5 L12,18 L5.8,21.5 L7,14.5 L2,9.5 L9,8.5 Z" },
            "heart" => new[] { "M12,21 L3.5,12.5 C-1,8 3,1 8,4 L12,7 L16,4 C21,1 25,8 20.5,12.5 Z" },
            "music" => new[] { "M9,17 V5 L20,3 V15 M9,9 L20,7 M6,17 A3,2.5 0 1 0 6,22 A3,2.5 0 1 0 6,17 Z M17,15 A3,2.5 0 1 0 17,20 A3,2.5 0 1 0 17,15 Z" },
            "image" => new[] { "M4,3 H20 Q21,3 21,4 V20 Q21,21 20,21 H4 Q3,21 3,20 V4 Q3,3 4,3 Z M3,17 L8,12 L12,16 L16,11 L21,16 M8,6 A2,2 0 1 0 8,10 A2,2 0 1 0 8,6 Z" },
            "video" => new[] { "M4,5 H14 Q16,5 16,7 V17 Q16,19 14,19 H4 Q2,19 2,17 V7 Q2,5 4,5 Z M16,9 L22,6 V18 L16,15" },
            "globe" => new[] { "M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 Z M3,12 H21 M12,3 C6,8 6,16 12,21 C18,16 18,8 12,3 Z" },
            "book" => new[] { "M12,5 Q6,2 2,4 V19 Q6,17 12,20 Q18,17 22,19 V4 Q18,2 12,5 Z M12,5 V20 M5,8 H8 M5,12 H8 M16,8 H19 M16,12 H19" },
            "mail" => new[] { "M4,5 H20 Q22,5 22,7 V17 Q22,19 20,19 H4 Q2,19 2,17 V7 Q2,5 4,5 Z M2,7 L12,14 L22,7" },
            "calendar" => new[] { "M4,5 H20 Q21,5 21,6 V20 Q21,21 20,21 H4 Q3,21 3,20 V6 Q3,5 4,5 Z M7,2 V7 M17,2 V7 M3,10 H21 M7,14 H10 M14,14 H17 M7,18 H10" },
            "cloud" => new[] { "M7,19 H18 C24,19 25,10 19,9 C17,1 6,2 6,10 C-1,10 -1,19 7,19 Z" },
            "history" => new[] { "M3,9 A9,9 0 1 1 3,15 M2,3 V9 H8 M12,7 V12 L16,14" },
            "back" => new[] { "M10,5 L3,12 L10,19 M3,12 H21" },
            "next" => new[] { "M4,12 H18 M12,6 L18,12 L12,18 M21,5 V19" },
            "up" or "move-up" => new[] { "M5,10 L12,3 L19,10 M12,3 V21" },
            "down" or "move-down" => new[] { "M5,14 L12,21 L19,14 M12,3 V21" },
            "chevron-up" => new[] { "M5,15 L12,8 L19,15" },
            "chevron-down" => new[] { "M5,9 L12,16 L19,9" },
            "chevron-left" => new[] { "M15,5 L8,12 L15,19" },
            "chevron-right" => new[] { "M9,5 L16,12 L9,19" },
            "home" => new[] { "M3,11 L12,3 L21,11 M5,9 V19 Q5,21 7,21 H17 Q19,21 19,19 V9 M9,21 V14 H15 V21" },
            "help" => new[] { "M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 Z M9,8 C9,5 15,5 15,9 C15,12 12,11 12,14" },
            "draw" => new[] { "M3,8 V3 H8 M16,3 H21 V8 M21,16 V21 H16 M8,21 H3 V16 M7,12 H17 M12,7 V17" },
            "undo" => new[] { "M8,4 L3,9 L8,14 M3,9 H14 C23,9 23,20 14,20 H12" },
            "edit" => new[] { "M4,20 L5,15 L16,4 Q17,3 18,4 L20,6 Q21,7 20,8 L9,19 L4,20 Z M14,6 L18,10" },
            "trash" or "delete" => new[] { "M4,6 H20 M9,6 V3 H15 V6 M6,6 L7,20 Q7,21 9,21 H15 Q17,21 17,20 L18,6 M10,10 V17 M14,10 V17" },
            "check" => new[] { "M4,12 L9,17 L20,6" },
            "close" => new[] { "M6,6 L18,18 M18,6 L6,18" },
            "minimize" => new[] { "M5,12 H19" },
            "maximize" => new[] { "M5,5 H19 V19 H5 Z" },
            "restore" => new[] { "M8,3 H21 V16 M3,8 H16 V21 H3 Z" },
            "search" => new[] { "M10,3 A7,7 0 1 0 10,17 A7,7 0 1 0 10,3 Z M15,15 L21,21" },
            "pin" => new[] { "M8,3 H16 L15,9 L18,12 V14 H6 V12 L9,9 L8,3 Z M12,14 V21" },
            "pin-off" => new[] { "M10,3 H16 L15,9 L18,12 V14 M7,10 L6,12 V14 H12 M12,14 V21 M3,3 L21,21" },
            "play" => new[] { "M7,4 L20,12 L7,20 Z" },
            "pause" => new[] { "M6,4 H9 V20 H6 Z M15,4 H18 V20 H15 Z" },
            "reset" => new[] { "M4,8 A9,9 0 1 1 3,14 M3,3 V8 H8" },
            "refresh" => new[] { "M4,9 A8,8 0 0 1 19,6 L21,9 M21,4 V9 H16 M20,15 A8,8 0 0 1 5,18 L3,15 M3,20 V15 H8" },
            "desktop" or "monitor" or "monitors" => new[] { "M5,4 H19 Q21,4 21,6 V15 Q21,17 19,17 H5 Q3,17 3,15 V6 Q3,4 5,4 Z M8,21 H16 M12,17 V21" },
            "memory" => new[] { "M4,6 H20 Q21,6 21,7 V16 H3 V7 Q3,6 4,6 Z M6,16 V20 M10,16 V20 M14,16 V20 M18,16 V20 M6,9 H9 V13 H6 Z M15,9 H18 V13 H15 Z" },
            "drive" or "drives" => new[] { "M5,4 H19 L21,14 V18 Q21,20 19,20 H5 Q3,20 3,18 V14 L5,4 Z M3,14 H21" },
            "processor" or "cpu" => new[] { "M6,6 H18 V18 H6 Z M9,9 H15 V15 H9 Z M9,3 V6 M15,3 V6 M9,18 V21 M15,18 V21 M3,9 H6 M3,15 H6 M18,9 H21 M18,15 H21" },
            "processes" => new[] { "M4,4 H20 Q21,4 21,5 V19 Q21,20 20,20 H4 Q3,20 3,19 V5 Q3,4 4,4 Z M3,8 H21 M7,12 H10 M7,16 H14 M17,11 V17" },
            "gpu" => new[] { "M3,6 H19 Q21,6 21,8 V16 H3 Z M3,4 V20 M7,16 V19 H15 V16 M17,10 H19 M11,8 A3,3 0 1 0 11,14 A3,3 0 1 0 11,8 Z M11,10 V12" },
            "network" => new[] { "M3,8 C8,2 16,2 21,8 M6,11 C10,7 14,7 18,11 M9,14 C11,12 13,12 15,14 M12,17 V20" },
            "battery" => new[] { "M4,6 H18 Q20,6 20,8 V16 Q20,18 18,18 H4 Q2,18 2,16 V8 Q2,6 4,6 Z M20,10 H22 V14 H20 M6,10 V14 M10,10 V14 M14,10 V14" },
            "uptime" => new[] { "M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 Z M12,7 V12 L16,14 M3,4 V9 H8" },
            "download" => new[] { "M12,3 V16 M7,11 L12,16 L17,11 M4,18 V21 H20 V18" },
            "upload" => new[] { "M12,16 V3 M7,8 L12,3 L17,8 M4,18 V21 H20 V18" },
            "temp" or "temperature" => new[] { "M8,13 V5 C8,1 14,1 14,5 V13 A5,5 0 1 1 8,13 Z M11,7 V17 M17,6 H21 M17,10 H20" },
            "eye" or "visibility" => new[] { "M2,12 C7,3 17,3 22,12 C17,21 7,21 2,12 Z M12,8 A4,4 0 1 0 12,16 A4,4 0 1 0 12,8 Z" },
            "eye-off" => new[] { "M9,6 C14,4 19,7 22,12 C21,14 19,16 17,17 M6,7 C4,8 3,10 2,12 C7,21 14,20 17,17 M10,10 A3,3 0 0 0 14,14 M3,3 L21,21" },
            "clock" => new[] { "M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 Z M12,7 V12 L16,14" },
            "note" => new[] { "M5,3 H19 Q21,3 21,5 V15 L15,21 H5 Q3,21 3,19 V5 Q3,3 5,3 Z M15,21 V15 H21 M7,8 H17 M7,12 H13" },
            "copy" => new[] { "M9,3 H19 Q21,3 21,5 V15 M5,7 H15 Q17,7 17,9 V19 Q17,21 15,21 H5 Q3,21 3,19 V9 Q3,7 5,7 Z" },
            "external-link" => new[] { "M14,3 H21 V10 M21,3 L10,14 M10,5 H5 Q3,5 3,7 V19 Q3,21 5,21 H17 Q19,21 19,19 V14" },
            "startup" => new[] { "M7,4 A9,9 0 1 0 17,4 M12,2 V11" },
            "exit" => new[] { "M10,3 H5 Q3,3 3,5 V19 Q3,21 5,21 H10 M9,12 H22 M17,7 L22,12 L17,17" },
            "import" => new[] { "M12,3 V15 M7,10 L12,15 L17,10 M4,15 V19 Q4,21 6,21 H18 Q20,21 20,19 V15" },
            "export" => new[] { "M12,15 V3 M7,8 L12,3 L17,8 M4,15 V19 Q4,21 6,21 H18 Q20,21 20,19 V15" },
            "language" => new[] { "M3,5 H15 M9,3 V5 M13,5 C12,12 9,15 4,17 M5,8 C7,12 10,15 14,17 M14,21 L18,11 L22,21 M16,17 H20" },
            "warning" => new[] { "M10,4 Q12,1 14,4 L22,18 Q23,21 20,21 H4 Q1,21 2,18 L10,4 Z M12,8 V13" },
            _ => new[] { "M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 Z M9,8 C9,5 15,5 15,9 C15,12 12,11 12,14" }
        };
        var group = new DrawingGroup(); group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
        // 1.5 on this 24-DIP grid becomes a one-pixel stroke at the common 16-DIP size.
        var pen = new Pen(brush, 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        foreach (var path in paths) group.Children.Add(new GeometryDrawing(id == "star-filled" ? brush : null, pen, Geometry.Parse(path)));
        void Dot(double x, double y, double radius = 1.25) => group.Children.Add(new GeometryDrawing(brush, null, new EllipseGeometry(new Point(x, y), radius, radius)));
        switch (id)
        {
            case "more": foreach (var x in new[] { 6.0, 12, 18 }) Dot(x, 12); break;
            case "grip": foreach (var x in new[] { 9.0, 15 }) foreach (var y in new[] { 5.0, 12, 19 }) Dot(x, y, 1.15); break;
            case "appearance": Dot(7, 10, .9); Dot(10, 6.5, .9); Dot(15, 6.5, .9); Dot(17.5, 10, .9); break;
            case "help": Dot(12, 17.5, .85); break;
            case "warning": Dot(12, 17, .9); break;
            case "drive": case "drives": Dot(17, 17, .9); break;
        }
        var image = new Image { Source = new DrawingImage(group), Width = size, Height = size, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, SnapsToDevicePixels = true, Focusable = false, IsHitTestVisible = false, Tag = id };
        return image;
    }
    internal static Image NavigationIcon(string id, Brush brush) => Icon(id == "groups" ? "desktop" : id, brush, 16);
}
