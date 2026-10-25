// Shared look & feel: true-black, iOS/macOS-style palette and small control helpers.
using System;
using System.Collections.Generic;
using System.Linq;
using IO = System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace WaktuShalat
{
    static class Ui
    {
        public static Brush B(string hex)
        {
            var b = (Brush)new BrushConverter().ConvertFromString(hex);
            b.Freeze();
            return b;
        }

        public static readonly Brush Label = B("#FFFFFF");
        public static readonly Brush LabelSoft = B("#EBEBF5");
        public static readonly Brush Secondary = B("#8E8E93");
        public static readonly Brush Tertiary = B("#48484A");
        public static readonly Brush Dim = B("#5A5A5F");
        public static readonly Brush CardBg = B("#1C1C1E");
        public static readonly Brush CardHover = B("#2C2C2E");
        public static readonly Brush Separator = B("#2C2C2E");
        public static readonly Brush Blue = B("#0A84FF");
        public static readonly Brush Orange = B("#FF9F0A");
        // Emerald accent: the one colour in the black theme.
        public static readonly Brush Accent = B("#34D399");
        public static readonly Brush AccentDeep = B("#0F3A2B");
        public static readonly Brush Sky = B("#64D2FF");
        public static readonly Brush GreenBrush = Accent;
        public static readonly Brush Raised = B("#3A3A3C");
        public static readonly Color Green = Color.FromRgb(0x34, 0xD3, 0x99);
        public static readonly Color SwitchOff = Color.FromRgb(0x39, 0x39, 0x3D);

        static readonly string FontsDir = ExtractFonts();
        public static readonly FontFamily Display = LoadFont("Inter Display", "Segoe UI Variable Display, Segoe UI");
        public static readonly FontFamily Text = LoadFont("Inter", "Segoe UI Variable Text, Segoe UI");

        // Inter ships inside the exe ("font.*.ttf" resources); WPF reads it from a private folder.
        static string ExtractFonts()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var names = asm.GetManifestResourceNames().Where(n => n.StartsWith("font.")).ToArray();
                if (names.Length == 0) return null;
                var dir = IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WaktuShalat", "fonts");
                IO.Directory.CreateDirectory(dir);
                foreach (var n in names)
                {
                    var path = IO.Path.Combine(dir, n.Substring(5));
                    using (var rs = asm.GetManifestResourceStream(n))
                    {
                        if (IO.File.Exists(path) && new IO.FileInfo(path).Length == rs.Length) continue;
                        using (var fs = IO.File.Create(path)) rs.CopyTo(fs);
                    }
                }
                return dir;
            }
            catch { return null; }
        }

        static FontFamily LoadFont(string family, string fallback)
        {
            if (FontsDir == null) return new FontFamily(fallback);
            return new FontFamily(new Uri(FontsDir + IO.Path.DirectorySeparatorChar), "./#" + family + ", " + fallback);
        }
        public static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        public static readonly IEasingFunction Ease = new QuinticEase { EasingMode = EasingMode.EaseOut };

        public static Duration Ms(int ms) { return new Duration(TimeSpan.FromMilliseconds(ms)); }

        public static TextBlock T(string text, double size, Brush fg, FontWeight weight)
        {
            var t = new TextBlock { Text = text, FontSize = size, Foreground = fg, FontWeight = weight, FontFamily = size >= 20 ? Display : Text };
            Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
            return t;
        }

        public static TextBlock Icon(string glyph, double size, Brush fg)
        {
            return new TextBlock { Text = glyph, FontFamily = Icons, FontSize = size, Foreground = fg, VerticalAlignment = VerticalAlignment.Center };
        }

        // Makes any element behave like a button. hoverBg == null -> opacity feedback instead of a highlight.
        public static void Clickable(FrameworkElement fe, Action onClick, Brush hoverBg)
        {
            fe.Cursor = Cursors.Hand;
            var panel = fe as Panel;
            var border = fe as Border;
            Brush normal = panel != null ? panel.Background : border != null ? border.Background : null;
            if (normal == null) { normal = Brushes.Transparent; SetBg(fe, normal); }
            bool pressed = false;
            fe.MouseEnter += (s, e) => { if (hoverBg != null) SetBg(fe, hoverBg); else fe.Opacity = 0.8; };
            fe.MouseLeave += (s, e) => { pressed = false; if (hoverBg != null) SetBg(fe, normal); fe.Opacity = 1; };
            fe.MouseLeftButtonDown += (s, e) => { pressed = true; e.Handled = true; fe.Opacity = hoverBg != null ? 0.85 : 0.45; };
            fe.MouseLeftButtonUp += (s, e) =>
            {
                if (!pressed) return;
                pressed = false; e.Handled = true;
                fe.Opacity = hoverBg != null ? 1 : 0.8;
                onClick();
            };
        }

        static void SetBg(FrameworkElement fe, Brush b)
        {
            var p = fe as Panel; if (p != null) { p.Background = b; return; }
            var bd = fe as Border; if (bd != null) bd.Background = b;
        }

        public static void ClipRounded(FrameworkElement fe, double radius)
        {
            fe.SizeChanged += (s, e) => fe.Clip = new RectangleGeometry(new Rect(fe.RenderSize), radius, radius);
        }

        public static Border Card(UIElement child)
        {
            var b = new Border { Background = CardBg, CornerRadius = new CornerRadius(14), Child = child };
            ClipRounded(b, 14);
            return b;
        }

        public static Border CircleButton(double size, Brush bg, string glyph, double glyphSize, Brush fg, Action click, out TextBlock glyphText)
        {
            glyphText = Icon(glyph, glyphSize, fg);
            glyphText.HorizontalAlignment = HorizontalAlignment.Center;
            var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = bg, Child = glyphText };
            Clickable(b, click, null);
            return b;
        }

        // Fades an element in or out; IsHitTestVisible doubles as the "shown" flag.
        public static void Show(UIElement el, bool on, int ms = 160, int delayMs = 0)
        {
            if (el.IsHitTestVisible == on) return;
            el.IsHitTestVisible = on;
            el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(on ? 1 : 0, Ms(ms))
            {
                BeginTime = TimeSpan.FromMilliseconds(on ? delayMs : 0),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
        }

        public static void Hide(UIElement el)
        {
            el.BeginAnimation(UIElement.OpacityProperty, null);
            el.Opacity = 0;
            el.IsHitTestVisible = false;
        }

        // Rub el Hizb: two overlapping squares merged into an 8-point star, with a small ring inside.
        public static Path Star8(double size, Brush stroke, double thickness)
        {
            double c = size / 2, side = size / Math.Sqrt(2);
            var a = new RectangleGeometry(new Rect(c - side / 2, c - side / 2, side, side));
            var b = new RectangleGeometry(new Rect(c - side / 2, c - side / 2, side, side)) { Transform = new RotateTransform(45, c, c) };
            var g = new GeometryGroup();
            g.Children.Add(new CombinedGeometry(GeometryCombineMode.Union, a, b));
            g.Children.Add(new EllipseGeometry(new Point(c, c), side * 0.3, side * 0.3));
            g.Freeze();
            return new Path { Data = g, Stroke = stroke, StrokeThickness = thickness, Width = size, Height = size, IsHitTestVisible = false };
        }

        public static Border Sep()
        {
            return new Border { Height = 1, Background = Separator, Margin = new Thickness(16, 0, 0, 0), SnapsToDevicePixels = true };
        }

        public static Border Switch(bool on, Action<bool> changed)
        {
            var bg = new SolidColorBrush(on ? Green : SwitchOff);
            var track = new Border { Width = 46, Height = 28, CornerRadius = new CornerRadius(14), Background = bg, Cursor = Cursors.Hand };
            var tt = new TranslateTransform(on ? 18 : 0, 0);
            var knob = new Ellipse
            {
                Width = 24, Height = 24, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(2, 0, 0, 0), RenderTransform = tt,
                Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.35 }
            };
            track.Child = knob;
            bool state = on;
            track.MouseLeftButtonDown += (s, e) => e.Handled = true;
            track.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                state = !state;
                tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(state ? 18 : 0, Ms(280)) { EasingFunction = Ease });
                bg.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(state ? Green : SwitchOff, Ms(220)));
                changed(state);
            };
            return track;
        }

        // macOS traffic lights. Glyphs appear on hover.
        public static StackPanel Lights(Action close, Action minimize, Action zoom)
        {
            var sp = new StackPanel
            {
                Orientation = Orientation.Horizontal, Margin = new Thickness(18, 20, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Background = Brushes.Transparent
            };
            var glyphs = new List<TextBlock>();
            Action<string, string, Action> add = (color, glyph, act) =>
            {
                var g = new Grid { Width = 12, Height = 12, Margin = new Thickness(0, 0, 8, 0) };
                g.Children.Add(new Ellipse { Fill = act == null ? B("#3A3A3C") : B(color) });
                if (act != null)
                {
                    var t = new TextBlock
                    {
                        Text = glyph, FontFamily = Icons, FontSize = 6.5, Foreground = B("#A0000000"), Opacity = 0,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                    };
                    glyphs.Add(t); g.Children.Add(t);
                    Clickable(g, act, null);
                }
                sp.Children.Add(g);
            };
            add("#FF5F57", "", close);
            add("#FEBC2E", "", minimize);
            add("#28C840", "", zoom);
            sp.MouseEnter += (s, e) => { foreach (var t in glyphs) t.Opacity = 1; };
            sp.MouseLeave += (s, e) => { foreach (var t in glyphs) t.Opacity = 0; };
            return sp;
        }

        // Borderless, rounded, shadowed black window chrome. Returns the content host.
        public static Grid Chrome(Window w, double width, double height)
        {
            const double pad = 18;
            w.WindowStyle = WindowStyle.None;
            w.AllowsTransparency = true;
            w.Background = Brushes.Transparent;
            w.ResizeMode = ResizeMode.CanMinimize;
            w.Width = width + pad * 2;
            w.Height = height + pad * 2;
            w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            w.FontFamily = Text;
            TextOptions.SetTextRenderingMode(w, TextRenderingMode.Grayscale);

            var host = new Grid { Background = Brushes.Black };
            var frame = new Border
            {
                Margin = new Thickness(pad), CornerRadius = new CornerRadius(22), Background = Brushes.Black,
                BorderBrush = B("#2A2A2D"), BorderThickness = new Thickness(1), Child = host,
                Effect = new DropShadowEffect { BlurRadius = 32, ShadowDepth = 6, Opacity = 0.55, Color = Colors.Black }
            };
            host.SizeChanged += (s, e) => host.Clip = new RectangleGeometry(new Rect(host.RenderSize), 21, 21);
            frame.MouseLeftButtonDown += (s, e) => { try { w.DragMove(); } catch { } };
            w.Content = frame;
            return host;
        }
    }

    // Underdamped spring step response: settles fast with ~6% overshoot.
    class SpringEase : EasingFunctionBase
    {
        public SpringEase() { EasingMode = EasingMode.EaseIn; }

        protected override double EaseInCore(double t)
        {
            return 1 - Math.Exp(-7 * t) * (Math.Cos(8 * t) + 7.0 / 8 * Math.Sin(8 * t));
        }

        protected override Freezable CreateInstanceCore() { return new SpringEase(); }
    }

    // Circular progress ring (Activity-ring style).
    class Ring : Grid
    {
        readonly Path arc;
        readonly double size, thickness;

        public Ring(double size, double thickness, Brush track, Brush stroke)
        {
            this.size = size;
            this.thickness = thickness;
            Width = Height = size;
            Children.Add(new Ellipse { Stroke = track, StrokeThickness = thickness });
            arc = new Path { Stroke = stroke, StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            Children.Add(arc);
        }

        public Brush Stroke { set { arc.Stroke = value; } }

        public void Set(double p)
        {
            p = Math.Max(0, Math.Min(1, p));
            double r = (size - thickness) / 2, c = size / 2;
            if (p <= 0.001) { arc.Data = null; return; }
            if (p >= 0.999) { arc.Data = new EllipseGeometry(new Point(c, c), r, r); return; }
            double a = p * 2 * Math.PI;
            var fig = new PathFigure { StartPoint = new Point(c, c - r), IsClosed = false };
            fig.Segments.Add(new ArcSegment(new Point(c + r * Math.Sin(a), c - r * Math.Cos(a)), new Size(r, r), 0, p > 0.5, SweepDirection.Clockwise, true));
            var g = new PathGeometry();
            g.Figures.Add(fig);
            arc.Data = g;
        }
    }

    // iOS segmented control with a sliding thumb.
    class Segmented : Border
    {
        readonly TranslateTransform tt = new TranslateTransform();
        readonly List<TextBlock> labels = new List<TextBlock>();
        readonly double itemW;
        public event Action<int> Changed;

        public Segmented(string[] items, double itemW)
        {
            this.itemW = itemW;
            Background = Ui.CardBg;
            CornerRadius = new CornerRadius(10);
            Height = 32;
            Padding = new Thickness(2);
            HorizontalAlignment = HorizontalAlignment.Left;
            var g = new Grid();
            g.Children.Add(new Border { Width = itemW, CornerRadius = new CornerRadius(8), Background = Ui.Raised, HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = tt });
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < items.Length; i++)
            {
                var t = Ui.T(items[i], 13, Ui.Secondary, FontWeights.SemiBold);
                t.HorizontalAlignment = HorizontalAlignment.Center;
                t.VerticalAlignment = VerticalAlignment.Center;
                var cell = new Grid { Width = itemW, Background = Brushes.Transparent };
                cell.Children.Add(t);
                int idx = i;
                Ui.Clickable(cell, () => { if (Changed != null) Changed(idx); }, null);
                labels.Add(t);
                sp.Children.Add(cell);
            }
            g.Children.Add(sp);
            Child = g;
        }

        public void Select(int i, bool animate)
        {
            for (int j = 0; j < labels.Count; j++) labels[j].Foreground = j == i ? Ui.Label : Ui.Secondary;
            tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(i * itemW, Ui.Ms(animate ? 340 : 0)) { EasingFunction = Ui.Ease });
        }
    }
}
