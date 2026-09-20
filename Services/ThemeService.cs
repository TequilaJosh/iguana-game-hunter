using System;
using System.Windows;
using System.Windows.Media;
using GameTracker.Models;

namespace GameTracker.Services
{
    /// <summary>
    /// Applies the user's colour theme at runtime by overriding the app-level brush
    /// resources (ThemeAccent / ThemeAccentDeep / ThemeAccent2) and rebuilding the
    /// scale-pattern background (ScaleBrush). The XAML references these via DynamicResource,
    /// so a swap re-colours every open window instantly.
    /// </summary>
    public static class ThemeService
    {
        public static ThemeSettings Current { get; private set; } = new();

        /// <summary>Load the saved theme and apply it. Call once at startup.</summary>
        public static void Initialize() => Apply(SettingsService.LoadTheme(), save: false);

        public static void Apply(ThemeSettings t, bool save = true)
        {
            Current = t ?? new ThemeSettings();
            var res = Application.Current?.Resources;
            if (res == null) return;

            res["ThemeAccent"] = Solid(Current.Accent, "#7cc44a");
            res["ThemeAccentDeep"] = Solid(Current.AccentDeep, "#4a7c3a");
            res["ThemeAccent2"] = Solid(Current.Accent2, "#d4a437");

            // Text family — lets the Light theme use dark text on a light background.
            res["ThemeText"] = Solid(Current.Text, "#e8e0c4");
            res["ThemeTextDim"] = Solid(Current.TextDim, "#a8c488");
            res["ThemeTextFaint"] = Solid(Current.TextFaint, "#7a9070");

            // Surface family: lift the background base toward the tile colour (same hue),
            // so panels/borders match whatever background the theme uses.
            var baseC = ParseColor(Current.BgBase, "#0a1410");
            var tileC = ParseColor(Current.BgTile, "#1c2a1e");
            res["ThemeBg"] = Frozen(baseC);
            res["ThemeSurface"] = Frozen(Extrapolate(baseC, tileC, 0.6));
            res["ThemeSurface2"] = Frozen(Extrapolate(baseC, tileC, 1.6));
            res["ThemeBorder"] = Frozen(Extrapolate(baseC, tileC, 2.6));

            // Accent tints over the background: column headers, badges, chips and the
            // wheel slices use these, so they follow the theme instead of staying the
            // Reptile greens/ambers they were originally drawn in.
            var accentC  = ParseColor(Current.Accent, "#7cc44a");
            var accent2C = ParseColor(Current.Accent2, "#d4a437");
            res["ThemeAccentSoft"]   = Frozen(Extrapolate(baseC, accentC, 0.22));
            res["ThemeAccentMid"]    = Frozen(Extrapolate(baseC, accentC, 0.38));
            res["ThemeAccentEdge"]   = Frozen(Extrapolate(baseC, accentC, 0.52));
            res["ThemeAccentBright"] = Frozen(Extrapolate(accentC, Colors.White, 0.35));
            res["ThemeAccent2Soft"]  = Frozen(Extrapolate(baseC, accent2C, 0.22));
            res["ThemeAccent2Mid"]   = Frozen(Extrapolate(baseC, accent2C, 0.38));
            res["ThemeAccent2Edge"]  = Frozen(Extrapolate(baseC, accent2C, 0.52));

            res["ScaleBrush"] = BuildScaleBrush(Current.BgBase, Current.BgTile);

            // Recolour the OBS overlay to match (and let it load per-theme artwork).
            try { OverlayServer.SetTheme(Current); } catch { /* overlay optional */ }

            if (save) SettingsService.SaveTheme(Current);
        }

        /// <summary>Map an app text hex to the live theme brush so code-built UI follows the
        /// theme (and stays readable under the Light theme). Non-text hexes pass through as
        /// their literal color. Views' Brush()/Brush2()/TryBrush() helpers delegate here.</summary>
        public static SolidColorBrush MapBrush(string hex)
        {
            var key = (hex ?? "").ToLowerInvariant() switch
            {
                "#e8e0c4" => "ThemeText",
                "#a8c488" or "#c4d4a8" => "ThemeTextDim",
                "#7a9070" or "#506050" => "ThemeTextFaint",
                // Reptile accent/surface constants used by code-built UI — same mapping
                // the XAML sweep applied, so views that build brushes in code follow too.
                "#7cc44a" => "ThemeAccent",
                "#4a7c3a" => "ThemeAccentDeep",
                "#d4a437" => "ThemeAccent2",
                "#0a1410" => "ThemeBg",
                "#142016" => "ThemeSurface",
                "#1c2a1e" or "#243a26" => "ThemeSurface2",
                "#2e4a30" => "ThemeBorder",
                "#1e3a1e" or "#0f1810" => "ThemeAccentSoft",
                "#3a5a2a" => "ThemeAccentMid",
                "#a8e060" => "ThemeAccentBright",
                "#3a2a10" or "#1a1408" => "ThemeAccent2Soft",
                "#5a3a1a" => "ThemeAccent2Edge",
                _ => null,
            };
            if (key != null && Application.Current?.Resources[key] is SolidColorBrush tb) return tb;
            try { return new SolidColorBrush(ParseColor(hex, "#e8e0c4")); }
            catch { return new SolidColorBrush(Colors.Gray); }
        }

        private static SolidColorBrush Solid(string hex, string fallback)
        {
            var b = new SolidColorBrush(ParseColor(hex, fallback));
            b.Freeze();
            return b;
        }

        private static SolidColorBrush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        // Move from a toward b by factor f (f>1 extrapolates past b, staying on the hue line).
        private static Color Extrapolate(Color a, Color b, double f)
        {
            byte Ch(byte x, byte y) => (byte)Math.Clamp(x + (y - x) * f, 0, 255);
            return Color.FromRgb(Ch(a.R, b.R), Ch(a.G, b.G), Ch(a.B, b.B));
        }

        private static Color ParseColor(string hex, string fallback)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return (Color)ColorConverter.ConvertFromString(fallback); }
        }

        // Recreates the App.xaml snake-scale DrawingBrush with themed colours.
        private static DrawingBrush BuildScaleBrush(string baseHex, string tileHex)
        {
            var baseColor = ParseColor(baseHex, "#0a1410");
            var tileColor = ParseColor(tileHex, "#1c2a1e");

            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(
                new SolidColorBrush(baseColor), null,
                new RectangleGeometry(new Rect(0, 0, 28, 28))));

            var pen = new Pen(new SolidColorBrush(tileColor), 1);
            var pg = new PathGeometry();
            pg.Figures.Add(Arc(new Point(0, 8), new Point(14, 8)));
            pg.Figures.Add(Arc(new Point(14, 8), new Point(28, 8)));
            pg.Figures.Add(Arc(new Point(-7, 22), new Point(7, 22)));
            pg.Figures.Add(Arc(new Point(7, 22), new Point(21, 22)));
            pg.Figures.Add(Arc(new Point(21, 22), new Point(35, 22)));
            group.Children.Add(new GeometryDrawing(null, pen, pg));

            var db = new DrawingBrush(group)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 28, 28),
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.None,
            };
            db.Freeze();
            return db;
        }

        private static PathFigure Arc(Point start, Point end)
        {
            var f = new PathFigure { StartPoint = start };
            f.Segments.Add(new ArcSegment(end, new Size(7, 7), 0, false, SweepDirection.Clockwise, true));
            return f;
        }
    }
}
