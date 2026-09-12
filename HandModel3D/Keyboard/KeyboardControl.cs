using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace HandModel3D.KeyboardUi
{
    /// <summary>
    /// 배경 렌더링 키보드 (3-6). <260811_19>: 원본인 OpenTypingPlus 의 KeyLayoutBox/KeyBox 를
    /// **좌표·크기·키값·글꼴까지 그대로** 옮긴 2D 그림이다. 원본 치수(px):
    ///   · 키 50×50, 가로 간격 2, 행 피치 52 → 전체 777×260
    ///   · KeyBack 50 / KeyTop 43.7 / 눌림 2.8 / 라운드 5 / 테두리 2 / 글자 11pt
    ///   · 특수키 폭: Backspace 100, Tab 80, Caps Lock 100, Enter 103, 왼Shift 130, 오른Shift 125,
    ///     1행 마지막 키 70, Ctrl·Alt 62.5, 한자·한/영 50, Space 415 (Win·Menu 없는 106키 배열)
    ///   · 검지 홈 포지션(2행 4번째·7번째 = 물리 F/J)에는 KeyTop 바닥에 밑줄
    /// <260811_19-1>: **모든 키가 똑같은 흰 키**다. 원본은 Tab·Shift 같은 특수키를 회색(#DEE2E6)으로
    /// 칠하지만, 이 배경 키보드는 어떤 키든 [Ctrl]+좌클릭으로 '진한 노란색 눌린 키'가 되어야 하므로
    /// 색을 흰색으로 통일했다(되돌리지 말 것 — 사용자 지시).
    /// [Ctrl]+클릭으로 키 토글(Shift 제외 노란 키는 1개만), [Ctrl]+드래그로 비율 고정 확대/축소.
    /// [Ctrl]을 안 누르면 순수 배경(입력은 MainWindow 가 Ctrl 분기로 전달).
    /// </summary>
    public sealed class KeyboardControl : FrameworkElement
    {
        public bool Dubeolsik = true;

        // 눌린 상태
        public (int row, int col)? PressedKey;    // Shift 제외 1개만
        public bool LeftShiftPressed, RightShiftPressed;

        public event Action StateChanged;

        // ----- 원본 좌표계(px) -----
        public const double LayoutW = 777.0;
        public const double LayoutH = 260.0;
        private const double RowPitch = 52.0;   // 키 50 + 아래 간격 2
        private const double KeyW = 50.0;
        private const double KeyH = 50.0;       // KeyBack
        private const double TopH = 43.7;       // KeyTop
        private const double PressDiff = 2.8;
        private const double CornerR = 5.0;
        private const double BorderW = 2.0;
        private const double FontDip = 11.0 * 96.0 / 72.0;   // 11pt
        private const int HomeRow = 2, HomeColF = 3, HomeColJ = 6;   // 물리 F·J 자리 밑줄

        // <260811_19-1> 특수키에도 물리 위치 (Row,Col)을 준다 — 마스터 표(0~3행 letters)와 겹치지
        // 않는 자리를 쓰므로, 저장 형식(KbPressedRow/Col 정수 2개)을 바꾸지 않고 모든 키를 누른
        // 상태로 기록할 수 있다. Backspace(0,13) Tab(1,-1) Caps(2,-1) Enter(2,11)
        // Shift(3,-1)/(3,10) 아래 행(4,0)~(4,6).
        private struct KeySlot
        {
            public double X, Y, W;      // 원본 px
            public int Row, Col;        // 모든 키가 고유한 물리 위치를 갖는다
            public string Label;        // 특수키 라벨(Tab 등), 마스터 표 키면 null
            public bool IsLeftShift, IsRightShift, HomeMark;
        }
        private readonly List<KeySlot> _slots = new List<KeySlot>();

        public KeyboardControl()
        {
            IsHitTestVisible = false;   // 순수 배경 — 입력은 MainWindow 의 Ctrl 분기가 처리
            BuildSlots();
        }

        private void BuildSlots()
        {
            _slots.Clear();

            // 0행: 13키 + Backspace(100)
            AddRow(0, 13, 0);
            AddSpecial(676, 0, 100, "Backspace", 13);

            // 1행: Tab(80) + 12키 + 마지막 키(70)
            AddSpecial(0, 1, 80, "Tab", -1);
            AddRow(1, 12, 82);
            _slots.Add(new KeySlot { X = 706, Y = RowPitch, W = 70, Row = 1, Col = 12 });

            // 2행: Caps Lock(100) + 11키 + Enter(103)
            AddSpecial(0, 2, 100, "Caps Lock", -1);
            AddRow(2, 11, 102);
            AddSpecial(674, 2, 103, "Enter", 11);

            // 3행: Shift(130) + 10키 + Shift(125)
            _slots.Add(new KeySlot { X = 0, Y = 3 * RowPitch, W = 130, Row = 3, Col = -1, Label = "Shift", IsLeftShift = true });
            AddRow(3, 10, 132);
            _slots.Add(new KeySlot { X = 652, Y = 3 * RowPitch, W = 125, Row = 3, Col = 10, Label = "Shift", IsRightShift = true });

            // 4행: Ctrl·Alt·한자·Space·한/영·Alt·Ctrl (Win·Menu 없는 106키 변형 — 원본과 같다)
            AddSpecial(0, 4, 62.5, "Ctrl", 0);
            AddSpecial(64.5, 4, 62.5, "Alt", 1);
            AddSpecial(129, 4, 50, "한자", 2);
            AddSpecial(181, 4, 415, "Space", 3);
            AddSpecial(598, 4, 50, "한/영", 4);
            AddSpecial(650, 4, 62.5, "Alt", 5);
            AddSpecial(714.5, 4, 62.5, "Ctrl", 6);
        }

        private void AddRow(int row, int count, double startX)
        {
            for (int c = 0; c < count; c++)
                _slots.Add(new KeySlot
                {
                    X = startX + c * RowPitch,
                    Y = row * RowPitch,
                    W = KeyW,
                    Row = row,
                    Col = c,
                    HomeMark = row == HomeRow && (c == HomeColF || c == HomeColJ),
                });
        }

        private void AddSpecial(double x, int row, double w, string label, int col)
            => _slots.Add(new KeySlot { X = x, Y = row * RowPitch, W = w, Row = row, Col = col, Label = label });

        // ----- 상호작용 -----

        /// <summary>컨트롤 로컬 좌표(픽셀)의 키를 토글. (3-6-1) 규칙 적용.</summary>
        public bool ToggleAt(Point local)
        {
            double s = Scale;
            if (s <= 0) return false;
            foreach (var sl in _slots)
            {
                var rect = new Rect(sl.X * s, sl.Y * s, sl.W * s, KeyH * s);
                if (!rect.Contains(local)) continue;

                // <260811_19-1> Tab·Space 를 포함한 **모든 키**가 토글된다.
                if (sl.IsLeftShift) { LeftShiftPressed = !LeftShiftPressed; }
                else if (sl.IsRightShift) { RightShiftPressed = !RightShiftPressed; }
                else if (PressedKey.HasValue && PressedKey.Value == (sl.Row, sl.Col)) PressedKey = null;
                else PressedKey = (sl.Row, sl.Col);   // 기존 노란 키는 자동 해제(1개만 허용)

                InvalidateVisual();
                StateChanged?.Invoke();
                return true;
            }
            return false;
        }

        /// <summary>
        /// 캡처 파일명용: 눌린 non-Shift 키의 (라벨, "행-열"). 없으면 null. (3-10-3-1)
        /// <260811_19-1> 마스터 표에 없는 특수키(Space 등)도 라벨은 주고 "행-열"은 null 로 —
        /// 그 경우 [행-열] 사본 파일은 만들지 않는다.
        /// </summary>
        public (string label, string rowcol)? GetCaptureKey()
        {
            if (!PressedKey.HasValue) return null;
            var mk = KeyMaster.Find(PressedKey.Value.row, PressedKey.Value.col);
            if (mk != null)
            {
                bool shift = LeftShiftPressed || RightShiftPressed;
                return (shift ? mk.ShiftFor(Dubeolsik) : mk.BaseFor(Dubeolsik), mk.OutputBaseName);
            }
            foreach (var sl in _slots)
                if (sl.Row == PressedKey.Value.row && sl.Col == PressedKey.Value.col && sl.Label != null)
                    return (sl.Label, null);
            return null;
        }

        /// <summary>검증용: 키마다 하나씩, 컨트롤 로컬 좌표의 중심점 목록.</summary>
        public List<Point> KeyCenters()
        {
            double s = Scale;
            var list = new List<Point>();
            foreach (var sl in _slots) list.Add(new Point((sl.X + sl.W / 2) * s, (sl.Y + KeyH / 2) * s));
            return list;
        }

        /// <summary>
        /// <260811_24 측정> 키 하나의 컨트롤 로컬 사각형(ToggleAt·OnRender 와 같은 계산).
        /// label 을 주면 그 라벨의 특수키(예: "Space"), 아니면 마스터 표 (row,col) 키를 찾는다.
        /// '눌림 영역' 판정처럼 키의 중심·크기가 필요한 진단에 쓴다.
        /// </summary>
        public bool TryGetKeyRect(int row, int col, string label, out Rect rect)
        {
            double s = Scale;
            rect = default;
            if (s <= 0 || double.IsNaN(s)) return false;
            foreach (var sl in _slots)
            {
                bool hit = label != null ? sl.Label == label : (sl.Label == null && sl.Row == row && sl.Col == col);
                if (!hit) continue;
                rect = new Rect(sl.X * s, sl.Y * s, sl.W * s, KeyH * s);
                return true;
            }
            return false;
        }

        /// <summary>
        /// <260811_24 측정> 왼/오른 [Shift] 키의 컨트롤 로컬 사각형. 두 슬롯 모두 라벨이 "Shift"라
        /// TryGetKeyRect(label)로는 왼쪽만 잡히므로 전용 접근자를 둔다.
        /// </summary>
        public bool TryGetShiftRect(bool right, out Rect rect)
        {
            double s = Scale;
            rect = default;
            if (s <= 0 || double.IsNaN(s)) return false;
            foreach (var sl in _slots)
            {
                if (!(right ? sl.IsRightShift : sl.IsLeftShift)) continue;
                rect = new Rect(sl.X * s, sl.Y * s, sl.W * s, KeyH * s);
                return true;
            }
            return false;
        }

        private double Scale => ActualWidth / LayoutW;

        protected override Size MeasureOverride(Size availableSize)
        {
            double w = double.IsInfinity(availableSize.Width) ? 720 : availableSize.Width;
            return new Size(w, w / LayoutW * LayoutH);
        }

        // ----- 그리기 (원본 KeyBox 재현) -----

        private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0x21, 0x25, 0x29));
        private static readonly Brush WhiteTop = Brushes.White;
        private static readonly Brush WhiteShadow = new SolidColorBrush(Color.FromRgb(206, 212, 218));
        private static readonly Brush YellowTop = new SolidColorBrush(Color.FromRgb(250, 176, 5));
        private static readonly Brush YellowShadow = new SolidColorBrush(Color.FromRgb(245, 159, 0));

        // 원본과 같은 글꼴(나눔바른고딕). 리소스로 넣어 두어 PC 설치 여부와 무관하게 같은 모양이 나온다.
        private static readonly Typeface Face = new Typeface(
            new FontFamily(new Uri("pack://application:,,,/"), "./Resources/Fonts/#나눔바른고딕"),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        protected override void OnRender(DrawingContext dc)
        {
            double s = Scale;
            if (s <= 0 || double.IsNaN(s)) return;

            var pen = new Pen(Ink, BorderW * s);
            double corner = CornerR * s;
            double fontSize = FontDip * s;
            double lineH = MakeText("가", fontSize).Height;   // 빈 줄도 이만큼 자리를 차지한다

            foreach (var sl in _slots)
            {
                bool pressed = sl.IsLeftShift ? LeftShiftPressed
                             : sl.IsRightShift ? RightShiftPressed
                             : PressedKey.HasValue && PressedKey.Value == (sl.Row, sl.Col);

                double x = sl.X * s, y = sl.Y * s, w = sl.W * s;
                // <260811_19-1> 모든 키가 흰 키 — 눌렸을 때만 진한 노란색
                Brush top = pressed ? YellowTop : WhiteTop;
                Brush shadow = pressed ? YellowShadow : WhiteShadow;

                // 눌리면 KeyBack·KeyTop 이 함께 2.8 내려가고 KeyBack 만 그만큼 짧아진다(원본 SetPressedShape).
                double off = pressed ? PressDiff * s : 0;
                dc.DrawRoundedRectangle(shadow, pen, new Rect(x, y + off, w, KeyH * s - off), corner, corner);
                double topY = y + off;
                dc.DrawRoundedRectangle(top, pen, new Rect(x, topY, w, TopH * s), corner, corner);

                // 라벨: 원본은 [윗글쇠 줄][기본 줄] 두 줄짜리 세로 스택을 KeyTop 가운데에 놓는다.
                // 윗글쇠가 없어도 빈 줄이 자리를 차지하므로 기본 값은 늘 '아래 줄'에 그려진다.
                string baseTxt = sl.Label, shiftTxt = null;
                if (sl.Label == null)
                {
                    var mk = KeyMaster.Find(sl.Row, sl.Col);
                    if (mk != null)
                    {
                        baseTxt = mk.BaseFor(Dubeolsik);
                        string sh = Dubeolsik ? mk.DubeolShift : mk.QwertyShift;
                        if (!string.IsNullOrEmpty(sh) && sh != baseTxt) shiftTxt = sh;
                    }
                }

                double avail = w - 2 * BorderW * s;   // 원본 Viewbox 는 넘칠 때만 줄인다(DownOnly)
                var ftShift = MakeText(shiftTxt, fontSize);
                var ftBase = MakeText(baseTxt, fontSize);
                double k1 = Fit(ftShift, avail), k2 = Fit(ftBase, avail);
                double h1 = lineH * k1, h2 = lineH * k2;
                double y0 = topY + (TopH * s - (h1 + h2)) / 2;
                DrawLine(dc, ftShift, x + w / 2, y0, k1);
                DrawLine(dc, ftBase, x + w / 2, y0 + h1, k2);

                // 검지 홈 포지션 밑줄(14×1.5, KeyTop 안쪽 바닥에서 1px 위)
                if (sl.HomeMark)
                    dc.DrawRectangle(Ink, null,
                        new Rect(x + w / 2 - 7 * s, topY + (TopH - 2 - 1.5) * s, 14 * s, 1.5 * s));
            }
        }

        private static FormattedText MakeText(string text, double size)
        {
            if (string.IsNullOrEmpty(text)) text = "";
            return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                                     Face, size, Ink, 1.25);
        }

        private static double Fit(FormattedText ft, double avail)
            => ft.Width > avail && ft.Width > 0 ? avail / ft.Width : 1.0;

        private static void DrawLine(DrawingContext dc, FormattedText ft, double cx, double top, double k)
        {
            if (ft.Width <= 0) return;
            if (k >= 1.0) { dc.DrawText(ft, new Point(cx - ft.Width / 2, top)); return; }
            dc.PushTransform(new ScaleTransform(k, k, cx, top));
            dc.DrawText(ft, new Point(cx - ft.Width / 2, top));
            dc.Pop();
        }
    }
}
