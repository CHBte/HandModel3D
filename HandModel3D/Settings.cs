using System;

namespace HandModel3D
{
    /// <summary>
    /// 앱 설정. 캡처/저장 기본 위치는 어느 PC 든 바탕화면 (3-10-3, 3-11).
    /// <260811_15>: 위치를 비트맵 캡처(PNG)·벡터 캡처(SVG)·저장 파일(json) 셋으로 나누고,
    /// 캡처 두 종류는 파일 이름을 캡처할 때 직접 입력할지(수동) 자동 규칙을 쓸지 고를 수 있다.
    /// </summary>
    public static class AppSettings
    {
        private static readonly string DesktopDir =
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        public static string PngDir = DesktopDir;    // 비트맵 캡처(PNG)
        public static string SvgDir = DesktopDir;    // 벡터 캡처(SVG)
        public static string JsonDir = DesktopDir;   // 저장 파일(json) — 저장/열기 대화상자의 시작 폴더

        // true = 캡처할 때마다 저장 대화상자로 파일 이름을 직접 입력한다(자동 이름을 미리 채워 준다).
        public static bool PngManualName;
        public static bool SvgManualName;

        // <260811_21> '초기값' — 설정 창(세 위치 지정 영역 아래)에서 편집한다. 여기 아래 상수는
        // **저장해 둔 초기값이 없을 때의 기본값**이며, HandScene/HandPose/MainWindow.xaml 의 같은
        // 값과 짝을 이룬다(그쪽은 이 값이 적용되기 전 한순간의 시작값).
        // 실제 반영 경로(<260811_21 추가지시>): 앱 시작 = DefaultValues.LoadUserSettings() →
        // MainWindow.ApplyDefaultsToScene(), 설정 창에서 고침 = SaveUserSettings() + 즉시
        // ApplyDefaultsToScene(). 다음 실행용 저장 위치는 DefaultValues.UserSettingsPath.
        public static double DefOverallScale = 0.95;
        public static double DefWidthScale = 1.3;
        public static double DefWebbingHeight = 0;
        public static double DefOffsetLeftX = 1;
        public static double DefOffsetLeftY = -4.9;
        public static double DefOffsetRightX = -5.5;
        public static double DefOffsetRightY = -4.9;
        // <260811_21> 두 엄지의 좌우 CMC(%) — HandPose.ThumbCmcLRDefault 와 같은 값.
        public static double DefThumbCmcLR = Hand.HandPose.ThumbCmcLRDefault;

        /// <summary>헤드리스 검증용: 세 위치를 한 폴더로 몰아준다(일반 실행 경로에서는 쓰지 않는다).</summary>
        public static void UseSingleDir(string dir) { PngDir = SvgDir = JsonDir = dir; }
    }
}
