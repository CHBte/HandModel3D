using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using HandModel3D.Controls;
using HandModel3D.Hand;
using HandModel3D.Render;

namespace HandModel3D
{
    public partial class MainWindow : Window
    {
        private readonly HandScene _scene = new HandScene();
        private readonly Camera3D _camera = new Camera3D();
        private readonly SoftRenderer _renderer = new SoftRenderer();
        private JointPanel _leftJoints, _rightJoints;

        // 윤곽선 상태
        private bool _outlineShow, _outlineOnly, _outlineOcclude = true;
        private double _outlineThickness = 2.5;
        private Color _outlineColor = Color.FromRgb(0xE6, 0xA0, 0x3C);

        // 배경 키보드 종류
        public enum KbKind { None, Dubeolsik, Qwerty }
        private KbKind _keyboard = KbKind.None;

        private double _labelScale = 1.0;   // 관절 라벨 글씨 크기
        private bool _labelsShown;

        public MainWindow()
        {
            InitializeComponent();
            Title = VersionInfo.WindowTitle; // <2600912_5-1>
            BuildLengthSliders();
            BuildSwatches();
            BuildJointPanels();
            BuildRotationSliders();
            BuildHandPosSliders();
            BuildViewRotSliders();

            // <260810_3>(9): 상단 패널이 전부 보이는 폭을 창의 최소 가로로 삼는다(레이아웃 확정 후 계산).
            ContentRendered += (s, e) =>
            {
                double w = 0;
                foreach (FrameworkElement c in TopWrap.Children)
                    w += c.ActualWidth + c.Margin.Left + c.Margin.Right;
                double chrome = ActualWidth - ((FrameworkElement)Content).ActualWidth; // 창 테두리 폭
                if (double.IsNaN(chrome) || chrome < 0) chrome = 16;
                MinWidth = Math.Ceiling(w + TopWrap.Margin.Left + TopWrap.Margin.Right + chrome + 4);
            };

            SizeSlider.ValueChanged += (s, e) => { _scene.OverallScale = SizeSlider.Value; SizeLabel.Text = (int)Math.Round(SizeSlider.Value * 100) + "%"; Recompute(); };
            MakeValueEditable(SizeLabel, () => SizeSlider.Value * 100, v => SizeSlider.Value = v / 100.0);   // <260811_6-1>(1)
            VolumeSlider.ValueChanged += (s, e) => { _scene.Volume = VolumeSlider.Value; VolumeLabel.Text = (int)Math.Round(VolumeSlider.Value * 100) + "%"; Recompute(); };
            // <260811_12>: 좌우 비율(양손 상하 대비 좌우 길이)
            WidthSlider.ValueChanged += (s, e) => { _scene.WidthScale = WidthSlider.Value; WidthLabel.Text = (int)Math.Round(WidthSlider.Value * 100) + "%"; Recompute(); };
            MakeValueEditable(WidthLabel, () => WidthSlider.Value * 100, v => WidthSlider.Value = v / 100.0);
            // <260810_3-2>(12): 패널의 숫자 값(%)을 클릭하면 키보드로 직접 입력할 수 있게 한다.
            MakeValueEditable(VolumeLabel, () => VolumeSlider.Value * 100, v => VolumeSlider.Value = v / 100.0);
            MakeValueEditable(PinOpacityLabel, () => PinOpacity.Value, v => PinOpacity.Value = v);
            TranspSlider.ValueChanged += (s, e) => { _scene.Transparency = TranspSlider.Value; RenderScene(); };
            OutlineThick.ValueChanged += (s, e) => { _outlineThickness = OutlineThick.Value; RenderScene(); };
            PinOpacity.ValueChanged += (s, e) => { PinOpacityLabel.Text = (int)Math.Round(PinOpacity.Value) + "%"; RenderScene(); };

            // 그래픽 영역 입력(시점 제어)
            GraphicsHost.MouseDown += Graphics_MouseDown;
            GraphicsHost.MouseMove += Graphics_MouseMove;
            GraphicsHost.MouseUp += Graphics_MouseUp;
            GraphicsHost.MouseWheel += Graphics_MouseWheel;
            // <260811_14-1> 상태 표시줄 두께가 변하면 보정값을 갱신한다(그림은 제자리 유지).
            StatusBar.SizeChanged += (s, e) => SyncStatusOverflow();
            Focusable = true;
            // <260810_3>(11): 버블링 KeyDown 은 포커스된 컨트롤(슬라이더·라디오 등)이 방향키를 먹어
            // '배경 키보드'를 띄운 뒤 방향키가 회전 슬라이더를 움직이는 버그가 있었다. 터널링
            // PreviewKeyDown 으로 포커스와 무관하게 방향키를 가로챈다(텍스트 입력 중만 예외).
            PreviewKeyDown += Window_ArrowKeyDown;
            // <260811_17> json 파일을 창에 끌어다 놓으면 열기. 터널링(Preview)으로 받아 숫자 입력
            // 칸(TextBox) 같은 자식이 먼저 가로채 경로 문자열을 넣어 버리는 것을 막는다.
            PreviewDragOver += Window_DragOver;
            PreviewDrop += Window_Drop;

            // <260811_21 추가지시> 저장해 둔 초기값(있으면)을 읽어 시작 상태에 적용한다.
            // ApplyDebugArgs 보다 **앞**에 둬야 검증 스위치(-fist 등)가 이 값을 덮어쓸 수 있다.
            DefaultValues.LoadUserSettings();
            ApplyDefaultsToScene();

            ApplyDebugArgs();
            _scene.Recompute();
            SizeChanged += (s, e) => RenderScene();
            Loaded += (s, e) =>
            {
                RenderScene(); Status();
                CommitHistorySnapshot();   // 실행 취소의 기준점(시작 상태) (<260810_7>)
                if (_poseShotJson != null) { RunPoseShot(); return; }
                if (_svgExportJson != null) { RunSvgExport(); return; }
                if (_svgExportAllIn != null) { RunSvgExportAll(); return; }
                if (_capSvgOnLoad) RunCapSvgCheck();
                if (_ikTestOnLoad) RunIkTest();
                if (_saveTestOnLoad) RunSaveTest();
                if (_edgeCapOnLoad) RunEdgeCapTest();
                if (_undoTestOnLoad) RunUndoTest();
                if (_statusBarTestOnLoad) RunStatusBarTest();
                if (_dirTestOnLoad) RunDirTest();
                if (_dropTestOnLoad) RunDropTest();
                if (_saveResetTestOnLoad) RunSaveResetTest();
                if (_defaultsTestOnLoad) RunDefaultsTest();
                if (_pinCapTestOnLoad) RunPinCapTest();
                if (_kbToggleTestOnLoad) RunKbToggleTest();
                if (_vecTestOnLoad) RunVecTest();
                if (_helpTestOnLoad) { RunHelpTest(); return; }
                if (_shotOnLoad)
                    Dispatcher.BeginInvoke(new Action(() => SaveWindowSnapshot()),
                                           System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                if (_settingsShotOnLoad)
                    Dispatcher.BeginInvoke(new Action(SaveSettingsSnapshot),
                                           System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
        }

        // 개발 검증용 커맨드라인 스위치(-verify 등). 일반 실행(더블클릭)에는 영향 없음.
        private void ApplyDebugArgs()
        {
            string[] args = Environment.GetCommandLineArgs();
            // "-poseshot <상태json> <출력png>": 저장 파일을 로드해 렌더 스냅샷만 찍고 종료(버그 재현용)
            for (int i = 0; i < args.Length - 2; i++)
                if (args[i] == "-poseshot") { _poseShotJson = args[i + 1]; _poseShotPng = args[i + 2]; }

            // <260811_26>(2-1) "-svgexport <상태json> <왼손svg경로> <오른손svg경로>": 저장 파일을 로드해
            // 윤곽선만 모드로 양손을 각각 SVG로 저장하고 종료한다. "손 모양 json 추출"(json → 손 모델
            // 3D → svg → OpenTypingPlus 손가락 레이어 이식) 파이프라인의 1단계 산출물을 만드는 용도.
            for (int i = 0; i < args.Length - 3; i++)
                if (args[i] == "-svgexport")
                {
                    _svgExportJson = args[i + 1];
                    _svgExportLeftSvg = args[i + 2];
                    _svgExportRightSvg = args[i + 3];
                }

            // <260811_31> "-svgexportall <입력폴더> <출력폴더>": 폴더의 모든 상태 json 을 한 번의 실행으로
            // 훑어 각각 "<원본이름>-left.svg" / "-right.svg" 를 만든다(앱 시작 비용을 파일마다 치르지
            // 않으려는 것 — 67키 산출물을 다시 뽑을 때마다 쓴다). 결과 요약은 출력폴더\_export.log.
            for (int i = 0; i < args.Length - 2; i++)
                if (args[i] == "-svgexportall") { _svgExportAllIn = args[i + 1]; _svgExportAllOut = args[i + 2]; }

            foreach (string a in args)
            {
                switch (a)
                {
                    case "-verify": DebugSaveVerify = true; break;
                    case "-outlineonly":
                        _outlineShow = true; _outlineOnly = true; _outlineColor = Colors.Black;
                        OutlineShow.IsChecked = true; OutlineOnly.IsChecked = true;
                        break;
                    case "-alwaysmode":
                        _outlineOcclude = false; break;
                    case "-bend":
                        _scene.Pose.Set("R7.PIP", 10); _scene.Pose.Set("R7.DIP", 10); _scene.Pose.Set("R7.MCP.UD", 40);
                        break;
                    case "-capsvg": _capSvgOnLoad = true; break;
                    case "-kbtest":
                        KbDubeol.IsChecked = true;
                        Keyboard_Changed(null, null);
                        KbControl.PressedKey = (1, 3);          // [ㄱ]
                        KbControl.RightShiftPressed = true;      // + [Shift] → 파일명 [ㄲ]
                        break;
                    case "-shot": _shotOnLoad = true; break;
                    case "-settingsshot": _settingsShotOnLoad = true; break;   // <260811_21> 잠복 진단
                    case "-helptest": _helpTestOnLoad = true; break;           // <260811_32> 도움말 창
                    case "-labels":
                        _labelsShown = true;
                        _pins.Add("R7.DIP"); _pins.Add("L3.TIP");   // 오버레이 검증용 샘플 고정점
                        break;
                    case "-iktest": _ikTestOnLoad = true; break;
                    case "-savetest": _saveTestOnLoad = true; break;
                    case "-fist":
                        // 옆에서 본 주먹(너클 돌출 검증): 오른손 전체 굽힘 + 옆 시점
                        foreach (var d in new[] { "R7", "R8", "R9", "R0" })
                        {
                            _scene.Pose.Set(d + ".MCP.UD", 25);
                            _scene.Pose.Set(d + ".PIP", 15);
                            _scene.Pose.Set(d + ".DIP", 40);
                        }
                        _camera.YawDeg = 85; _camera.PitchDeg = 5; _camera.Distance = 55;
                        _camera.Target = new System.Windows.Media.Media3D.Point3D(12.5, 7, 0);
                        break;
                    case "-rottest":
                        _scene.RotLeft = new HandRotation { RollDeg = -35 };
                        _scene.RotRight = new HandRotation { RollDeg = 35 };
                        break;
                    case "-capleft":
                        _capTarget = CaptureTarget.Left;
                        CapLeft.IsChecked = true; CapBoth.IsChecked = false;
                        BitmapCamBtn.IsEnabled = false;
                        break;
                    case "-bgtest":
                        Loaded += (s2, e2) => LoadBgImage(@"D:\myclaude\claud6_typing_Csharp\학습용_손구조\1_내부구조\최대한펼친손_2.jpg");
                        break;
                    case "-edgecap": _edgeCapOnLoad = true; break;
                    case "-undotest": _undoTestOnLoad = true; break;
                    case "-statusbartest": _statusBarTestOnLoad = true; break;
                    case "-dirtest": _dirTestOnLoad = true; break;
                    case "-droptest": _dropTestOnLoad = true; break;
                    case "-savereset": _saveResetTestOnLoad = true; break;
                    case "-defaultstest": _defaultsTestOnLoad = true; break;   // <260811_21>
                    case "-pincaptest": _pinCapTestOnLoad = true; break;       // <260811_24>
                    case "-kbtoggle": _kbToggleTestOnLoad = true; break;
                    case "-vectest": _vecTestOnLoad = true; break;   // <260811_26> 벡터 캡처 777x260 변환 검증
                    case "-bendmore":
                        _scene.Pose.Set("R7.MCP.UD", 15); _scene.Pose.Set("R7.PIP", 30); _scene.Pose.Set("R7.DIP", 60);
                        _scene.Pose.Set("R8.MCP.UD", 20); _scene.Pose.Set("R8.PIP", 35); _scene.Pose.Set("R8.DIP", 60);
                        break;
                }
            }
        }

        private bool _shotOnLoad;
        private bool _settingsShotOnLoad;   // <260811_21> 잠복 진단(설정 창 '초기값' 영역 확인용)
        private bool _helpTestOnLoad;       // <260811_32> 도움말 창 자가 테스트
        private bool _defaultsTestOnLoad;   // <260811_21>
        private bool _pinCapTestOnLoad;     // <260811_24>
        private bool _ikTestOnLoad;
        private bool _edgeCapOnLoad;
        private string _poseShotJson, _poseShotPng;   // "-poseshot" 재현 캡처용
        private string _svgExportJson, _svgExportLeftSvg, _svgExportRightSvg;   // <260811_26> "-svgexport"
        private string _svgExportAllIn, _svgExportAllOut;                      // <260811_31> "-svgexportall"

        // 저장 상태를 로드해 스냅샷 한 장을 찍고 종료한다(헤드리스 버그 재현, <260810_2>).
        private void RunPoseShot()
        {
            try
            {
                SceneState.Load(_poseShotJson, _scene, _camera, this);
                SyncUiFromScene();   // <260811_6> 스냅샷에 UI(슬라이더·숫자)도 로드 상태 그대로 나오게
                _currentFile = _poseShotJson;   // <260811_20>(2) 스냅샷에 제목 표시줄도 로드 상태 그대로
                UpdateTitleBar();
                _scene.Recompute();
                RenderScene();
            }
            catch { }
            Dispatcher.BeginInvoke(new Action(() =>
            {
                SaveWindowSnapshot(_poseShotPng);
                DumpThumbDiag(_poseShotPng + ".txt");
                if (Environment.GetEnvironmentVariable("HM3D_TAGDUMP") != null)   // 잠복 진단(윤곽선 원인 확인용)
                    try { _renderer.DumpTagsPng(_poseShotPng + ".tags.png"); } catch { }
                if (Environment.GetEnvironmentVariable("HM3D_COLDUMP") != null)   // 잠복 진단(<260811_5> 충돌 임계 보정용)
                    try { System.IO.File.WriteAllText(_poseShotPng + ".col.txt", DumpCollisionRatios()); } catch { }
                if (Environment.GetEnvironmentVariable("HM3D_PINZONE") != null)   // 잠복 진단(<260811_24> '눌림 영역' 판정)
                    try { System.IO.File.WriteAllText(_poseShotPng + ".zone.txt", DumpPinZoneCheck()); } catch { }
                // <260811_29> 잠복 진단: 같은 렌더의 벡터 캡처를 **캔버스 좌표 그대로** 옆에 남긴다
                // (비트맵과 픽셀 단위로 겹쳐 보기 위한 것 — 변환·viewBox 확장 없이).
                if (Environment.GetEnvironmentVariable("HM3D_SVGCAP") != null)
                    try
                    {
                        Capture.SaveVector(_renderer, null, false, (int)_capTarget, _poseShotPng + ".svg", null, null, null,
                                           lean: true, strokeColor: _outlineColor, strokeWidthPx: _outlineThickness * DpiScale);
                    }
                    catch (Exception ex)
                    {
                        try { System.IO.File.AppendAllText(_poseShotPng + ".txt", "\nsvgcap ERR " + ex.Message); } catch { }
                    }
                if (Environment.GetEnvironmentVariable("HM3D_BMPCAP") != null)    // 잠복 진단(<260811_14> 비트맵 캡처 합성 확인용)
                {
                    string png0 = AppSettings.PngDir, svg0 = AppSettings.SvgDir, json0 = AppSettings.JsonDir;
                    try
                    {
                        AppSettings.UseSingleDir(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_poseShotPng)));
                        System.IO.File.AppendAllText(_poseShotPng + ".txt", "\nbmpcap=" + CaptureBitmapCore(false));
                        if (Environment.GetEnvironmentVariable("HM3D_BMPCAP") == "2")   // 값 2 = 확장 캡처도
                            System.IO.File.AppendAllText(_poseShotPng + ".txt", "\nbmpcap2=" + CaptureBitmapCore(true));
                    }
                    catch (Exception ex)
                    {
                        try { System.IO.File.AppendAllText(_poseShotPng + ".txt", "\nbmpcap ERR " + ex.Message); } catch { }
                    }
                    // 진단이 기본 캡처·저장 위치를 남겨 바꾸지 않게 원복
                    finally { AppSettings.PngDir = png0; AppSettings.SvgDir = svg0; AppSettings.JsonDir = json0; }
                }
                Application.Current.Shutdown();
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        // <260811_26>(2-1) "손 모양 json 추출": 저장 상태를 로드해 윤곽선만 모드로 렌더한 뒤 왼손·
        // 오른손을 각각 별개 SVG 파일로 저장하고 종료한다(OpenTypingPlus 손가락 레이어는 LeftHandPath/
        // RightHandPath 두 Path 요소를 따로 두므로, 손마다 분리된 SVG가 이식하기 알맞다). '벡터 내부
        // 채움'은 끔(fill=none) — FingerLayer 정의(CLAUDE.md)의 "내부 투명"과 맞춘다.
        // ⚠️ 캡처 직후 좌표는 렌더 캔버스의 물리 픽셀(예 2476x1341)이라 OTP 의 777x260 손가락 레이어
        // 좌표계와 다르다. <260811_19>에서 손 모델 3D 배경 키보드(KbControl)를 OTP KeyLayoutBox/KeyBox
        // 원본 치수(777x260, 키 50x50, 행 피치 52)와 **똑같이** 맞춰 뒀으므로, "화면에 그려진 KbControl
        // 이 지금 몇 물리픽셀인가"를 거꾸로 풀면 물리픽셀→777 단위 정합 변환을 바로 얻는다(추가 보정용
        // 기준점 없이도 정확 — KbControl 자체가 기준점이다). 이 변환을 Capture.SaveVector 에 그대로
        // 넘겨 처음부터 777 단위로 쓰게 한다('벡터 캡처' 버튼과 똑같은 경로).
        private void RunSvgExport()
        {
            try
            {
                SceneState.Load(_svgExportJson, _scene, _camera, this);
                SyncUiFromScene();
                _scene.Recompute();
                _outlineShow = true; _outlineOnly = true; _outlineColor = Colors.Black;
                OutlineShow.IsChecked = true; OutlineOnly.IsChecked = true;
                RenderScene();
            }
            catch (Exception ex)
            {
                try { System.IO.File.WriteAllText(_svgExportLeftSvg + ".err.txt", "LOAD EXCEPTION: " + ex); } catch { }
            }
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (_keyboard == KbKind.None)
                        throw new InvalidOperationException("배경 키보드가 꺼져 있어 정합 기준(KbControl)이 없다 — 상태 파일에 Keyboard 설정 필요");
                    Func<double, double, (double x, double y)> xform =
                        (x, y) => { var u = PhysicalPxToKeyboardUnits(new Point(x, y)); return (u.X, u.Y); };
                    double lw = KeyboardUi.KeyboardControl.LayoutW, lh = KeyboardUi.KeyboardControl.LayoutH;
                    Capture.SaveVector(_renderer, null, false, (int)CaptureTarget.Left, _svgExportLeftSvg, xform, lw, lh, lean: true);
                    Capture.SaveVector(_renderer, null, false, (int)CaptureTarget.Right, _svgExportRightSvg, xform, lw, lh, lean: true);
                }
                catch (Exception ex)
                {
                    try { System.IO.File.WriteAllText(_svgExportLeftSvg + ".err.txt", "EXCEPTION: " + ex); } catch { }
                }
                Application.Current.Shutdown();
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        // <260811_31> 폴더 일괄 추출 — 앱을 한 번만 띄워 모든 상태 json 을 SVG 로 뽑는다.
        // 각 파일마다: 로드 → UI 동기화 → 재계산 → 윤곽선만 렌더 → 양손 SVG 저장.
        // 배경 키보드가 꺼진 파일은 777x260 정합 기준이 없으므로 건너뛰고 로그에 남긴다.
        private void RunSvgExportAll()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var log = new System.Text.StringBuilder();
                int ok = 0, skip = 0;
                try
                {
                    System.IO.Directory.CreateDirectory(_svgExportAllOut);
                    _outlineShow = true; _outlineOnly = true;
                    OutlineShow.IsChecked = true; OutlineOnly.IsChecked = true;

                    var files = System.IO.Directory.GetFiles(_svgExportAllIn, "*.json");
                    Array.Sort(files, StringComparer.Ordinal);
                    foreach (string f in files)
                    {
                        string stem = System.IO.Path.GetFileNameWithoutExtension(f);
                        try
                        {
                            SceneState.Load(f, _scene, _camera, this);
                            SyncUiFromScene();
                            // 추출은 언제나 윤곽선만 모드로(상태 파일의 윤곽선 설정에 좌우되지 않게)
                            _outlineShow = true; _outlineOnly = true;
                            OutlineShow.IsChecked = true; OutlineOnly.IsChecked = true;
                            _scene.Recompute();
                            UpdateLayout();     // KbControl 확대율이 바뀌었을 수 있어 배치를 확정한 뒤 정합
                            RenderScene();

                            if (_keyboard == KbKind.None)
                            {
                                log.AppendLine($"SKIP {stem}: 배경 키보드 꺼짐(정합 기준 없음)");
                                skip++;
                                continue;
                            }
                            Func<double, double, (double x, double y)> xform =
                                (x, y) => { var u = PhysicalPxToKeyboardUnits(new Point(x, y)); return (u.X, u.Y); };
                            double lw = KeyboardUi.KeyboardControl.LayoutW, lh = KeyboardUi.KeyboardControl.LayoutH;
                            string lf = System.IO.Path.Combine(_svgExportAllOut, stem + "-left.svg");
                            string rf = System.IO.Path.Combine(_svgExportAllOut, stem + "-right.svg");
                            Capture.SaveVector(_renderer, null, false, (int)CaptureTarget.Left, lf, xform, lw, lh, lean: true);
                            Capture.SaveVector(_renderer, null, false, (int)CaptureTarget.Right, rf, xform, lw, lh, lean: true);
                            // 검증용: 각 손가락 끝 관절의 위치를 777x260 단위로 함께 남긴다(어느 키를
                            // 짚었는지 그림이 아니라 기하로 확인할 수 있게 — 아랫줄처럼 손끝이 위로
                            // 솟지 않는 포즈도 정확히 판정된다).
                            log.AppendLine($"OK   {stem}  kb={_keyboard} kbScale={KbScale.ScaleX:F2}  {TipsInKeyboardUnits()}");
                            ok++;
                        }
                        catch (Exception ex) { log.AppendLine($"ERR  {stem}: {ex.Message}"); skip++; }
                    }
                }
                catch (Exception ex) { log.AppendLine("FATAL " + ex); }
                log.AppendLine($"--- 성공 {ok} / 건너뜀·실패 {skip}");
                try { System.IO.File.WriteAllText(System.IO.Path.Combine(_svgExportAllOut, "_export.log"), log.ToString()); } catch { }
                Application.Current.Shutdown();
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        /// <summary><260811_31> 열 손가락 끝 관절을 777x260 키보드 단위로 — "L1=(x,y) …" 한 줄.</summary>
        private string TipsInKeyboardUnits()
        {
            var sb = new System.Text.StringBuilder();
            _camera.Update();
            foreach (var (side, res) in new[] { (HandSide.Left, _scene.Left), (HandSide.Right, _scene.Right) })
            {
                if (res == null) continue;
                foreach (var j in res.Joints)
                {
                    if (!j.IsTip) continue;
                    var p = _camera.Project(j.Pos);
                    var u = PhysicalPxToKeyboardUnits(new Point(p.X, p.Y));
                    sb.Append($"{Anatomy.FingerCode(side, j.Digit)}=({u.X:F1},{u.Y:F1}) ");
                }
            }
            return sb.ToString().Trim();
        }

        /// <summary>물리 픽셀(렌더 캔버스) 좌표 하나를 KbControl 의 777x260 로컬 단위로 변환한다.</summary>
        private Point PhysicalPxToKeyboardUnits(Point px)
        {
            double s = DpiScale;
            var dip = new Point(px.X / s, px.Y / s);                        // 물리px -> GraphicsHost DIP
            var kbLocal = GraphicsHost.TranslatePoint(dip, KbControl);      // GraphicsHost DIP -> KbControl 로컬 DIP
            double scale777 = KbControl.ActualWidth / KeyboardUi.KeyboardControl.LayoutW;
            return new Point(kbLocal.X / scale777, kbLocal.Y / scale777);   // KbControl 로컬 DIP -> 777 단위
        }

        // <260811_4-1-1> 손가락 뼈대 선분(월드, 회전·오프셋 반영) — 렌더러의 관통 교차 지대 태그
        // 정정("픽셀의 피부는 뼈축이 더 앞인 손가락의 것")에 쓴다. 손가락(엄지 포함)만, 손바닥 제외.
        private List<SoftRenderer.BoneSeg> BuildBoneSegs()
        {
            var list = new List<SoftRenderer.BoneSeg>();
            foreach (var (side, r) in new[] { (HandSide.Left, _scene.Left), (HandSide.Right, _scene.Right) })
            {
                if (r == null) continue;
                var byDigit = new Dictionary<Digit, List<JointNode>>();
                foreach (var j in r.Joints)
                {
                    if (!byDigit.TryGetValue(j.Digit, out var l)) { l = new List<JointNode>(); byDigit[j.Digit] = l; }
                    l.Add(j);
                }
                foreach (var kv in byDigit)
                {
                    kv.Value.Sort((a, b) => a.ChainIndex.CompareTo(b.ChainIndex));
                    int tag = HandScene.DigitTag(side, kv.Key);
                    for (int i = 0; i + 1 < kv.Value.Count; i++)
                        list.Add(new SoftRenderer.BoneSeg { Tag = tag, A = kv.Value[i].Pos, B = kv.Value[i + 1].Pos });
                }
            }
            return list;
        }

        // -poseshot 진단: 양손 엄지 CMC/MCP·검지 MCP 좌표(무회전 기준)를 덤프(웹 지오메트리 디버그용)
        private void DumpThumbDiag(string path)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                foreach (var side in new[] { HandSide.Left, HandSide.Right })
                {
                    // qksqhr(2026-08-11): 메시 빌더가 받는 무회전 좌표와 일치하도록 offset·widthScale 전달
                    var r0 = HandKinematics.Build(side, _scene.Pose, _scene.OverallScale, _scene.FingerLen,
                                                  offset: _scene.OffsetFor(side), widthScale: _scene.WidthScale);
                    System.Windows.Media.Media3D.Point3D tc = default, tm = default, ik = default, pk = default;
                    foreach (var j in r0.Joints)
                    {
                        if (j.IsTip) continue;
                        if (j.Digit == Digit.Thumb && j.Kind == JointKind.CMC) tc = j.Pos;
                        if (j.Digit == Digit.Thumb && j.Kind == JointKind.MCP) tm = j.Pos;
                        if (j.Digit == Digit.Index && j.Kind == JointKind.MCP) ik = j.Pos;
                        if (j.Digit == Digit.Pinky && j.Kind == JointKind.MCP) pk = j.Pos;
                    }
                    double s = r0.Scale;
                    double bend01 = Math.Min(1, Math.Max(0, tc.Z - tm.Z) / (2.2 * s));
                    sb.AppendLine($"{side}: tc=({tc.X:F2},{tc.Y:F2},{tc.Z:F2}) tm=({tm.X:F2},{tm.Y:F2},{tm.Z:F2}) idxK=({ik.X:F2},{ik.Y:F2},{ik.Z:F2}) pkyK=({pk.X:F2},{pk.Y:F2},{pk.Z:F2}) s={s:F2} bend01={bend01:F2}");
                }
                System.IO.File.WriteAllText(path, sb.ToString());
            }
            catch { }
        }

        // (3-12-2) 확장 비트맵 캡처 헤드리스 검증: 손목쪽을 화면 밖으로 팬한 뒤 일반/확장 캡처 비교
        private void RunEdgeCapTest()
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                AppSettings.UseSingleDir(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
                // qksqhr(2026-09-13): 부호 오류 수정 — PanY 는 화면 좌표(Y 아래로 증가)에 그대로
                // 더해지므로 음수를 주면 손이 위로 밀려 오히려 기본 프레이밍에서 이미 잘려 있던
                // 손목쪽이 화면 안으로 들어와 버렸다(touchesEdge 가 항상 False 로 나와 이 검증이
                // 실제로는 아무것도 테스트하지 못하고 있었음 — 실행·캡처로 확인). 손목쪽을 아래
                // 밖으로 더 밀어내려면 양수를 줘야 한다.
                _camera.PanY = GraphicsHost.ActualHeight * DpiScale * 0.30;   // 손을 아래로 밀어 손목쪽이 아래 밖으로
                RenderScene();
                sb.AppendLine($"touchesEdge={HandTouchesEdge()}");
                string p1 = CaptureBitmapCore(false);
                string p2 = CaptureBitmapCore(true);
                sb.AppendLine($"normal={p1}");
                sb.AppendLine($"expanded={p2}");
            }
            catch (Exception ex) { sb.AppendLine("ERR " + ex.Message); }
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "3dm_qa.txt"), sb.ToString());
            }
            catch { }
        }
        private bool _saveTestOnLoad;
        private bool _undoTestOnLoad;
        private bool _statusBarTestOnLoad;
        private bool _dirTestOnLoad;
        private bool _dropTestOnLoad;
        private bool _saveResetTestOnLoad;
        private bool _kbToggleTestOnLoad;
        private bool _vecTestOnLoad;   // <260811_26>

        // <260811_14-1> 상태 표시줄이 두꺼워져도 배경 키보드·배경 이미지·3D 손이 제자리인지 검증
        // (-statusbartest). 캡처 문구 같은 추가 줄이 그래픽 영역을 밀어 올리던 문제의 회귀 테스트.
        private void RunStatusBarTest()
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                AppSettings.UseSingleDir(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
                KbDubeol.IsChecked = true; Keyboard_Changed(null, null);
                UpdateLayout();
                RenderScene();
                var a = MeasureStatusInvariants();
                SaveWindowSnapshot("3dm_status_before.png");
                sb.AppendLine("cap1=" + CaptureBitmapCore(false));

                for (int i = 0; i < 3; i++) AddStatus($"상태 표시줄 두께 시험 문구 {i + 1}", false);
                UpdateLayout();   // 상태 표시줄 높이 변경 → 보정 반영
                UpdateLayout();   // 보정으로 바뀐 여백의 배치까지 확정
                RenderScene();
                var b = MeasureStatusInvariants();
                SaveWindowSnapshot("3dm_status_after.png");
                sb.AppendLine("cap2=" + CaptureBitmapCore(false));

                bool grew = b.statusH > a.statusH + 0.5;   // 시험 전제: 상태 표시줄이 실제로 두꺼워졌는가
                bool ok = grew
                          && Math.Abs(a.kbX - b.kbX) < 0.5 && Math.Abs(a.kbY - b.kbY) < 0.5
                          && a.bufW == b.bufW && a.bufH == b.bufH
                          && a.left == b.left && a.top == b.top && a.right == b.right && a.bottom == b.bottom;
                sb.AppendLine($"before: statusH={a.statusH:F1} kb=({a.kbX:F1},{a.kbY:F1}) buf={a.bufW}x{a.bufH} hand=[{a.left},{a.top}]-[{a.right},{a.bottom}]");
                sb.AppendLine($"after : statusH={b.statusH:F1} kb=({b.kbX:F1},{b.kbY:F1}) buf={b.bufW}x{b.bufH} hand=[{b.left},{b.top}]-[{b.right},{b.bottom}]");
                sb.AppendLine($"grew={grew}");
                sb.AppendLine("STATUSBAR: " + (ok ? "PASS" : "FAIL"));
            }
            catch (Exception ex) { sb.AppendLine("ERR " + ex); }
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "3dm_qa.txt"), sb.ToString());
            }
            catch { }
        }

        // <260811_19-1> 모든 키가 [Ctrl]+클릭으로 눌린 키가 되는지 검증 (-kbtoggle).
        private void RunKbToggleTest()
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                KbDubeol.IsChecked = true; Keyboard_Changed(null, null);
                UpdateLayout();

                var centers = KbControl.KeyCenters();
                int n = centers.Count, ok = 0, shifts = 0;
                var pressedSeen = new HashSet<(int, int)>();
                foreach (var c in centers)
                {
                    KbControl.PressedKey = null;
                    KbControl.LeftShiftPressed = KbControl.RightShiftPressed = false;
                    if (!KbControl.ToggleAt(c)) continue;
                    if (KbControl.PressedKey.HasValue) { ok++; pressedSeen.Add(KbControl.PressedKey.Value); }
                    else if (KbControl.LeftShiftPressed || KbControl.RightShiftPressed) { ok++; shifts++; }
                }

                // 특수키(Space)도 눌리고, 다른 키를 누르면 1개 규칙대로 교체되는지
                KbControl.PressedKey = null;
                KbControl.ToggleAt(centers[centers.Count - 4]);        // 아래 행 Space
                var afterSpace = KbControl.PressedKey;
                string spaceLabel = KbControl.GetCaptureKey()?.label;
                string spaceRowCol = KbControl.GetCaptureKey()?.rowcol;
                KbControl.ToggleAt(centers[0]);                        // 0행 첫 키
                var afterFirst = KbControl.PressedKey;

                bool allToggled = ok == n;
                bool uniquePos = pressedSeen.Count + shifts == n;      // 위치가 겹치지 않는가
                bool spaceOk = afterSpace.HasValue && spaceLabel == "Space" && spaceRowCol == null;
                bool single = afterFirst.HasValue && afterFirst.Value == (0, 0);

                sb.AppendLine($"keys={n} toggled={ok} (shift {shifts}) 고유위치={pressedSeen.Count + shifts}");
                sb.AppendLine($"space=({afterSpace?.row},{afterSpace?.col}) label={spaceLabel} rowcol={spaceRowCol ?? "(null)"}");
                sb.AppendLine($"after-first=({afterFirst?.row},{afterFirst?.col}) 단일선택={single}");
                sb.AppendLine("KBTOGGLE: " + (allToggled && uniquePos && spaceOk && single ? "PASS" : "FAIL"));
            }
            catch (Exception ex) { sb.AppendLine("ERR " + ex); }
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "3dm_qa.txt"), sb.ToString());
            }
            catch { }
        }

        // <260811_26> 벡터 캡처(SVG) 버튼(VectorCapture_Click 실제 경로)이 배경 키보드 켬/끔에 따라
        // 올바르게 동작하는지 검증(-vectest). 켬 = KbControl 정합 기준 777x260 단위 출력, 끔 = 종전처럼
        // 캔버스 물리 픽셀 그대로 + 경고 문구.
        private void RunVecTest()
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                AppSettings.UseSingleDir(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
                AppSettings.SvgManualName = false;   // 대화상자 없이 자동 이름으로

                string GetSavedPath()
                {
                    foreach (var it in StatusList.Items)
                        if (it is TextBlock tb && tb.Text.StartsWith("벡터(SVG) 캡처 저장: "))
                            return tb.Text.Substring("벡터(SVG) 캡처 저장: ".Length);
                    return null;
                }

                // (1) 배경 키보드 켬 → KbControl 정합 기준 777x260 단위로 나와야 한다
                KbDubeol.IsChecked = true; Keyboard_Changed(null, null);
                UpdateLayout();
                RenderScene();
                VectorCapture_Click(null, null);
                string pathOn = GetSavedPath();
                string svgOn = pathOn != null ? System.IO.File.ReadAllText(pathOn) : null;

                // 좌표는 777 단위 정합(키 크기 50 기준)이어야 하고, viewBox 는 키보드 상자와 그림
                // 범위의 합집합이라 **그림이 하나도 잘리지 않아야** 한다 (<260811_26> 잘림 수정).
                var (vx, vy, vw, vh) = ParseViewBox(svgOn);
                var bounds = SvgPathBounds(svgOn);
                bool unitsOk = bounds.HasValue && bounds.Value.maxX <= 900 && bounds.Value.minX >= -150;
                bool noClip = bounds.HasValue
                              && bounds.Value.minX >= vx - 0.01 && bounds.Value.minY >= vy - 0.01
                              && bounds.Value.maxX <= vx + vw + 0.01 && bounds.Value.maxY <= vy + vh + 0.01;
                bool coversKb = vx <= 0.01 && vy <= 0.01 && vx + vw >= 776.99 && vy + vh >= 259.99;
                sb.AppendLine($"켬: path={pathOn}");
                sb.AppendLine($"켬: viewBox=({vx:F1},{vy:F1},{vw:F1},{vh:F1})  그림범위=({bounds?.minX:F1}~{bounds?.maxX:F1}, {bounds?.minY:F1}~{bounds?.maxY:F1})");
                sb.AppendLine($"켬: 777단위 정합={unitsOk} 잘림없음={noClip} 키보드상자포함={coversKb}");

                // (2) 배경 키보드 끔 → 종전처럼 캔버스 물리 픽셀 그대로 + 경고 문구
                KbNone.IsChecked = true; Keyboard_Changed(null, null);
                UpdateLayout();
                RenderScene();
                _graphicsMsg = null;
                int w = _renderer.RW, h = _renderer.RH;
                VectorCapture_Click(null, null);
                string pathOff = GetSavedPath();
                string svgOff = pathOff != null ? System.IO.File.ReadAllText(pathOff) : null;
                bool vboxOff = svgOff != null && svgOff.Contains($"width=\"{w}\"") && svgOff.Contains($"height=\"{h}\"")
                               && svgOff.Contains($"viewBox=\"0 0 {w} {h}\"");
                var boundsOff = SvgPathBounds(svgOff);
                bool noClipOff = boundsOff.HasValue
                                 && boundsOff.Value.minX >= -0.01 && boundsOff.Value.minY >= -0.01
                                 && boundsOff.Value.maxX <= w + 0.01 && boundsOff.Value.maxY <= h + 0.01;
                sb.AppendLine($"끔: path={pathOff}");
                sb.AppendLine($"끔: viewBox 캔버스({w}x{h})={vboxOff} 잘림없음={noClipOff}");
                sb.AppendLine($"끔: 경고 문구=\"{_graphicsMsg}\"");

                bool warnOk = _graphicsMsg != null && _graphicsMsg.Contains("777×260");
                bool ok = unitsOk && noClip && coversKb && vboxOff && noClipOff && warnOk;
                sb.AppendLine("VECTEST: " + (ok ? "PASS" : "FAIL"));
            }
            catch (Exception ex) { sb.AppendLine("ERR " + ex); }
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "3dm_qa.txt"), sb.ToString());
            }
            catch { }
        }

        /// <summary>-vectest 보조: SVG 헤더의 viewBox 4값.</summary>
        private static (double x, double y, double w, double h) ParseViewBox(string svg)
        {
            var m = System.Text.RegularExpressions.Regex.Match(svg ?? "", "viewBox=\"([^\"]*)\"");
            if (!m.Success) return (0, 0, 0, 0);
            string[] v = m.Groups[1].Value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (v.Length != 4) return (0, 0, 0, 0);
            double P(int i) => double.Parse(v[i], System.Globalization.CultureInfo.InvariantCulture);
            return (P(0), P(1), P(2), P(3));
        }

        /// <summary>-vectest 보조: SVG 안 모든 &lt;path d&gt; 점들의 좌표 범위(잘림 판정용).</summary>
        private static (double minX, double minY, double maxX, double maxY)? SvgPathBounds(string svg)
        {
            if (svg == null) return null;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            bool any = false;
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(svg, "<path d=\"([^\"]*)\""))
            {
                string[] t = m.Groups[1].Value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < t.Length; i++)
                {
                    if (t[i] != "M" && t[i] != "L") continue;
                    double x = double.Parse(t[i + 1], System.Globalization.CultureInfo.InvariantCulture);
                    double y = double.Parse(t[i + 2], System.Globalization.CultureInfo.InvariantCulture);
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                    any = true; i += 2;
                }
            }
            return any ? ((double, double, double, double)?)(minX, minY, maxX, maxY) : null;
        }

        // <260811_18> 저장 '달라진 것 없음' 판정과 관절 '초기화' 검증 (-savereset).
        // (저장 대화상자 자체는 헤드리스로 못 띄우므로, 창을 띄울지 정하는 판정 함수를 검사한다.)
        private void RunSaveResetTest()
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "3dm_saveresettest");
                System.IO.Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, "state.json");

                // (1) 저장 직후 = 달라진 것 없음
                _scene.Pose.Set("L2.PIP", 44);
                _scene.Recompute();
                SceneState.Save(path, _scene, _camera, this);
                _currentFile = path;
                bool ok1 = StateMatchesCurrentFile();

                // 상태를 바꾸면 달라짐
                _scene.Pose.Set("L2.PIP", 45);
                _scene.Recompute();
                bool ok2 = !StateMatchesCurrentFile();

                // 되돌리면 다시 같음 / 기준 파일이 없으면 항상 '달라짐'(= 저장 창을 연다)
                _scene.Pose.Set("L2.PIP", 44);
                _scene.Recompute();
                bool ok3 = StateMatchesCurrentFile();
                _currentFile = null;
                bool ok4 = !StateMatchesCurrentFile();
                _currentFile = path;

                // (2) 관절 초기화 — 그 손만 초기값(100)으로, 반대 손은 그대로
                _scene.Pose.Set("L1.DIP", 12); _scene.Pose.Set("L5.CMC.LR", 20); _scene.Pose.Set("L.ARCH", 33);
                _scene.Pose.Set("R7.PIP", 21); _scene.Pose.Set("R.ARCH", 55);
                _scene.Recompute();
                ResetJoints(HandSide.Left);

                bool leftAllDefault = true, rightKept =
                    Math.Abs(_scene.Pose.Get("R7.PIP") - 21) < 1e-9 && Math.Abs(_scene.Pose.Get("R.ARCH") - 55) < 1e-9;
                var def = HandPose.Default();
                foreach (var id in HandPose.AllSliderIds())
                {
                    if (id.Side != HandSide.Left) continue;
                    if (Math.Abs(_scene.Pose.Get(id.Key) - def.Get(id.Key)) > 1e-9) { leftAllDefault = false; break; }
                }
                // 슬라이더 표시도 따라왔는지(모델만 바뀌고 UI 가 남는 <260811_11> 류 사고 방지)
                var ls = _leftJoints.Get("L1.DIP");
                bool uiSynced = ls != null && Math.Abs(ls.Value - 100) < 1e-6;

                bool ok5 = leftAllDefault && rightKept && uiSynced;
                ResetJoints(HandSide.Right);
                bool ok6 = Math.Abs(_scene.Pose.Get("R7.PIP") - 100) < 1e-9 && Math.Abs(_scene.Pose.Get("R.ARCH") - 100) < 1e-9;

                sb.AppendLine($"same-after-save={ok1} changed={ok2} same-again={ok3} nofile-always-dialog={ok4}");
                sb.AppendLine($"left-reset={leftAllDefault} right-kept={rightKept} ui-synced={uiSynced} right-reset={ok6}");
                sb.AppendLine("SAVERESET: " + (ok1 && ok2 && ok3 && ok4 && ok5 && ok6 ? "PASS" : "FAIL"));
            }
            catch (Exception ex) { sb.AppendLine("ERR " + ex); }
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "3dm_qa.txt"), sb.ToString());
            }
            catch { }
        }

        // <260811_21> '초기값' 저장·불러오기 왕복 + 판별자·손상 파일 클램프 검증 (-defaultstest).
        private void RunDefaultsTest()
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "3dm_defaultstest");
                System.IO.Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, "초기값_test.json");
                string handPath = System.IO.Path.Combine(dir, "hand.json");

                // 특징적인 값으로 저장
                AppSettings.DefOverallScale = 1.11; AppSettings.DefWidthScale = 0.87;
                AppSettings.DefWebbingHeight = 42; AppSettings.DefOffsetLeftX = 7.5; AppSettings.DefOffsetLeftY = -3.1;
                AppSettings.DefOffsetRightX = -6.6; AppSettings.DefOffsetRightY = 2.2; AppSettings.DefThumbCmcLR = 37;
                DefaultValues.Save(path);

                bool ok1 = DefaultValues.IsDefaultsFile(path);

                // 흐트러뜨린 뒤 다시 불러와 복원되는지
                AppSettings.DefOverallScale = 1; AppSettings.DefWidthScale = 1;
                AppSettings.DefWebbingHeight = 0; AppSettings.DefOffsetLeftX = 0; AppSettings.DefOffsetLeftY = 0;
                AppSettings.DefOffsetRightX = 0; AppSettings.DefOffsetRightY = 0; AppSettings.DefThumbCmcLR = 0;
                bool loaded = DefaultValues.Load(path);
                bool ok2 = loaded
                    && Math.Abs(AppSettings.DefOverallScale - 1.11) < 1e-9 && Math.Abs(AppSettings.DefWidthScale - 0.87) < 1e-9
                    && Math.Abs(AppSettings.DefWebbingHeight - 42) < 1e-9 && Math.Abs(AppSettings.DefOffsetLeftX - 7.5) < 1e-9
                    && Math.Abs(AppSettings.DefOffsetLeftY - (-3.1)) < 1e-9 && Math.Abs(AppSettings.DefOffsetRightX - (-6.6)) < 1e-9
                    && Math.Abs(AppSettings.DefOffsetRightY - 2.2) < 1e-9 && Math.Abs(AppSettings.DefThumbCmcLR - 37) < 1e-9;

                // 손 모양 파일(good hand state)은 초기값 파일로 판별되지 않아야 함 — 역방향 판별자
                _scene.Recompute();
                SceneState.Save(handPath, _scene, _camera, this);
                bool ok3 = !DefaultValues.IsDefaultsFile(handPath) && SceneState.IsStateFile(handPath);

                // 손상 파일(범위 밖 값) — qksqhr 클램프. 표준 json은 NaN 토큰을 못 담으므로(파싱 자체가
                // 실패해 Deserialize 이전에 예외) 범위 밖 유한값으로 시험한다 — 실제 손상 파일의 형태.
                string badPath = System.IO.Path.Combine(dir, "bad.json");
                System.IO.File.WriteAllText(badPath,
                    "{\"IsDefaultsFile\":true,\"OverallScale\":1e300,\"WidthScale\":99,\"WebbingHeight\":-500,\"OffsetLeftX\":9999,\"OffsetLeftY\":-9999,\"OffsetRightX\":0,\"OffsetRightY\":0,\"ThumbCmcLR\":1e9}");
                bool loadedBad = DefaultValues.Load(badPath);
                bool ok4 = loadedBad
                    && Math.Abs(AppSettings.DefOverallScale - 1.8) < 1e-9    // 1e300 → 상한 1.8
                    && Math.Abs(AppSettings.DefWidthScale - 1.3) < 1e-9      // 99 → 상한 1.3
                    && Math.Abs(AppSettings.DefWebbingHeight - 0) < 1e-9     // -500 → 하한 0
                    && Math.Abs(AppSettings.DefOffsetLeftX - 30) < 1e-9      // 9999 → 상한 30
                    && Math.Abs(AppSettings.DefOffsetLeftY - (-30)) < 1e-9   // -9999 → 하한 -30
                    && Math.Abs(AppSettings.DefThumbCmcLR - 100) < 1e-9;     // 1e9 → 상한 100

                // <260811_21> 두 엄지 좌우 CMC 초기값이 실제 시작 포즈에 들어갔는지 + 그 값에서
                // 두 손이 충돌하지 않는지(초기값 충돌 문제의 회귀 방지)
                var def = HandPose.Default();
                // 모델(포즈)뿐 아니라 좌·우 패널 슬라이더 표시도 50 인지 함께 본다
                // (<260811_11> 류 '모델만 바뀌고 UI 는 남는' 사고 방지).
                // 저장해 둔 초기값 파일이 있든 없든 결과가 같도록, 소스 기본값을 명시로 세운 뒤 적용한다.
                AppSettings.DefOverallScale = 0.95; AppSettings.DefWidthScale = 1.3;
                AppSettings.DefWebbingHeight = 0; AppSettings.DefOffsetLeftX = 1; AppSettings.DefOffsetLeftY = -4.9;
                AppSettings.DefOffsetRightX = -5.5; AppSettings.DefOffsetRightY = -4.9;
                AppSettings.DefThumbCmcLR = HandPose.ThumbCmcLRDefault;
                ResetJoints(HandSide.Left);
                ResetJoints(HandSide.Right);
                ApplyDefaultsToScene();

                var lc = _leftJoints.Get("L5.CMC.LR");
                var rc = _rightJoints.Get("R6.CMC.LR");
                bool ok5 = Math.Abs(def.Get("L5.CMC.LR") - 50) < 1e-9 && Math.Abs(def.Get("R6.CMC.LR") - 50) < 1e-9
                           && Math.Abs(def.Get("L5.CMC.UD") - 100) < 1e-9   // 다른 엄지 축은 100 유지
                           && Math.Abs(_scene.Pose.Get("L5.CMC.LR") - 50) < 1e-9
                           && lc != null && Math.Abs(lc.Value - 50) < 1e-6
                           && rc != null && Math.Abs(rc.Value - 50) < 1e-6;

                // 시작 상태(크기 95%·좌우 130%·손 위치·엄지 CMC 50)에서 충돌이 없어야 한다.
                // ⚠️ 개수는 반드시 지금 지역변수로 떠 둔다 — 아래 ApplyDefaultsToScene 이
                // RenderScene 을 거치며 _fingerColPairs/_handColPairs 를 다시 채우므로, 보고 줄에서
                // 그 목록을 읽으면 '판정은 통과인데 개수는 0이 아닌' 거짓 진단이 나온다(실제로 겪음).
                DetectFingerCollision(_fingerColPairs);
                DetectHandsCollision(_handColPairs);
                int startFingerCol = _fingerColPairs.Count, startHandsCol = _handColPairs.Count;
                bool ok6 = startFingerCol == 0 && startHandsCol == 0;

                // <260811_21 추가지시> '즉시 반영' 경로: Def* 를 바꿔 ApplyDefaultsToScene 을 부르면
                // 모델과 UI(상단 패널·손 위치·관절 패널)가 모두 그 값으로 따라와야 한다.
                AppSettings.DefOverallScale = 1.4; AppSettings.DefWidthScale = 0.8;
                AppSettings.DefWebbingHeight = 150; AppSettings.DefOffsetLeftX = 6; AppSettings.DefOffsetLeftY = 3;
                AppSettings.DefOffsetRightX = -7; AppSettings.DefOffsetRightY = -2; AppSettings.DefThumbCmcLR = 80;
                ApplyDefaultsToScene();
                bool ok7 =
                    Math.Abs(_scene.OverallScale - 1.4) < 1e-6 && Math.Abs(SizeSlider.Value - 1.4) < 1e-6
                    && Math.Abs(_scene.WidthScale - 0.8) < 1e-6 && Math.Abs(WidthSlider.Value - 0.8) < 1e-6
                    && Math.Abs(_scene.WebbingHeight - 150) < 1e-6 && Math.Abs(_webbingSlider.Value - 150) < 1e-6
                    && Math.Abs(_scene.OffsetLeft.X - 6) < 1e-6 && Math.Abs(_handPosSliders["1X"].Value - 6) < 1e-6
                    && Math.Abs(_scene.OffsetRight.Y - (-2)) < 1e-6 && Math.Abs(_handPosSliders["2Y"].Value - (-2)) < 1e-6
                    && Math.Abs(_scene.Pose.Get("L5.CMC.LR") - 80) < 1e-6
                    && Math.Abs(_leftJoints.Get("L5.CMC.LR").Value - 80) < 1e-6;

                // 범위 밖 Def* 는 ApplyDefaultsToScene 이 클램프해야 한다(직접 입력·손상 파일 방어)
                AppSettings.DefOverallScale = 99; AppSettings.DefThumbCmcLR = -50;
                ApplyDefaultsToScene();
                bool ok8 = Math.Abs(_scene.OverallScale - 1.8) < 1e-6
                           && Math.Abs(_scene.Pose.Get("L5.CMC.LR") - 0) < 1e-6;

                sb.AppendLine($"isDefaultsFile={ok1}");
                sb.AppendLine($"roundtrip={ok2}");
                sb.AppendLine($"handFileNotDefaults={ok3}");
                sb.AppendLine($"corruptClamp={ok4}");
                sb.AppendLine($"startPoseThumbCmc50={ok5}");
                sb.AppendLine($"startPoseNoCollision={ok6} finger={startFingerCol} hands={startHandsCol}");
                sb.AppendLine($"applyImmediate={ok7}");
                sb.AppendLine($"applyClamp={ok8}");
                sb.AppendLine("DEFAULTS: " + (ok1 && ok2 && ok3 && ok4 && ok5 && ok6 && ok7 && ok8 ? "PASS" : "FAIL"));
            }
            catch (Exception ex) { sb.AppendLine("ERR " + ex); }
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "3dm_qa.txt"), sb.ToString());
            }
            catch { }
        }

        /// <summary>
        /// <260811_24 측정> '눌림 영역' 판정 덤프. 좌표는 모두 물리 픽셀(카메라 투영과 같은 계).
        /// '눌림 영역' = 키 중심 기준, 반축 a=키가로/4, b=키세로/4 타원. 판정식 (dx/a)²+(dy/b)² ≤ 1.
        /// [Shift]만 별도 정의(장축 2/3·단축 1/2) → a=키가로/3, b=키세로/4.
        /// 두 모드가 있다:
        /// · 목표 키 모드 — 노란 눌린 키(PressedKey·Shift)가 있으면, 그 키를 (3) 배정표의 담당 손가락
        ///   끝(TIP)이 누르고 있는지 판정한다(고정점 활성 여부는 pinActive 로만 표시 — 작업 중엔
        ///   고정점을 꺼 둘 수 있으므로 판정은 TIP 위치로 한다. 고정점은 TIP 투영 위치에 그려지는
        ///   표식이라 두 값은 같은 점이다).
        /// · 기본자세 모드 — 눌린 키가 없으면 활성 고정점을 <260811_16-1> 기본자세 담당 키(엄지는
        ///   Space)로 판정한다.
        /// 끝에 충돌 상태(손가락끼리/두 손)를 함께 덤프한다 — (4-2)의 최소 결과 판정에 필요.
        /// </summary>
        private string DumpPinZoneCheck()
        {
            var sb = new System.Text.StringBuilder();
            double s = DpiScale;
            _camera.Update();

            // 키 사각형(컨트롤 로컬) → 물리 픽셀 중심·반축으로 변환해 TIP 투영과 비교
            void Judge(string pinId, string keyName, Rect local, double aDiv, ref int pass, ref int total)
            {
                var j = FindJoint(pinId);
                if (j == null) { sb.AppendLine($"{pinId}\t{keyName}\t(관절 없음)"); return; }
                var tl = KbControl.TranslatePoint(local.TopLeft, GraphicsHost);
                var br = KbControl.TranslatePoint(local.BottomRight, GraphicsHost);
                double cx = (tl.X + br.X) / 2 * s, cy = (tl.Y + br.Y) / 2 * s;
                double a = Math.Abs(br.X - tl.X) * s / aDiv, b = Math.Abs(br.Y - tl.Y) * s / 4;
                var p = _camera.Project(j.Pos);
                double dx = p.X - cx, dy = p.Y - cy;
                double ratio = (dx / a) * (dx / a) + (dy / b) * (dy / b);
                bool ok = ratio <= 1.0;
                if (ok) pass++;
                total++;
                bool pinActive = _pins.Contains(pinId);
                sb.AppendLine($"{pinId}\t{keyName}\t{dx:F1}\t{dy:F1}\t{a:F1}\t{b:F1}\t{ratio:F2}\t{ok}\tpin={pinActive}");
            }

            int pass0 = 0, total0 = 0;
            bool targetMode = KbControl.PressedKey.HasValue || KbControl.LeftShiftPressed || KbControl.RightShiftPressed;
            sb.AppendLine("pin\tkey\tdx\tdy\ta\tb\tratio\tinZone\tpinActive");

            if (targetMode)
            {
                // (3) 배정표: 마스터 표 (row, col) → 담당 손가락. 표준 두벌식 운지 그대로.
                var rows = new[]
                {
                    new[] { "L1", "L1", "L2", "L3", "L4", "L4", "R7", "R7", "R8", "R9", "R0", "R0", "R0" },       // 0행 `~1…=
                    new[] { "L1", "L2", "L3", "L4", "L4", "R7", "R7", "R8", "R9", "R0", "R0", "R0", "R0" },       // 1행 ㅂ…⧵
                    new[] { "L1", "L2", "L3", "L4", "L4", "R7", "R7", "R8", "R9", "R0", "R0" },                   // 2행 ㅁ…'
                    new[] { "L1", "L2", "L3", "L4", "L4", "R7", "R7", "R8", "R9", "R0" },                         // 3행 ㅋ…/
                };
                if (KbControl.PressedKey.HasValue)
                {
                    var (r, c) = KbControl.PressedKey.Value;
                    if (r >= 0 && r < rows.Length && c >= 0 && c < rows[r].Length)
                    {
                        var mk = KeyboardUi.KeyMaster.Find(r, c);
                        bool shifted = KbControl.LeftShiftPressed || KbControl.RightShiftPressed;
                        string keyName = mk == null ? $"({r},{c})"
                            : (shifted ? mk.ShiftFor(true) : mk.DubeolBase);
                        if (KbControl.TryGetKeyRect(r, c, null, out var local))
                            Judge(rows[r][c] + ".TIP", keyName, local, 4, ref pass0, ref total0);
                        else sb.AppendLine($"({r},{c})\t(키 못 찾음)");
                    }
                    else sb.AppendLine($"({r},{c})\t(배정표 밖 — 특수키)");
                }
                // [Shift]는 반대 손 소지가 담당. 별도 타원(장축 2/3 → a=가로/3).
                if (KbControl.LeftShiftPressed && KbControl.TryGetShiftRect(false, out var lsr))
                    Judge("L1.TIP", "왼쪽Shift", lsr, 3, ref pass0, ref total0);
                if (KbControl.RightShiftPressed && KbControl.TryGetShiftRect(true, out var rsr))
                    Judge("R0.TIP", "오른쪽Shift", rsr, 3, ref pass0, ref total0);
            }
            else
            {
                // 기본자세 모드: 활성 고정점을 <260811_16-1> 담당 키로 판정
                var target = new Dictionary<string, string>
                {
                    { "L1", "ㅁ" }, { "L2", "ㄴ" }, { "L3", "ㅇ" }, { "L4", "ㄹ" }, { "L5", "Space" },
                    { "R6", "Space" }, { "R7", "ㅓ" }, { "R8", "ㅏ" }, { "R9", "ㅣ" }, { "R0", ";" },
                };
                var ids = new List<string>(_pins);
                ids.Sort(StringComparer.Ordinal);
                foreach (string id in ids)
                {
                    string code = id.Split('.')[0];
                    if (!target.TryGetValue(code, out string label)) continue;
                    Rect local;
                    bool got = label == "Space"
                        ? KbControl.TryGetKeyRect(0, 0, "Space", out local)
                        : TryGetMasterKeyRect(label, out local);
                    if (!got) { sb.AppendLine($"{id}\t{label}\t(키 못 찾음)"); continue; }
                    Judge(id, label, local, 4, ref pass0, ref total0);
                }
            }

            sb.AppendLine($"PASS {pass0}/{total0}");
            // 충돌 상태 — RenderScene(포즈샷 직전 실행)이 채운 목록 그대로
            sb.AppendLine($"collision fingers={_fingerColPairs.Count} hands={_handColPairs.Count}");
            foreach (string p in _fingerColPairs) sb.AppendLine($"colF\t{p}");
            foreach (string p in _handColPairs) sb.AppendLine($"colH\t{p}");
            return sb.ToString();
        }

        /// <summary>두벌식 아랫글쇠 라벨로 마스터 표 키를 찾아 컨트롤 로컬 사각형을 얻는다.</summary>
        private bool TryGetMasterKeyRect(string dubeolBase, out Rect rect)
        {
            foreach (var mk in KeyboardUi.KeyMaster.Keys)
                if (mk.DubeolBase == dubeolBase)
                    return KbControl.TryGetKeyRect(mk.Row, mk.Col, null, out rect);
            rect = default;
            return false;
        }

        /// <summary>지정한 물리 픽셀 좌표의 색이 빨강(고정점 색)인지. 저장된 PNG 를 다시 읽어 판정한다.</summary>
        private static bool IsRedPixel(string pngPath, System.Windows.Point posPx)
        {
            int x = (int)Math.Round(posPx.X), y = (int)Math.Round(posPx.Y);
            var img = new BitmapImage();
            using (var fs = System.IO.File.OpenRead(pngPath))
            {
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.StreamSource = fs;
                img.EndInit();
            }
            if (x < 0 || y < 0 || x >= img.PixelWidth || y >= img.PixelHeight) return false;
            var fmt = new FormatConvertedBitmap(new CroppedBitmap(img, new Int32Rect(x, y, 1, 1)), PixelFormats.Bgra32, null, 0);
            byte[] px = new byte[4];
            fmt.CopyPixels(px, 4, 0);
            byte b = px[0], g = px[1], r = px[2], a = px[3];
            return a > 40 && r > 150 && g < 100 && b < 100;
        }

        // <260811_24> 비트맵 캡처에 활성화된 빨간 고정점이 포함되는지 (-pincaptest). 저장된 PNG 를
        // 다시 읽어 픽셀 색으로 판정 — 눈으로 보는 스냅샷과 달리 항상 자동 재검증할 수 있다.
        private void RunPinCapTest()
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                AppSettings.UseSingleDir(dir);

                var j = FindJoint("R7.TIP");
                var target = _camera.Project(j.Pos);   // 고정점 유무와 무관한 순수 투영 위치
                var targetPx = new Point(target.X, target.Y);

                // 대조군: 고정점 없음 — 그 자리에 빨강이 없어야 한다(오탐 방지)
                _pins.Clear();
                RenderScene();
                string pathOff = CaptureBitmapCore(false);
                bool noPinClean = !IsRedPixel(pathOff, targetPx);

                // 고정점 하나 활성화 — 그 자리에 빨강이 있어야 한다
                _pins.Add("R7.TIP");
                RenderScene();
                var dots = ComputeActivePinDotsPx();
                string pathOn = CaptureBitmapCore(false);
                bool pinShows = dots.Count == 1 && IsRedPixel(pathOn, targetPx);

                // 벡터(SVG) 캡처는 영향 없어야 한다(<260811_24>는 비트맵 한정).
                // <260811_29> 크랙 추적으로 <path> 요소가 1개로 묶였으므로 개수 대신 **고정점 있음/없음
                // 두 캡처의 경로 데이터가 같은지**로 본다 — 검사 의도를 더 곧바로 확인한다.
                string PathData(string file)
                {
                    var m = System.Text.RegularExpressions.Regex.Match(
                        System.IO.File.ReadAllText(file), "<path d=\"([^\"]*)\"");
                    return m.Success ? m.Groups[1].Value : "";
                }
                _renderer.WorldScale = _scene.OverallScale;
                _renderer.Render(_camera, _scene.GetRenderMeshes(!_outlineOnly), 96 * DpiScale, CurrentOutlineOptions(), BuildBoneSegs());
                string dWithPin = PathData(Capture.SaveVector(_renderer, CaptureKeyInfo(), false, (int)_capTarget));

                _pins.Clear();
                RenderScene();
                _renderer.Render(_camera, _scene.GetRenderMeshes(!_outlineOnly), 96 * DpiScale, CurrentOutlineOptions(), BuildBoneSegs());
                string dNoPin = PathData(Capture.SaveVector(_renderer, CaptureKeyInfo(), false, (int)_capTarget));
                int paths = dWithPin.Length > 0 ? 1 : 0;
                bool svgUnaffected = dWithPin.Length > 0 && dWithPin == dNoPin;

                sb.AppendLine($"target=({targetPx.X:F1},{targetPx.Y:F1})");
                sb.AppendLine($"noPinClean={noPinClean}");
                sb.AppendLine($"pinShows={pinShows} dots={dots.Count}");
                sb.AppendLine($"svgUnaffected={svgUnaffected} paths={paths}");
                sb.AppendLine("PINCAP: " + (noPinClean && pinShows && svgUnaffected ? "PASS" : "FAIL"));
            }
            catch (Exception ex) { sb.AppendLine("ERR " + ex); }
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "3dm_qa.txt"), sb.ToString());
            }
            catch { }
        }

        // <260811_17> json 끌어다 놓기 검증 (-droptest): 손 모델 json 만 열리고, 다른 json·다른
        // 확장자는 상태를 건드리지 않아야 한다. (OLE 드래그 자체는 헤드리스로 못 만들므로,
        // Drop 처리기가 그대로 호출하는 TryOpenDroppedFiles 와 AllowDrop 설정을 검사한다.)
        private void RunDropTest()
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "3dm_droptest");
                System.IO.Directory.CreateDirectory(dir);
                string good = System.IO.Path.Combine(dir, "good.json");
                string bad = System.IO.Path.Combine(dir, "bad.json");
                string txt = System.IO.Path.Combine(dir, "note.txt");
                string broken = System.IO.Path.Combine(dir, "broken.json");
                string defaults = System.IO.Path.Combine(dir, "초기값_test.json");   // <260811_21>

                _scene.Pose.Set("L2.PIP", 37);
                _scene.OverallScale = 1.17;
                _scene.Recompute();
                SceneState.Save(good, _scene, _camera, this);   // 진짜 손 모델 파일
                System.IO.File.WriteAllText(bad, "{\"hello\":1,\"Pose\":{\"nope\":1}}");
                System.IO.File.WriteAllText(txt, "그냥 텍스트");
                System.IO.File.WriteAllText(broken, "{ this is not json");
                DefaultValues.Save(defaults);   // <260811_21> 초기값 파일

                // 상태를 흐트러뜨린 뒤 각 경우를 시험
                _scene.Pose.Set("L2.PIP", 90); _scene.OverallScale = 0.8; _scene.Recompute();

                string r1 = TryOpenDroppedFiles(new[] { txt });
                bool ok1 = r1 == null && Math.Abs(_scene.OverallScale - 0.8) < 1e-9;

                string r2 = TryOpenDroppedFiles(new[] { bad });
                bool ok2 = r2 == null && Math.Abs(_scene.OverallScale - 0.8) < 1e-9;

                string r3 = TryOpenDroppedFiles(new[] { broken });
                bool ok3 = r3 == null && Math.Abs(_scene.OverallScale - 0.8) < 1e-9;

                string r4 = TryOpenDroppedFiles(new[] { txt, bad, good });   // 섞여 있으면 손 모델 파일을 고른다
                bool ok4 = r4 == good
                           && Math.Abs(_scene.OverallScale - 1.17) < 1e-9
                           && Math.Abs(_scene.Pose.Get("L2.PIP") - 37) < 1e-9;

                bool ok5 = AllowDrop;   // 창이 놓기를 받도록 설정돼 있는지

                // <260811_21>: '초기값' 파일을 손 모양 열기(드롭)로 읽으면 전용 문구 + 미반영
                double scaleBeforeR6 = _scene.OverallScale;   // r4 로드로 1.17 이 된 상태 — 이 값이 그대로 유지돼야 함
                string r6 = TryOpenDroppedFiles(new[] { defaults });
                string lastMsg = StatusList.Items.Count > 0 ? ((TextBlock)StatusList.Items[StatusList.Items.Count - 1]).Text : "";
                bool ok6 = r6 == null && Math.Abs(_scene.OverallScale - scaleBeforeR6) < 1e-9
                           && lastMsg == "이 파일은 초기값을 담고 있습니다. 손 모양이 담긴 파일을 불러오세요.";

                sb.AppendLine($"txt={r1 ?? "(null)"} ok={ok1}");
                sb.AppendLine($"non-hand json={r2 ?? "(null)"} ok={ok2}");
                sb.AppendLine($"broken json={r3 ?? "(null)"} ok={ok3}");
                sb.AppendLine($"mixed→good={(r4 == null ? "(null)" : System.IO.Path.GetFileName(r4))} scale={_scene.OverallScale:F2} L2.PIP={_scene.Pose.Get("L2.PIP"):F0} ok={ok4}");
                sb.AppendLine($"AllowDrop={AllowDrop}");
                sb.AppendLine($"defaults-file={r6 ?? "(null)"} msg=\"{lastMsg}\" ok={ok6}");
                sb.AppendLine("DROP: " + (ok1 && ok2 && ok3 && ok4 && ok5 && ok6 ? "PASS" : "FAIL"));
            }
            catch (Exception ex) { sb.AppendLine("ERR " + ex); }
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "3dm_qa.txt"), sb.ToString());
            }
            catch { }
        }

        // <260811_15> 설정의 파일 위치 3분할과 '파일 이름 수동 입력' 저장 경로 검증 (-dirtest).
        // 자동 이름이면 각 종류의 폴더에 규칙대로([라벨]+[행-열] 2개), 수동 이름이면 지정한 경로에
        // 그 파일 하나만 저장돼야 한다. (대화상자는 사용자 클릭 경로에만 있으므로 여기선 경로를 직접 준다.)
        private void RunDirTest()
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                string root = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "3dm_dirtest");
                if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true);
                string pngDir = System.IO.Path.Combine(root, "png");
                string svgDir = System.IO.Path.Combine(root, "svg");
                string manDir = System.IO.Path.Combine(root, "manual");
                System.IO.Directory.CreateDirectory(manDir);
                AppSettings.PngDir = pngDir;
                AppSettings.SvgDir = svgDir;
                AppSettings.JsonDir = System.IO.Path.Combine(root, "json");

                // 키를 눌러 둔 상태 = 자동 이름 규칙이 [라벨]·[행-열] 두 파일을 만든다 (3-10-3-1)
                KbDubeol.IsChecked = true; Keyboard_Changed(null, null);
                KbControl.PressedKey = (1, 3);
                UpdateLayout();
                RenderScene();

                string p1 = CaptureBitmapCore(false);
                string v1 = Capture.SaveVector(_renderer, CaptureKeyInfo(), false, (int)_capTarget);
                int pngN = System.IO.Directory.GetFiles(pngDir, "*.png").Length;
                int svgN = System.IO.Directory.GetFiles(svgDir, "*.svg").Length;

                string mp = System.IO.Path.Combine(manDir, "내가 정한 이름.png");
                string mv = System.IO.Path.Combine(manDir, "내가 정한 이름.svg");
                string p2 = CaptureBitmapCore(false, mp);
                string v2 = Capture.SaveVector(_renderer, CaptureKeyInfo(), false, (int)_capTarget, mv);

                bool ok =
                    p1.StartsWith(pngDir) && v1.StartsWith(svgDir) &&        // 종류별 폴더로 갈라짐
                    pngN == 2 && svgN == 2 &&                                // 자동 = [라벨] + [행-열]
                    p2 == mp && v2 == mv &&
                    System.IO.File.Exists(mp) && System.IO.File.Exists(mv) &&
                    System.IO.Directory.GetFiles(manDir).Length == 2 &&      // 수동 = 지정한 파일만
                    System.IO.Directory.GetFiles(pngDir, "*.png").Length == 2 &&   // 수동이 자동 폴더를 안 건드림
                    System.IO.Directory.GetFiles(svgDir, "*.svg").Length == 2;

                sb.AppendLine($"auto png={pngN} svg={svgN} p1={p1}");
                sb.AppendLine($"auto svg1={v1}");
                sb.AppendLine($"manual png={p2} exists={System.IO.File.Exists(mp)}");
                sb.AppendLine($"manual svg={v2} exists={System.IO.File.Exists(mv)} manualFiles={System.IO.Directory.GetFiles(manDir).Length}");
                sb.AppendLine("DIRSPLIT: " + (ok ? "PASS" : "FAIL"));
            }
            catch (Exception ex) { sb.AppendLine("ERR " + ex); }
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "3dm_qa.txt"), sb.ToString());
            }
            catch { }
        }

        /// <summary>상태 표시줄 두께와 무관해야 하는 값들: 키보드의 창 좌표·렌더 버퍼 크기·손 픽셀 범위.</summary>
        private (double kbX, double kbY, int bufW, int bufH, int left, int top, int right, int bottom, double statusH)
            MeasureStatusInvariants()
        {
            var p = KbControl.TranslatePoint(new Point(0, 0), this);
            var tags = _renderer.TagBuffer;
            int w = _renderer.RW, h = _renderer.RH;
            int left = -1, top = -1, right = -1, bottom = -1;
            if (tags != null)
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        if (tags[y * w + x] >= 0)
                        {
                            if (left < 0 || x < left) left = x;
                            if (x > right) right = x;
                            if (top < 0) top = y;
                            bottom = y;
                        }
            return (p.X, p.Y, w, h, left, top, right, bottom, StatusBar.ActualHeight);
        }

        // 실행 취소/다시 실행 자가 테스트 (-undotest, <260810_7>)
        private void RunUndoTest()
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                double v0 = _scene.Volume, f0 = _scene.FleshResponse;   // 시작 상태(Loaded 에서 기록됨)

                VolumeSlider.Value = 1.4; CommitHistorySnapshot();      // 편집 1
                // 편집 2 — <260811_11>: 살 반응이 ValueSlider 로 바뀌어 프로그램적 .Value 설정은
                // 이벤트가 억제된다(사용자 드래그만 이벤트 발생). 테스트는 드래그와 같은 효과를
                // 직접 수행한다: 모델+UI 설정 후 재계산.
                _scene.FleshResponse = 20; _fleshSlider.Value = 20; Recompute();
                CommitHistorySnapshot();
                sb.AppendLine($"edits: vol={_scene.Volume:F2} flesh={_scene.FleshResponse:F0} hist={_history.Count}");

                Undo();
                bool ok1 = Math.Abs(_scene.Volume - 1.4) < 1e-6 && Math.Abs(_scene.FleshResponse - f0) < 1e-6;
                sb.AppendLine($"undo1: vol={_scene.Volume:F2} flesh={_scene.FleshResponse:F0} (기대 1.40/{f0:F0}) {(ok1 ? "OK" : "NG")}");

                Undo();
                bool ok2 = Math.Abs(_scene.Volume - v0) < 1e-6 && Math.Abs(_scene.FleshResponse - f0) < 1e-6;
                sb.AppendLine($"undo2: vol={_scene.Volume:F2} flesh={_scene.FleshResponse:F0} (기대 {v0:F2}/{f0:F0}) {(ok2 ? "OK" : "NG")}");

                Redo();
                bool ok3 = Math.Abs(_scene.Volume - 1.4) < 1e-6;
                sb.AppendLine($"redo1: vol={_scene.Volume:F2} (기대 1.40) {(ok3 ? "OK" : "NG")}");

                Redo();
                bool ok4 = Math.Abs(_scene.FleshResponse - 20) < 1e-6 && Math.Abs(_scene.Volume - 1.4) < 1e-6;
                sb.AppendLine($"redo2: vol={_scene.Volume:F2} flesh={_scene.FleshResponse:F0} (기대 1.40/20) {(ok4 ? "OK" : "NG")}");

                // 되돌린 뒤 새 편집을 하면 앞의 '다시 실행' 기록은 사라져야 한다
                Undo();
                SizeSlider.Value = 1.25; CommitHistorySnapshot();
                bool ok5 = _histIndex == _history.Count - 1;
                sb.AppendLine($"new-edit-after-undo: index={_histIndex} count={_history.Count} {(ok5 ? "OK" : "NG")}");

                sb.AppendLine("UNDO: " + (ok1 && ok2 && ok3 && ok4 && ok5 ? "PASS" : "FAIL"));
            }
            catch (Exception ex) { sb.AppendLine("ERR " + ex); }
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "3dm_qa.txt"), sb.ToString());
            }
            catch { }
        }

        // <260811_32> 도움말 창 자가 테스트 (-helptest)
        // ① 제목 표시줄 단추가 왼→오른 순서로 열기/저장/설정/도움말/정보 인지
        // ② 사용법 문서가 실행 파일에서 읽히는지, ③ 마크다운이 실제로 그려지는지(제목·표·코드블록 개수),
        // ④ '출력'이 쓰는 코드가 원본과 한 글자도 다르지 않은 파일을 만드는지.
        private void RunHelpTest()
        {
            var sb = new System.Text.StringBuilder();
            bool ok1 = false, ok2 = false, ok3 = false, ok4 = false;
            try
            {
                // ① 단추 순서 — DockPanel.Dock=Right 는 선언 순서의 역순으로 놓이므로 뒤집어 비교한다.
                var names = new System.Collections.Generic.List<string>();
                foreach (object child in TitleBarPanel.Children)
                    if (child is Button b && b.Content is string s) names.Add(s);
                names.Reverse();
                string order = string.Join("/", names);
                ok1 = order == "열기/저장/설정/도움말/정보";
                sb.AppendLine($"buttons: {order} (기대 열기/저장/설정/도움말/정보) {(ok1 ? "OK" : "NG")}");

                var win = new HelpWindow { Owner = this };
                string md = win.Markdown;
                ok2 = md != null && md.Length > 1000 && md.StartsWith("# 손 모델 3D");
                sb.AppendLine($"resource: {md?.Length ?? 0}자 {(ok2 ? "OK" : "NG")}");

                win.Show();
                win.UpdateLayout();
                // 문서 안에 있는 만큼 실제로 그려졌는가 — 원문을 세어 렌더 결과와 대조한다.
                int wantHead = 0, wantTable = 0, wantFence = 0;
                foreach (string ln in (md ?? "").Replace("\r\n", "\n").Split('\n'))
                {
                    if (System.Text.RegularExpressions.Regex.IsMatch(ln, @"^#{1,6}\s")) wantHead++;
                    else if (System.Text.RegularExpressions.Regex.IsMatch(ln, @"^\s*\|\s*:?-{2,}")) wantTable++;
                    else if (ln.TrimStart().StartsWith("```")) wantFence++;
                }
                int wantCode = wantFence / 2;
                ok3 = win.HeadingCount == wantHead && win.TableCount == wantTable && win.CodeCount == wantCode
                      && wantHead > 0 && wantTable > 0;
                sb.AppendLine($"render: 제목 {win.HeadingCount}/{wantHead} 표 {win.TableCount}/{wantTable} " +
                              $"코드블록 {win.CodeCount}/{wantCode} {(ok3 ? "OK" : "NG")}");

                // 창 그림도 남겨 눈으로 확인할 수 있게 한다
                double scale = DpiScale;
                int ww = (int)(win.ActualWidth * scale), hh = (int)(win.ActualHeight * scale);
                if (ww > 0 && hh > 0 && win.Content is Visual v)
                {
                    var rtb = new RenderTargetBitmap(ww, hh, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                    rtb.Render(v);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(rtb));
                    using (var fs = System.IO.File.Create(System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "3dm_help.png")))
                        enc.Save(fs);
                }

                // ④ '출력' 왕복
                string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "3dm_help_export.md");
                win.WriteTo(tmp);
                string back = System.IO.File.ReadAllText(tmp, System.Text.Encoding.UTF8);
                byte[] head = System.IO.File.ReadAllBytes(tmp);
                bool noBom = head.Length >= 3 && !(head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF);
                ok4 = back == md && noBom;
                sb.AppendLine($"export: {head.Length}바이트 동일={back == md} BOM없음={noBom} {(ok4 ? "OK" : "NG")}");
                try { System.IO.File.Delete(tmp); } catch { }

                win.Close();
                sb.AppendLine("HELP: " + (ok1 && ok2 && ok3 && ok4 ? "PASS" : "FAIL"));
            }
            catch (Exception ex) { sb.AppendLine("ERR " + ex); }
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "3dm_qa.txt"), sb.ToString());
            }
            catch { }
            Application.Current.Shutdown();
        }

        // 저장/열기 완벽 재현(3-11) 라운드트립 자가 테스트 (-savetest)
        private void RunSaveTest()
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string path = System.IO.Path.Combine(dir, "3Dm_roundtrip.json");

                // 상태를 특징적으로 설정
                _scene.Pose.Set("L2.PIP", 33);
                _scene.OverallScale = 1.21;
                _scene.Volume = 1.17;
                _scene.FleshResponse = 63;
                _scene.Transparency = 40;
                _scene.SkinColor = Color.FromRgb(0x9A, 0x6A, 0x45);
                _scene.TipSkin["R8"] = Color.FromRgb(0x11, 0x22, 0x33);
                _scene.FingerLen["3"] = 1.31;
                _camera.YawDeg = 17; _camera.Distance = 96; _camera.RollDeg = 12;
                _pins.Add("R9.PIP");
                _scene.RotLeft = new HandRotation { RollDeg = -22, PitchDeg = 8, SwingDeg = 3 };
                _scene.OffsetLeft = new Vector3D(2.5, -1.5, 0.5);
                _scene.OffsetRight = new Vector3D(-3, 1, 0);
                KbDubeol.IsChecked = true; Keyboard_Changed(null, null);
                KbControl.PressedKey = (2, 3);
                KbScale.ScaleX = KbScale.ScaleY = 1.4;
                _scene.Recompute();

                SceneState.Save(path, _scene, _camera, this);

                // 상태를 흐트러뜨림
                _scene.Pose.Set("L2.PIP", 90);
                _scene.OverallScale = 0.8; _scene.Volume = 0.9; _scene.FleshResponse = 10; _scene.Transparency = 0;
                _scene.SkinColor = Colors.White; _scene.TipSkin.Clear(); _scene.FingerLen.Clear();
                _camera.YawDeg = -50; _camera.Distance = 60; _camera.RollDeg = 0;
                _pins.Clear();
                _scene.RotLeft = default;
                _scene.OffsetLeft = _scene.OffsetRight = new Vector3D();
                KbControl.PressedKey = null; KbScale.ScaleX = KbScale.ScaleY = 1.0;

                SceneState.Load(path, _scene, _camera, this);

                bool ok =
                    Math.Abs(_scene.Pose.Get("L2.PIP") - 33) < 1e-6 &&
                    Math.Abs(_scene.OverallScale - 1.21) < 1e-6 &&
                    Math.Abs(_scene.Volume - 1.17) < 1e-6 &&
                    Math.Abs(_scene.FleshResponse - 63) < 1e-6 &&
                    Math.Abs(_scene.Transparency - 40) < 1e-6 &&
                    _scene.SkinColor == Color.FromRgb(0x9A, 0x6A, 0x45) &&
                    _scene.TipSkin.TryGetValue("R8", out var tc) && tc == Color.FromRgb(0x11, 0x22, 0x33) &&
                    Math.Abs(_scene.FingerLen["3"] - 1.31) < 1e-6 &&
                    Math.Abs(_camera.YawDeg - 17) < 1e-6 &&
                    Math.Abs(_camera.Distance - 96) < 1e-6 &&
                    Math.Abs(_camera.RollDeg - 12) < 1e-6 &&
                    _pins.Contains("R9.PIP") &&
                    KbControl.PressedKey.HasValue && KbControl.PressedKey.Value == (2, 3) &&
                    Math.Abs(KbScale.ScaleX - 1.4) < 1e-6 &&
                    Math.Abs(_scene.RotLeft.RollDeg - (-22)) < 1e-6 &&
                    Math.Abs(_scene.RotLeft.PitchDeg - 8) < 1e-6 &&
                    Math.Abs(_scene.RotLeft.SwingDeg - 3) < 1e-6 &&
                    Math.Abs(_scene.OffsetLeft.X - 2.5) < 1e-6 &&
                    Math.Abs(_scene.OffsetLeft.Y - (-1.5)) < 1e-6 &&
                    Math.Abs(_scene.OffsetLeft.Z - 0.5) < 1e-6 &&
                    Math.Abs(_scene.OffsetRight.X - (-3)) < 1e-6;
                sb.AppendLine($"SAVELOAD: {(ok ? "PASS" : "FAIL")}");
            }
            catch (Exception ex) { sb.AppendLine("ERR " + ex); }
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "3dm_qa.txt"), sb.ToString());
            }
            catch { }
        }

        // IK·고정점 규칙 헤드리스 자가 테스트 (-iktest). 결과를 3dm_qa.txt 로.
        private void RunIkTest()
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                // 1) IK: 오른손 검지 끝을 손바닥 쪽(-Y,-Z)으로 이동
                _ikSide = HandSide.Right; _ikDigit = Digit.Index; _ikIsPalm = false;
                BeginIkDrag();
                var tip0 = EvalJoint("R7.TIP").Value;
                var target = tip0 + new System.Windows.Media.Media3D.Vector3D(0, -4, -3);
                double d0 = (tip0 - target).Length;
                SolveIk(target);
                var tip1 = EvalJoint("R7.TIP").Value;
                double d1 = (tip1 - target).Length;
                sb.AppendLine($"IK: d0={d0:F2} -> d1={d1:F2} (개선={(d0 - d1):F2}) {(d1 < d0 * 0.5 ? "PASS" : "FAIL")}");

                // 2) 핀 거부: L4.TIP 고정 → L4 MCP 슬라이더 거부돼야 함
                _scene.Recompute();
                _pins.Clear(); _pins.Add("L4.TIP");
                OnJointSlider("L4.MCP.UD", 50);
                double v = _scene.Pose.Get("L4.MCP.UD");
                sb.AppendLine($"PIN-REJECT: L4.MCP.UD={v:F0} {(Math.Abs(v - 100) < 1e-6 ? "PASS" : "FAIL")}");

                // 3) 아치: 소지 핀 → 거부 / 검지 핀 → 허용 (3-3-3-1-3-1)
                _pins.Clear(); _pins.Add("L1.TIP");
                OnJointSlider("L.ARCH", 50);
                double arch1 = _scene.Pose.Get("L.ARCH");
                _pins.Clear(); _pins.Add("L4.TIP");
                OnJointSlider("L.ARCH", 40);
                double arch2 = _scene.Pose.Get("L.ARCH");
                sb.AppendLine($"ARCH: 소지핀시={arch1:F0}(100이어야) 검지핀시={arch2:F0}(40이어야) " +
                              $"{(Math.Abs(arch1 - 100) < 1e-6 && Math.Abs(arch2 - 40) < 1e-6 ? "PASS" : "FAIL")}");

                // 정리
                _pins.Clear();
                _scene.Pose.Set("L.ARCH", 100);
                _scene.Recompute();
            }
            catch (Exception ex) { sb.AppendLine("ERR " + ex); }
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "3dm_qa.txt"), sb.ToString());
            }
            catch { }
        }

        private bool _capSvgOnLoad;
        private void RunCapSvgCheck()
        {
            try
            {
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                AppSettings.UseSingleDir(dir);
                string svg = Capture.SaveVector(_renderer, CaptureKeyInfo(), false, (int)_capTarget);
                // <260811_29> 크랙 추적으로 바뀌어 조각이 여러 개다 — <path> 요소는 1개로 묶이므로
                // 조각 수(M 명령)와 닫힌 조각 수(Z)를 함께 본다. 두 손 실루엣이 있으면 닫힌 조각 ≥ 2.
                string text = System.IO.File.ReadAllText(svg);
                int paths = System.Text.RegularExpressions.Regex.Matches(text, "<path ").Count;
                var dm = System.Text.RegularExpressions.Regex.Match(text, "<path d=\"([^\"]*)\"");
                string d = dm.Success ? dm.Groups[1].Value : "";
                int subs = System.Text.RegularExpressions.Regex.Matches(d, @"M ").Count;
                int closed = System.Text.RegularExpressions.Regex.Matches(d, @"Z").Count;
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "3dm_qa.txt"),
                    $"svg={svg}\npaths={paths}\nsubpaths={subs}\nclosed={closed}");
            }
            catch (Exception ex)
            {
                try { System.IO.File.WriteAllText(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "3dm_qa.txt"), "ERR " + ex); } catch { }
            }
        }

        // ---------- 구성 ----------

        // ---------- 손 회전 (<260719_8-1>): 손별 3축 ±180° ----------

        private readonly Dictionary<string, ValueSlider> _rotSliders = new Dictionary<string, ValueSlider>();

        /// <summary>패널 안의 한 세트를 검은 선 테두리 상자로 감싼다.
        /// 손 회전·손 위치·시점 회전 패널이 같은 모양을 공유한다 (<260810_5>).</summary>
        private static Border SetBox(UIElement child, double leftMargin)
        {
            return new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x21, 0x25, 0x29)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 2, 4, 2),
                Margin = new Thickness(leftMargin, 0, 0, 0),
                Child = child
            };
        }

        // 손 회전 슬라이더: 왼손·오른손 세트를 각각 검은 선 테두리 상자로 감싼다 (<260810_5>(2)).
        private void BuildRotationSliders()
        {
            string[] axisNames = { "세로축", "가로축", "수직축" };

            for (int side = 0; side < 2; side++)
            {
                string handName = side == 0 ? "왼손" : "오른손";

                var grid = new Grid();
                for (int c = 0; c < 2; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                for (int r = 0; r < 3; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                for (int a = 0; a < 3; a++)
                {
                    var lbl = new TextBlock
                    {
                        Text = (a == 0 ? handName + " " : "") + axisNames[a],
                        FontSize = 9,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 2, 0)
                    };
                    Grid.SetRow(lbl, a); Grid.SetColumn(lbl, 0);
                    grid.Children.Add(lbl);

                    // 숫자 값 클릭 입력 가능(<260810_4>(2)): Slider 대신 ValueSlider(슬라이더+편집 숫자)
                    string key = (side == 0 ? "L" : "R") + a;
                    var sl = new ValueSlider("", -180, 180, 0, 74) { VerticalAlignment = VerticalAlignment.Center };
                    sl.ValueChanged += (s, v) => OnRotSlider(key, v);
                    sl.ToolTip = handName + " " + axisNames[a] + " 회전(°)";
                    _rotSliders[key] = sl;
                    Grid.SetRow(sl, a); Grid.SetColumn(sl, 1);
                    grid.Children.Add(sl);
                }

                RotRow.Children.Add(SetBox(grid, side == 0 ? 0 : 6));
            }
        }

        // ---------- 손 위치 (<260810_3-1>(5)(6)(7)) ----------
        // '위치 이동' = 착각 효과: 공통은 팬(시점 평행이동)의 반대 제어, 왼손/오른손은 그 손만의
        // 화면 이동(손별 오프셋 — 두 손의 시점이 어긋나는 효과). 값의 부호는 모두
        // "손이 화면에서 움직여 보이는 방향" 기준이다.

        private readonly Dictionary<string, ValueSlider> _handPosSliders = new Dictionary<string, ValueSlider>();
        private const double HandPosRange = 30;    // 손별 오프셋(월드 단위)
        private const double CommonPanRange = 400; // 공통 팬(화면 논리픽셀)

        private void BuildHandPosSliders()
        {
            var grid = HandPosGrid;
            for (int c = 0; c < 3; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            string[] groups = { "공통", "왼손", "오른손" };
            for (int g = 0; g < 3; g++)
            {
                var col = new StackPanel();
                col.Children.Add(new TextBlock { Text = groups[g], FontSize = 9, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center });
                foreach (string axis in new[] { "X", "Y", "Z" })
                {
                    // 공통 X/Y 는 팬(논리픽셀), 그 외(공통 Z 포함)는 손 오프셋(월드 단위) (<260810_9>)
                    double range = g == 0 && axis != "Z" ? CommonPanRange : HandPosRange;
                    string key = g + axis;
                    // <260811_21>: 왼손/오른손 X·Y 초기값(AppSettings.DefOffset* · HandScene.OffsetLeft/Right
                    // 와 동일 상수). 공통(g=0)과 Z 축은 0 유지.
                    double initial = g == 1 && axis == "X" ? 1
                                    : g == 1 && axis == "Y" ? -4.9
                                    : g == 2 && axis == "X" ? -5.5
                                    : g == 2 && axis == "Y" ? -4.9
                                    : 0;
                    var vs = new ValueSlider(axis, -range, range, initial, 56);
                    int gg = g;
                    vs.ValueChanged += (s, v) => OnHandPosSlider(gg, axis, v);
                    _handPosSliders[key] = vs;
                    col.Children.Add(vs);
                }
                // <260810_5>(1): 세 세트를 각각 검은 선 테두리로 감싼다
                var box = SetBox(col, g == 0 ? 0 : 4);
                Grid.SetColumn(box, g);
                grid.Children.Add(box);
            }
        }

        private void OnHandPosSlider(int group, string axis, double v)
        {
            if (group == 0 && axis != "Z")
            {
                // 공통 X/Y = 팬 반대 제어(착각 효과). 표시값(논리픽셀) +X = 손이 오른쪽으로 보임 = PanX +.
                double px0 = _camera.PanX, py0 = _camera.PanY;
                bool wasOob = FingersOutOfBounds();
                if (axis == "X") _camera.PanX = v * DpiScale;
                else _camera.PanY = -v * DpiScale;   // 표시 +Y = 위쪽 = 화면 PanY 감소
                if (!wasOob && FingersOutOfBounds())
                {
                    _camera.PanX = px0; _camera.PanY = py0;
                    SyncHandPosSliders();
                    Status("그래픽 영역을 벗어나지 못하는 3D 손의 영역이 있습니다.", true);
                    RenderScene();
                    return;
                }
                RenderScene();
                return;
            }

            Vector3D l = _scene.OffsetLeft, r = _scene.OffsetRight;
            double Axis(Vector3D o) => axis == "X" ? o.X : (axis == "Y" ? o.Y : o.Z);
            Vector3D dL = new Vector3D(), dR = new Vector3D();
            void SetAxis(ref Vector3D d, double delta)
            {
                if (axis == "X") d.X = delta;
                else if (axis == "Y") d.Y = delta;
                else d.Z = delta;
            }

            if (group == 0)
            {
                // 공통 Z (<260810_9>): 팬에는 깊이가 없으므로 양손 오프셋 Z 를 함께 움직인다.
                double d0 = v - (Axis(l) + Axis(r)) / 2;
                SetAxis(ref dL, d0); SetAxis(ref dR, d0);
            }
            else if (group == 1) SetAxis(ref dL, v - Axis(l));
            else SetAxis(ref dR, v - Axis(r));
            MoveHands(dL, dR);
        }

        /// <summary>손 위치 슬라이더 표시를 현재 팬·오프셋과 동기화(이벤트 재발화 없음).</summary>
        private void SyncHandPosSliders()
        {
            if (_handPosSliders.Count == 0) return;
            _handPosSliders["0X"].Value = _camera.PanX / DpiScale;
            _handPosSliders["0Y"].Value = -_camera.PanY / DpiScale;
            Vector3D l = _scene.OffsetLeft, r = _scene.OffsetRight;
            _handPosSliders["0Z"].Value = (l.Z + r.Z) / 2;
            _handPosSliders["1X"].Value = l.X;
            _handPosSliders["1Y"].Value = l.Y;
            _handPosSliders["1Z"].Value = l.Z;
            _handPosSliders["2X"].Value = r.X;
            _handPosSliders["2Y"].Value = r.Y;
            _handPosSliders["2Z"].Value = r.Z;
        }

        private void HandPosReset_Click(object sender, RoutedEventArgs e)
        {
            // qksqhr(2026-08-11): 다른 손 위치·팬 경로처럼 (3-12) 영역 이탈 거부를 적용한다.
            // 이전엔 무조건 0을 적용해, 리셋 한 번으로 손가락이 화면 밖으로 잘린 상태가
            // 경고 없이 수용되는 우회로가 있었다(예: 공통 Z로 멀어진 상태에서 최대 줌인 후 리셋).
            double px0 = _camera.PanX, py0 = _camera.PanY;
            var ol0 = _scene.OffsetLeft; var or0 = _scene.OffsetRight;
            bool wasOob = FingersOutOfBounds();
            _camera.PanX = _camera.PanY = 0;
            _scene.OffsetLeft = new Vector3D();
            _scene.OffsetRight = new Vector3D();
            _scene.Recompute();
            if (!wasOob && FingersOutOfBounds())
            {
                _camera.PanX = px0; _camera.PanY = py0;
                _scene.OffsetLeft = ol0; _scene.OffsetRight = or0;
                _scene.Recompute();
                SyncHandPosSliders();
                Status("그래픽 영역을 벗어나지 못하는 3D 손의 영역이 있습니다.", true);
                RenderScene();
                return;
            }
            SyncHandPosSliders();
            RenderScene();
        }

        // ---------- 시점 회전 (<260810_3-1>(4)): 기즈모 좌표축 제어 ----------

        private ValueSlider _vrX, _vrY, _vrZ;

        // 기즈모 오른쪽 오버레이 패널: 기즈모 드래그와 같은 시점 회전을 슬라이더·숫자로 제어.
        // x=상하 궤도(Pitch), y=좌우 궤도(Yaw), z=시선축 기울임(Roll). 가로 배치 (<260810_3-2>(4)).
        private void BuildViewRotSliders()
        {
            ValueSlider Make(string label, double min, double max, bool first)
            {
                var vs = new ValueSlider(label, min, max, 0, 64);
                vs.ValueChanged += (s, v) => OnViewRotSlider();
                // <260810_5>(1): 축마다 검은 선 테두리로 감싸고 사이를 충분히 벌린다
                ViewRotRow.Children.Add(SetBox(vs, first ? 0 : 8));
                return vs;
            }
            _vrX = Make("x", -88, 88, true);
            _vrY = Make("y", -180, 180, false);
            _vrZ = Make("z", -180, 180, false);
            SyncViewRotSliders();
            BuildWebbingSlider();
        }

        // <260811_4-1>(1): 물갈퀴 살(손가락 사이 웹빙) 높이 슬라이더 — '시점 회전' 패널 오른쪽.
        private ValueSlider _webbingSlider;
        private void BuildWebbingSlider()
        {
            // <260811_21>: 초기값 0%(AppSettings.DefWebbingHeight·HandScene.WebbingHeight 과 동일 상수)
            _webbingSlider = new ValueSlider("높이", 0, 200, 0, 64);
            _webbingSlider.ValueChanged += (s, v) =>
            {
                if (Math.Abs(_scene.WebbingHeight - v) < 0.001) return;
                _scene.WebbingHeight = v;
                Recompute();
            };
            WebbingRow.Children.Add(SetBox(_webbingSlider, 0));
            BuildFleshSlider();
        }

        // <260811_11>: '살 반응'을 상단 패널에서 '물갈퀴 살' 오른쪽 오버레이 패널로 이동(좌우 슬라이더).
        private ValueSlider _fleshSlider;
        private void BuildFleshSlider()
        {
            _fleshSlider = new ValueSlider("정도", 0, 100, 50, 64);
            _fleshSlider.ValueChanged += (s, v) =>
            {
                if (Math.Abs(_scene.FleshResponse - v) < 0.001) return;
                _scene.FleshResponse = v;
                Recompute();
            };
            FleshRow.Children.Add(SetBox(_fleshSlider, 0));
        }

        // <260810_3-2>(4): 손 회전·손 위치 패널처럼 클릭 한 번으로 0.
        private void ViewRotReset_Click(object sender, RoutedEventArgs e)
        {
            double p0 = _camera.PitchDeg, y0 = _camera.YawDeg, r0 = _camera.RollDeg;
            bool wasOob = FingersOutOfBounds();
            _camera.PitchDeg = 0; _camera.YawDeg = 0; _camera.RollDeg = 0;
            if (!wasOob && FingersOutOfBounds())
            {
                _camera.PitchDeg = p0; _camera.YawDeg = y0; _camera.RollDeg = r0;
                Status("그래픽 영역을 벗어나지 못하는 3D 손의 영역이 있습니다.", true);
                return;
            }
            SyncViewRotSliders();
            RenderScene();
        }

        private void OnViewRotSlider()
        {
            double p0 = _camera.PitchDeg, y0 = _camera.YawDeg, r0 = _camera.RollDeg;
            bool wasOob = FingersOutOfBounds();
            _camera.PitchDeg = _vrX.Value;
            _camera.YawDeg = _vrY.Value;
            _camera.RollDeg = _vrZ.Value;
            if (!wasOob && FingersOutOfBounds())
            {
                _camera.PitchDeg = p0; _camera.YawDeg = y0; _camera.RollDeg = r0;
                SyncViewRotSliders();
                Status("그래픽 영역을 벗어나지 못하는 3D 손의 영역이 있습니다.", true);
                RenderScene();
                return;
            }
            RenderScene();
        }

        private void SyncViewRotSliders()
        {
            if (_vrX == null) return;
            _vrX.Value = _camera.PitchDeg;
            _vrY.Value = _camera.YawDeg;
            _vrZ.Value = _camera.RollDeg;
        }

        private void OnRotSlider(string key, double deg)
        {
            bool left = key[0] == 'L';
            int axis = key[1] - '0';
            var rot = left ? _scene.RotLeft : _scene.RotRight;
            if (axis == 0) rot.RollDeg = deg;
            else if (axis == 1) rot.PitchDeg = deg;
            else rot.SwingDeg = deg;
            if (left) _scene.RotLeft = rot; else _scene.RotRight = rot;
            Recompute();
        }

        private void RotReset_Click(object sender, RoutedEventArgs e)
        {
            _scene.RotLeft = default;
            _scene.RotRight = default;
            foreach (var kv in _rotSliders) kv.Value.Value = 0;
            Recompute();
        }

        private void SyncRotSliders()
        {
            void Set(string key, double v) { if (_rotSliders.TryGetValue(key, out var sl)) sl.Value = v; }
            Set("L0", _scene.RotLeft.RollDeg); Set("L1", _scene.RotLeft.PitchDeg); Set("L2", _scene.RotLeft.SwingDeg);
            Set("R0", _scene.RotRight.RollDeg); Set("R1", _scene.RotRight.PitchDeg); Set("R2", _scene.RotRight.SwingDeg);
        }

        private void BuildJointPanels()
        {
            _leftJoints = new JointPanel(HandSide.Left, _scene.Pose);
            _leftJoints.SliderChanged += OnJointSlider;
            LeftPanelHost.Children.Add(_leftJoints);

            _rightJoints = new JointPanel(HandSide.Right, _scene.Pose);
            _rightJoints.SliderChanged += OnJointSlider;
            RightPanelHost.Children.Add(_rightJoints);
        }

        // <260811_18>(2) '초기화' — 그 손의 모든 관절 슬라이더를 초기값으로 되돌린다.
        private void JointResetLeft_Click(object sender, RoutedEventArgs e) => ResetJoints(HandSide.Left);
        private void JointResetRight_Click(object sender, RoutedEventArgs e) => ResetJoints(HandSide.Right);

        public void ResetJoints(HandSide side)
        {
            var def = HandPose.Default();
            var before = new Dictionary<string, double>();
            bool wasOob = FingersOutOfBounds();

            foreach (var id in HandPose.AllSliderIds())
            {
                if (id.Side != side) continue;
                before[id.Key] = _scene.Pose.Get(id.Key);
                _scene.Pose.Set(id.Key, def.Get(id.Key));
            }
            _scene.Recompute();

            // 다른 리셋 버튼(손 위치·시점 회전)과 같은 (3-12) 영역 이탈 거부
            if (!wasOob && FingersOutOfBounds())
            {
                foreach (var kv in before) _scene.Pose.Set(kv.Key, kv.Value);
                _scene.Recompute();
                Status("그래픽 영역을 벗어나지 못하는 3D 손의 영역이 있습니다.", true);
                RenderScene();
                return;
            }

            foreach (string key in before.Keys) SyncSlider(key, _scene.Pose.Get(key));
            RenderScene();
            Status((side == HandSide.Left ? "왼손" : "오른손") + " 관절을 초기값으로 되돌렸습니다.");
        }

        private void OnJointSlider(string key, double pct)
        {
            double prev = _scene.Pose.Get(key);
            if (Math.Abs(prev - pct) < 1e-9) return;

            var pinnedBefore = CapturePinPositions();
            bool wasOob = FingersOutOfBounds();

            _scene.Pose.Set(key, pct);
            _scene.Recompute();

            // (3-3-3-1-3) 고정점 위반 → 거부. (3-3-2-3) 때문에 손목쪽 뼈로 보상하지 않는다.
            if (PinsMoved(pinnedBefore))
            {
                _scene.Pose.Set(key, prev);
                _scene.Recompute();
                SyncSlider(key, prev);
                RenderScene();
                return;
            }
            // (3-12) 손가락(MCP~끝)이 그래픽 영역을 벗어나면 거부 + 빨간 문구
            if (!wasOob && FingersOutOfBounds())
            {
                _scene.Pose.Set(key, prev);
                _scene.Recompute();
                SyncSlider(key, prev);
                Status("그래픽 영역을 벗어나지 못하는 3D 손의 영역이 있습니다.", true);
                RenderScene();
                return;
            }
            RenderScene();
        }

        private void SyncSlider(string key, double val)
        {
            var ls = _leftJoints.Get(key); if (ls != null) ls.Value = val;
            var rs = _rightJoints.Get(key); if (rs != null) rs.Value = val;
        }

        private Dictionary<string, System.Windows.Media.Media3D.Point3D> CapturePinPositions()
        {
            var d = new Dictionary<string, System.Windows.Media.Media3D.Point3D>();
            foreach (string id in _pins)
            {
                var j = FindJoint(id);
                if (j != null) d[id] = j.Pos;
            }
            return d;
        }

        private bool PinsMoved(Dictionary<string, System.Windows.Media.Media3D.Point3D> before, double eps = 0.05)
        {
            foreach (var kv in before)
            {
                var j = FindJoint(kv.Key);
                if (j == null) continue;
                if ((j.Pos - kv.Value).Length > eps) return true;
            }
            return false;
        }

        /// <summary>(3-12) MCP 포함 손끝쪽 관절이 그래픽 영역 밖인지. 엄지 CMC(손목쪽)는 제외.</summary>
        private bool FingersOutOfBounds()
        {
            _camera.Update();
            foreach (var j in _scene.AllJoints())
            {
                if (j.Kind == JointKind.CMC) continue;   // 손목쪽 — 벗어나도 됨
                var p = _camera.Project(j.Pos);
                if (!p.InFront) return true;
                if (p.X < 0 || p.X >= _camera.Width || p.Y < 0 || p.Y >= _camera.Height) return true;
            }
            return false;
        }

        private readonly Dictionary<string, Slider> _lenSliders = new Dictionary<string, Slider>();
        private bool _syncingLenSliders;   // qksqhr: SyncUiFromScene 중 손가락 길이 이벤트 억제
        private void BuildLengthSliders()
        {
            // 양손 대칭 5쌍(해석 ⓓ). 손가락 종류(Digit)로 키 → 좌우 같은 값.
            Digit[] digits = { Digit.Pinky, Digit.Ring, Digit.Middle, Digit.Index, Digit.Thumb };
            string[] names = { "소지", "약지", "중지", "검지", "엄지" };
            for (int i = 0; i < digits.Length; i++)
            {
                var sp = new StackPanel { Margin = new Thickness(2, 0, 2, 0) };
                sp.Children.Add(new TextBlock { Text = names[i], FontSize = 9, TextAlignment = TextAlignment.Center });
                var sl = new Slider { Orientation = Orientation.Vertical, Height = 44, Minimum = 0.6, Maximum = 1.5, Value = 1, HorizontalAlignment = HorizontalAlignment.Center };
                string key = ((int)digits[i]).ToString();
                // <260811_6>(2): 슬라이더 아래 숫자(%) — 클릭하면 키보드로 직접 입력.
                var num = new TextBlock { Text = "100%", FontSize = 9, TextAlignment = TextAlignment.Center };
                sl.ValueChanged += (s, e) =>
                {
                    num.Text = (int)Math.Round(sl.Value * 100) + "%";
                    // qksqhr(2026-08-11): SyncUiFromScene(열기·실행 취소)이 슬라이더를 맞출 때는
                    // 표시만 갱신하고 모델 기록·재계산은 건너뛴다 — 가드 없이는 동기화가 방금
                    // 복원한 scene 을 슬라이더 클램프 값으로 되써서 상태를 오염시켰다.
                    if (_syncingLenSliders) return;
                    _scene.FingerLen[key] = sl.Value;
                    Recompute();
                };
                _lenSliders[key] = sl;
                sp.Children.Add(sl);
                sp.Children.Add(num);
                // <260811_6-2> 버그 수정: MakeValueEditable 은 라벨이 패널에 **들어간 뒤** 호출해야
                // 한다(내부에서 label.Parent 를 검사해 편집 상자를 옆에 끼워 넣음). 이전엔 넣기 전에
                // 호출해 조용히 무시돼, 손가락 길이 숫자가 클릭해도 편집되지 않았다.
                MakeValueEditable(num, () => sl.Value * 100, v => sl.Value = v / 100.0);
                LengthSliders.Children.Add(sp);
            }
        }

        private static readonly (string name, Color c)[] SkinPresets =
        {
            ("밝은살", Color.FromRgb(0xF2, 0xC9, 0xA8)),
            ("보통살", Color.FromRgb(0xE8, 0xB6, 0x98)),
            ("황갈색", Color.FromRgb(0xC9, 0x94, 0x6B)),
            ("갈색",   Color.FromRgb(0x9A, 0x6A, 0x45)),
            ("짙은갈", Color.FromRgb(0x6B, 0x47, 0x2E)),
            ("회청(디자인)", Color.FromRgb(0x8A, 0xA6, 0xC0)),
        };

        private void BuildSwatches()
        {
            foreach (var (name, c) in SkinPresets)
                SkinSwatches.Children.Add(MakeSwatch(c, name, () => { _scene.SkinColor = c; RenderScene(); }));

            Color[] outlineCols = { Color.FromRgb(0xE6, 0xA0, 0x3C), Colors.Black, Color.FromRgb(0x1C, 0x7E, 0xD6), Color.FromRgb(0x2B, 0x8A, 0x3E), Colors.White };
            foreach (var c in outlineCols)
                OutlineSwatches.Children.Add(MakeSwatch(c, c.ToString(), () => { _outlineColor = c; RenderScene(); }));
        }

        private Border MakeSwatch(Color c, string tip, Action onClick)
        {
            var b = new Border
            {
                Width = 16,
                Height = 16,
                Margin = new Thickness(1),
                Background = new SolidColorBrush(c),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x21, 0x25, 0x29)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Cursor = Cursors.Hand,
                ToolTip = tip
            };
            b.MouseLeftButtonUp += (s, e) => onClick();
            return b;
        }

        // ---------- 렌더링 ----------

        private void Recompute()
        {
            _scene.Recompute();
            RenderScene();
        }

        private double DpiScale => Math.Max(1.0, VisualTreeHelper.GetDpi(this).DpiScaleX);

        /// <summary>현재 화면과 같은 윤곽선 설정(캡처의 확장 렌더에도 동일 적용).</summary>
        private SoftRenderer.OutlineOptions? CurrentOutlineOptions()
        {
            if (!_outlineShow && !_outlineOnly) return null;
            return new SoftRenderer.OutlineOptions
            {
                Show = true,
                Only = _outlineOnly,
                AlwaysMode = !_outlineOcclude,
                ThicknessPx = _outlineThickness * DpiScale,
                Color = _outlineColor
            };
        }

        private WriteableBitmap _lastBitmap;

        // <260811_14-1> 상태 표시줄이 1줄보다 두꺼운 만큼(DIP). 캡처 문구 같은 추가 줄이 붙으면
        // 상태 표시줄이 두꺼워지고 그래픽 영역이 그만큼 줄어, 배경 키보드·배경 이미지·3D 손이
        // 모두 절반만큼 위로 밀렸다(측정: 3줄 추가 시 키보드 -23.9 DIP, 손 -48px).
        // 해결: 그림의 기준을 '상태 표시줄 1줄일 때의 그래픽 영역'(= 두께와 무관한 고정 사각형)으로
        // 고정한다. 두꺼워진 만큼은 상태 표시줄이 그 아래를 덮을 뿐, 그림은 제자리에 있다.
        private double _statusOverflow;

        private void SyncStatusOverflow()
        {
            double extra = 0;
            for (int i = 1; i < StatusList.Items.Count; i++)
                if (StatusList.Items[i] is FrameworkElement fe) extra += fe.ActualHeight;
            if (Math.Abs(extra - _statusOverflow) < 0.5) return;

            _statusOverflow = extra;
            // 가운데 정렬 요소는 아래쪽 음수 여백의 절반만큼 내려가 고정 사각형의 중앙에 놓인다.
            var m = new Thickness(0, 0, 0, -extra);
            KbControl.Margin = m;
            BgImage.Margin = m;
            RenderScene();
        }

        private void RenderScene()
        {
            double dipW = GraphicsHost.ActualWidth, dipH = GraphicsHost.ActualHeight + _statusOverflow;
            if (dipW <= 0 || dipH <= 0) return;
            double scale = DpiScale;
            int w = (int)(dipW * scale), h = (int)(dipH * scale);
            _camera.Width = w; _camera.Height = h;

            _renderer.WorldScale = _scene.OverallScale;   // qksqhr: 윤곽 임계값 크기 비례
            var bmp = _renderer.Render(_camera, _scene.GetRenderMeshes(!_outlineOnly), 96 * scale, CurrentOutlineOptions(), BuildBoneSegs());
            _lastBitmap = bmp;
            SceneImage.Source = bmp;
            // <260811_5>/<260811_5-1>/<260811_5-2> 충돌 감지(부위 쌍 수집) — 오버레이에 그리므로 먼저 계산
            DetectFingerCollision(_fingerColPairs);
            DetectHandsCollision(_handColPairs);
            UpdateOverlay();
            SaveVerify(bmp);

            // 화면이 바뀐 뒤 잠시 조용해지면 실행 취소 스냅샷을 남긴다 (<260810_7>).
            ScheduleHistorySnapshot();
        }

        // ---------- 오버레이: 관절 라벨(표시/크기) + 빨간 고정점 + xyz 축 ----------
        // 오버레이 자체(라벨·축·충돌 경고 등)는 캡처에 포함되지 않는다 (3-10). <260811_24>: 단, 활성화된
        // 빨간 고정점만은 예외 — ComputeActivePinDotsPx() 로 물리 픽셀 좌표를 따로 계산해 비트맵
        // 캡처에 다시 그려 넣는다(Capture.SaveBitmap 의 pinDots). 벡터(SVG) 캡처는 대상이 아니다.

        private readonly HashSet<string> _pins = new HashSet<string>();
        public IReadOnlyCollection<string> Pins => _pins;

        private void UpdateOverlay()
        {
            Overlay.Children.Clear();
            double s = DpiScale;

            // 관절 약어 라벨 (3-3-2-2). 축(↕↔) 구분 없이 관절당 하나, 아치는 "아치".
            if (_labelsShown)
            {
                foreach (var j in _scene.AllJoints())
                {
                    if (j.IsTip) continue;
                    var p = _camera.Project(j.Pos);
                    if (!p.InFront) continue;
                    AddOverlayText(j.Label, p.X / s + 5, p.Y / s - 6, 9 * _labelScale, Brushes.DarkSlateBlue);
                }
                foreach (var side in new[] { HandSide.Left, HandSide.Right })
                {
                    var res = _scene.ResultFor(side);
                    if (res == null) continue;
                    // 아치 라벨: 약지·소지 MCP 중간에서 손목쪽으로 당긴 위치(CMC 들 가운데)
                    System.Windows.Media.Media3D.Point3D? ring = null, pinky = null;
                    foreach (var j in res.Joints)
                    {
                        if (j.Kind == JointKind.MCP && j.Digit == Digit.Ring) ring = j.Pos;
                        if (j.Kind == JointKind.MCP && j.Digit == Digit.Pinky) pinky = j.Pos;
                    }
                    if (ring.HasValue && pinky.HasValue)
                    {
                        var mid = new System.Windows.Media.Media3D.Point3D(
                            (ring.Value.X + pinky.Value.X) / 2, (ring.Value.Y + pinky.Value.Y) / 2, (ring.Value.Z + pinky.Value.Z) / 2);
                        var toward = res.WristCenter - mid;
                        var pos = mid + toward * 0.45;
                        var p = _camera.Project(pos);
                        if (p.InFront)
                            AddOverlayText("아치", p.X / s + 5, p.Y / s - 6, 9 * _labelScale, Brushes.DarkSlateBlue);
                    }
                }
            }

            // 빨간 고정점 (3-3-3-1). 화면 표시는 물리 픽셀 계산(ComputeActivePinDotsPx, <260811_24>
            // 비트맵 캡처와 공유)을 DIP 로 나눠 그린다 — 화면·캡처가 같은 반지름·불투명도를 쓰게 된다.
            foreach (var (posPx, rPxPhys, opacity) in ComputeActivePinDotsPx())
            {
                double rPx = rPxPhys / s;
                var dot = new System.Windows.Shapes.Ellipse
                {
                    Width = rPx * 2,
                    Height = rPx * 2,
                    Fill = Brushes.Red,
                    Opacity = opacity
                };
                Canvas.SetLeft(dot, posPx.X / s - rPx);
                Canvas.SetTop(dot, posPx.Y / s - rPx);
                Overlay.Children.Add(dot);
            }

            DrawGizmo(s);

            // <260811_5-1> 충돌 경고 — 그래픽 영역 상단 중앙, 굵은 빨간 글씨(캡처에는 포함되지 않음).
            {
                double cw = GraphicsHost.ActualWidth;
                double yWarn = 10;
                void Warn(string msg, Brush color)
                {
                    var tb = new TextBlock
                    {
                        Text = msg,
                        FontSize = 17,
                        FontWeight = FontWeights.Bold,
                        Foreground = color
                    };
                    tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Canvas.SetLeft(tb, Math.Max(4, (cw - tb.DesiredSize.Width) / 2));
                    Canvas.SetTop(tb, yWarn);
                    Overlay.Children.Add(tb);
                    yWarn += tb.DesiredSize.Height + 2;
                }
                // <260811_5-2> 충돌 부위를 문구 앞에 밝힌다: "(오른손 검지, 오른손 중지)손가락끼리 …"
                if (_fingerCollision) Warn($"경고: {PairsText(_fingerColPairs)}손가락끼리 충돌(살 관통)하고 있습니다.", Brushes.Red);
                if (_handsCollision) Warn($"경고: {PairsText(_handColPairs)}두 손이 충돌하고 있습니다.", Brushes.Red);
                // <260811_18>(1) 잠깐 띄우는 안내 문구(예: "달라진 것이 없습니다.") — 경고가 아니라
                // 알림이므로 안내 문구 색(#1c7ed6)을 쓴다.
                if (_graphicsMsg != null) Warn(_graphicsMsg, new SolidColorBrush(Color.FromRgb(0x1C, 0x7E, 0xD6)));
            }
        }

        private void AddOverlayText(string text, double x, double y, double size, Brush brush)
        {
            var tb = new TextBlock { Text = text, FontSize = size, Foreground = brush, FontWeight = FontWeights.SemiBold };
            Canvas.SetLeft(tb, x);
            Canvas.SetTop(tb, y);
            Overlay.Children.Add(tb);
        }

        private JointNode FindJoint(string id)
        {
            foreach (var j in _scene.AllJoints()) if (j.Id == id) return j;
            return null;
        }

        /// <summary>
        /// <260811_24> 활성화된 빨간 고정점을 **물리 픽셀** 좌표(위치·반지름)+불투명도로 계산.
        /// UpdateOverlay(화면, DIP=이 값/DpiScale)와 CaptureBitmapCore(비트맵 캡처, 이 값 그대로)가
        /// 공유한다 — 호출 시점의 _camera 투영 상태를 그대로 쓰므로, 캡처의 확장 렌더처럼 카메라
        /// 크기·팬을 임시로 바꿔 다시 투영한 직후에 불러도 그 상태 기준으로 정확하다.
        /// </summary>
        private List<(Point pos, double r, double opacity)> ComputeActivePinDotsPx()
        {
            var list = new List<(Point, double, double)>();
            double opacity = 1.0 - PinOpacity.Value / 100.0;
            if (opacity <= 0.001) return list;
            foreach (string id in _pins)
            {
                var j = FindJoint(id);
                if (j == null) continue;
                var p = _camera.Project(j.Pos);
                if (!p.InFront) continue;
                // 지름 = 손가락 굵기보다 작게: 관절 반지름의 투영 크기 * 0.9
                var pr = _camera.Project(j.Pos + _camera.Right * (j.Radius * 0.45));
                double rPx = Math.Max(3, Math.Abs(pr.X - p.X));
                list.Add((new Point(p.X, p.Y), rPx, opacity));
            }
            return list;
        }

        // xyz 좌표축(작은 기즈모, 좌하단). 드래그로 시점 회전 (3-4).
        private Point _gizmoCenter;
        private const double GizmoRadius = 46;

        private void DrawGizmo(double s)
        {
            double cx = 56, cy = GraphicsHost.ActualHeight - 62;
            _gizmoCenter = new Point(cx, cy);
            var axes = new (System.Windows.Media.Media3D.Vector3D v, Brush b, string name)[]
            {
                (new System.Windows.Media.Media3D.Vector3D(1, 0, 0), Brushes.Red, "x"),
                (new System.Windows.Media.Media3D.Vector3D(0, 1, 0), Brushes.Green, "y"),
                (new System.Windows.Media.Media3D.Vector3D(0, 0, 1), Brushes.RoyalBlue, "z"),
            };
            foreach (var (v, b, name) in axes)
            {
                double dx = System.Windows.Media.Media3D.Vector3D.DotProduct(v, _camera.Right);
                double dy = System.Windows.Media.Media3D.Vector3D.DotProduct(v, _camera.Up);
                var line = new System.Windows.Shapes.Line
                {
                    X1 = cx, Y1 = cy,
                    X2 = cx + dx * 34, Y2 = cy - dy * 34,
                    Stroke = b, StrokeThickness = 2.2
                };
                Overlay.Children.Add(line);
                AddOverlayText(name, cx + dx * 40 - 4, cy - dy * 40 - 8, 11, b);
            }
        }

        // ---------- 시점 제어 (3-4) ----------

        private Point _lastMouse;
        private bool _dragging, _panning;
        // [Ctrl]+한 손 드래그 = 그 손만 위치 이동 (<260810_3>(6))
        private bool _moveDragging;
        private HandSide _moveSide;
        private Point _moveStartMouse;
        private Vector3D _moveStartOffset;
        // [Ctrl] 키보드/배경 이미지 조작 상태 (3-6-1, <260719_10>)
        private bool _kbDragging, _kbMoved;
        private Point _kbStart;
        private double _kbScaleStart;
        private double _bgScaleStartX = 1, _bgScaleStartY = 1;

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);
        // 손 드래그(IK) 상태 (3-3-3)
        private bool _handDown, _handMoved;
        private Point _handStart;
        private HandSide _ikSide;
        private Digit _ikDigit;
        private string _ikHandleId;
        private double _ikDepth;
        private Dictionary<string, System.Windows.Media.Media3D.Point3D> _ikPinsBefore;

        /// <summary>화면 DIP 좌표의 렌더 버퍼 태그(-1 = 손 아님).</summary>
        private int TagAt(Point dip)
        {
            var tags = _renderer.TagBuffer;
            if (tags == null) return -1;
            int px = (int)(dip.X * DpiScale), py = (int)(dip.Y * DpiScale);
            if (px < 0 || py < 0 || px >= _renderer.RW || py >= _renderer.RH) return -1;
            return tags[py * _renderer.RW + px];
        }

        private void Graphics_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(GraphicsHost);
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                // <260810_3>(6): [Ctrl]+한 손 드래그 = 그 손만 '위치 이동'.
                int moveTag = TagAt(pos);
                if (moveTag >= 0 && e.ChangedButton == MouseButton.Left)
                {
                    _moveDragging = true;
                    _moveSide = moveTag >= 10 ? HandSide.Right : HandSide.Left;
                    _moveStartMouse = pos;
                    _moveStartOffset = _scene.OffsetFor(_moveSide);
                    _camera.Update();   // Right/Up·픽셀당 월드 단위 최신화
                    GraphicsHost.CaptureMouse();
                    return;
                }

                // [Ctrl] = 배경(키보드/이미지) 조작. 3D 손/시점은 영향받지 않는다 (3-6-3).
                if ((_keyboard != KbKind.None || BgImageActive) && e.ChangedButton == MouseButton.Left)
                {
                    _kbDragging = true; _kbMoved = false;
                    _kbStart = pos; _kbScaleStart = KbScale.ScaleX;
                    _bgScaleStartX = BgScale.ScaleX; _bgScaleStartY = BgScale.ScaleY;
                    GraphicsHost.CaptureMouse();
                }
                return;
            }

            GraphicsHost.CaptureMouse();
            Focus();
            _lastMouse = pos;

            // xyz 좌표축 기즈모 드래그 = 시점 회전 (3-4)
            if ((pos - _gizmoCenter).Length < GizmoRadius)
            {
                _dragging = true; _panning = false;
                return;
            }

            int tag = TagAt(pos);
            bool onHand = tag >= 0 && e.ChangedButton == MouseButton.Left &&
                          !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            if (onHand)
            {
                // 손 위: 클릭=고정점, 드래그=IK (배경 드래그만 시점 회전)
                _handDown = true; _handMoved = false; _handStart = pos;
                _ikSide = tag >= 10 ? HandSide.Right : HandSide.Left;
                int digitCode = tag % 10;
                _ikDigit = digitCode == 0 ? Digit.Middle : (Digit)(digitCode - 1);  // 손바닥이면 IK 없음(아래서 걸러짐)
                _ikIsPalm = digitCode == 0;
                _camera.Update();
                _ikDepth = _renderer.TagDepthBuffer[( (int)(pos.Y * DpiScale)) * _renderer.RW + (int)(pos.X * DpiScale)];
                return;
            }

            _panning = e.RightButton == MouseButtonState.Pressed || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            _dragging = !_panning;
        }

        private bool _ikIsPalm;

        private void Graphics_MouseMove(object sender, MouseEventArgs e)
        {
            var p = e.GetPosition(GraphicsHost);
            if (_moveDragging)
            {
                // <260810_3>(6): 픽셀 이동량을 카메라 Right/Up 방향의 월드 이동으로 바꿔 그 손만 옮긴다.
                double wpp = _camera.WorldPerPixelAtTarget() * DpiScale;
                Vector3D delta = _camera.Right * ((p.X - _moveStartMouse.X) * wpp)
                               + _camera.Up * (-(p.Y - _moveStartMouse.Y) * wpp);
                Vector3D want = _moveStartOffset + delta;
                Vector3D cur = _scene.OffsetFor(_moveSide);
                Vector3D d = want - cur;
                if (_moveSide == HandSide.Left) MoveHands(d, new Vector3D());
                else MoveHands(new Vector3D(), d);
                return;
            }
            if (_kbDragging)
            {
                double kdx = p.X - _kbStart.X;
                double kdy = p.Y - _kbStart.Y;
                if (Math.Abs(kdy) > 4 || Math.Abs(kdx) > 4) _kbMoved = true;
                if (_kbMoved)
                {
                    if (BgImageActive)
                    {
                        // <260719_10>: [Ctrl]+드래그 = 배경 이미지 크기(비율 유지),
                        // [Ctrl]+[Shift]+드래그 = 가로/세로 비율 무시(가로=좌우, 세로=상하 드래그)
                        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                        {
                            BgScale.ScaleX = Clamp(_bgScaleStartX * Math.Pow(1.35, kdx / 120.0), 0.1, 6.0);
                            BgScale.ScaleY = Clamp(_bgScaleStartY * Math.Pow(1.35, -kdy / 120.0), 0.1, 6.0);
                        }
                        else
                        {
                            double f = Math.Pow(1.35, -kdy / 120.0);
                            BgScale.ScaleX = Clamp(_bgScaleStartX * f, 0.1, 6.0);
                            BgScale.ScaleY = Clamp(_bgScaleStartY * f, 0.1, 6.0);
                        }
                    }
                    else
                    {
                        // 위로 드래그 = 확대, 아래로 = 축소. 가로세로 비율 고정 (3-6-1).
                        double sK = Clamp(_kbScaleStart * Math.Pow(1.35, -kdy / 120.0), 0.3, 3.0);
                        KbScale.ScaleX = KbScale.ScaleY = sK;
                    }
                }
                return;
            }

            if (_handDown)
            {
                if (!_handMoved && (Math.Abs(p.X - _handStart.X) > 4 || Math.Abs(p.Y - _handStart.Y) > 4))
                {
                    if (_ikIsPalm) { _handDown = false; return; }   // 손바닥 드래그는 IK 대상 아님
                    _handMoved = true;
                    BeginIkDrag();
                }
                if (_handMoved)
                {
                    // 마우스 광선 위 초기 깊이 지점 = 목표
                    _camera.Update();
                    _camera.Ray(p.X * DpiScale, p.Y * DpiScale, out var origin, out var dir);
                    var target = origin + dir * _ikDepth;
                    SolveIk(target);
                    _scene.Recompute();
                    RenderScene();
                }
                return;
            }

            if (!_dragging && !_panning) return;
            double dx = p.X - _lastMouse.X, dyy = p.Y - _lastMouse.Y;
            _lastMouse = p;

            bool wasOob = FingersOutOfBounds();
            double yaw0 = _camera.YawDeg, pitch0 = _camera.PitchDeg, panX0 = _camera.PanX, panY0 = _camera.PanY;
            if (_dragging)
            {
                _camera.YawDeg -= dx * 0.4;
                // 시점 회전 슬라이더(±180) 표시와 어긋나지 않게 yaw 를 (-180,180]로 정규화
                if (_camera.YawDeg > 180) _camera.YawDeg -= 360;
                if (_camera.YawDeg <= -180) _camera.YawDeg += 360;
                _camera.PitchDeg += dyy * 0.4;
                if (_camera.PitchDeg > 88) _camera.PitchDeg = 88;
                if (_camera.PitchDeg < -88) _camera.PitchDeg = -88;
            }
            else // pan
            {
                _camera.PanX += dx * DpiScale;
                _camera.PanY += dyy * DpiScale;
                ClampPan();   // '손 위치' 공통 슬라이더 범위와 일치(표시-실제 괴리 방지)
            }
            // (3-12) 손가락이 영역을 벗어나는 시점 조작은 되돌린다
            if (!wasOob && FingersOutOfBounds())
            {
                _camera.YawDeg = yaw0; _camera.PitchDeg = pitch0; _camera.PanX = panX0; _camera.PanY = panY0;
                Status("그래픽 영역을 벗어나지 못하는 3D 손의 영역이 있습니다.", true);
                return;
            }
            if (_dragging) SyncViewRotSliders();        // <260810_3-1>(4)
            else SyncHandPosSliders();                  // 팬 → 공통 위치 표시 갱신
            RenderScene();
        }

        private void Graphics_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_moveDragging)
            {
                GraphicsHost.ReleaseMouseCapture();
                _moveDragging = false;
                return;
            }
            if (_kbDragging)
            {
                GraphicsHost.ReleaseMouseCapture();
                _kbDragging = false;
                if (!_kbMoved && _keyboard != KbKind.None)
                {
                    // [Ctrl]+클릭 = 키 토글 (Shift 제외 노란 키 1개 규칙은 KeyboardControl 이 적용)
                    var local = GraphicsHost.TranslatePoint(e.GetPosition(GraphicsHost), KbControl);
                    KbControl.ToggleAt(local);
                }
                // 키 토글·키보드/배경 스케일은 저장 상태의 일부 — 실행 취소 기록에 반영
                ScheduleHistorySnapshot();
                return;
            }
            if (_handDown)
            {
                GraphicsHost.ReleaseMouseCapture();
                bool click = !_handMoved;
                _handDown = _handMoved = false;
                if (click) TogglePinNear(e.GetPosition(GraphicsHost));
                return;
            }
            GraphicsHost.ReleaseMouseCapture();
            _dragging = _panning = false;
        }

        /// <summary>클릭 지점에서 가장 가까운 관절/손가락 끝의 빨간 고정점을 토글 (3-3-3-1).</summary>
        private void TogglePinNear(Point dip)
        {
            _camera.Update();
            double px = dip.X * DpiScale, py = dip.Y * DpiScale;
            string best = null;
            double bestD = 45 * DpiScale;   // 픽셀 반경 내에서만
            foreach (var j in _scene.AllJoints())
            {
                var p = _camera.Project(j.Pos);
                if (!p.InFront) continue;
                double d = Math.Sqrt((p.X - px) * (p.X - px) + (p.Y - py) * (p.Y - py));
                if (d < bestD) { bestD = d; best = j.Id; }
            }
            if (best == null) return;
            if (!_pins.Add(best)) _pins.Remove(best);
            UpdateOverlay();
            ScheduleHistorySnapshot();   // 고정점은 저장 상태의 일부 — 실행 취소 기록에 반영 (<260810_9> qksqhr)
        }

        // ----- 드래그 IK (3-3-3-1-1): 뼈·관절 구조(관절 각도)만 바꿔 목표를 따라간다 -----

        private string[] _ikDofs;

        private void BeginIkDrag()
        {
            _ikPinsBefore = CapturePinPositions();
            string code = Anatomy.FingerCode(_ikSide, _ikDigit);
            _ikDofs = _ikDigit == Digit.Thumb
                ? new[] { code + ".CMC.UD", code + ".CMC.LR", code + ".MCP.UD", code + ".MCP.LR", code + ".IP" }
                : new[] { code + ".MCP.UD", code + ".MCP.LR", code + ".PIP", code + ".DIP" };
            // 핸들 = 잡은 손가락의 끝(TIP)
            _ikHandleId = code + ".TIP";
        }

        private System.Windows.Media.Media3D.Point3D? EvalJoint(string id)
        {
            var res = _scene.BuildSide(_ikSide);   // 손 전체 회전 포함(화면 좌표와 일치)
            foreach (var j in res.Joints) if (j.Id == id) return j.Pos;
            return null;
        }

        private bool IkPinsViolated()
        {
            var res = _scene.BuildSide(_ikSide);
            foreach (var kv in _ikPinsBefore)
            {
                foreach (var j in res.Joints)
                    if (j.Id == kv.Key && (j.Pos - kv.Value).Length > 0.05) return true;
            }
            return false;
        }

        private void SolveIk(System.Windows.Media.Media3D.Point3D target)
        {
            // 좌표하강: 각 자유도를 여러 스텝 크기로 시도해 손끝-목표 거리를 줄인다.
            // 고정점을 위반하는 스텝은 버린다((3-3-3-1-3), 드래그는 (3-3-2-3)에서 자유).
            for (int iter = 0; iter < 6; iter++)
            {
                bool improvedAny = false;
                foreach (string key in _ikDofs)
                {
                    double cur = _scene.Pose.Get(key);
                    var hp = EvalJoint(_ikHandleId);
                    if (hp == null) return;
                    double bestD = (hp.Value - target).Length;
                    double bestVal = cur;
                    foreach (double step in new[] { 18.0, -18.0, 7.0, -7.0, 2.5, -2.5 })
                    {
                        double v = Math.Max(0, Math.Min(100, cur + step));
                        if (Math.Abs(v - cur) < 1e-9) continue;
                        _scene.Pose.Set(key, v);
                        if (IkPinsViolated()) { _scene.Pose.Set(key, cur); continue; }
                        var hp2 = EvalJoint(_ikHandleId);
                        double d = hp2.HasValue ? (hp2.Value - target).Length : double.MaxValue;
                        if (d < bestD - 1e-6) { bestD = d; bestVal = v; }
                        _scene.Pose.Set(key, cur);
                    }
                    if (Math.Abs(bestVal - cur) > 1e-9)
                    {
                        _scene.Pose.Set(key, bestVal);
                        SyncSlider(key, bestVal);
                        improvedAny = true;
                    }
                }
                if (!improvedAny) break;
            }
        }

        private void Graphics_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return; // Ctrl = 키보드 조작(배경)
            bool wasOob = FingersOutOfBounds();
            double d0 = _camera.Distance;
            _camera.Distance *= e.Delta > 0 ? 0.9 : 1.1;
            if (_camera.Distance < 12) _camera.Distance = 12;
            if (_camera.Distance > 400) _camera.Distance = 400;
            // (3-12) 줌인으로 손가락이 잘리게 되면 막는다(수용된 해석)
            if (!wasOob && FingersOutOfBounds())
            {
                _camera.Distance = d0;
                Status("그래픽 영역을 벗어나지 못하는 3D 손의 영역이 있습니다.", true);
                return;
            }
            RenderScene();
        }

        // <260810_3-1>(5): 방향키 = 팬(시점 평행이동)을 **반대로** 제어하는 '위치 이동' 착각 효과.
        // [←]를 누르면 카메라 창을 오른쪽으로 밀어(화면 이미지는 왼쪽으로 이동) 마치 두 손이
        // 왼쪽으로 이동한 것처럼 보인다. 손의 실제 좌표는 바뀌지 않는다.
        private void Window_ArrowKeyDown(object sender, KeyEventArgs e)
        {
            // 슬라이더 % 값 등 텍스트 입력 중이면 방향키·단축키를 가로채지 않는다(입력 칸 자체 편집 우선).
            if (Keyboard.FocusedElement is TextBox) return;

            // <260810_7> [Ctrl]+[Z] = 실행 취소, [Ctrl]+[Y] = 다시 실행
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (e.Key == Key.Z) { Undo(); e.Handled = true; return; }
                if (e.Key == Key.Y) { Redo(); e.Handled = true; return; }
                return;   // 그 밖의 [Ctrl] 조합은 기존 동작(키보드/배경 조작 등)에 맡긴다
            }
            double step = 18 * DpiScale;
            double px0 = _camera.PanX, py0 = _camera.PanY;
            bool wasOob = FingersOutOfBounds();
            switch (e.Key)
            {
                case Key.Left: _camera.PanX -= step; break;   // 손이 왼쪽으로 이동한 것처럼
                case Key.Right: _camera.PanX += step; break;  // 손이 오른쪽으로 이동한 것처럼
                case Key.Up: _camera.PanY -= step; break;     // 손이 위쪽으로 이동한 것처럼
                case Key.Down: _camera.PanY += step; break;   // 손이 아래쪽으로 이동한 것처럼
                default: return;
            }
            ClampPan();   // '손 위치' 공통 슬라이더 범위와 일치(표시-실제 괴리 방지)
            if (!wasOob && FingersOutOfBounds())
            {
                _camera.PanX = px0; _camera.PanY = py0;
                Status("그래픽 영역을 벗어나지 못하는 3D 손의 영역이 있습니다.", true);
                e.Handled = true;
                return;
            }
            SyncHandPosSliders();
            RenderScene();
            e.Handled = true;
        }

        /// <summary>팬을 '손 위치' 공통 슬라이더 범위(±CommonPanRange 논리픽셀)로 제한한다.</summary>
        private void ClampPan()
        {
            double lim = CommonPanRange * DpiScale;
            _camera.PanX = Clamp(_camera.PanX, -lim, lim);
            _camera.PanY = Clamp(_camera.PanY, -lim, lim);
        }

        /// <summary>손 오프셋 이동 공통부: 적용 → 영역 밖(3-12)이면 롤백+경고. 이동됐으면 true.
        /// 오프셋은 슬라이더 범위(±HandPosRange)로 클램프해 표시값과 실제 상태가 어긋나지 않게 한다.</summary>
        private bool MoveHands(Vector3D dLeft, Vector3D dRight)
        {
            Vector3D ClampOff(Vector3D o) => new Vector3D(
                Clamp(o.X, -HandPosRange, HandPosRange),
                Clamp(o.Y, -HandPosRange, HandPosRange),
                Clamp(o.Z, -HandPosRange, HandPosRange));

            Vector3D l0 = _scene.OffsetLeft, r0 = _scene.OffsetRight;
            bool wasOob = FingersOutOfBounds();
            _scene.OffsetLeft = ClampOff(l0 + dLeft);
            _scene.OffsetRight = ClampOff(r0 + dRight);
            _scene.Recompute();
            if (!wasOob && FingersOutOfBounds())
            {
                _scene.OffsetLeft = l0; _scene.OffsetRight = r0;
                _scene.Recompute();
                SyncHandPosSliders();   // 거부 시에도 슬라이더 표시를 실제 오프셋으로 되돌린다
                Status("그래픽 영역을 벗어나지 못하는 3D 손의 영역이 있습니다.", true);
                RenderScene();
                return false;
            }
            SyncHandPosSliders();
            RenderScene();
            return true;
        }

        // ---------- 실행 취소 / 다시 실행 (<260810_7>) ----------
        // 상태 전체(포즈·크기·색·카메라·키보드·고정점·손 회전/위치 …)를 저장 파일과 같은 JSON 스냅샷으로
        // 기록한다. 슬라이더 드래그처럼 값이 연속으로 바뀌는 조작은 잠깐 멈춘 뒤에 한 번만 기록해
        // ([Ctrl]+[Z] 한 번에 드래그 한 동작이 통째로 취소되도록) 히스토리가 잘게 쪼개지지 않게 한다.

        private readonly List<string> _history = new List<string>();
        private int _histIndex = -1;
        private bool _applyingHistory;
        private System.Windows.Threading.DispatcherTimer _histTimer;
        private const int HistoryLimit = 100;

        /// <summary>상태가 바뀐 뒤 호출. 잠시 조용해지면 스냅샷 하나를 기록한다.</summary>
        private void ScheduleHistorySnapshot()
        {
            if (_applyingHistory || !IsLoaded) return;
            if (_histTimer == null)
            {
                _histTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(450)
                };
                _histTimer.Tick += (s, e) => { _histTimer.Stop(); CommitHistorySnapshot(); };
            }
            _histTimer.Stop();
            _histTimer.Start();
        }

        private void CommitHistorySnapshot()
        {
            if (_applyingHistory) return;
            string json;
            try { json = SceneState.Serialize(_scene, _camera, this); }
            catch { return; }

            if (_histIndex >= 0 && _histIndex < _history.Count && _history[_histIndex] == json) return; // 실제 변화 없음

            // 되돌린 뒤 새로 조작하면 그 앞의 '다시 실행' 기록은 버린다(일반적인 편집기 동작).
            if (_histIndex < _history.Count - 1)
                _history.RemoveRange(_histIndex + 1, _history.Count - _histIndex - 1);

            _history.Add(json);
            if (_history.Count > HistoryLimit) _history.RemoveAt(0);
            _histIndex = _history.Count - 1;
        }

        private void Undo()
        {
            _histTimer?.Stop();
            CommitHistorySnapshot();          // 진행 중이던 변경을 먼저 확정
            if (_histIndex <= 0) { Status("더 취소할 작업이 없습니다.", true); return; }
            _histIndex--;
            ApplyHistory();
            Status("실행 취소 (" + (_histIndex + 1) + "/" + _history.Count + ")");
        }

        private void Redo()
        {
            _histTimer?.Stop();
            // Undo와 동일하게 진행 중(미커밋) 편집을 먼저 확정한다 — 안 하면 Undo 후 새로 한 편집을
            // [Ctrl]+[Y]가 흔적 없이 파괴한다(새 편집이 있으면 redo 기록이 잘려 아래 가드에 걸리는 게 정상).
            CommitHistorySnapshot();
            if (_histIndex >= _history.Count - 1) { Status("다시 실행할 작업이 없습니다.", true); return; }
            _histIndex++;
            ApplyHistory();
            Status("다시 실행 (" + (_histIndex + 1) + "/" + _history.Count + ")");
        }

        private void ApplyHistory()
        {
            _applyingHistory = true;
            try
            {
                SceneState.Deserialize(_history[_histIndex], _scene, _camera, this);
                SyncUiFromScene();
                _scene.Recompute();
                RenderScene();
            }
            catch { }
            finally { _applyingHistory = false; }
        }

        // ---------- 상단 패널 핸들러 ----------

        /// <summary>숫자 라벨을 클릭하면 입력 칸으로 바뀌어 키보드로 값을 넣을 수 있게 한다 (<260810_3-2>(12)).
        /// [Enter]/포커스 잃음 = 적용, [Esc] = 취소. 값 범위는 연결된 슬라이더가 클램프한다.</summary>
        private void MakeValueEditable(TextBlock label, Func<double> get, Action<double> set)
        {
            if (!(label.Parent is Panel panel)) return;
            var box = new TextBox
            {
                FontSize = label.FontSize,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Visibility = Visibility.Collapsed,
                Padding = new Thickness(0),
            };
            panel.Children.Insert(panel.Children.IndexOf(label) + 1, box);

            void End(bool commit)
            {
                if (box.Visibility != Visibility.Visible) return;
                // NaN 은 슬라이더 클램프를 통과해 Slider.Value 예외를 일으키므로 거부한다
                if (commit && double.TryParse(box.Text, System.Globalization.NumberStyles.Any,
                                              System.Globalization.CultureInfo.InvariantCulture, out double v)
                    && !double.IsNaN(v))
                    set(v);
                box.Visibility = Visibility.Collapsed;
                label.Visibility = Visibility.Visible;
            }

            label.Cursor = Cursors.Hand;
            label.MouseLeftButtonUp += (s, e) =>
            {
                // <260811_8>: 입력 상자 폭을 라벨 실제 폭에 맞춰, 편집 중에도 패널 좌우가 안 늘어나게.
                box.Width = Math.Max(24, label.ActualWidth);
                box.Text = get().ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
                box.Visibility = Visibility.Visible;
                label.Visibility = Visibility.Collapsed;
                box.Focus();
                box.SelectAll();
            };
            box.KeyDown += (s, e) => { if (e.Key == Key.Enter) End(true); else if (e.Key == Key.Escape) End(false); };
            box.LostFocus += (s, e) => End(true);
        }

        private void FullyTransparent_Click(object sender, RoutedEventArgs e)
        {
            TranspSlider.Value = 100;
        }

        // <260810_3>(8): 슬라이더를 맨 왼쪽(투명도 0 = 완전히 불투명)으로.
        private void FullyOpaque_Click(object sender, RoutedEventArgs e)
        {
            TranspSlider.Value = 0;
        }

        private void Color_Click(object sender, RoutedEventArgs e)
        {
            var win = new ColorWindow(_scene.SkinColor) { Owner = this };
            if (win.ShowDialog() == true) { _scene.SkinColor = win.SelectedColor; RenderScene(); }
        }

        private void Outline_Changed(object sender, RoutedEventArgs e)
        {
            _outlineShow = OutlineShow.IsChecked == true;
            _outlineOnly = OutlineOnly.IsChecked == true;
            RenderScene();
        }

        private void OutlineMode_Click(object sender, RoutedEventArgs e)
        {
            _outlineOcclude = !_outlineOcclude;
            OutlineModeBtn.Content = _outlineOcclude ? "모드: 가림" : "모드: 항상";
            RenderScene();
        }

        private void TipColor_Click(object sender, RoutedEventArgs e)
        {
            var win = new TipColorWindow(_scene) { Owner = this };
            win.ShowDialog();
            RenderScene();
        }

        private void Keyboard_Changed(object sender, RoutedEventArgs e)
        {
            _keyboard = KbNone.IsChecked == true ? KbKind.None : (KbDubeol.IsChecked == true ? KbKind.Dubeolsik : KbKind.Qwerty);
            KbControl.Visibility = _keyboard == KbKind.None ? Visibility.Collapsed : Visibility.Visible;
            KbControl.Dubeolsik = _keyboard == KbKind.Dubeolsik;
            KbControl.InvalidateVisual();
            Status();
            RenderScene();
        }

        private void ToggleLabels_Click(object sender, RoutedEventArgs e)
        {
            _labelsShown = !_labelsShown;
            RenderScene();
        }

        private void LabelSize_Click(object sender, RoutedEventArgs e)
        {
            // 해석 ⓙ: '크기' 버튼 클릭 → 작은 슬라이더 팝업으로 약어 글씨 크기 조절
            var slider = new Slider { Minimum = 0.6, Maximum = 2.2, Value = _labelScale, Width = 140, TickFrequency = 0.2 };
            slider.ValueChanged += (o, a) => { _labelScale = slider.Value; UpdateOverlay(); ScheduleHistorySnapshot(); };
            var border = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x21, 0x25, 0x29)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 4, 8, 4),
                Child = slider
            };
            var popup = new System.Windows.Controls.Primitives.Popup
            {
                PlacementTarget = sender as UIElement,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                StaysOpen = false,
                Child = border,
                IsOpen = true
            };
        }

        // ---------- 카메라 캡처 (3-10) ----------

        // 캡처 대상 (<260719_9>): 왼손만/오른손만/둘 다. 비트맵은 '둘 다'만 가능.
        public enum CaptureTarget { Both, Left, Right }
        private CaptureTarget _capTarget = CaptureTarget.Both;

        private void CaptureTarget_Changed(object sender, RoutedEventArgs e)
        {
            _capTarget = CapLeft.IsChecked == true ? CaptureTarget.Left
                       : CapRight.IsChecked == true ? CaptureTarget.Right : CaptureTarget.Both;
            BitmapCamBtn.IsEnabled = _capTarget == CaptureTarget.Both;
            if (_capTarget != CaptureTarget.Both)
                Status("비트맵 캡처는 '둘 다'일 때만 가능합니다.");
            else Status();
            ScheduleHistorySnapshot();   // 캡처 대상은 저장 상태의 일부
        }

        // ---------- 배경 이미지 (<260719_10>): 트레이싱용 ----------

        private string _bgImagePath;

        private void BgImageOpen_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "이미지 파일|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|모든 파일|*.*"
            };
            if (dlg.ShowDialog() != true) return;
            LoadBgImage(dlg.FileName);
        }

        /// <summary>
        /// qksqhr(2026-09-13) 보안: path 가 UNC(\\서버\공유\...) 등 네트워크 경로가 아닌 로컬 파일
        /// 경로인지 확인한다. 저장 파일에서 불러온 BgImagePath 처럼 신뢰할 수 없는 경로를
        /// File.Exists/이미지 로드에 넘기기 전 걸러내는 용도(사용자가 대화상자로 직접 고른
        /// 경로는 이 검사를 거치지 않는다 — 본인이 의도한 선택이므로).
        /// qksqhr(2026-09-13) 후속: Uri 기반 판정은 Win32 확장 길이 경로(\\?\C:\...)를 UNC와
        /// 구분하지 못해 정상적인 로컬 경로까지 거부했다(code-review 지적, 재현 확인) — 문자열
        /// 규칙으로 다시 짰다: \\?\ · \\.\ 접두어는 벗겨서 판정하되, 그 뒤가 "UNC\"면(확장형
        /// UNC 표기) 네트워크 경로로 계속 거부한다.
        /// </summary>
        private static bool IsLocalFilePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string p = path;
            if (p.StartsWith(@"\\?\", StringComparison.Ordinal) || p.StartsWith(@"\\.\", StringComparison.Ordinal))
                p = p.Substring(4);
            if (p.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase)) return false;
            if (p.StartsWith(@"\\", StringComparison.Ordinal) || p.StartsWith("//", StringComparison.Ordinal))
                return false;
            // 남은 형태는 "C:\..." 처럼 드라이브 문자로 시작하는 로컬 경로여야 한다.
            return p.Length >= 3 && char.IsLetter(p[0]) && p[1] == ':' && (p[2] == '\\' || p[2] == '/');
        }

        private void LoadBgImage(string path)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                BgImage.Source = bmp;
                // 그래픽 영역에 맞게 초기 표시 크기(비율 유지) 지정
                double fit = Math.Min(GraphicsHost.ActualWidth * 0.9 / bmp.PixelWidth,
                                      GraphicsHost.ActualHeight * 0.9 / bmp.PixelHeight);
                if (fit <= 0 || double.IsInfinity(fit)) fit = 1;
                BgImage.Width = bmp.PixelWidth * fit;
                BgImage.Height = bmp.PixelHeight * fit;
                BgScale.ScaleX = BgScale.ScaleY = 1.0;
                BgImage.Visibility = Visibility.Visible;
                _bgImagePath = path;
                BgFileLabel.Text = System.IO.Path.GetFileName(path);
                Status();
                ScheduleHistorySnapshot();   // 배경 이미지는 저장 상태의 일부
            }
            catch (Exception ex) { Status("배경 이미지 열기 실패: " + ex.Message, true); }
        }

        private void BgImageClear_Click(object sender, RoutedEventArgs e)
        {
            BgImage.Source = null;
            BgImage.Visibility = Visibility.Collapsed;
            _bgImagePath = null;
            BgFileLabel.Text = "(없음)";
            Status();
            ScheduleHistorySnapshot();   // 배경 이미지 제거도 실행 취소 기록에 반영
        }

        private bool BgImageActive => BgImage.Visibility == Visibility.Visible && BgImage.Source != null;

        private (string label, string rowcol)? CaptureKeyInfo()
            => _keyboard == KbKind.None ? null : KbControl.GetCaptureKey();

        /// <summary>
        /// <260811_15> '캡처 시 파일 이름 수동 입력'일 때 쓰는 저장 대화상자. 자동 이름을 미리 채워
        /// 주고, 취소하면 null(= 캡처 중단). 대화상자는 사용자 클릭 경로에서만 띄운다 — 헤드리스
        /// 검증(-edgecap 등)이 부르는 CaptureBitmapCore 안에 두면 멈춰 버린다.
        /// </summary>
        private string AskCaptureFileName(string title, string filter, string ext, string dir)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = title,
                Filter = filter,
                DefaultExt = ext,
                FileName = Capture.AutoFileName(CaptureKeyInfo()?.label) + ext,
                InitialDirectory = System.IO.Directory.Exists(dir) ? dir : null,
                OverwritePrompt = true,
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        private void BitmapCapture_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string manual = null;
                if (AppSettings.PngManualName)
                {
                    manual = AskCaptureFileName("비트맵 캡처(PNG)", "PNG 이미지 (*.png)|*.png", ".png", AppSettings.PngDir);
                    if (manual == null) { Status("비트맵 캡처를 취소했습니다."); return; }
                }

                // (3-12-2) 영역 밖으로 나간 부분(손목쪽)이 있으면 포함 여부를 묻는다
                bool expand = false;
                if (HandTouchesEdge())
                {
                    var r = MessageBox.Show(this,
                        "3D 손의 일부가 그래픽 영역 밖으로 나가 있습니다.\n밖으로 나간 부분을 캡처에 포함할까요?",
                        "비트맵 캡처", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    expand = r == MessageBoxResult.Yes;
                }
                string path = CaptureBitmapCore(expand, manual);
                Status($"비트맵 캡처 저장: {path}");
            }
            catch (Exception ex) { Status("캡처 실패: " + ex.Message, true); }
        }

        /// <summary>비트맵 캡처 실행. includeOutside 면 캔버스를 사방으로 넓혀 잘린 부분까지 담는다.
        /// explicitPath 는 <260811_15> 수동 이름 입력으로 정해진 전체 경로(없으면 자동 이름).</summary>
        private string CaptureBitmapCore(bool includeOutside, string explicitPath = null)
        {
            double s = DpiScale;

            Visual kbVis = null;
            Rect kbRect = Rect.Empty;
            if (_keyboard != KbKind.None && KbControl.ActualWidth > 0)
            {
                var origin = KbControl.TranslatePoint(new Point(0, 0), GraphicsHost);
                double kw = KbControl.ActualWidth * KbScale.ScaleX;
                double kh = KbControl.ActualHeight * KbScale.ScaleY;
                kbRect = new Rect(origin.X * s, origin.Y * s, kw * s, kh * s);

                // <260811_14>: 화면의 KbControl 을 그대로 RenderTargetBitmap 으로 구우면 레이아웃
                // 오프셋(가운데 정렬)과 KbScale(RenderTransform)까지 함께 구워져, 자연 크기 비트맵
                // 안에서 내용이 오른쪽·아래로 밀리고 잘린 채 kbRect 에 얹혔다(배치 어긋남+일부 잘림).
                // 같은 상태의 오프스크린 사본을 (0,0)에 캡처 픽셀 크기로 배치해 구우면 오프셋·변환이
                // 없고, 벡터라 최종 해상도 그대로 선명하다.
                var clone = new KeyboardUi.KeyboardControl
                {
                    Dubeolsik = KbControl.Dubeolsik,
                    PressedKey = KbControl.PressedKey,
                    LeftShiftPressed = KbControl.LeftShiftPressed,
                    RightShiftPressed = KbControl.RightShiftPressed,
                };
                clone.Measure(new Size(kbRect.Width, kbRect.Height));
                clone.Arrange(new Rect(0, 0, kbRect.Width, kbRect.Height));
                kbVis = clone;
            }

            if (!includeOutside)
                // <260811_24>: 지금 _camera 상태(마지막 RenderScene 과 같음)로 고정점을 투영 — _lastBitmap 과 좌표계 일치
                return Capture.SaveBitmap(_lastBitmap, CaptureKeyInfo(), kbVis, kbRect, 96 * s, explicitPath, ComputeActivePinDotsPx());

            // 확장 렌더: 같은 투영으로 캔버스만 사방 m 픽셀 넓혀 다시 그린다(키보드 위치도 +m 이동)
            int m = Math.Max(_camera.Width, _camera.Height) / 2;
            int w0 = _camera.Width, h0 = _camera.Height;
            double px0 = _camera.PanX, py0 = _camera.PanY;
            try
            {
                // 캔버스만 키우고 투영 스케일은 화면과 동일하게(ViewW/H) — 아니면 확대가 돼 버린다
                _camera.Width = w0 + 2 * m; _camera.Height = h0 + 2 * m;
                _camera.ViewW = w0; _camera.ViewH = h0;
                _camera.PanX = px0 + m; _camera.PanY = py0 + m;
                _renderer.WorldScale = _scene.OverallScale;
                var big = _renderer.Render(_camera, _scene.GetRenderMeshes(!_outlineOnly), 96 * s, CurrentOutlineOptions(), BuildBoneSegs());
                if (!kbRect.IsEmpty) kbRect = new Rect(kbRect.X + m, kbRect.Y + m, kbRect.Width, kbRect.Height);
                // <260811_24>: Render() 가 방금 cam.Update() 를 이 확장된 크기·팬으로 돌렸으므로,
                // 지금 투영해야 big 의 좌표계(원본보다 사방 m 만큼 넓음)와 일치한다.
                return Capture.SaveBitmap(big, CaptureKeyInfo(), kbVis, kbRect, 96 * s, explicitPath, ComputeActivePinDotsPx());
            }
            finally
            {
                _camera.Width = w0; _camera.Height = h0;
                _camera.ViewW = 0; _camera.ViewH = 0;
                _camera.PanX = px0; _camera.PanY = py0;
                RenderScene();   // 화면 상태 복원
            }
        }

        /// <summary>손(팔 포함)의 일부가 그래픽 영역 가장자리에서 잘려 있는지.</summary>
        private bool HandTouchesEdge()
        {
            var tags = _renderer.TagBuffer;
            if (tags == null) return false;
            int w = _renderer.RW, h = _renderer.RH;
            for (int x = 0; x < w; x++)
                if (tags[x] >= 0 || tags[(h - 1) * w + x] >= 0) return true;
            for (int y = 0; y < h; y++)
                if (tags[y * w] >= 0 || tags[y * w + w - 1] >= 0) return true;
            return false;
        }

        private void VectorCapture_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string manual = null;
                if (AppSettings.SvgManualName)
                {
                    manual = AskCaptureFileName("벡터 캡처(SVG)", "SVG 벡터 (*.svg)|*.svg", ".svg", AppSettings.SvgDir);
                    if (manual == null) { Status("벡터 캡처를 취소했습니다."); return; }
                }

                // <260811_26> 배경 키보드가 있으면 KbControl 을 정합 기준 삼아(RunSvgExport 와 같은
                // 원리) 물리 픽셀 좌표를 OTP 의 777x260 키보드 단위로 바꿔 출력한다. 꺼져 있으면
                // 정합 기준이 없어 종전처럼 캔버스 물리 픽셀 그대로 출력하고, 그 사실을 안내한다.
                bool hasKeyboard = _keyboard != KbKind.None;
                Func<double, double, (double x, double y)> xform = hasKeyboard
                    ? (x, y) => { var u = PhysicalPxToKeyboardUnits(new Point(x, y)); return (u.X, u.Y); }
                    : null;
                double? vbw = hasKeyboard ? (double?)KeyboardUi.KeyboardControl.LayoutW : null;
                double? vbh = hasKeyboard ? (double?)KeyboardUi.KeyboardControl.LayoutH : null;
                if (!hasKeyboard)
                    ShowGraphicsMessage("배경 키보드가 없으면 777×260 단위가 아닌 캔버스로 출력됩니다.");

                // (3-12-1) 영역 밖으로 나간 부분(손목쪽)이 있으면 포함 여부를 묻는다
                bool expand = false;
                if (HandTouchesEdge())
                {
                    var r = MessageBox.Show(this,
                        "3D 손의 일부가 그래픽 영역 밖으로 나가 있습니다.\n밖으로 나간 부분을 캡처에 포함할까요?",
                        "벡터 캡처", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    expand = r == MessageBoxResult.Yes;
                }

                string path;
                if (expand)
                {
                    // 캔버스를 사방으로 넓혀 같은 투영으로 다시 태그 패스를 얻은 뒤 추출
                    int m = Math.Max(_camera.Width, _camera.Height) / 2;
                    int w0 = _camera.Width, h0 = _camera.Height;
                    double px0 = _camera.PanX, py0 = _camera.PanY;
                    // 확장 캔버스는 원점이 사방 m 만큼 밀려 있다(CaptureBitmapCore 의 kbRect 보정과
                    // 같은 이유) — 777 변환 기준인 KbControl 은 화면에 고정돼 있으므로, 되돌린
                    // 좌표로 변환해야 정합이 맞는다.
                    Func<double, double, (double x, double y)> xformExpand = hasKeyboard
                        ? (x, y) => { var u = PhysicalPxToKeyboardUnits(new Point(x - m, y - m)); return (u.X, u.Y); }
                        : null;
                    try
                    {
                        // 캔버스만 키우고 투영 스케일은 화면과 동일하게(ViewW/H)
                        _camera.Width = w0 + 2 * m; _camera.Height = h0 + 2 * m;
                        _camera.ViewW = w0; _camera.ViewH = h0;
                        _camera.PanX = px0 + m; _camera.PanY = py0 + m;
                        _renderer.WorldScale = _scene.OverallScale;
                        _renderer.Render(_camera, _scene.GetRenderMeshes(!_outlineOnly), 96 * DpiScale, null, BuildBoneSegs());
                        path = Capture.SaveVector(_renderer, CaptureKeyInfo(), VectorFill.IsChecked == true, (int)_capTarget, manual, xformExpand, vbw, vbh,
                                                  strokeColor: _outlineColor, strokeWidthPx: _outlineThickness * DpiScale);
                    }
                    finally
                    {
                        _camera.Width = w0; _camera.Height = h0;
                        _camera.ViewW = 0; _camera.ViewH = 0;
                        _camera.PanX = px0; _camera.PanY = py0;
                        RenderScene();   // 화면 상태 복원
                    }
                }
                else
                {
                    path = Capture.SaveVector(_renderer, CaptureKeyInfo(), VectorFill.IsChecked == true, (int)_capTarget, manual, xform, vbw, vbh,
                                              strokeColor: _outlineColor, strokeWidthPx: _outlineThickness * DpiScale);
                }

                Status($"벡터(SVG) 캡처 저장: {path}");
                // (3-10-2-1-1-1) 벡터 카메라 활성 시 안내 문구
                AddStatus("캡처된 SVG 파일을 다른 프로그램에서 재저장하면 Open Typing Plus에 활용할 정보가 지워집니다.", false);
            }
            catch (Exception ex) { Status("벡터 캡처 실패: " + ex.Message, true); }
        }

        // ---------- 제목 표시줄 ----------

        // ---------- <260811_18>(1) 저장 기준 파일 + 그래픽 영역 안내 문구 ----------

        /// <summary>마지막으로 열거나 저장한 파일. '저장'의 비교·덮어쓰기 기준.</summary>
        private string _currentFile;

        /// <summary>
        /// <260811_20> 제목 표시줄(OS 캡션)·파란 띠 문구를 _currentFile 에 맞춰 갱신.
        /// 파일이 없으면 기본 이름만, 있으면 "- 파일명(폴더 경로)"를 덧붙인다. 길어서 넘치면
        /// OS 캡션은 시스템 버튼 앞에서, 파란 띠는 TextTrimming(XAML)으로 각각 스스로 말줄임(...) 처리한다.
        /// </summary>
        private void UpdateTitleBar()
        {
            string text = "손 모델 3D(Hand Model 3D)";
            if (!string.IsNullOrEmpty(_currentFile))
            {
                string fname = System.IO.Path.GetFileName(_currentFile);
                string dir = System.IO.Path.GetDirectoryName(_currentFile);
                text += $"- {fname}({dir})";
            }
            Title = text;
            TitleText.Text = text;
        }

        private string _graphicsMsg;
        private System.Windows.Threading.DispatcherTimer _graphicsMsgTimer;

        /// <summary>그래픽 영역 상단에 안내 문구를 3초 동안 띄운다(캡처에는 포함되지 않음).</summary>
        private void ShowGraphicsMessage(string msg)
        {
            _graphicsMsg = msg;
            if (_graphicsMsgTimer == null)
            {
                _graphicsMsgTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                _graphicsMsgTimer.Tick += (s, e) => { _graphicsMsgTimer.Stop(); _graphicsMsg = null; UpdateOverlay(); };
            }
            _graphicsMsgTimer.Stop();
            _graphicsMsgTimer.Start();
            UpdateOverlay();
        }

        /// <summary>
        /// 지금 상태가 기준 파일의 내용과 같은지(= 저장할 것이 없는지). 기억해 둔 문자열이 아니라
        /// **파일을 다시 읽어** 비교하므로, 밖에서 파일이 바뀌었으면 '달라진 것'으로 본다.
        /// </summary>
        public bool StateMatchesCurrentFile()
        {
            if (string.IsNullOrEmpty(_currentFile) || !System.IO.File.Exists(_currentFile)) return false;
            try
            {
                string now = SceneState.Serialize(_scene, _camera, this);
                string saved = System.IO.File.ReadAllText(_currentFile);
                return string.Equals(now.Trim(), saved.Trim(), StringComparison.Ordinal);
            }
            catch { return false; }   // 비교 못 하면 평소대로 저장 창을 연다
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            // <260811_15> 시작 폴더 = 설정의 '저장 파일(json) 위치'(기본값은 종전과 같은 바탕화면)
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "손 모델 3D 저장 (*.json)|3Dm_*.json;*.json", InitialDirectory = AppSettings.JsonDir };
            if (dlg.ShowDialog() == true)
            {
                // <260811_21>: '초기값' 파일은 손 모양을 담지 않으므로 여기(손 모양 열기)로는 안 읽는다.
                if (DefaultValues.IsDefaultsFile(dlg.FileName))
                {
                    Status("이 파일은 초기값을 담고 있습니다. 손 모양이 담긴 파일을 불러오세요.", true);
                    return;
                }
                try
                {
                    SceneState.Load(dlg.FileName, _scene, _camera, this);
                    SyncUiFromScene(); Recompute();
                    _currentFile = dlg.FileName;   // <260811_18>(1) 저장 기준
                    UpdateTitleBar();   // <260811_20>(2)
                    Status("열기 완료: " + dlg.FileName);
                }
                catch (Exception ex) { Status("열기 실패: " + ex.Message, true); }
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // <260811_18>(1) 연 파일과 달라진 게 없으면 저장 창도 띄우지 않고 문구만 보여 준다.
            if (StateMatchesCurrentFile()) { ShowGraphicsMessage("달라진 것이 없습니다."); return; }

            bool hasFile = !string.IsNullOrEmpty(_currentFile);
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "저장",
                Filter = "손 모델 3D 저장 (*.json)|*.json",
                // 기준 파일이 있으면 그 폴더가 열리고 그 이름이 이름 칸에 블록으로 선택돼 나온다.
                // 그대로 '저장'을 누르면 덮어쓰기, 이름을 고치면 다른 이름으로 저장.
                FileName = hasFile ? System.IO.Path.GetFileName(_currentFile) : "3Dm_",
                InitialDirectory = hasFile ? System.IO.Path.GetDirectoryName(_currentFile) : AppSettings.JsonDir,
                OverwritePrompt = false,   // 덮어쓰기가 이 흐름의 정상 동작 — 확인 창을 더 띄우지 않는다
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    SceneState.Save(dlg.FileName, _scene, _camera, this);
                    _currentFile = dlg.FileName;
                    UpdateTitleBar();   // <260811_20>(2)
                    Status("저장 완료: " + dlg.FileName);
                }
                catch (Exception ex) { Status("저장 실패: " + ex.Message, true); }
            }
        }

        // ---------- 끌어다 놓기로 열기 (<260811_17>) ----------

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            // 커서 모양만 정한다(파일 내용 검사는 놓는 순간에 — DragOver 는 마우스가 움직일 때마다 온다)
            e.Effects = HasJsonFile(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (!HasJsonFile(e.Data)) return;
            e.Handled = true;
            TryOpenDroppedFiles((string[])e.Data.GetData(DataFormats.FileDrop));
        }

        private static bool HasJsonFile(IDataObject data)
        {
            if (data == null || !data.GetDataPresent(DataFormats.FileDrop)) return false;
            if (!(data.GetData(DataFormats.FileDrop) is string[] files)) return false;
            foreach (string f in files) if (IsJsonName(f)) return true;
            return false;
        }

        private static bool IsJsonName(string f)
            => !string.IsNullOrEmpty(f) && f.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 끌어다 놓은 파일 중 '손 모델 정보가 있는 json'을 연다(여러 개면 그런 첫 파일).
        /// 반환 = 실제로 연 경로, 없으면 null. 헤드리스 검증(-droptest)도 이 메서드를 쓴다.
        /// </summary>
        public string TryOpenDroppedFiles(string[] files)
        {
            if (files == null) return null;
            string firstJson = null;
            string firstDefaultsFile = null;   // <260811_21>
            foreach (string f in files)
            {
                if (!IsJsonName(f)) continue;
                if (firstJson == null) firstJson = f;
                // <260811_21>: '초기값' 파일은 손 모양을 담지 않으므로 여기(손 모양 열기)로는 안 읽는다.
                if (firstDefaultsFile == null && DefaultValues.IsDefaultsFile(f)) firstDefaultsFile = f;
                if (!SceneState.IsStateFile(f)) continue;
                try
                {
                    SceneState.Load(f, _scene, _camera, this);
                    SyncUiFromScene();
                    Recompute();
                    _currentFile = f;   // <260811_18>(1) 저장 기준
                    UpdateTitleBar();   // <260811_20>(2)
                    Status("열기 완료: " + f);
                    return f;
                }
                catch (Exception ex) { Status("열기 실패: " + ex.Message, true); return null; }
            }
            if (firstDefaultsFile != null)
                Status("이 파일은 초기값을 담고 있습니다. 손 모양이 담긴 파일을 불러오세요.", true);
            else if (firstJson != null)
                Status("손 모델 정보가 없는 JSON 파일입니다: " + System.IO.Path.GetFileName(firstJson), true);
            return null;
        }

        private void Info_Click(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();

        /// <summary><260811_32> 실행 파일에 박아 둔 사용법 문서를 새 창으로 보여 준다.</summary>
        private void Help_Click(object sender, RoutedEventArgs e) => new HelpWindow { Owner = this }.ShowDialog();

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var win = new SettingsWindow { Owner = this };
            win.ShowDialog();
            // <260811_21 추가지시> 초기값을 **고쳤을 때만** 즉시 손에 반영하고, 다음 실행에도 남도록
            // 저장한다. 안 고쳤으면(경로만 바꿨을 때 등) 지금 잡아 둔 손 모양을 건드리지 않는다.
            if (win.DefaultsChanged)
            {
                DefaultValues.SaveUserSettings();   // 다음 실행부터
                ApplyDefaultsToScene();             // 즉시
                Status("초기값을 반영했습니다.");
            }
        }

        /// <summary>
        /// <260811_21 추가지시> AppSettings.Def* 의 초기값을 실제 손에 적용한다(앱 시작 때와 설정 창에서
        /// 초기값을 고쳤을 때 호출). 모델을 먼저 쓰고 SyncUiFromScene 으로 표시를 맞춘다 — ValueSlider 는
        /// 프로그램적 .Value 설정에서 이벤트를 억제하므로 UI 만 바꿔서는 모델이 안 따라온다(<260811_11> 교훈).
        /// 값은 각 슬라이더의 실제 범위로 클램프한다(손상 파일·직접 입력 방어).
        /// </summary>
        public void ApplyDefaultsToScene()
        {
            double C(double v, double lo, double hi, double dflt)
                => double.IsNaN(v) ? dflt : Math.Max(lo, Math.Min(hi, v));

            _scene.OverallScale = C(AppSettings.DefOverallScale, 0.5, 1.8, 0.95);
            _scene.WidthScale = C(AppSettings.DefWidthScale, 0.7, 1.3, 1.3);
            _scene.WebbingHeight = C(AppSettings.DefWebbingHeight, 0, 200, 0);
            // 손 위치는 X/Y 만 초기값 대상 — Z 는 현재 값을 유지한다(초기값 항목이 아니다).
            _scene.OffsetLeft = new Vector3D(
                C(AppSettings.DefOffsetLeftX, -HandPosRange, HandPosRange, 1),
                C(AppSettings.DefOffsetLeftY, -HandPosRange, HandPosRange, -4.9),
                _scene.OffsetLeft.Z);
            _scene.OffsetRight = new Vector3D(
                C(AppSettings.DefOffsetRightX, -HandPosRange, HandPosRange, -5.5),
                C(AppSettings.DefOffsetRightY, -HandPosRange, HandPosRange, -4.9),
                _scene.OffsetRight.Z);
            double cmc = C(AppSettings.DefThumbCmcLR, 0, 100, HandPose.ThumbCmcLRDefault);
            _scene.Pose.Set("L5.CMC.LR", cmc);
            _scene.Pose.Set("R6.CMC.LR", cmc);

            SyncUiFromScene();   // 상단 패널·손 위치·관절 패널 표시 일괄 동기
            Recompute();
        }

        public KbKind Keyboard_ => _keyboard;

        // ---------- 저장/열기용 추가 상태 ----------
        public void CaptureExtra(StateModel m)
        {
            m.Keyboard = (int)_keyboard;
            m.KbPressedRow = KbControl.PressedKey?.row ?? -1;
            m.KbPressedCol = KbControl.PressedKey?.col ?? -1;
            m.KbLShift = KbControl.LeftShiftPressed;
            m.KbRShift = KbControl.RightShiftPressed;
            m.KbScaleFactor = KbScale.ScaleX;
            m.Pins.Clear();
            foreach (string id in _pins) m.Pins.Add(id);
            m.OutlineShow = _outlineShow;
            m.OutlineOnly = _outlineOnly;
            m.OutlineOcclude = _outlineOcclude;
            m.OutlineThickness = _outlineThickness;
            m.OutlineColor = _outlineColor.ToString();
            m.LabelsShown = _labelsShown;
            m.LabelScale = _labelScale;
            m.PinOpacity = PinOpacity.Value;
            m.RotLeft = new[] { _scene.RotLeft.RollDeg, _scene.RotLeft.PitchDeg, _scene.RotLeft.SwingDeg };
            m.RotRight = new[] { _scene.RotRight.RollDeg, _scene.RotRight.PitchDeg, _scene.RotRight.SwingDeg };
            m.CaptureTarget = (int)_capTarget;
            m.BgImagePath = _bgImagePath;
            // 이미지가 없으면 Width/Height 가 NaN(Auto)이라 JSON 직렬화가 실패하므로 0 처리
            m.BgWidth = BgImageActive && !double.IsNaN(BgImage.Width) ? BgImage.Width : 0;
            m.BgHeight = BgImageActive && !double.IsNaN(BgImage.Height) ? BgImage.Height : 0;
            m.BgScaleX = BgScale.ScaleX; m.BgScaleY = BgScale.ScaleY;
        }

        public void ApplyExtra(StateModel m)
        {
            _keyboard = (KbKind)m.Keyboard;
            KbNone.IsChecked = _keyboard == KbKind.None;
            KbDubeol.IsChecked = _keyboard == KbKind.Dubeolsik;
            KbQwerty.IsChecked = _keyboard == KbKind.Qwerty;
            KbControl.Visibility = _keyboard == KbKind.None ? Visibility.Collapsed : Visibility.Visible;
            KbControl.Dubeolsik = _keyboard == KbKind.Dubeolsik;
            KbControl.PressedKey = m.KbPressedRow >= 0 ? (m.KbPressedRow, m.KbPressedCol) : ((int, int)?)null;
            KbControl.LeftShiftPressed = m.KbLShift;
            KbControl.RightShiftPressed = m.KbRShift;
            // <260811_14> 후속(qksqhr 원칙): [Ctrl]+드래그 범위(0.3~3.0)로 클램프 — 손상 파일의
            // 극단값이 캡처 키보드 RTB 의 거대 할당(OOM)을 만들 수 있다. NaN 방어 포함.
            KbScale.ScaleX = KbScale.ScaleY = double.IsNaN(m.KbScaleFactor) || m.KbScaleFactor <= 0
                ? 1 : Math.Max(0.3, Math.Min(3.0, m.KbScaleFactor));
            KbControl.InvalidateVisual();
            _pins.Clear();
            if (m.Pins != null) foreach (string id in m.Pins) _pins.Add(id);
            _outlineShow = m.OutlineShow; OutlineShow.IsChecked = _outlineShow;
            _outlineOnly = m.OutlineOnly; OutlineOnly.IsChecked = _outlineOnly;
            _outlineOcclude = m.OutlineOcclude; OutlineModeBtn.Content = _outlineOcclude ? "모드: 가림" : "모드: 항상";
            _outlineThickness = m.OutlineThickness; OutlineThick.Value = _outlineThickness;
            if (!string.IsNullOrEmpty(m.OutlineColor)) _outlineColor = (Color)ColorConverter.ConvertFromString(m.OutlineColor);
            _labelsShown = m.LabelsShown;
            // qksqhr(2026-08-11): 손상·악성 저장 파일의 LabelScale 이 WPF FontSize 유효 범위를 벗어나면
            // 렌더마다 예외가 반복되는 상태에 빠진다 — 라벨 크기 팝업 슬라이더와 같은 범위로 클램프.
            _labelScale = double.IsNaN(m.LabelScale) ? 1.0 : Math.Max(0.6, Math.Min(2.2, m.LabelScale));
            PinOpacity.Value = m.PinOpacity;
            // <260719_8-1> 손 회전
            if (m.RotLeft != null && m.RotLeft.Length == 3)
                _scene.RotLeft = new HandRotation { RollDeg = m.RotLeft[0], PitchDeg = m.RotLeft[1], SwingDeg = m.RotLeft[2] };
            if (m.RotRight != null && m.RotRight.Length == 3)
                _scene.RotRight = new HandRotation { RollDeg = m.RotRight[0], PitchDeg = m.RotRight[1], SwingDeg = m.RotRight[2] };
            SyncRotSliders();
            // <260719_9> 캡처 대상
            _capTarget = (CaptureTarget)m.CaptureTarget;
            CapBoth.IsChecked = _capTarget == CaptureTarget.Both;
            CapLeft.IsChecked = _capTarget == CaptureTarget.Left;
            CapRight.IsChecked = _capTarget == CaptureTarget.Right;
            BitmapCamBtn.IsEnabled = _capTarget == CaptureTarget.Both;
            // <260719_10> 배경 이미지
            // qksqhr(2026-09-13) 보안: BgImagePath 는 신뢰 못 할 입력이다(남이 만든 손 모양 json을
            // 열 수 있음). UNC 경로(\\서버\공유\...)를 그대로 File.Exists 에 넘기면 존재 확인
            // 시도만으로 OS가 그 서버로 SMB 연결·NTLM 인증을 시도해 자격 증명이 유출될 수 있다
            // (알려진 UNC 경로 공격 패턴). 로컬 파일 경로만 허용한다.
            if (!string.IsNullOrEmpty(m.BgImagePath) && IsLocalFilePath(m.BgImagePath) && System.IO.File.Exists(m.BgImagePath))
            {
                LoadBgImage(m.BgImagePath);
                if (m.BgWidth > 0) BgImage.Width = m.BgWidth;
                if (m.BgHeight > 0) BgImage.Height = m.BgHeight;
                BgScale.ScaleX = m.BgScaleX <= 0 ? 1 : m.BgScaleX;
                BgScale.ScaleY = m.BgScaleY <= 0 ? 1 : m.BgScaleY;
            }
            else
            {
                BgImageClear_Click(null, null);
            }
            Status();
        }

        private void SyncUiFromScene()
        {
            SizeSlider.Value = _scene.OverallScale;
            VolumeSlider.Value = _scene.Volume;
            if (_fleshSlider != null) _fleshSlider.Value = _scene.FleshResponse;   // <260811_11>
            WidthSlider.Value = _scene.WidthScale;                                 // <260811_12>
            if (_webbingSlider != null) _webbingSlider.Value = _scene.WebbingHeight;   // <260811_4-1>(1)
            TranspSlider.Value = _scene.Transparency;
            SyncHandPosSliders();       // <260810_3>(7)
            SyncViewRotSliders();       // <260810_3-1>(4)
            // 키가 없으면 기본 길이(1.0)로 되돌린다 — 안 하면 열기/실행 취소로 길이가 리셋돼도
            // 슬라이더가 이전 값에 남아 다음 조작에서 길이가 점프한다.
            _syncingLenSliders = true;   // qksqhr: 동기화 중 모델 되쓰기 억제(표시만 갱신)
            try
            {
                foreach (var kv in _lenSliders) kv.Value.Value = _scene.FingerLen.TryGetValue(kv.Key, out var v) ? v : 1.0;
            }
            finally { _syncingLenSliders = false; }
            _leftJoints.SyncFromPose();
            _rightJoints.SyncFromPose();
        }

        // ---------- 상태 표시줄 ----------

        // <260811_5> 손가락끼리 충돌(살 관통) 경고 — 막지는 않고 문구만 띄운다.
        // <260811_5-1> 두 손 사이 충돌도 감지, 문구는 상태 표시줄 대신 그래픽 영역(오버레이)에.
        // <260811_5-2> 어느 부위끼리 충돌하는지 쌍 목록으로 수집해 경고문에 밝힌다.
        private readonly List<string> _fingerColPairs = new List<string>();   // "(왼손 검지, 왼손 중지)" …
        private readonly List<string> _handColPairs = new List<string>();     // "(왼손 엄지, 오른손 손바닥)" …
        private bool _fingerCollision => _fingerColPairs.Count > 0;
        private bool _handsCollision => _handColPairs.Count > 0;

        private static string SideName(HandSide s) => s == HandSide.Left ? "왼손" : "오른손";
        private static string DigitName(Digit d)
        {
            switch (d)
            {
                case Digit.Thumb: return "엄지";
                case Digit.Index: return "검지";
                case Digit.Middle: return "중지";
                case Digit.Ring: return "약지";
                default: return d == Digit.Pinky ? "소지" : d.ToString();
            }
        }

        /// <summary>충돌 쌍 목록을 "(A, B)(C, D)" + 3쌍 초과 시 " 외 N곳" 으로 요약.</summary>
        private static string PairsText(List<string> pairs)
        {
            int show = Math.Min(3, pairs.Count);
            string s = string.Concat(pairs.GetRange(0, show));
            if (pairs.Count > show) s += $" 외 {pairs.Count - show}곳 ";
            return s;
        }

        /// <summary>
        /// <260811_5-1> 왼손↔오른손 충돌 검사. 두 손은 원래 겹칠 일이 없으므로(맞닿음 예외 불필요)
        /// 각도 조건 없이, 손가락 마디 + 손바닥 근사 캡슐(손목 중심→각 손가락 밑동 부채) 선분끼리
        /// 축거리 &lt; 반지름합×0.85 이면 충돌로 본다. <260811_5-2>: 충돌한 부위 쌍을 모아 반환.
        /// </summary>
        private void DetectHandsCollision(List<string> pairsOut)
        {
            pairsOut.Clear();
            var l = _scene.Left; var r = _scene.Right;
            if (l == null || r == null) return;
            var segsL = HandSegs(l, HandSide.Left);
            var segsR = HandSegs(r, HandSide.Right);
            var seen = new HashSet<string>();
            foreach (var (aP, aQ, aR, aName) in segsL)
                foreach (var (bP, bQ, bR, bName) in segsR)
                    if (SegSegDistance(aP, aQ, bP, bQ) < (aR + bR) * 0.85)
                    {
                        string key = $"({aName}, {bName})";
                        if (seen.Add(key)) pairsOut.Add(key);
                    }

            List<(System.Windows.Media.Media3D.Point3D p, System.Windows.Media.Media3D.Point3D q, double rad, string name)> HandSegs(HandResult h, HandSide side)
            {
                double v = _scene.Volume;
                var list = new List<(System.Windows.Media.Media3D.Point3D, System.Windows.Media.Media3D.Point3D, double, string)>();
                var byDigit = new Dictionary<Digit, List<JointNode>>();
                foreach (var j in h.Joints)
                {
                    if (!byDigit.TryGetValue(j.Digit, out var dl)) { dl = new List<JointNode>(); byDigit[j.Digit] = dl; }
                    dl.Add(j);
                }
                foreach (var kv in byDigit)
                {
                    kv.Value.Sort((x, y) => x.ChainIndex.CompareTo(y.ChainIndex));
                    string fname = SideName(side) + " " + DigitName(kv.Key);
                    for (int i = 0; i + 1 < kv.Value.Count; i++)
                        list.Add((kv.Value[i].Pos, kv.Value[i + 1].Pos,
                                  (kv.Value[i].Radius + kv.Value[i + 1].Radius) / 2 * v, fname));
                    // 손바닥 근사: 손목 중심 → 각 손가락 밑동(부채). 팜 슬래브 반두께 정도의 굵기.
                    list.Add((h.WristCenter, kv.Value[0].Pos, 1.25 * h.Scale * v, SideName(side) + " 손바닥"));
                }
                return list;
            }
        }

        /// <summary>
        /// 같은 손의 서로 다른 손가락 마디(관절 사슬 선분 + 반지름)가 서로를 관통하는지 검사한다.
        /// 임계값은 포즈별 실측(잠복 진단 HM3D_COLDUMP)으로 정했다. 두 규칙 중 하나면 충돌:
        /// ① 가로지르는 관통: 축거리 &lt; 반지름합×0.85 이면서 교차각 &gt; 35°
        ///    (2.json 검지끝×중지 r≈0.76/53°, 벌린 채 주먹 r≈0.43/75°).
        /// ② 깊은 관통(<260811_5-1 추가 지시>): 각도와 무관하게 축거리 &lt; 반지름합×0.40
        ///    (엄지 밀착 r≈0.01 — 나란해도 축이 살 한복판을 지나면 관통이므로 경고).
        /// 경고하지 않는 정상 접촉: 🖐️ 기본 r≈0.79/13°, 손가락 모음 r≈0.62~0.72/0~8°,
        /// 아치 r≈0.79/14° — 이 모델이 '맞닿음'을 얕은 살 겹침으로 표현하는 범위다.
        /// </summary>
        private void DetectFingerCollision(List<string> pairsOut)
        {
            pairsOut.Clear();
            foreach (var (side, r) in new[] { (HandSide.Left, _scene.Left), (HandSide.Right, _scene.Right) })
            {
                if (r == null) continue;
                var byDigit = new Dictionary<Digit, List<JointNode>>();
                foreach (var j in r.Joints)
                {
                    if (!byDigit.TryGetValue(j.Digit, out var l)) { l = new List<JointNode>(); byDigit[j.Digit] = l; }
                    l.Add(j);
                }
                var digits = new List<Digit>(byDigit.Keys);
                foreach (var kv in byDigit) kv.Value.Sort((a, b) => a.ChainIndex.CompareTo(b.ChainIndex));
                double v = _scene.Volume;
                const double maxParallelCos = 0.81915;   // cos 35°
                for (int a = 0; a < digits.Count; a++)
                    for (int b = a + 1; b < digits.Count; b++)
                    {
                        var ja = byDigit[digits[a]];
                        var jb = byDigit[digits[b]];
                        bool hit = false;
                        for (int i = 0; i + 1 < ja.Count && !hit; i++)
                            for (int k = 0; k + 1 < jb.Count && !hit; k++)
                            {
                                double ra = (ja[i].Radius + ja[i + 1].Radius) / 2 * v;
                                double rb = (jb[k].Radius + jb[k + 1].Radius) / 2 * v;
                                double d = SegSegDistance(ja[i].Pos, ja[i + 1].Pos, jb[k].Pos, jb[k + 1].Pos);
                                if (d < (ra + rb) * 0.40) { hit = true; break; }   // ② 깊은 관통 — 각도 무관
                                if (d >= (ra + rb) * 0.85) continue;
                                var da = ja[i + 1].Pos - ja[i].Pos;
                                var db = jb[k + 1].Pos - jb[k].Pos;
                                if (da.Length < 1e-6 || db.Length < 1e-6) continue;
                                da.Normalize(); db.Normalize();
                                double c = Math.Abs(System.Windows.Media.Media3D.Vector3D.DotProduct(da, db));
                                if (c < maxParallelCos) { hit = true; break; }     // ① 가로지르며 파고듦
                            }
                        if (hit)
                            pairsOut.Add($"({SideName(side)} {DigitName(digits[a])}, {SideName(side)} {DigitName(digits[b])})");
                    }
            }
        }

        /// <summary>3차원 선분-선분 최단 거리.</summary>
        private static double SegSegDistance(System.Windows.Media.Media3D.Point3D p1, System.Windows.Media.Media3D.Point3D q1,
                                             System.Windows.Media.Media3D.Point3D p2, System.Windows.Media.Media3D.Point3D q2)
        {
            var d1 = q1 - p1; var d2 = q2 - p2; var r = p1 - p2;
            double A = d1.LengthSquared, e = d2.LengthSquared, f = System.Windows.Media.Media3D.Vector3D.DotProduct(d2, r);
            double s, t;
            const double EPS = 1e-9;
            if (A <= EPS && e <= EPS) { s = 0; t = 0; }
            else if (A <= EPS) { s = 0; t = Math.Max(0, Math.Min(1, f / e)); }
            else
            {
                double c = System.Windows.Media.Media3D.Vector3D.DotProduct(d1, r);
                if (e <= EPS) { t = 0; s = Math.Max(0, Math.Min(1, -c / A)); }
                else
                {
                    double bb = System.Windows.Media.Media3D.Vector3D.DotProduct(d1, d2);
                    double denom = A * e - bb * bb;
                    s = denom > EPS ? Math.Max(0, Math.Min(1, (bb * f - c * e) / denom)) : 0;
                    t = (bb * s + f) / e;
                    if (t < 0) { t = 0; s = Math.Max(0, Math.Min(1, -c / A)); }
                    else if (t > 1) { t = 1; s = Math.Max(0, Math.Min(1, (bb - c) / A)); }
                }
            }
            var c1 = p1 + d1 * s; var c2 = p2 + d2 * t;
            return (c1 - c2).Length;
        }

        /// <summary>잠복 진단(<260811_5>): 손별 손가락 쌍의 최소 축거리/반지름합 비율 덤프 — 임계 보정용.</summary>
        private string DumpCollisionRatios()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var (side, r) in new[] { (HandSide.Left, _scene.Left), (HandSide.Right, _scene.Right) })
            {
                if (r == null) continue;
                var byDigit = new Dictionary<Digit, List<JointNode>>();
                foreach (var j in r.Joints)
                {
                    if (!byDigit.TryGetValue(j.Digit, out var l)) { l = new List<JointNode>(); byDigit[j.Digit] = l; }
                    l.Add(j);
                }
                foreach (var kv in byDigit) kv.Value.Sort((a, b) => a.ChainIndex.CompareTo(b.ChainIndex));
                var digits = new List<Digit>(byDigit.Keys);
                double v = _scene.Volume;
                for (int a = 0; a < digits.Count; a++)
                    for (int b = a + 1; b < digits.Count; b++)
                    {
                        var ja = byDigit[digits[a]]; var jb = byDigit[digits[b]];
                        for (int i = 0; i + 1 < ja.Count; i++)
                            for (int k = 0; k + 1 < jb.Count; k++)
                            {
                                double ra = (ja[i].Radius + ja[i + 1].Radius) / 2 * v;
                                double rb = (jb[k].Radius + jb[k + 1].Radius) / 2 * v;
                                double d = SegSegDistance(ja[i].Pos, ja[i + 1].Pos, jb[k].Pos, jb[k + 1].Pos);
                                var da = ja[i + 1].Pos - ja[i].Pos; var db = jb[k + 1].Pos - jb[k].Pos;
                                double ang = 0;
                                if (da.Length > 1e-6 && db.Length > 1e-6)
                                {
                                    da.Normalize(); db.Normalize();
                                    ang = Math.Acos(Math.Min(1, Math.Abs(System.Windows.Media.Media3D.Vector3D.DotProduct(da, db)))) * 180 / Math.PI;
                                }
                                if (d / (ra + rb) < 1.1)
                                    sb.AppendLine($"{side} {digits[a]}[{i}]-{digits[b]}[{k}]: r={d / (ra + rb):F3} ang={ang:F0}");
                            }
                    }
            }
            return sb.ToString();
        }

        private void Status(string extra = null, bool red = false)
        {
            StatusList.Items.Clear();
            AddStatus("키보드 방향키로 3D 손을 옮길 수 있습니다.", false);
            if (_keyboard != KbKind.None)
            {
                AddStatus("키보드를 조작하기 위해서는 [Ctrl]을 눌러야 합니다.", false);
            }
            if (BgImageActive)
            {
                AddStatus("[Ctrl]+드래그: 배경 이미지 크기 조절(비율 유지) / [Ctrl]+[Shift]+드래그: 비율 무시", false);
            }
            if (extra != null) AddStatus(extra, red);
        }

        private void AddStatus(string text, bool red)
        {
            StatusList.Items.Add(new TextBlock
            {
                Text = text,
                Foreground = red ? Brushes.Red : new SolidColorBrush(Color.FromRgb(0x21, 0x25, 0x29)),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
        }

        // ---------- 검증용 PNG 저장 (개발 확인용; 배포 exe 에서는 꺼둔다) ----------
        // 라이브 창 표시가 샌드박스 DPI 문제로 안 보일 때만 true 로 켜 렌더 비트맵을 파일로 확인한다.
        public bool DebugSaveVerify = false;
        private void SaveVerify(BitmapSource bmp)
        {
            if (!DebugSaveVerify) return;
            try
            {
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using (var fs = System.IO.File.Create(System.IO.Path.Combine(dir, "3dm_verify.png"))) enc.Save(fs);
            }
            catch { }
        }

        /// <summary>전체 창(UI 포함)을 PNG 로 저장(검증/문서용). RTB 라 2D UI 는 정확히 나온다.</summary>
        public void SaveWindowSnapshot(string fileName = "3dm_window.png")
        {
            try
            {
                double scale = DpiScale;
                int ww = (int)(ActualWidth * scale), hh = (int)(ActualHeight * scale);
                if (ww <= 0 || hh <= 0 || !(Content is Visual v)) return;
                var rtb = new RenderTargetBitmap(ww, hh, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                rtb.Render(v);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                // 전체 경로가 오면 그대로, 파일명만 오면 LocalApplicationData 아래에 저장
                string path = System.IO.Path.IsPathRooted(fileName)
                    ? fileName
                    : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), fileName);
                using (var fs = System.IO.File.Create(path)) enc.Save(fs);
            }
            catch { }
        }

        // <260811_21> 잠복 진단(-settingsshot): 설정 창을 화면에 띄우지 않고 레이아웃만 강제한 뒤
        // Content 를 그대로 구워 저장 — 새 '초기값' 영역이 정상 렌더되는지 헤드리스로 확인한다.
        private void SaveSettingsSnapshot()
        {
            try
            {
                var win = new SettingsWindow { Owner = this };
                win.Show();
                win.UpdateLayout();
                double scale = DpiScale;
                int ww = (int)(win.ActualWidth * scale), hh = (int)(win.ActualHeight * scale);
                if (ww > 0 && hh > 0 && win.Content is Visual v)
                {
                    var rtb = new RenderTargetBitmap(ww, hh, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                    rtb.Render(v);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(rtb));
                    string path = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "3dm_settings.png");
                    using (var fs = System.IO.File.Create(path)) enc.Save(fs);
                }
                win.Close();
            }
            catch { }
            Application.Current.Shutdown();
        }
    }
}
