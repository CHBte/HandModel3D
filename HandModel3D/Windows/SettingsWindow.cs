using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HandModel3D
{
    /// <summary>
    /// 설정 창 (3-10-3, 3-11). <260811_15>: 파일 위치를 비트맵 캡처(PNG)·벡터 캡처(SVG)·
    /// 저장 파일(json) 셋으로 나누고, 캡처 두 종류는 파일 이름을 자동/수동 중에서 고른다.
    /// 구역 구분 테두리는 기존 색 선택 창·손 회전 패널과 같은 모양(1px #212529, 라운드 3).
    /// </summary>
    public sealed class SettingsWindow : Window
    {
        /// <summary>
        /// <260811_21 추가지시> '확인'을 눌렀을 때 초기값 8개 중 하나라도 달라졌는지. 창을 닫은 뒤
        /// 메인 창이 이 값을 보고 즉시 반영 + 다음 실행용 저장을 한다(안 고쳤으면 손을 안 건드린다).
        /// </summary>
        public bool DefaultsChanged { get; private set; }

        public SettingsWindow()
        {
            Title = "설정";
            // <260811_21> '초기값' 8개 칸이 한 줄에 다 들어가도록 넓혔다(560 → 640).
            Width = 640;
            SizeToContent = SizeToContent.Height;   // 글꼴·DPI 가 달라도 '확인' 버튼이 잘리지 않게
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF3, 0xF5));
            FontFamily = new FontFamily("Malgun Gothic, Segoe UI");

            var sp = new StackPanel { Margin = new Thickness(20) };

            var png = AddSection(sp, "비트맵 캡처 파일(PNG) 위치", AppSettings.PngDir, "PngName", AppSettings.PngManualName);
            var svg = AddSection(sp, "벡터 캡처 파일(SVG) 위치", AppSettings.SvgDir, "SvgName", AppSettings.SvgManualName);
            var json = AddSection(sp, "저장 파일(json) 위치", AppSettings.JsonDir, null, false);

            // <260811_21> 세 위치 지정 영역 아래, '초기값' 편집 영역(값들을 나란히 배치).
            var (applyDefaults, restoreDefaults) = AddDefaultsSection(sp);

            // '저장…'·'불러오기…'는 누르는 즉시 AppSettings 의 초기값을 바꾼다. '확인' 없이 창을 닫으면(제목 표시줄의 X —
            // 취소 버튼은 없다) 그 값이 메모리에만 남아 손·다음 실행에는 반영되지 않는데, 다음에 창을 열면 칸과 비교
            // 기준이 같아져 '바뀐 것 없음'으로 영영 반영되지 않는다. 그래서 '확인'이 아닌 닫힘이면 창을 열 때의 값으로 되돌린다.
            bool okPressed = false;
            Closing += (s, e) => { if (!okPressed) restoreDefaults(); };

            var ok = new Button
            {
                Content = "확인",
                Width = 80,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0),
                Padding = new Thickness(6, 3, 6, 3),
                IsDefault = true,
            };
            ok.Click += (s, e) =>
            {
                // 빈칸이면 기존 위치를 유지한다(빈 경로로 캡처가 실패하는 것을 막는다).
                AppSettings.PngDir = Keep(png.box.Text, AppSettings.PngDir);
                AppSettings.SvgDir = Keep(svg.box.Text, AppSettings.SvgDir);
                AppSettings.JsonDir = Keep(json.box.Text, AppSettings.JsonDir);
                AppSettings.PngManualName = png.manual.IsChecked == true;
                AppSettings.SvgManualName = svg.manual.IsChecked == true;
                DefaultsChanged = applyDefaults();
                okPressed = true;
                Close();
            };
            sp.Children.Add(ok);
            Content = sp;
        }

        private static string Keep(string entered, string current)
            => string.IsNullOrWhiteSpace(entered) ? current : entered.Trim();

        /// <summary>위치 한 구역(제목 + 폴더칸/찾아보기 + 선택적으로 파일 이름 라디오 단추).</summary>
        private static (TextBox box, RadioButton manual) AddSection(Panel parent, string title, string dir,
                                                                    string radioGroup, bool manualOn)
        {
            var inner = new StackPanel();
            inner.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 6),
            });

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var box = new TextBox { Text = dir, Width = 380, VerticalAlignment = VerticalAlignment.Center };
            var browse = new Button { Content = "찾아보기…", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(8, 3, 8, 3) };
            browse.Click += (s, e) =>
            {
                var dlg = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = box.Text };
                if (dlg.ShowDialog() == true) box.Text = dlg.FolderName;
            };
            row.Children.Add(box);
            row.Children.Add(browse);
            inner.Children.Add(row);

            RadioButton manual = null;
            if (radioGroup != null)
            {
                var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
                nameRow.Children.Add(new TextBlock
                {
                    Text = "파일 이름",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0),
                });
                nameRow.Children.Add(new RadioButton
                {
                    Content = "자동",
                    GroupName = radioGroup,
                    IsChecked = !manualOn,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                manual = new RadioButton
                {
                    Content = "캡처 시 파일 이름 수동 입력",
                    GroupName = radioGroup,
                    IsChecked = manualOn,
                    Margin = new Thickness(16, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                nameRow.Children.Add(manual);
                inner.Children.Add(nameRow);
            }

            parent.Children.Add(new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x21, 0x25, 0x29)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 6, 8, 8),
                Margin = new Thickness(0, 0, 0, 10),
                Child = inner,
            });
            return (box, manual);
        }

        /// <summary>
        /// <260811_21> '초기값'(크기·좌우·물갈퀴 살·손 위치 X/Y) 편집 영역 — 3개 위치 지정
        /// 영역 아래, 값들을 나란히(가로) 배치. "저장…"은 "초기값_"으로 시작하는 json 파일로
        /// 내보내고, "불러오기…"는 그런 파일을 읽어 칸에 채운다(손 모양 파일을 고르면 거부).
        /// 반환값은 '확인' 버튼이 호출할 "칸 → AppSettings.Def*" 반영 함수(창을 연 뒤 하나라도 달라지면 true)와,
        /// '확인' 없이 닫힐 때 AppSettings.Def* 를 창을 연 시점의 값으로 되돌리는 함수다.
        /// </summary>
        private static (Func<bool> apply, Action restore) AddDefaultsSection(Panel parent)
        {
            var inner = new StackPanel();
            inner.Children.Add(new TextBlock
            {
                Text = "초기값- 고치면 바로 반영되고, 다음 실행에도 적용됩니다.",
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 6),
            });

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            TextBox MakeField(string label, double value)
            {
                var col = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
                col.Children.Add(new TextBlock { Text = label, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center });
                var box = new TextBox
                {
                    Text = value.ToString("0.##", CultureInfo.InvariantCulture),
                    Width = 48,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 0),
                };
                col.Children.Add(box);
                row.Children.Add(col);
                return box;
            }

            var scaleBox = MakeField("크기(%)", AppSettings.DefOverallScale * 100);
            var widthBox = MakeField("좌우(%)", AppSettings.DefWidthScale * 100);
            var webBox = MakeField("물갈퀴 살(%)", AppSettings.DefWebbingHeight);
            var lxBox = MakeField("왼손 X", AppSettings.DefOffsetLeftX);
            var lyBox = MakeField("왼손 Y", AppSettings.DefOffsetLeftY);
            var rxBox = MakeField("오른손 X", AppSettings.DefOffsetRightX);
            var ryBox = MakeField("오른손 Y", AppSettings.DefOffsetRightY);
            var cmcBox = MakeField("엄지 CMC↔", AppSettings.DefThumbCmcLR);   // <260811_21>
            inner.Children.Add(row);

            // 창을 연 시점의 초기값 8개. '저장…'·'불러오기…'가 AppSettings 를 먼저 바꾸므로, '확인'을 눌렀을 때
            // 달라졌는지는 칸이 아니라 이 값과 견줘야 한다(안 그러면 불러온 값이 손에 반영·저장되지 않는다).
            double s0 = AppSettings.DefOverallScale, w0 = AppSettings.DefWidthScale, h0 = AppSettings.DefWebbingHeight;
            double lx0 = AppSettings.DefOffsetLeftX, ly0 = AppSettings.DefOffsetLeftY;
            double rx0 = AppSettings.DefOffsetRightX, ry0 = AppSettings.DefOffsetRightY, c0 = AppSettings.DefThumbCmcLR;

            // 빈칸·글자 등 잘못된 입력이면 현재 AppSettings 값을 그대로 유지한다(다른 칸과 같은 원칙).
            // 값은 각 슬라이더의 실제 범위로 클램프해 담는다 — 저장 파일·다음에 열었을 때의 칸 표시·
            // 실제 적용값이 어긋나지 않게(qksqhr 원칙). 반환 = 창을 연 뒤 8개 중 하나라도 달라졌는지.
            bool ApplyFields()
            {
                // 쉼표(천 단위)·통화 기호 같은 건 받지 않는다 — "4,5"가 45가 되는 식의 오독을 막는다.
                // 무한대("Infinity"·1e999)도 거부한다.
                double P(TextBox b, double cur, double lo, double hi)
                    => double.TryParse(b.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                       && !double.IsNaN(v) && !double.IsInfinity(v)
                        ? Math.Max(lo, Math.Min(hi, v)) : cur;

                AppSettings.DefOverallScale = P(scaleBox, AppSettings.DefOverallScale * 100, 50, 180) / 100.0;
                AppSettings.DefWidthScale = P(widthBox, AppSettings.DefWidthScale * 100, 70, 130) / 100.0;
                AppSettings.DefWebbingHeight = P(webBox, AppSettings.DefWebbingHeight, 0, 200);
                AppSettings.DefOffsetLeftX = P(lxBox, AppSettings.DefOffsetLeftX, -30, 30);
                AppSettings.DefOffsetLeftY = P(lyBox, AppSettings.DefOffsetLeftY, -30, 30);
                AppSettings.DefOffsetRightX = P(rxBox, AppSettings.DefOffsetRightX, -30, 30);
                AppSettings.DefOffsetRightY = P(ryBox, AppSettings.DefOffsetRightY, -30, 30);
                AppSettings.DefThumbCmcLR = P(cmcBox, AppSettings.DefThumbCmcLR, 0, 100);

                const double Eps = 1e-9;
                return Math.Abs(AppSettings.DefOverallScale - s0) > Eps
                    || Math.Abs(AppSettings.DefWidthScale - w0) > Eps
                    || Math.Abs(AppSettings.DefWebbingHeight - h0) > Eps
                    || Math.Abs(AppSettings.DefOffsetLeftX - lx0) > Eps
                    || Math.Abs(AppSettings.DefOffsetLeftY - ly0) > Eps
                    || Math.Abs(AppSettings.DefOffsetRightX - rx0) > Eps
                    || Math.Abs(AppSettings.DefOffsetRightY - ry0) > Eps
                    || Math.Abs(AppSettings.DefThumbCmcLR - c0) > Eps;
            }

            void FillFieldsFromSettings()
            {
                scaleBox.Text = (AppSettings.DefOverallScale * 100).ToString("0.##", CultureInfo.InvariantCulture);
                widthBox.Text = (AppSettings.DefWidthScale * 100).ToString("0.##", CultureInfo.InvariantCulture);
                webBox.Text = AppSettings.DefWebbingHeight.ToString("0.##", CultureInfo.InvariantCulture);
                lxBox.Text = AppSettings.DefOffsetLeftX.ToString("0.##", CultureInfo.InvariantCulture);
                lyBox.Text = AppSettings.DefOffsetLeftY.ToString("0.##", CultureInfo.InvariantCulture);
                rxBox.Text = AppSettings.DefOffsetRightX.ToString("0.##", CultureInfo.InvariantCulture);
                ryBox.Text = AppSettings.DefOffsetRightY.ToString("0.##", CultureInfo.InvariantCulture);
                cmcBox.Text = AppSettings.DefThumbCmcLR.ToString("0.##", CultureInfo.InvariantCulture);
            }

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            var saveBtn = new Button { Content = "저장…", Padding = new Thickness(8, 3, 8, 3) };
            var loadBtn = new Button { Content = "불러오기…", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(8, 3, 8, 3) };
            btnRow.Children.Add(saveBtn);
            btnRow.Children.Add(loadBtn);
            inner.Children.Add(btnRow);

            saveBtn.Click += (s, e) =>
            {
                ApplyFields();
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "초기값 저장",
                    Filter = "손 모델 3D 초기값 (*.json)|*.json",
                    FileName = "초기값_" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + ".json",
                    InitialDirectory = System.IO.Directory.Exists(AppSettings.JsonDir) ? AppSettings.JsonDir : null,
                };
                if (dlg.ShowDialog() == true)
                {
                    try { DefaultValues.Save(dlg.FileName); }
                    catch (Exception ex) { MessageBox.Show("저장 실패: " + ex.Message, "초기값 저장"); }
                }
            };

            loadBtn.Click += (s, e) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "초기값 불러오기",
                    Filter = "손 모델 3D 초기값 (*.json)|*.json",
                    InitialDirectory = System.IO.Directory.Exists(AppSettings.JsonDir) ? AppSettings.JsonDir : null,
                };
                if (dlg.ShowDialog() != true) return;
                // 손 모양이 담긴 파일(SceneState)을 골랐으면 거부 — 반대 방향 안내(대칭 대비용).
                if (SceneState.IsStateFile(dlg.FileName))
                {
                    MessageBox.Show("이 파일은 손 모양이 담긴 파일입니다. 초기값이 담긴 파일을 불러오세요.", "초기값 불러오기");
                    return;
                }
                if (!DefaultValues.Load(dlg.FileName))
                {
                    MessageBox.Show("올바른 초기값 파일이 아닙니다.", "초기값 불러오기");
                    return;
                }
                FillFieldsFromSettings();
            };

            parent.Children.Add(new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x21, 0x25, 0x29)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 6, 8, 8),
                Margin = new Thickness(0, 0, 0, 10),
                Child = inner,
            });
            // '확인' 없이 닫힐 때 부르는 되돌리기: AppSettings 초기값을 창을 연 시점(s0..c0)으로.
            void RestoreOriginal()
            {
                AppSettings.DefOverallScale = s0; AppSettings.DefWidthScale = w0; AppSettings.DefWebbingHeight = h0;
                AppSettings.DefOffsetLeftX = lx0; AppSettings.DefOffsetLeftY = ly0;
                AppSettings.DefOffsetRightX = rx0; AppSettings.DefOffsetRightY = ry0; AppSettings.DefThumbCmcLR = c0;
            }

            return (ApplyFields, RestoreOriginal);
        }
    }
}
