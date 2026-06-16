using System.Collections.Generic;
using System.Windows.Media;

namespace OrokinMonitor
{
    // A theme is just the set of brushes the UI binds to.
    public sealed class Theme
    {
        public string Key = "";
        public string Display = "";

        public Brush Window = Brushes.Black;     // outer background
        public Brush Panel = Brushes.Black;      // panel fill
        public Brush PanelEdge = Brushes.Gray;   // panel border
        public Brush Accent = Brushes.Goldenrod; // headers, bars, rune dots
        public Brush Text = Brushes.White;        // primary readouts
        public Brush TextDim = Brushes.Gray;     // labels / secondary
        public Brush BarBack = Brushes.Gray;     // bar track
        public Brush Hot = Brushes.OrangeRed;    // over-threshold temp

        public double RuneOpacity = 0.25;        // pastel wants ~0 (runes look noisy on light bg)

        private static Brush B(string hex)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            var br = new SolidColorBrush(c);
            br.Freeze();
            return br;
        }

        public static readonly Theme Orokin = new()
        {
            Key = "orokin", Display = "Orokin (gold)",
            Window = B("#FF0C0A08"),
            Panel = B("#FF15110C"),
            PanelEdge = B("#FFC9A763"),
            Accent = B("#FFC9A763"),
            Text = B("#FFEBE7DC"),
            TextDim = B("#99EBE7DC"),
            BarBack = B("#33C9A763"),
            Hot = B("#FFC45C3A"),
            RuneOpacity = 0.22,
        };

        // High contrast: deep navy bg, bright royal-blue accent, pure white text.
        public static readonly Theme Royal = new()
        {
            Key = "royal", Display = "Royal (high contrast)",
            Window = B("#FF05070F"),
            Panel = B("#FF0E1426"),
            PanelEdge = B("#FF3D6BFF"),
            Accent = B("#FF4F86FF"),
            Text = B("#FFFFFFFF"),
            TextDim = B("#B0CBD6FF"),
            BarBack = B("#333D6BFF"),
            Hot = B("#FFFF5C5C"),
            RuneOpacity = 0.30,
        };

        // Pastel: light warm bg, soft ink text, muted lavender/teal accent. Bright, low strain.
        public static readonly Theme Pastel = new()
        {
            Key = "pastel", Display = "Pastel (light)",
            Window = B("#FFF3F0EA"),
            Panel = B("#FFFBFAF6"),
            PanelEdge = B("#FFC9B8E8"),
            Accent = B("#FF8B7BD8"),   // soft lavender
            Text = B("#FF3A3740"),     // soft ink, not pure black
            TextDim = B("#993A3740"),
            BarBack = B("#33A99BE0"),
            Hot = B("#FFD87093"),      // muted rose instead of harsh red
            RuneOpacity = 0.0,
        };

        public static readonly List<Theme> All = new() { Orokin, Royal, Pastel };

        public static Theme ByKey(string? key)
        {
            foreach (var t in All) if (t.Key == key) return t;
            return Orokin;
        }
    }
}
