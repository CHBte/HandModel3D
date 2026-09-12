using System;
using System.IO;
using System.Text.Json;

namespace HandModel3D
{
    /// <summary>
    /// <260811_21> '초기값' 파일(설정 창의 저장/불러오기). 크기·좌우·물갈퀴 살·손 위치(X/Y) 7개
    /// 값만 담는다 — 손 모델의 자세(Pose) 등은 담지 않으므로 StateModel(SceneState.cs)과는 다른,
    /// 완전히 별개의 작은 스키마다. IsDefaultsFile 표식으로 손 모양 파일과 구별한다.
    /// </summary>
    public sealed class DefaultValuesModel
    {
        public bool IsDefaultsFile { get; set; } = true;
        public double OverallScale { get; set; }
        public double WidthScale { get; set; }
        public double WebbingHeight { get; set; }
        public double OffsetLeftX { get; set; }
        public double OffsetLeftY { get; set; }
        public double OffsetRightX { get; set; }
        public double OffsetRightY { get; set; }
        public double ThumbCmcLR { get; set; } = Hand.HandPose.ThumbCmcLRDefault;   // <260811_21> 두 엄지 좌우 CMC(%)
    }

    public static class DefaultValues
    {
        private static readonly JsonSerializerOptions Opt = new JsonSerializerOptions { WriteIndented = true };

        /// <summary>
        /// <260811_21 추가지시> 설정 창에서 고친 초기값이 **다음 실행부터도** 적용되도록 두는 자리.
        /// 사용자가 고른 "초기값_*.json"(내보내기용)과는 별개로, 앱이 스스로 읽고 쓰는 파일이다.
        /// </summary>
        public static string UserSettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HandModel3D", "defaults.json");

        /// <summary>앱 시작 때 1회: 저장해 둔 초기값이 있으면 AppSettings.Def* 에 반영(없으면 소스 기본값 유지).</summary>
        public static void LoadUserSettings()
        {
            try { if (File.Exists(UserSettingsPath)) Load(UserSettingsPath); }
            catch { }   // 손상·권한 문제여도 소스 기본값으로 그냥 뜬다
        }

        /// <summary>설정 창에서 초기값을 고쳤을 때: 다음 실행에도 남도록 저장. 실패는 무시(기능 저해 없음).</summary>
        public static void SaveUserSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(UserSettingsPath));
                Save(UserSettingsPath);
            }
            catch { }
        }

        public static void Save(string path)
        {
            var m = new DefaultValuesModel
            {
                OverallScale = AppSettings.DefOverallScale,
                WidthScale = AppSettings.DefWidthScale,
                WebbingHeight = AppSettings.DefWebbingHeight,
                OffsetLeftX = AppSettings.DefOffsetLeftX,
                OffsetLeftY = AppSettings.DefOffsetLeftY,
                OffsetRightX = AppSettings.DefOffsetRightX,
                OffsetRightY = AppSettings.DefOffsetRightY,
                ThumbCmcLR = AppSettings.DefThumbCmcLR,
            };
            File.WriteAllText(path, JsonSerializer.Serialize(m, Opt));
        }

        /// <summary>그 json이 '초기값 파일'인지(표식 IsDefaultsFile=true) 예외 없이 판정한다.</summary>
        public static bool IsDefaultsFile(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length == 0 || info.Length > 1024 * 1024) return false;   // 초기값 파일은 수백 바이트
                using (var doc = JsonDocument.Parse(File.ReadAllText(path)))
                {
                    return doc.RootElement.ValueKind == JsonValueKind.Object
                        && doc.RootElement.TryGetProperty("IsDefaultsFile", out var flag)
                        && flag.ValueKind == JsonValueKind.True;
                }
            }
            catch { return false; }   // 읽기 실패·깨진 json = 초기값 파일 아님
        }

        /// <summary>
        /// 초기값 파일을 읽어 AppSettings.Def* 에 반영한다. 손상 파일 방어(qksqhr 원칙) — NaN·범위
        /// 밖 값은 각 슬라이더의 실제 허용 범위로 클램프한다(0.5~1.8·0.7~1.3·0~200·±30).
        /// 실패하면(초기값 파일이 아니거나 깨짐) false — 호출부가 메시지를 출력한다.
        /// </summary>
        public static bool Load(string path)
        {
            try
            {
                var m = JsonSerializer.Deserialize<DefaultValuesModel>(File.ReadAllText(path));
                if (m == null || !m.IsDefaultsFile) return false;

                double Clamp(double v, double lo, double hi, double dflt)
                    => double.IsNaN(v) ? dflt : Math.Max(lo, Math.Min(hi, v));

                AppSettings.DefOverallScale = Clamp(m.OverallScale, 0.5, 1.8, 0.95);
                AppSettings.DefWidthScale = Clamp(m.WidthScale, 0.7, 1.3, 1.3);
                AppSettings.DefWebbingHeight = Clamp(m.WebbingHeight, 0, 200, 0);
                AppSettings.DefOffsetLeftX = Clamp(m.OffsetLeftX, -30, 30, 1);
                AppSettings.DefOffsetLeftY = Clamp(m.OffsetLeftY, -30, 30, -4.9);
                AppSettings.DefOffsetRightX = Clamp(m.OffsetRightX, -30, 30, -5.5);
                AppSettings.DefOffsetRightY = Clamp(m.OffsetRightY, -30, 30, -4.9);
                // <260811_21> 관절 슬라이더와 같은 범위(0~100%)
                AppSettings.DefThumbCmcLR = Clamp(m.ThumbCmcLR, 0, 100, Hand.HandPose.ThumbCmcLRDefault);
                return true;
            }
            catch { return false; }
        }
    }
}
