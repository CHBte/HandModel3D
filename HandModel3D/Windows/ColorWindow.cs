using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HandModel3D
{
    /// <summary>피부색/윤곽선색 선택 창. 프리셋 + RGB + HSL 슬라이더 + 스펙트럼. (3-8)</summary>
    public sealed class ColorWindow : Window
    {
        public Color SelectedColor { get; private set; }
        private readonly Border _preview;
        private readonly Slider _r, _g, _b, _h, _s, _l;
        private bool _sync;

        public ColorWindow(Color initial)
        {
            SelectedColor = initial;
            Title = "색 선택";
            Width = 420; Height = 440; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF3, 0xF5));
            FontFamily = new FontFamily("Malgun Gothic, Segoe UI");

            var root = new StackPanel { Margin = new Thickness(16) };

            // 프리셋(인종 피부색 + 디자인용)
            root.Children.Add(new TextBlock { Text = "예시 색", FontWeight = FontWeights.Bold });
            var presets = new WrapPanel { Margin = new Thickness(0, 4, 0, 10) };
            Color[] cols =
            {
                Color.FromRgb(0xFF,0xE0,0xC4), Color.FromRgb(0xF2,0xC9,0xA8), Color.FromRgb(0xE8,0xB6,0x98),
                Color.FromRgb(0xC9,0x94,0x6B), Color.FromRgb(0x9A,0x6A,0x45), Color.FromRgb(0x6B,0x47,0x2E),
                Color.FromRgb(0x3D,0x2A,0x1E), Colors.White, Colors.Black, Color.FromRgb(0x8A,0xA6,0xC0),
                Color.FromRgb(0xB0,0xE0,0x9A), Color.FromRgb(0xE0,0x9A,0xC4)
            };
            foreach (var c in cols)
            {
                var sw = new Border { Width = 22, Height = 22, Margin = new Thickness(2), Background = new SolidColorBrush(c), BorderBrush = Brushes.Black, BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand };
                sw.MouseLeftButtonUp += (s, e) => SetColor(c);
                presets.Children.Add(sw);
            }
            root.Children.Add(presets);

            _preview = new Border { Height = 34, Background = new SolidColorBrush(initial), BorderBrush = Brushes.Black, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 8) };
            root.Children.Add(_preview);

            // <260811_11>: RGB 영역과 HSL 영역을 각각 검은 선 테두리로 감싸 구분(<260810_5> 테두리와 동일 모양).
            Border Boxed(Panel inner, double bottomMargin) => new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x21, 0x25, 0x29)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 2, 4, 2),
                Margin = new Thickness(0, 0, 0, bottomMargin),
                Child = inner,
            };
            var rgbPanel = new StackPanel();
            _r = AddSlider(rgbPanel, "R", 0, 255, initial.R);
            _g = AddSlider(rgbPanel, "G", 0, 255, initial.G);
            _b = AddSlider(rgbPanel, "B", 0, 255, initial.B);
            root.Children.Add(Boxed(rgbPanel, 6));
            RgbToHsl(initial, out double hh, out double ss, out double ll);
            var hslPanel = new StackPanel();
            _h = AddSlider(hslPanel, "H", 0, 360, hh);
            _s = AddSlider(hslPanel, "S", 0, 100, ss * 100);
            _l = AddSlider(hslPanel, "L", 0, 100, ll * 100);
            root.Children.Add(Boxed(hslPanel, 0));

            _r.ValueChanged += (s, e) => FromRgb();
            _g.ValueChanged += (s, e) => FromRgb();
            _b.ValueChanged += (s, e) => FromRgb();
            _h.ValueChanged += (s, e) => FromHsl();
            _s.ValueChanged += (s, e) => FromHsl();
            _l.ValueChanged += (s, e) => FromHsl();

            var ok = new Button { Content = "확인", Width = 80, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(6, 3, 6, 3), IsDefault = true };
            ok.Click += (s, e) => { DialogResult = true; Close(); };
            root.Children.Add(ok);
            Content = root;
        }

        private Slider AddSlider(Panel host, string name, double min, double max, double val)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
            row.Children.Add(new TextBlock { Text = name, Width = 18, VerticalAlignment = VerticalAlignment.Center });
            var sl = new Slider { Minimum = min, Maximum = max, Value = val, Width = 320, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(sl);
            host.Children.Add(row);
            return sl;
        }

        private void SetColor(Color c)
        {
            _sync = true;
            _r.Value = c.R; _g.Value = c.G; _b.Value = c.B;
            RgbToHsl(c, out double h, out double s, out double l);
            _h.Value = h; _s.Value = s * 100; _l.Value = l * 100;
            _sync = false;
            Apply(c);
        }

        private void FromRgb()
        {
            if (_sync) return;
            var c = Color.FromRgb((byte)_r.Value, (byte)_g.Value, (byte)_b.Value);
            _sync = true;
            RgbToHsl(c, out double h, out double s, out double l);
            _h.Value = h; _s.Value = s * 100; _l.Value = l * 100;
            _sync = false;
            Apply(c);
        }

        private void FromHsl()
        {
            if (_sync) return;
            var c = HslToRgb(_h.Value, _s.Value / 100, _l.Value / 100);
            _sync = true;
            _r.Value = c.R; _g.Value = c.G; _b.Value = c.B;
            _sync = false;
            Apply(c);
        }

        private void Apply(Color c) { SelectedColor = c; _preview.Background = new SolidColorBrush(c); }

        public static void RgbToHsl(Color c, out double h, out double s, out double l)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2;
            if (max == min) { h = 0; s = 0; return; }
            double d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h *= 60;
        }

        public static Color HslToRgb(double h, double s, double l)
        {
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = l - c / 2;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; }
            else if (h < 120) { r = x; g = c; }
            else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; }
            else if (h < 300) { r = x; b = c; }
            else { r = c; b = x; }
            return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
        }
    }
}
