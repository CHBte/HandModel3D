using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HandModel3D.Hand;

namespace HandModel3D
{
    /// <summary>
    /// 손끝 마디(마지막 관절부터 끝)의 피부색/윤곽선색을 손가락별로 지정 (3-9-1-4).
    /// 약어 활용: "L3 DIP의 마디"는 왼손 중지 끝마디를 뜻한다. 이 색은 (3-8) 전체 색보다 우선.
    /// </summary>
    public sealed class TipColorWindow : Window
    {
        private readonly HandScene _scene;

        public TipColorWindow(HandScene scene)
        {
            _scene = scene;
            Title = "손끝 마디 색";
            Width = 420; Height = 460; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF3, 0xF5));
            FontFamily = new FontFamily("Malgun Gothic, Segoe UI");

            var root = new StackPanel { Margin = new Thickness(16) };
            root.Children.Add(new TextBlock { Text = "각 손끝 마디의 피부색 (전체 색보다 우선)", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });

            (HandSide s, Digit d)[] all =
            {
                (HandSide.Left, Digit.Pinky),(HandSide.Left,Digit.Ring),(HandSide.Left,Digit.Middle),(HandSide.Left,Digit.Index),(HandSide.Left,Digit.Thumb),
                (HandSide.Right, Digit.Thumb),(HandSide.Right,Digit.Index),(HandSide.Right,Digit.Middle),(HandSide.Right,Digit.Ring),(HandSide.Right,Digit.Pinky),
            };

            foreach (var (s, d) in all)
            {
                string code = Anatomy.FingerCode(s, d);
                string joint = d == Digit.Thumb ? "IP" : "DIP";
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                row.Children.Add(new TextBlock { Text = $"{code} {joint}의 마디", Width = 130, VerticalAlignment = VerticalAlignment.Center });

                Color cur = _scene.TipSkin.TryGetValue(code, out var tc) ? tc : _scene.SkinColor;
                var sw = new Border { Width = 40, Height = 20, Background = new SolidColorBrush(cur), BorderBrush = Brushes.Black, BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand };
                sw.MouseLeftButtonUp += (o, e) =>
                {
                    var win = new ColorWindow(((SolidColorBrush)sw.Background).Color) { Owner = this };
                    if (win.ShowDialog() == true) { _scene.TipSkin[code] = win.SelectedColor; sw.Background = new SolidColorBrush(win.SelectedColor); }
                };
                row.Children.Add(sw);

                var reset = new Button { Content = "기본", FontSize = 10, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(6, 2, 6, 2) };
                reset.Click += (o, e) => { _scene.TipSkin.Remove(code); sw.Background = new SolidColorBrush(_scene.SkinColor); };
                row.Children.Add(reset);
                root.Children.Add(row);
            }

            var ok = new Button { Content = "확인", Width = 80, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(6, 3, 6, 3), IsDefault = true };
            ok.Click += (s, e) => Close();
            root.Children.Add(ok);
            Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }
    }
}
