using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HandModel3D.Controls
{
    /// <summary>
    /// 세로 슬라이더 + 관절 약어 라벨 + 퍼센트 값(클릭하면 숫자 입력 칸으로 바뀜). (3-3-2-1)
    /// 값 0~100. 드래그하면 입력 칸이 자동으로 일반 표시로 돌아간다.
    /// </summary>
    public sealed class PercentSlider : UserControl
    {
        public string Key { get; }
        public event Action<PercentSlider, double> ValueChanged;

        private readonly Slider _slider;
        private readonly TextBlock _valText;
        private readonly TextBox _valEdit;
        private bool _suppress;

        public double Value
        {
            get => _slider.Value;
            set { _suppress = true; _slider.Value = Clamp(value); UpdateText(); _suppress = false; }
        }

        public PercentSlider(string key, string label, double initial)
        {
            Key = key;
            Width = 44;
            Margin = new Thickness(1, 0, 1, 0);

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var lbl = new TextBlock
            {
                Text = label,
                FontSize = 9,
                TextAlignment = TextAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0x21, 0x25, 0x29)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 2)
            };
            Grid.SetRow(lbl, 0);
            grid.Children.Add(lbl);

            _slider = new Slider
            {
                Orientation = Orientation.Vertical,
                Minimum = 0,
                Maximum = 100,
                Value = Clamp(initial),
                Height = 78,
                HorizontalAlignment = HorizontalAlignment.Center,
                IsSnapToTickEnabled = false,
                SmallChange = 1,
                LargeChange = 10
            };
            _slider.ValueChanged += (s, e) =>
            {
                UpdateText();
                if (!_suppress) ValueChanged?.Invoke(this, _slider.Value);
                // 드래그 중이면 입력 칸을 일반 표시로 되돌린다.
                if (_editing) EndEdit(false);
            };
            Grid.SetRow(_slider, 1);
            grid.Children.Add(_slider);

            _valText = new TextBlock
            {
                FontSize = 9.5,
                TextAlignment = TextAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0x21, 0x25, 0x29)),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 2, 0, 0)
            };
            _valText.MouseLeftButtonUp += (s, e) => BeginEdit();
            Grid.SetRow(_valText, 2);
            grid.Children.Add(_valText);

            _valEdit = new TextBox
            {
                FontSize = 9.5,
                TextAlignment = TextAlignment.Center,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 2, 0, 0)
            };
            _valEdit.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) EndEdit(true); else if (e.Key == System.Windows.Input.Key.Escape) EndEdit(false); };
            _valEdit.LostFocus += (s, e) => EndEdit(true);
            Grid.SetRow(_valEdit, 2);
            grid.Children.Add(_valEdit);

            Content = grid;
            UpdateText();
        }

        private bool _editing;

        private void BeginEdit()
        {
            _editing = true;
            _valEdit.Text = ((int)Math.Round(_slider.Value)).ToString(CultureInfo.InvariantCulture);
            _valEdit.Visibility = Visibility.Visible;
            _valText.Visibility = Visibility.Collapsed;
            _valEdit.Focus();
            _valEdit.SelectAll();
        }

        private void EndEdit(bool commit)
        {
            if (!_editing) return;
            _editing = false;
            // NaN 은 클램프 비교(<, >)를 모두 통과해 Slider.Value 예외를 일으키므로 거부한다
            if (commit && double.TryParse(_valEdit.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double v)
                && !double.IsNaN(v))
            {
                _slider.Value = Clamp(v);  // triggers ValueChanged
            }
            _valEdit.Visibility = Visibility.Collapsed;
            _valText.Visibility = Visibility.Visible;
            UpdateText();
        }

        private void UpdateText()
        {
            if (_valText != null) _valText.Text = ((int)Math.Round(_slider.Value)) + "%";
        }

        private static double Clamp(double v) => v < 0 ? 0 : (v > 100 ? 100 : v);
    }
}
