using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using HandModel3D.Hand;

namespace HandModel3D.Controls
{
    /// <summary>
    /// 한 손의 관절 슬라이더 패널. (3-3-2) 의 상대 위치를 모방한 그리드로 배치한다.
    /// 왼손: 손가락(L1~L4) 왼쪽, 엄지(L5) 오른쪽. 오른손: 엄지(R6) 왼쪽, 손가락(R7~R0) 오른쪽.
    /// </summary>
    public sealed class JointPanel : UserControl
    {
        public event Action<string, double> SliderChanged;  // (key, percent)
        private readonly Dictionary<string, PercentSlider> _sliders = new Dictionary<string, PercentSlider>();
        private readonly HandSide _side;
        private readonly HandPose _pose;

        public JointPanel(HandSide side, HandPose pose)
        {
            _side = side;
            _pose = pose;

            var grid = new Grid { Margin = new Thickness(4) };
            for (int c = 0; c < 5; c++) grid.ColumnDefinitions.Add(new ColumnDefinition());
            for (int r = 0; r < 7; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 손가락 열 순서: 왼손 = [L1 L2 L3 L4 L5], 오른손 = [R6 R7 R8 R9 R0]
            Digit[] fingerCols;      // col 0..3 손가락, col 4 = 왼손 엄지
            int thumbCol;
            if (side == HandSide.Left)
            {
                fingerCols = new[] { Digit.Pinky, Digit.Ring, Digit.Middle, Digit.Index };
                thumbCol = 4;
            }
            else
            {
                // 오른손: col0 = 엄지(R6), col1..4 = R7 R8 R9 R0
                fingerCols = new[] { Digit.Index, Digit.Middle, Digit.Ring, Digit.Pinky };
                thumbCol = 0;
            }

            int fingerStartCol = side == HandSide.Left ? 0 : 1;

            // Row 0: DIP (손가락만)
            for (int i = 0; i < 4; i++)
                AddSlider(grid, SliderId.Joint(side, fingerCols[i], JointKind.DIP, Axis.Flex),
                          Label(side, fingerCols[i], "DIP"), 0, fingerStartCol + i);

            // Row 1: PIP (손가락) + 엄지 IP
            for (int i = 0; i < 4; i++)
                AddSlider(grid, SliderId.Joint(side, fingerCols[i], JointKind.PIP, Axis.Flex),
                          Label(side, fingerCols[i], "PIP"), 1, fingerStartCol + i);
            AddSlider(grid, SliderId.Joint(side, Digit.Thumb, JointKind.IP, Axis.Flex),
                      Label(side, Digit.Thumb, "IP"), 1, thumbCol);

            // Row 2: MCP 상하(↕)
            for (int i = 0; i < 4; i++)
                AddSlider(grid, SliderId.Joint(side, fingerCols[i], JointKind.MCP, Axis.Flex),
                          Label(side, fingerCols[i], "MCP↕"), 2, fingerStartCol + i);
            AddSlider(grid, SliderId.Joint(side, Digit.Thumb, JointKind.MCP, Axis.Flex),
                      Label(side, Digit.Thumb, "MCP↕"), 2, thumbCol);

            // Row 3: MCP 좌우(↔)
            for (int i = 0; i < 4; i++)
                AddSlider(grid, SliderId.Joint(side, fingerCols[i], JointKind.MCP, Axis.Abduct),
                          Label(side, fingerCols[i], "MCP↔"), 3, fingerStartCol + i);
            AddSlider(grid, SliderId.Joint(side, Digit.Thumb, JointKind.MCP, Axis.Abduct),
                      Label(side, Digit.Thumb, "MCP↔"), 3, thumbCol);

            // Row 4: 손바닥 아치 (가운데)
            AddSlider(grid, SliderId.Arch(side), "손바닥\n아치", 4, 2);

            // Row 5,6: 엄지 CMC 상하/좌우 (가운데)
            AddSlider(grid, SliderId.Joint(side, Digit.Thumb, JointKind.CMC, Axis.Flex),
                      Label(side, Digit.Thumb, "CMC↕"), 5, 2);
            AddSlider(grid, SliderId.Joint(side, Digit.Thumb, JointKind.CMC, Axis.Abduct),
                      Label(side, Digit.Thumb, "CMC↔"), 6, 2);

            Content = grid;
        }

        private string Label(HandSide side, Digit d, string joint)
            => Anatomy.FingerCode(side, d) + " " + joint;

        private void AddSlider(Grid grid, SliderId id, string label, int row, int col)
        {
            var ps = new PercentSlider(id.Key, label, _pose.Get(id));
            ps.ValueChanged += (s, v) => SliderChanged?.Invoke(s.Key, v);
            Grid.SetRow(ps, row);
            Grid.SetColumn(ps, col);
            grid.Children.Add(ps);
            _sliders[id.Key] = ps;
        }

        /// <summary>포즈가 외부(드래그·열기)로 바뀌면 슬라이더 표시를 갱신(이벤트 없이).</summary>
        public void SyncFromPose()
        {
            foreach (var kv in _sliders)
                kv.Value.Value = _pose.Get(kv.Key);
        }

        public PercentSlider Get(string key) => _sliders.TryGetValue(key, out var s) ? s : null;
    }
}
