using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HandModel3D.Render;

namespace HandModel3D.Hand
{
    /// <summary>
    /// 두 손의 상태(포즈·크기·볼륨·색·투명도·손끝색)와 3D 모델 생성을 담당하는 컨트롤러.
    /// 슬라이더·드래그·저장/열기가 이 상태를 읽고 쓴다.
    /// </summary>
    public sealed class HandScene
    {
        public HandPose Pose = HandPose.Default();
        // <260811_21> 아래 필드의 초기값(0.95/0/1.3)은 설정 창 '초기값' 기본값(AppSettings.Def*)과
        // 같은 값. 실제 시작값은 MainWindow 가 ApplyDefaultsToScene() 으로 덮어쓰므로(저장해 둔
        // 초기값이 있으면 그것), 여기 값은 그 직전 한순간과 헤드리스 검증의 기준선이다.
        public double OverallScale = 0.95;
        public double Volume = 1.0;                 // 근육·지방 부피(굵기)
        public double FleshResponse = 50;           // 살 반응 % (<260810_2>): 모으면 볼록·벌리면 얇게, 0=끔
        public double WebbingHeight = 0;            // 물갈퀴 살 높이 % (<260811_4-1>): 0=없음, 100=기본, 200=최대
        public double WidthScale = 1.3;             // 좌우 비율 (<260811_12>): 상하 대비 좌우 길이, 0.7~1.3
        public readonly Dictionary<string, double> FingerLen = new Dictionary<string, double>(); // 손가락코드 → 길이배율(양손 대칭)
        public Color SkinColor = Color.FromRgb(0xE8, 0xB6, 0x98); // 기본 피부색
        public double Transparency = 0;             // 0~100(%) — 100이면 완전 투명
        public readonly Dictionary<string, Color> TipSkin = new Dictionary<string, Color>();   // 손가락코드 → 끝마디 피부색 override

        public HandResult Left { get; private set; }
        public HandResult Right { get; private set; }
        public HandMeshes LeftMesh { get; private set; }
        public HandMeshes RightMesh { get; private set; }

        // 손 위치 오프셋(<260810_3>(5)(6)(7)): 손 자체의 월드 이동. 방향키(양손)·[Ctrl]+한손 드래그·
        // '손 위치' 패널이 조작한다. 시점(카메라)과 무관하게 손이 실제로 움직인다.
        // <260811_21> 초기값: 왼손 X=1·Y=-4.9, 오른손 X=-5.5·Y=-4.9 (Z는 0 유지).
        public Vector3D OffsetLeft = new Vector3D(1, -4.9, 0);
        public Vector3D OffsetRight = new Vector3D(-5.5, -4.9, 0);

        // 손 전체 회전. 축은 처음 실행 때(디폴트 상태) 계산 후 고정.
        public HandRotation RotLeft, RotRight;
        public HandAxes AxesLeft { get; private set; }
        public HandAxes AxesRight { get; private set; }

        public HandRotation RotFor(HandSide s) => s == HandSide.Left ? RotLeft : RotRight;
        public HandAxes AxesFor(HandSide s) => s == HandSide.Left ? AxesLeft : AxesRight;

        public Vector3D OffsetFor(HandSide s) => s == HandSide.Left ? OffsetLeft : OffsetRight;

        /// <summary>픽킹·IK 용: 현재 상태(회전 포함)의 관절 좌표 계산.</summary>
        public HandResult BuildSide(HandSide s)
            => HandKinematics.Build(s, Pose, OverallScale, FingerLen, AxesFor(s), RotFor(s), OffsetFor(s), WidthScale);

        public void Recompute()
        {
            // 기준 축은 최초 1회(처음 실행 상태) 고정
            if (AxesLeft == null)
            {
                AxesLeft = HandAxes.From(HandKinematics.Build(HandSide.Left, Pose, OverallScale, FingerLen));
                AxesRight = HandAxes.From(HandKinematics.Build(HandSide.Right, Pose, OverallScale, FingerLen));
            }

            // 메시는 무회전 좌표로 만들고 정점만 회전(팜 로프트의 +Y 가정 유지),
            // 관절 결과(Left/Right)는 회전 포함 좌표(픽킹·IK·라벨·영역제한용).
            var left0 = HandKinematics.Build(HandSide.Left, Pose, OverallScale, FingerLen, offset: OffsetLeft, widthScale: WidthScale);
            var right0 = HandKinematics.Build(HandSide.Right, Pose, OverallScale, FingerLen, offset: OffsetRight, widthScale: WidthScale);
            Matrix3D? mL = RotLeft.IsZero ? (Matrix3D?)null : AxesLeft.Matrix(RotLeft);
            Matrix3D? mR = RotRight.IsZero ? (Matrix3D?)null : AxesRight.Matrix(RotRight);
            LeftMesh = HandMeshBuilder.Build(left0, Volume, mL, FleshResponse / 100.0, WebbingHeight / 100.0);
            RightMesh = HandMeshBuilder.Build(right0, Volume, mR, FleshResponse / 100.0, WebbingHeight / 100.0);
            Left = mL.HasValue ? BuildSide(HandSide.Left) : left0;
            Right = mR.HasValue ? BuildSide(HandSide.Right) : right0;
        }

        public IEnumerable<JointNode> AllJoints()
        {
            if (Left != null) foreach (var j in Left.Joints) yield return j;
            if (Right != null) foreach (var j in Right.Joints) yield return j;
        }

        public HandResult ResultFor(HandSide s) => s == HandSide.Left ? Left : Right;

        public double SkinAlpha => (100.0 - Transparency) / 100.0;

        /// <summary>
        /// 두 손의 렌더 메시 목록(몸통 + 손가락별 끝마디). skinVisible=false 면 빈 목록(윤곽선만 보기).
        /// </summary>
        /// <summary>
        /// 렌더 메시 목록. skinVisible=false('윤곽선만')면 알파와 무관하게 불투명으로 넘긴다(윤곽선
        /// 패스가 채움을 지움). 투명도 100%면 알파 0(피부 안 보임) — 태그 패스는 알파와 무관하게
        /// 실루엣을 기록하므로 윤곽선은 계속 정확하다.
        /// </summary>
        public List<RenderMesh> GetRenderMeshes(bool skinVisible = true)
        {
            var list = new List<RenderMesh>();
            if (Left == null) return list;
            double alpha = skinVisible ? SkinAlpha : 1.0;
            AddHand(list, HandSide.Left, LeftMesh, alpha);
            AddHand(list, HandSide.Right, RightMesh, alpha);
            return list;
        }

        public static int HandBaseTag(HandSide s) => s == HandSide.Left ? 0 : 10;
        public static int DigitTag(HandSide s, Digit d) => HandBaseTag(s) + 1 + (int)d;

        private void AddHand(List<RenderMesh> list, HandSide side, HandMeshes m, double alpha)
        {
            list.Add(new RenderMesh { Mesh = m.Palm, Color = SkinColor, Alpha = alpha, Tag = HandBaseTag(side) });
            foreach (var kv in m.Fingers)
                list.Add(new RenderMesh { Mesh = kv.Value, Color = SkinColor, Alpha = alpha, Tag = DigitTag(side, kv.Key) });
            foreach (var kv in m.Tips)
            {
                string code = Anatomy.FingerCode(side, kv.Key);
                Color c = TipSkin.TryGetValue(code, out var tc) ? tc : SkinColor;
                list.Add(new RenderMesh { Mesh = kv.Value, Color = c, Alpha = alpha, Tag = DigitTag(side, kv.Key) });
            }
        }
    }
}
