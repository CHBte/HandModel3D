using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HandModel3D.Controls
{
    /// <summary>
    /// 가로 슬라이더 + 라벨 + 숫자 값(클릭하면 입력 칸으로 바뀜). 임의의 min~max 범위.
    /// '손 위치'(<260810_3> (7))·'시점 중심'(<260810_3> (4)) 패널이 쓴다.
    /// </summary>
    public sealed class ValueSlider : UserControl
    {
        public event Action<ValueSlider, double> ValueChanged;

        private readonly Slider _slider;
        private readonly TextBlock _valText;
        private readonly TextBox _valEdit;
        private bool _suppress, _editing;

        public double Value
        {
            get => _slider.Value;
            set { _suppress = true; _slider.Value = Clamp(value); UpdateText(); _suppress = false; }
        }

        public ValueSlider(string label, double min, double max, double initial, double sliderWidth = 80)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };

            if (!string.IsNullOrEmpty(label))
            {
                row.Children.Add(new TextBlock
                {
                    Text = label,
                    FontSize = 9,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 1, 0),
                    Foreground = new SolidColorBrush(Color.FromRgb(0x21, 0x25, 0x29)),
                });
            }

            _slider = new Slider
            {
                Minimum = min,
                Maximum = max,
                Value = initial,
                Width = sliderWidth,
                VerticalAlignment = VerticalAlignment.Center,
                SmallChange = (max - min) / 100.0,
                LargeChange = (max - min) / 10.0,
            };
            _slider.ValueChanged += (s, e) =>
            {
                UpdateText();
                if (!_suppress) ValueChanged?.Invoke(this, _slider.Value);
                if (_editing) EndEdit(false);
            };
            row.Children.Add(_slider);

            _valText = new TextBlock
            {
                FontSize = 9.5,
                Width = 22,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                Margin = new Thickness(1, 0, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0x21, 0x25, 0x29)),
            };
            _valText.MouseLeftButtonUp += (s, e) => BeginEdit();
            row.Children.Add(_valText);

            _valEdit = new TextBox
            {
                FontSize = 9.5,
                Width = 22,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(1, 0, 0, 0),
            };
            _valEdit.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) EndEdit(true);
                else if (e.Key == Key.Escape) EndEdit(false);
            };
            _valEdit.LostFocus += (s, e) => EndEdit(true);
            row.Children.Add(_valEdit);

            Content = row;
            UpdateText();
        }

        private void BeginEdit()
        {
            _editing = true;
            _valEdit.Text = _slider.Value.ToString("0.#", CultureInfo.InvariantCulture);
            _valEdit.Visibility = Visibility.Visible;
            _valText.Visibility = Visibility.Collapsed;
            _valEdit.Focus();
            _valEdit.SelectAll();
        }

        private void EndEdit(bool commit)
        {
            if (!_editing) return;
            _editing = false;
            // NaN·무한대는 클램프 비교(<, >)를 통과하거나(NaN) 슬라이더 값 검증에서 예외를 내므로(∞) 거부한다.
            // 쉼표(천 단위)·통화 기호 표기는 받지 않는다("4,5"가 45로 읽히는 것을 막는다).
            if (commit && double.TryParse(_valEdit.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                && !double.IsNaN(v) && !double.IsInfinity(v))
                _slider.Value = Clamp(v);   // ValueChanged 발생
            _valEdit.Visibility = Visibility.Collapsed;
            _valText.Visibility = Visibility.Visible;
            UpdateText();
        }

        private void UpdateText() { if (_valText != null) _valText.Text = _slider.Value.ToString("0.#"); }

        private double Clamp(double v) => v < _slider.Minimum ? _slider.Minimum : (v > _slider.Maximum ? _slider.Maximum : v);
    }
}
