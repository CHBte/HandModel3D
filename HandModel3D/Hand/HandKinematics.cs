using System;
using System.Collections.Generic;
using System.Windows.Media.Media3D;

namespace HandModel3D.Hand
{
    public enum SegKind { Meta, Proximal, Intermediate, Distal }

    /// <summary>손가락 마디 하나(피부 볼륨의 뼈대). A→B 로 뻗고 반지름은 RA→RB.</summary>
    public struct Segment
    {
        public Point3D A, B;
        public double RA, RB;
        public HandSide Side;
        public Digit Digit;
        public SegKind Kind;
    }

    /// <summary>관절 또는 손가락 끝. 라벨·고정점·클리핑·IK 가 참조한다.</summary>
    public sealed class JointNode
    {
        public string Id;            // 예: "R7.DIP", "R7.TIP", "L5.CMC"
        public HandSide Side;
        public Digit Digit;
        public JointKind Kind;
        public bool IsTip;
        public Point3D Pos;          // 월드 좌표
        public double Radius;
        public string Label;         // 3D 표시용 약어("R7 DIP"), 끝마디/아치는 규칙에 따름
        public int ChainIndex;       // 손가락 사슬 내 순서(0=가장 손목쪽)
    }

    /// <summary>한 손의 계산 결과(월드 좌표).</summary>
    public sealed class HandResult
    {
        public HandSide Side;
        public readonly List<Segment> Segments = new List<Segment>();
        public readonly List<JointNode> Joints = new List<JointNode>();
        public Point3D WristCenter;
        public double Scale = 1.0;    // overallScale(메시 빌더가 손바닥 치수에 사용)
    }

    /// <summary>
    /// 손 전체 회전. 기준은 프로그램 처음 실행 때의 좌표에서 잡은 세 축:
    /// 세로축 = 손목 가운데→중지 뿌리(MCP) 직선, 가로축 = 그 중점에서 손바닥 면 안의 수직선,
    /// 수직축 = 손바닥 법선. 회전 중심은 세로축의 중점. 축은 회전해도 초기값에 고정된다.
    /// </summary>
    public struct HandRotation
    {
        public double RollDeg;    // 세로축 둘레(손등이 좌우 대각선 위를 향하게 기울임)
        public double PitchDeg;   // 가로축 둘레(앞뒤 기울임)
        public double SwingDeg;   // 수직축 둘레(좌우 스윙)
        public bool IsZero => RollDeg == 0 && PitchDeg == 0 && SwingDeg == 0;
    }

    /// <summary>손 회전의 기준 축(초기 실행 시 계산 후 고정).</summary>
    public sealed class HandAxes
    {
        public Vector3D LongAxis;    // 세로축(손목→중지 MCP)
        public Vector3D CrossAxis;   // 가로축(손바닥 면 안, 세로축에 수직)
        public Vector3D NormalAxis;  // 수직축(손바닥 법선)
        public Point3D Center;       // 세로축의 중점(회전 중심)

        public static HandAxes From(HandResult r)
        {
            Point3D midMcp = r.WristCenter;
            foreach (var j in r.Joints)
                if (j.Digit == Digit.Middle && j.Kind == JointKind.MCP && !j.IsTip) { midMcp = j.Pos; break; }
            var u = midMcp - r.WristCenter;
            if (u.Length < 1e-6) u = new Vector3D(0, 1, 0);
            u.Normalize();
            var n = new Vector3D(0, 0, 1);                    // 초기 손바닥 법선(손등 +Z)
            var v = Vector3D.CrossProduct(n, u); v.Normalize(); // 손바닥 면 안의 가로축
            return new HandAxes
            {
                LongAxis = u,
                CrossAxis = v,
                NormalAxis = n,
                Center = r.WristCenter + (midMcp - r.WristCenter) * 0.5
            };
        }

        /// <summary>회전 행렬(적용 순서: 세로축 롤 → 가로축 피치 → 수직축 스윙).</summary>
        public Matrix3D Matrix(HandRotation rot)
        {
            var m = Matrix3D.Identity;
            if (rot.RollDeg != 0) m.RotateAt(new Quaternion(LongAxis, rot.RollDeg), Center);
            if (rot.PitchDeg != 0) m.RotateAt(new Quaternion(CrossAxis, rot.PitchDeg), Center);
            if (rot.SwingDeg != 0) m.RotateAt(new Quaternion(NormalAxis, rot.SwingDeg), Center);
            return m;
        }
    }

    /// <summary>
    /// 슬라이더 퍼센트 → 관절 각도 → 월드 좌표(순운동학). 손목(RC)은 고정 루트이며 다루지 않는다.
    /// 오른손 로컬 좌표에서 계산 후, 왼손은 X 반전 + 위치 이동으로 배치한다.
    /// 로컬 축: +X=엄지쪽(요골), +Y=손끝(원위), +Z=손등(사용자 쪽). 굴곡은 손끝을 -Z 로 만다.
    /// </summary>
    public static class HandKinematics
    {
        public const double HandSpacing = 12.5;   // 좌우 손 중심 간 X 오프셋(±)
        public const double GlobalScale = 1.0;

        public static HandResult Build(HandSide side, HandPose pose,
                                       double overallScale, IDictionary<string, double> fingerLenScale,
                                       HandAxes rotAxes = null, HandRotation rot = default,
                                       Vector3D offset = default, double widthScale = 1.0)
        {
            var r = new HandResult { Side = side };

            // 손 배치 변환. 로컬 모델은 손등이 카메라(+Z)를 향하고 엄지가 +X 쪽인 **왼손**이다
            // (위에서 내려다본 타이핑 자세: 왼손 엄지는 오른쪽=안쪽). 왼손은 그대로 -X 에 배치하고,
            // 오른손은 X 반전(미러) 후 +X 에 배치해 두 엄지가 화면 중앙에서 마주 보게 한다.
            // offset(<260810_3>): 손 위치 이동 — 배치 후 월드 좌표로 더해진다.
            var place = new Matrix3D();
            place.Scale(new Vector3D(overallScale, overallScale, overallScale));
            if (side == HandSide.Left)
            {
                place.Translate(new Vector3D(-HandSpacing + offset.X, offset.Y, offset.Z));
            }
            else
            {
                place.Append(MirrorX());
                place.Translate(new Vector3D(HandSpacing + offset.X, offset.Y, offset.Z));
            }

            // ----- 팔(손바닥+손목) 기준점 -----
            r.WristCenter = place.Transform(new Point3D(0.3, 0.0, 0));
            r.Scale = overallScale;

            double archCup = (1.0 - pose.Get(SliderId.Arch(side)) / 100.0) * Anatomy.ArchMaxCup;

            foreach (Digit d in Anatomy.AllDigits)
                BuildDigit(r, side, d, pose, place, archCup, LenScale(fingerLenScale, side, d));

            // <260811_12> 좌우 비율: 상하(Y) 길이는 그대로 두고 좌우(X)만 손목 중심 기준으로 비율 적용.
            // 관절·선분(뼈대) 좌표를 스케일하면 메시·픽킹·IK·충돌·윤곽이 전부 일관되게 넓어/좁아진다
            // (메시는 이 관절 좌표에서 파생됨). 손 회전(아래 후처리)보다 먼저, 무회전 기준으로 적용한다.
            if (System.Math.Abs(widthScale - 1.0) > 1e-9)
            {
                double wcx = r.WristCenter.X;
                for (int i = 0; i < r.Segments.Count; i++)
                {
                    var sg = r.Segments[i];
                    sg.A = new Point3D(wcx + (sg.A.X - wcx) * widthScale, sg.A.Y, sg.A.Z);
                    sg.B = new Point3D(wcx + (sg.B.X - wcx) * widthScale, sg.B.Y, sg.B.Z);
                    r.Segments[i] = sg;
                }
                foreach (var j in r.Joints)
                    j.Pos = new Point3D(wcx + (j.Pos.X - wcx) * widthScale, j.Pos.Y, j.Pos.Z);
            }

            // 손 전체 회전: 초기 기준 축 둘레의 강체 회전을 월드 좌표에 후처리
            if (rotAxes != null && !rot.IsZero)
            {
                var m = rotAxes.Matrix(rot);
                r.WristCenter = m.Transform(r.WristCenter);
                for (int i = 0; i < r.Segments.Count; i++)
                {
                    var sg = r.Segments[i];
                    sg.A = m.Transform(sg.A);
                    sg.B = m.Transform(sg.B);
                    r.Segments[i] = sg;
                }
                foreach (var j in r.Joints) j.Pos = m.Transform(j.Pos);
            }

            return r;
        }

        private static double LenScale(IDictionary<string, double> map, HandSide side, Digit d)
        {
            // 길이는 양손 대칭(해석 ⓓ): 손가락 종류(Digit)로만 키를 잡아 좌우가 같은 값을 쓴다.
            if (map == null) return 1.0;
            return map.TryGetValue(((int)d).ToString(), out double v) ? v : 1.0;
        }

        private static void BuildDigit(HandResult r, HandSide side, Digit d, HandPose pose,
                                       Matrix3D place, double archCup, double lenScale)
        {
            var dim = Anatomy.Dims(d);
            string code = Anatomy.FingerCode(side, d);

            if (d == Digit.Thumb)
            {
                BuildThumb(r, side, pose, place, dim, code, lenScale);
                return;
            }

            // 아치: 약지·소지 손허리뼈머리를 팔 중심 부근 축(Y)으로 회전시켜 손바닥을 오목하게.
            // <260810_3>(10): +각은 소지(-X쪽)를 손등(+Z)으로 보내 아치가 손등으로 접혔다(버그).
            // 부호를 뒤집어 손바닥(-Z) 쪽으로 오목해지게 한다.
            double archAngle = 0;
            if (d == Digit.Ring) archAngle = -archCup * 0.45;
            else if (d == Digit.Pinky) archAngle = -archCup * 1.0;

            var knuckle = new Point3D(dim.KnuckleX, dim.KnuckleY, 0.2);
            var frame = Matrix3D.Identity;
            if (archAngle != 0)
            {
                var pivot = new Point3D(1.3, dim.KnuckleY, 0);
                var m = RotAboutAxisAt(new Vector3D(0, 1, 0), archAngle, pivot);
                knuckle = m.Transform(knuckle);
                frame.Append(RotAxis(new Vector3D(0, 1, 0), archAngle));
            }

            // MCP: 좌우(abduct, +Z 축) → 상하(flex, +X 축)
            double splay = pose.Get(SliderId.Joint(side, d, JointKind.MCP, Axis.Abduct)) / 100.0 * dim.SplayDeg;
            double mcpFlex = (1.0 - pose.Get(SliderId.Joint(side, d, JointKind.MCP, Axis.Flex)) / 100.0) * Anatomy.McpMaxFlex;
            // +Z 축 회전은 +Y 를 -X 로 보내므로, splay 부호를 뒤집어야 검지가 +X(엄지쪽)로 벌어진다.
            frame.Append(RotAxis(new Vector3D(0, 0, 1), -splay));
            frame.Prepend(RotAxis(new Vector3D(1, 0, 0), -mcpFlex));

            double pipFlex = (1.0 - pose.Get(SliderId.Joint(side, d, JointKind.PIP, Axis.Flex)) / 100.0) * Anatomy.PipMaxFlex;
            double dipFlex = (1.0 - pose.Get(SliderId.Joint(side, d, JointKind.DIP, Axis.Flex)) / 100.0) * Anatomy.DipMaxFlex;

            double rBase = dim.Radius;
            var chain = new List<(Point3D pos, double rad, JointKind kind, bool tip)>();

            Point3D p = knuckle;
            chain.Add((p, rBase, JointKind.MCP, false));

            // 첫마디뼈(proximal)
            p = Advance(ref frame, p, dim.ProxLen * lenScale);
            // PIP 굴곡
            frame.Prepend(RotAxis(new Vector3D(1, 0, 0), -pipFlex));
            chain.Add((p, rBase * 0.92, JointKind.PIP, false));

            // 중간마디뼈(intermediate)
            p = Advance(ref frame, p, dim.IntLen * lenScale);
            frame.Prepend(RotAxis(new Vector3D(1, 0, 0), -dipFlex));
            chain.Add((p, rBase * 0.82, JointKind.DIP, false));

            // 끝마디뼈(distal) + 끝
            p = Advance(ref frame, p, dim.DistLen * lenScale);
            chain.Add((p, rBase * 0.62, JointKind.DIP, true));

            EmitChain(r, side, d, code, place, chain);
        }

        private static void BuildThumb(HandResult r, HandSide side, HandPose pose,
                                       Matrix3D place, Anatomy.DigitDims dim, string code, double lenScale)
        {
            // CMC 앵커는 손바닥 볼륨 안(z=0)에 둔다 — 엄지 밑동이 손바닥 실루엣과 깊이-연속으로
            // 이어져 윤곽선에 내부선이 생기지 않는다(단일폐곡선 목적).
            var cmc = new Point3D(dim.KnuckleX, dim.KnuckleY, 0.0);

            // 엄지는 손바닥면에서 벌어지고(요골 abduction) 대립(opposition) 시 축회전.
            double cmcFlexPct = pose.Get(SliderId.Joint(side, Digit.Thumb, JointKind.CMC, Axis.Flex)) / 100.0;
            double cmcAbdPct = pose.Get(SliderId.Joint(side, Digit.Thumb, JointKind.CMC, Axis.Abduct)) / 100.0;
            double cmcFlex = (1.0 - cmcFlexPct) * Anatomy.ThumbCmcMaxFlex;   // 손가락 쪽으로 접힘

            // 안장관절 자동 축회전(대립): 굽힘·벌림이 클수록 지문면이 손가락을 향하도록 프로네이션.
            double opposition = (1.0 - cmcFlexPct) * 35 + (1.0 - cmcAbdPct) * 20;

            // 좌우(벌림·모음) = 손바닥 평면 안의 splay 각 하나로 통합 매핑 (<260811_3>).
            // 벌림 100% = 53.4°(= 옛 기저 38 + 0.28·55) — 기존 rest(🖐️)와 동일해 회귀 없음.
            // 모음 0% = 8° — 실제 손처럼 곧게 편 엄지를 편 검지 옆면에 밀착시킬 수 있다
            // (`모인 손가락_손등 방향.jpg`, `모았다가 펴는 손_손바닥 방향.jpg`). 옛 구현은
            // 기저 -38°가 고정이라 모음 끝(0%)에서도 엄지가 검지에서 38° 벌어져 있었다.
            double cmcSplay = 8 + (30 + Anatomy.ThumbCmcMaxAbduct * 0.28) * cmcAbdPct;
            var frame = RotAxis(new Vector3D(0, 0, 1), -cmcSplay);
            frame.Prepend(RotAxis(new Vector3D(1, 0, 0), -cmcFlex));       // 굽힘: 손바닥(-Z) 쪽으로
            frame.Prepend(RotAxis(new Vector3D(0, 1, 0), opposition));     // 대립 축회전

            double mcpFlex = (1.0 - pose.Get(SliderId.Joint(side, Digit.Thumb, JointKind.MCP, Axis.Flex)) / 100.0) * Anatomy.ThumbMcpMaxFlex;
            double mcpAbd = pose.Get(SliderId.Joint(side, Digit.Thumb, JointKind.MCP, Axis.Abduct)) / 100.0 * Anatomy.ThumbMcpMaxAbduct * 0.6;
            double ipFlex = (1.0 - pose.Get(SliderId.Joint(side, Digit.Thumb, JointKind.IP, Axis.Flex)) / 100.0) * Anatomy.IpMaxFlex;

            double rBase = dim.Radius;
            var chain = new List<(Point3D pos, double rad, JointKind kind, bool tip)>();

            Point3D p = cmc;
            chain.Add((p, rBase * 1.05, JointKind.CMC, false));

            // 손허리뼈(metacarpal)
            p = Advance(ref frame, p, dim.MetaLen * lenScale);
            frame.Prepend(RotAxis(new Vector3D(0, 0, 1), mcpAbd));
            frame.Prepend(RotAxis(new Vector3D(1, 0, 0), -mcpFlex));
            chain.Add((p, rBase * 0.92, JointKind.MCP, false));

            // 첫마디뼈(proximal)
            p = Advance(ref frame, p, dim.ProxLen * lenScale);
            frame.Prepend(RotAxis(new Vector3D(1, 0, 0), -ipFlex));
            chain.Add((p, rBase * 0.8, JointKind.IP, false));

            // 끝마디뼈(distal) + 끝
            p = Advance(ref frame, p, dim.DistLen * lenScale);
            chain.Add((p, rBase * 0.6, JointKind.IP, true));

            EmitChain(r, side, Digit.Thumb, code, place, chain);
        }

        private static void EmitChain(HandResult r, HandSide side, Digit d, string code, Matrix3D place,
                                      List<(Point3D pos, double rad, JointKind kind, bool tip)> chain)
        {
            // '크기'(OverallScale)는 place 행렬에 구워져 위치에는 적용되지만, 반지름은 스칼라라
            // 별도로 곱해야 한다 — 안 곱하면 크기를 줄일 때 손가락 간격만 좁아지고 굵기는 그대로라
            // 손가락이 서로 파묻히는 등 손 비율이 무너진다. 행렬 X축 길이 = 스케일(미러여도 양수).
            double radScale = new Vector3D(place.M11, place.M12, place.M13).Length;

            // 마디(세그먼트)
            for (int i = 0; i < chain.Count - 1; i++)
            {
                var a = chain[i];
                var b = chain[i + 1];
                r.Segments.Add(new Segment
                {
                    A = place.Transform(a.pos),
                    B = place.Transform(b.pos),
                    RA = a.rad * radScale,
                    RB = b.rad * radScale,
                    Side = side,
                    Digit = d,
                    Kind = SegKindFor(d, i)
                });
            }
            // 관절/끝 노드
            for (int i = 0; i < chain.Count; i++)
            {
                var c = chain[i];
                var node = new JointNode
                {
                    Side = side,
                    Digit = d,
                    Kind = c.kind,
                    IsTip = c.tip,
                    Pos = place.Transform(c.pos),
                    Radius = c.rad * radScale,
                    ChainIndex = i
                };
                node.Id = c.tip ? $"{code}.TIP" : $"{code}.{c.kind}";
                node.Label = c.tip ? null : $"{code} {c.kind}";
                r.Joints.Add(node);
            }
        }

        private static SegKind SegKindFor(Digit d, int i)
        {
            if (d == Digit.Thumb)
                return i == 0 ? SegKind.Meta : (i == 1 ? SegKind.Proximal : SegKind.Distal);
            return i == 0 ? SegKind.Proximal : (i == 1 ? SegKind.Intermediate : SegKind.Distal);
        }

        // ---- 회전/전진 헬퍼 (WPF 행벡터 규약: v' = v * M) ----

        /// <summary>현재 frame 의 로컬 +Y 방향으로 len 만큼 전진한 새 위치.</summary>
        private static Point3D Advance(ref Matrix3D frame, Point3D p, double len)
        {
            Vector3D fwd = frame.Transform(new Vector3D(0, 1, 0));
            fwd.Normalize();
            return p + fwd * len;
        }

        private static Matrix3D RotAxis(Vector3D axis, double deg)
        {
            var m = new Matrix3D();
            m.Rotate(new Quaternion(axis, deg));
            return m;
        }

        private static Matrix3D RotAboutAxisAt(Vector3D axis, double deg, Point3D center)
        {
            var m = new Matrix3D();
            m.RotateAt(new Quaternion(axis, deg), center);
            return m;
        }

        private static Matrix3D MirrorX()
        {
            return new Matrix3D(-1, 0, 0, 0,
                                 0, 1, 0, 0,
                                 0, 0, 1, 0,
                                 0, 0, 0, 1);
        }
    }
}
