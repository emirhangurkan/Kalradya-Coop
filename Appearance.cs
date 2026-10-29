using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Pen = System.Windows.Media.Pen;

namespace KalradyaCoop;

public static class Appearance
{
    public sealed record Choice(string Id, string Name, string Value);
    public static readonly Choice[] Colors =
    [
        new("gold", "Altın", "#D5B478"), new("red", "Bordo", "#DEA0A7"),
        new("blue", "Mavi", "#9BBFE3"), new("green", "Yeşil", "#B2C98F")
    ];
    public static readonly Choice[] Symbols =
    [
        new("swords", "Kılıçlar", "M16,17 L21,19 53,55 49,59 18,23 Z M56,17 L51,19 19,55 23,59 54,23 Z M41,53 L46,48 58,61 53,66 Z M14,61 L26,48 31,53 19,66 Z M50,66 L56,61 62,68 56,73 Z M10,68 L16,61 22,66 16,73 Z"),
        new("crown", "Taç", "M16,32 L28,42 36,22 44,42 56,32 51,58 21,58 Z M22,62 L50,62 50,67 22,67 Z"),
        new("sun", "Güneş", "M36,18 L40,29 50,23 47,34 59,36 49,43 56,53 44,52 43,64 36,55 29,64 28,52 16,53 23,43 13,36 25,34 22,23 32,29 Z"),
        new("mountain", "Dağ", "M12,60 L29,29 38,43 45,33 61,60 Z M25,44 L29,36 34,44 30,42 Z")
    ];

    public static Choice Palette(string? id) => Colors.FirstOrDefault(x => x.Id == id) ?? Colors[0];
    public static Choice Symbol(string? id) => Symbols.FirstOrDefault(x => x.Id == id) ?? Symbols[0];
    public static SolidColorBrush Accent(string? id) => new((Color)ColorConverter.ConvertFromString(Palette(id).Value));
    private static Color Scale(Color c, double factor) => Color.FromRgb((byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));
    public static LinearGradientBrush ButtonBrush(string? id)
    {
        var c = Accent(id).Color;
        return new(c, Scale(c, .8), new System.Windows.Point(0, 0), new System.Windows.Point(0, 1));
    }
    public static void Apply(string? color)
    {
        var resources = System.Windows.Application.Current.Resources;
        resources["Gold"] = Accent(color);
        resources["Bronze"] = ButtonBrush(color);
    }
    public static DrawingImage Emblem(string? symbol, string? color)
    {
        var accent = Accent(color);
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Scale(accent.Color, .25)), new Pen(accent, 2), Geometry.Parse("M4,5 L36,0 68,5 66,48 Q61,69 36,83 Q11,69 6,48 Z")));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(245, 233, 210)), null, Geometry.Parse(Symbol(symbol).Value)));
        var drawing = new DrawingImage(group);
        drawing.Freeze();
        return drawing;
    }
}
