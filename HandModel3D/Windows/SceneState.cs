using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using HandModel3D.Hand;
using HandModel3D.Render;

namespace HandModel3D
{
    /// <summary>그래픽 영역 상태 전체를 담는 직렬화 모델. 저장 파일을 열면 그대로 재현된다 (3-11, 해석 ⓗ).</summary>
    public sealed class StateModel
    {
        // 기본값은 null — 'Pose' 항목이 없는 json 이 빈 자세(전부 100%)로 읽혀 장면을 초기화하지 않게(Deserialize 가 거부).
        public Dictionary<string, double> Pose { get; set; }
        public double OverallScale { get; set; } = 1;
        public double Volume { get; set; } = 1;
        public double FleshResponse { get; set; } = 50;   // 살 반응 % (<260810_2>)
        public double WebbingHeight { get; set; } = 100;  // 물갈퀴 살 높이 % (<260811_4-1>)
        public double WidthScale { get; set; } = 1.0;     // 좌우 비율 (<260811_12>)
        public double Transparency { get; set; }
        public Dictionary<string, double> FingerLen { get; set; } = new Dictionary<string, double>();
        public string SkinColor { get; set; }
        public Dictionary<string, string> TipSkin { get; set; } = new Dictionary<string, string>();

        public double CamYaw { get; set; }
        public double CamPitch { get; set; }
        public double CamRoll { get; set; }   // <260810_3-1>(4) 시선축 기울임
        public double CamDist { get; set; } = 74;
        public double CamPanX { get; set; }
        public double CamPanY { get; set; }
        public double CamFov { get; set; } = 34;
        public double[] CamTarget { get; set; } = { 0, 8, 0 };

        public int Keyboard { get; set; }
        public int KbPressedRow { get; set; } = -1;
        public int KbPressedCol { get; set; } = -1;
        public bool KbLShift { get; set; }
        public bool KbRShift { get; set; }
        public double KbScaleFactor { get; set; } = 1;
        public bool OutlineShow { get; set; }
        public bool OutlineOnly { get; set; }
        public bool OutlineOcclude { get; set; } = true;
        public double OutlineThickness { get; set; } = 2.5;
        public string OutlineColor { get; set; }
        public bool LabelsShown { get; set; }
        public double LabelScale { get; set; } = 1;
        public double PinOpacity { get; set; }
        public List<string> Pins { get; set; } = new List<string>();

        // 손 회전(도): [세로축, 가로축, 수직축]
        public double[] RotLeft { get; set; } = { 0, 0, 0 };
        public double[] RotRight { get; set; } = { 0, 0, 0 };
        // <260810_3>(5)(6)(7) 손 위치 오프셋: [x, y, z]
        public double[] OffsetLeft { get; set; } = { 0, 0, 0 };
        public double[] OffsetRight { get; set; } = { 0, 0, 0 };
        // <260719_9> 캡처 대상(0=둘 다, 1=왼손만, 2=오른손만)
        public int CaptureTarget { get; set; }
        // <260719_10> 배경 이미지
        public string BgImagePath { get; set; }
        public double BgWidth { get; set; }
        public double BgHeight { get; set; }
        public double BgScaleX { get; set; } = 1;
        public double BgScaleY { get; set; } = 1;
    }

    public static class SceneState
    {
        private static readonly JsonSerializerOptions Opt = new JsonSerializerOptions { WriteIndented = true };

        public static void Save(string path, HandScene scene, Camera3D cam, MainWindow win)
            => File.WriteAllText(path, Serialize(scene, cam, win));

        public static void Load(string path, HandScene scene, Camera3D cam, MainWindow win)
            => Deserialize(File.ReadAllText(path), scene, cam, win);

        // <260811_17> 끌어다 놓기 판별용: 저장 파일의 필수 항목인 Pose 안에 이 프로그램이 아는
        // 관절 슬라이더 키가 하나라도 있어야 '손 모델 정보가 있는 json'으로 본다.
        private static readonly HashSet<string> SliderKeys = BuildSliderKeys();

        private static HashSet<string> BuildSliderKeys()
        {
            var set = new HashSet<string>();
            foreach (var id in HandPose.AllSliderIds()) set.Add(id.Key);
            return set;
        }

        /// <summary>그 json 파일에 손 모델 정보(관절 슬라이더 값)가 들어 있는지. 예외 없이 판정한다.</summary>
        public static bool IsStateFile(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length == 0 || info.Length > 8L * 1024 * 1024) return false;   // 상태 파일은 수십 KB
                using (var doc = JsonDocument.Parse(File.ReadAllText(path)))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
                    if (!doc.RootElement.TryGetProperty("Pose", out var pose) || pose.ValueKind != JsonValueKind.Object)
                        return false;
                    foreach (var p in pose.EnumerateObject())
                        if (SliderKeys.Contains(p.Name)) return true;
                    return false;
                }
            }
            catch { return false; }   // 읽기 실패·깨진 json = 손 모델 파일 아님
        }

        /// <summary>현재 상태 전체를 JSON 문자열로. 저장(파일)과 실행 취소 기록(<260810_7>)이 공유한다.</summary>
        public static string Serialize(HandScene scene, Camera3D cam, MainWindow win)
        {
            var m = new StateModel
            {
                Pose = new Dictionary<string, double>(scene.Pose.Percent),
                OverallScale = scene.OverallScale,
                Volume = scene.Volume,
                FleshResponse = scene.FleshResponse,
                WebbingHeight = scene.WebbingHeight,
                WidthScale = scene.WidthScale,
                Transparency = scene.Transparency,
                FingerLen = new Dictionary<string, double>(scene.FingerLen),
                SkinColor = scene.SkinColor.ToString(),
                CamYaw = cam.YawDeg,
                CamPitch = cam.PitchDeg,
                CamRoll = cam.RollDeg,
                CamDist = cam.Distance,
                CamPanX = cam.PanX,
                CamPanY = cam.PanY,
                CamFov = cam.FovDeg,
                CamTarget = new[] { cam.Target.X, cam.Target.Y, cam.Target.Z },
                OffsetLeft = new[] { scene.OffsetLeft.X, scene.OffsetLeft.Y, scene.OffsetLeft.Z },
                OffsetRight = new[] { scene.OffsetRight.X, scene.OffsetRight.Y, scene.OffsetRight.Z },
            };
            foreach (var kv in scene.TipSkin) m.TipSkin[kv.Key] = kv.Value.ToString();
            win.CaptureExtra(m);

            return JsonSerializer.Serialize(m, Opt);
        }

        /// <summary>JSON 문자열의 상태를 그대로 복원한다(열기·실행 취소 공용).</summary>
        public static void Deserialize(string json, HandScene scene, Camera3D cam, MainWindow win)
        {
            var m = JsonSerializer.Deserialize<StateModel>(json);
            if (m == null) return;

            // 예외가 날 수 있는 해석(색 변환·null)을 scene 을 건드리기 **전에** 전부 끝낸다.
            // 중간에 던지면 반쯤 적용된 상태로 남아, 손상 파일을 열었을 때 화면·UI·모델이 어긋난다.
            if (m.Pose == null) throw new System.FormatException("Pose 항목이 없습니다.");
            Color? skin = string.IsNullOrEmpty(m.SkinColor)
                ? (Color?)null : (Color)ColorConverter.ConvertFromString(m.SkinColor);
            var tips = new Dictionary<string, Color>();
            if (m.TipSkin != null)
                foreach (var kv in m.TipSkin) tips[kv.Key] = (Color)ColorConverter.ConvertFromString(kv.Value);
            if (!string.IsNullOrEmpty(m.OutlineColor))
                ColorConverter.ConvertFromString(m.OutlineColor);   // ApplyExtra 가 쓰기 전에 유효성만 확인

            scene.Pose.Percent.Clear();
            // qksqhr(2026-08-11): Set() 경유로 0~100 클램프를 적용 — 손상 파일의 범위 밖 값이
            // 그대로 들어가 슬라이더(클램프 표시)와 모델이 어긋나던 우회로를 막는다.
            foreach (var kv in m.Pose) scene.Pose.Set(kv.Key, kv.Value);
            // 슬라이더 범위로 클램프(크기 0.5~1.8, 부피 0.6~1.6, 투명도 0~100) — 슬라이더가 이미 그 끝에 있으면
            // 값이 안 바뀌어 되쓰기 이벤트가 없으므로, 범위 밖 값이 모델에 그대로 남는 우회로를 여기서 막는다.
            scene.OverallScale = double.IsNaN(m.OverallScale) ? 1.0 : System.Math.Max(0.5, System.Math.Min(1.8, m.OverallScale));
            scene.Volume = double.IsNaN(m.Volume) ? 1.0 : System.Math.Max(0.6, System.Math.Min(1.6, m.Volume));
            // qksqhr(2026-08-11): 살 반응·물갈퀴도 슬라이더 범위로 클램프 — <260811_11>의 ValueSlider
            // 전환으로 UI 동기화가 모델을 되쓰지 않게 되면서, 손상 파일의 범위 밖 값이 '표시 100/실제
            // 100000' 같은 괴리를 만들 수 있게 됐다(다른 필드들과 같은 원칙).
            scene.FleshResponse = double.IsNaN(m.FleshResponse) ? 50 : System.Math.Max(0, System.Math.Min(100, m.FleshResponse));
            scene.WebbingHeight = double.IsNaN(m.WebbingHeight) ? 100 : System.Math.Max(0, System.Math.Min(200, m.WebbingHeight));
            // <260811_12> 좌우 비율 — 슬라이더 범위로 클램프(손상 파일 방어, qksqhr 원칙)
            scene.WidthScale = double.IsNaN(m.WidthScale) ? 1.0 : System.Math.Max(0.7, System.Math.Min(1.3, m.WidthScale));
            scene.Transparency = double.IsNaN(m.Transparency) ? 0 : System.Math.Max(0, System.Math.Min(100, m.Transparency));
            scene.FingerLen.Clear();
            if (m.FingerLen != null)
                // qksqhr(2026-08-11): 슬라이더 범위(0.6~1.5)로 클램프 — 손상 파일의 범위 밖 값이
                // 그대로 들어가면 슬라이더 표시와 실제 렌더 길이가 어긋난다(Pose·LabelScale과 동일 원칙).
                foreach (var kv in m.FingerLen)
                    scene.FingerLen[kv.Key] = double.IsNaN(kv.Value) ? 1.0 : System.Math.Max(0.6, System.Math.Min(1.5, kv.Value));
            if (skin.HasValue) scene.SkinColor = skin.Value;
            scene.TipSkin.Clear();
            foreach (var kv in tips) scene.TipSkin[kv.Key] = kv.Value;

            if (m.OffsetLeft != null && m.OffsetLeft.Length == 3)
                scene.OffsetLeft = new System.Windows.Media.Media3D.Vector3D(m.OffsetLeft[0], m.OffsetLeft[1], m.OffsetLeft[2]);
            if (m.OffsetRight != null && m.OffsetRight.Length == 3)
                scene.OffsetRight = new System.Windows.Media.Media3D.Vector3D(m.OffsetRight[0], m.OffsetRight[1], m.OffsetRight[2]);

            cam.YawDeg = m.CamYaw; cam.PitchDeg = m.CamPitch; cam.RollDeg = m.CamRoll; cam.Distance = m.CamDist;
            cam.PanX = m.CamPanX; cam.PanY = m.CamPanY; cam.FovDeg = m.CamFov;
            if (m.CamTarget != null && m.CamTarget.Length == 3)
                cam.Target = new System.Windows.Media.Media3D.Point3D(m.CamTarget[0], m.CamTarget[1], m.CamTarget[2]);

            win.ApplyExtra(m);
        }
    }
}
