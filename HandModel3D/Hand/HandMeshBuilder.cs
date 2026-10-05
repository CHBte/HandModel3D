using System;
using System.Collections.Generic;
using System.Windows.Media.Media3D;
using HandModel3D.Geometry;

namespace HandModel3D.Hand
{
    /// <summary>
    /// 한 손의 메시 결과. 손바닥 / 손가락별 몸통 / 손가락별 끝마디로 나눈다.
    /// 손가락별 분리 = 윤곽선의 손가락 경계 검출(태그) + 끝마디 색 지정에 쓴다.
    /// </summary>
    public sealed class HandMeshes
    {
        public MeshGeometry3D Palm;
        public readonly Dictionary<Digit, MeshGeometry3D> Fingers = new Dictionary<Digit, MeshGeometry3D>();
        public readonly Dictionary<Digit, MeshGeometry3D> Tips = new Dictionary<Digit, MeshGeometry3D>();
    }

    /// <summary>
    /// 사람 손 형태의 피부 메시 생성. 뼈는 양끝(뼈 머리)이 굵고 골간이 잘록하므로(`뼈.png`)
    /// 손가락 마디를 그 프로파일의 레이드로 만들고, 관절 자리는 구로 볼록하게 잇는다.
    /// 손바닥은 손목에서 너클(MCP 줄)로 갈수록 넓어지는 수퍼타원 로프트 + 엄지쪽(무지구)·
    /// 소지쪽(소지구) 불룩 + 중수골 부채로 만든다. 모든 좌표는 월드(미러 반영 후) 기준.
    /// </summary>
    public static class HandMeshBuilder
    {
        /// <summary>골간이 잘록하고 양끝이 굵은 뼈 프로파일 반지름 배열.</summary>
        private static double[] BoneProfile(double rA, double rB, int n = 9)
        {
            var r = new double[n];
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / (n - 1);
                double baseR = rA + (rB - rA) * t;
                double bulge = 0.08 * Math.Exp(-Sq((t - 0.14) / 0.14)) + 0.08 * Math.Exp(-Sq((t - 0.86) / 0.14));
                double waist = 0.055 * Math.Exp(-Sq((t - 0.5) / 0.24));
                r[i] = baseR * (1 + bulge - waist);
            }
            return r;
        }

        private static double Sq(double x) => x * x;

        /// <summary>
        /// rotM: 손 전체 회전. 팜 로프트가 +Y 수직 가정으로 만들어지므로,
        /// 메시는 무회전 좌표(r)로 만들고 마지막에 정점·법선만 회전한다.
        /// fleshResponse: 살 반응 정도 0~1 (<260810_2>) — 뼈가 모이면 살이 매우 약간 볼록해지고
        /// 벌어지면 늘어나며 매우 약간 얇아지는 효과의 강도(0=끔).
        /// </summary>
        public static HandMeshes Build(HandResult r, double volume, Matrix3D? rotM = null,
                                       double fleshResponse = 0.5, double webbingHeight = 1.0)
        {
            double v = volume;
            double s = r.Scale;
            var meshes = new HandMeshes();

            // ----- 관절 위치 수집(월드) -----
            var mcp = new Dictionary<Digit, JointNode>();
            JointNode thumbCmc = null, thumbMcp = null;
            foreach (var j in r.Joints)
            {
                if (j.IsTip) continue;
                if (j.Kind == JointKind.MCP && j.Digit != Digit.Thumb) mcp[j.Digit] = j;
                if (j.Digit == Digit.Thumb && j.Kind == JointKind.CMC) thumbCmc = j;
                if (j.Digit == Digit.Thumb && j.Kind == JointKind.MCP) thumbMcp = j;
            }
            if (!mcp.ContainsKey(Digit.Index) || !mcp.ContainsKey(Digit.Pinky) || thumbCmc == null)
                return meshes;   // 예외적 상황 — 빈 메시

            var idxK = mcp[Digit.Index].Pos;
            var pkyK = mcp[Digit.Pinky].Pos;
            double avgKnuckY = 0;
            foreach (var kv in mcp) avgKnuckY += kv.Value.Pos.Y;
            avgKnuckY /= mcp.Count;

            // 살 반응 (<260810_2>) — 손가락 벌림 정도(0=모음, 1=벌림)를 PIP 스팬/너클 스팬 비로 측정.
            // 손가락을 모으면 너클 줄·손바닥 상단 살이 매우 약간 볼록해지고, 벌리면 매우 약간 얇아진다.
            var pip = new Dictionary<Digit, Point3D>();
            foreach (var j in r.Joints)
            {
                if (j.IsTip || j.Kind != JointKind.PIP) continue;
                if (j.Digit != Digit.Thumb) pip[j.Digit] = j.Pos;
            }
            Point3D? idxPip = pip.ContainsKey(Digit.Index) ? pip[Digit.Index] : (Point3D?)null;
            Point3D? pkyPip = pip.ContainsKey(Digit.Pinky) ? pip[Digit.Pinky] : (Point3D?)null;
            double fingerMul = 1;
            if (idxPip.HasValue && pkyPip.HasValue)
            {
                double knuckSpan = Math.Max(0.1, Math.Abs(idxK.X - pkyK.X));
                double ratio = Math.Abs(idxPip.Value.X - pkyPip.Value.X) / knuckSpan;
                double spreadF01 = Math.Max(0, Math.Min(1, (ratio - 1.0) / 0.4));
                fingerMul = 1 + fleshResponse * (0.10 * (1 - spreadF01) - 0.06 * spreadF01);
            }
            double thenarFleshMul = 1;   // 웹 블록에서 엄지 벌림 측정 후 채워진다

            // ----- 손바닥 로프트 -----
            // <260810_8>: 손바닥 살의 기준 좌표는 절대 상수가 아니라 손목 중심(WristCenter — 손 위치
            // 오프셋이 반영된 값)에서 파생해야 한다. 절대 상수를 쓰면 한 손만 위치 이동했을 때
            // 관절(뼈대)만 이동하고 손바닥 아래끝·단면 깊이가 제자리에 남아 손 모양이 망가진다.
            var palm = new MeshBuilder();
            double baseY = r.WristCenter.Y;   // 손 위치 오프셋 Y
            double baseZ = r.WristCenter.Z;   // 손 위치 오프셋 Z
            double knuckCX = (idxK.X + pkyK.X) / 2;
            double thumbDir = Math.Sign(thumbCmc.Pos.X - knuckCX);   // 엄지쪽 방향(왼손 +, 오른손 -)
            double knuckW = Math.Abs(idxK.X - pkyK.X) / 2 + 1.05 * s * v;
            double topY = avgKnuckY - 0.55 * s;
            double wristY = baseY - 2.3 * s;
            double wristW = knuckW * 0.70;
            double wristCX = r.WristCenter.X;
            double halfT = 1.22 * s * v;

            var rings = new List<(Point3D c, double halfW, double halfT)>();
            const int N = 11;
            for (int i = 0; i < N; i++)
            {
                double t = (double)i / (N - 1);
                double y = wristY + (topY - wristY) * t;
                // 오목 폭 곡선: 손목에서 빠르게 넓어지다 너클 쪽에서 완만(실제 손 실루엣, `뼈-살_4`)
                double w = wristW + (knuckW - wristW) * Math.Pow(t, 0.72);
                // 중간 높이에서 엄지쪽으로 살짝 넓힘(무지구가 실루엣에 곡선을 만들도록)
                double thenarBump = 0.55 * s * v * Math.Exp(-Sq((t - 0.55) / 0.34));
                double cx = wristCX + (knuckCX - wristCX) * t + thumbDir * thenarBump * 0.5;
                double tk = halfT * (0.94 + 0.06 * Math.Sin(Math.PI * t));
                double ww = w + thenarBump * 0.5;
                if (i == 0) { ww *= 0.92; tk *= 0.85; }   // 손목 절단면 둥글게
                rings.Add((new Point3D(cx, y, baseZ), ww, tk));
            }
            // 팜 상단 둥근 숄더(손가락 사이 웹빙 라인): 위로 갈수록 오므린 링 2개.
            // 손가락을 모으면 살 반응(fingerMul)으로 매우 약간 볼록해진다 (<260810_2>).
            // <260811_1>: 살은 손바닥 쪽에만 붙어 있으므로(손등은 뼈에 밀착), 두께 증가분만큼 중심을
            // 손바닥(-Z)으로 내려 등쪽 표면은 고정하고 손바닥 쪽으로만 부풀린다. 폭(X) 확장도 하지 않는다.
            {
                double sh1 = halfT * 0.93, sh2 = halfT * 0.80;
                double d1 = sh1 * (fingerMul - 1), d2 = sh2 * (fingerMul - 1);
                rings.Add((new Point3D(knuckCX, topY + 0.55 * s, baseZ - d1), knuckW * 0.92, sh1 + d1));
                rings.Add((new Point3D(knuckCX, topY + 1.0 * s, baseZ - d2), knuckW * 0.72, sh2 + d2));
            }
            palm.AddTube(rings, 2.6, 26);

            // 소지구(새끼두덩) — <260811_2 확장> 해부학 재학습(손가락관절-근육개요 영상 10:30~11:06
            // ADM/FDMB, Gray425, OpenStax1121) 반영: 뼈 축 대칭 타원체가 아니라, 새끼벌림근(ADM)이
            // 주역인 **손목(콩알뼈)→새끼 MCP 방추**다. 무지구와 같은 원리(기시→정지 사이에 걸린
            // 손바닥쪽 방추)로 만들되 더 좁고 낮고 길다. 배(belly)는 손목 쪽 1/3~1/2 지점이 가장
            // 두껍고, 척측(자쪽)으로 살짝 볼록해 위에서 본 자쪽 실루엣이 완만한 곡선이 된다.
            {
                double uDir = -thumbDir;                       // 척측(새끼쪽) 방향
                double hypoF = Math.Max(0, fingerMul - 1) * 0.5;   // 살 반응 절반 강도(변화 미미한 부위)
                var h0 = new Point3D(wristCX + uDir * wristW * 0.62, wristY + 1.2 * s,
                                     baseZ - 0.30 * halfT);    // 콩알뼈 자리(손목 자쪽·손바닥쪽)
                var h1 = new Point3D(pkyK.X + uDir * 0.10 * s, pkyK.Y - 0.5 * s,
                                     Math.Min(pkyK.Z, baseZ) - 0.25 * s);   // 새끼 MCP 바닥쪽
                double hDorsalCap = baseZ + halfT * 0.55;      // 등쪽 상한(무지구와 동일 규칙)
                double hMid = (0.92 + hypoF) * s * v;
                var hm = new Point3D(h0.X * 0.60 + h1.X * 0.40 + uDir * 0.30 * s,
                                     h0.Y * 0.60 + h1.Y * 0.40,
                                     Math.Min((h0.Z + h1.Z) / 2 - 0.25 * s, hDorsalCap - hMid));
                palm.AddTaperedCylinder(h0, hm, 0.62 * s * v, hMid, 14);
                palm.AddTaperedCylinder(hm, h1, hMid, 0.52 * s * v, 14);
                palm.AddSphere(hm, hMid, 10, 14);
            }

            // 첫째 등쪽뼈사이근(FDI) 마운드 — <260811_2 확장> 손등에서 겉으로 보이는 '유일한' 손
            // 내재근(영상 12:54, Gray428, OpenStax1121). 엄지-검지 중수골 사이 손등 쪽의 낮은
            // 눈물방울 융기로, 정점은 검지 중수골 몸통 중간의 엄지쪽 옆. 과장 금지(낮은 언덕) —
            // 정적 볼륨만 넣는다(엄지 모음 시 커지는 동적 변화는 뿔 회귀 위험이 커서 생략).
            // 정점 높이는 팜 등쪽 표면 + 약 0.15s로 낮게, X 범위는 팜-웹 모서리 안쪽으로 —
            // 엄지를 굽혀 웹이 수축한 포즈에서 타원체 가장자리가 혹처럼 홀로 드러나지 않게 한다.
            // 나머지 2~4번 등쪽·바닥쪽뼈사이근/벌레근/힘줄은 표면 불가시라 지오메트리 없음.
            // 중수골 사이 골짜기도 파지 않는다(정상 손은 근육이 메움 — 파이면 병적 외형).
            palm.AddEllipsoid(new Point3D(idxK.X + thumbDir * 0.50 * s, topY - 1.9 * s, baseZ + halfT * 0.50),
                              0.90 * s * v, 1.7 * s, 0.70 * s * v, 10, 14);

            // 엄지-검지 사이 웹(제1 물갈퀴 공간). `뼈-살` 자료: 엄지 중수골(CMC→MCP)은 거의
            // 전부 살에 묻히고, 웹 가장자리는 엄지 MCP에서 검지 MCP 하단으로 이어지는 완만한
            // 오목 곡선이다. 수평 단면 로프트로 그 살을 채운다(팜과 같은 태그 → 경계선 없음).
            //
            // <260810_2> 버그 수정: 이전 구현은 엄지 축 위치를 링의 절대 Y로 역산했는데
            // (분모 max(0.1, tm.Y-tc.Y)), CMC 굽힘으로 엄지가 손바닥 쪽으로 누우면(tm.Y≈tc.Y)
            // 분모 클램프 때문에 모든 링이 MCP의 X까지 뻗고 Z는 팜 평면(≈0)에 남아, 웹이
            // 손가락처럼 튀어나와 보였다. 축 위치를 링 파라미터 t로 CMC→MCP 를 3차원 직접
            // 보간(X·Z 모두 추종)하도록 바꿔 웹이 항상 엄지 중수골 살 속에 묻히게 한다.
            if (thumbMcp != null)
            {
                var tc = thumbCmc.Pos;
                var tm = thumbMcp.Pos;

                // 살 반응 (<260810_2>): 엄지가 손에서 벌어진 정도(0=모음, 1=벌림)를 기하로 측정해
                // 모으면 웹을 매우 약간 두껍게(살이 밀려 볼록), 벌리면 매우 약간 얇게(늘어짐) 한다.
                double dNorm = (tm - idxK).Length /
                               Math.Max(0.1, (tm - tc).Length + (idxK - tc).Length);
                double spread01 = Math.Max(0, Math.Min(1, (dNorm - 0.35) / 0.45));
                double fleshMul = 1 + fleshResponse * (0.35 * (1 - spread01) - 0.18 * spread01);

                // 웹 가장자리는 실물에서 오목하게 처진다: 상단을 엄지 MCP보다 살짝 낮게 잡아
                // 검지 뿌리(높음)→웹(낮음)→엄지로 이어지는 오목 곡선을 만든다.
                // 엄지가 완전히 굽어 MCP가 CMC 아래로 와도 로프트가 뒤집히지 않게 하한을 둔다.
                double webBotY = tc.Y - 1.4 * s;   // 팜 폭 안쪽 깊이에서 시작해 계단 없이 흡수
                double webTopY = Math.Min(idxK.Y - 1.5 * s, tm.Y - 0.1 * s);
                webTopY = Math.Max(webTopY, webBotY + 0.8 * s);

                // 엄지가 손바닥 쪽으로 굽은 정도(0=안 굽음, 1=크게 굽음). CMC 상하 굽힘은 엄지
                // MCP 가 -Z(손바닥 쪽)로 내려가는 것으로 나타난다. 굽을수록 웹 상단을 엄지 축에
                // 붙이고(치우침) 좁히고(테이퍼) Z 를 더 따라가게 해, 펴진 웹 판이 남아 지느러미처럼
                // 보이는 것을 막는다. rest(굽힘 0)에서는 아무 변화가 없다.
                double bend01 = Math.Min(1, Math.Max(0, tc.Z - tm.Z) / (2.2 * s));

                var webRings = new List<(Point3D c, double halfW, double halfT)>();
                int WN = 7;
                for (int i = 0; i < WN; i++)
                {
                    double t = (double)i / (WN - 1);
                    double y = webBotY + (webTopY - webBotY) * t;
                    // 엄지 축 위의 대응점: CMC→MCP 를 t 로 직접 보간(X·Z, Y 역산 없음 — 안정)
                    double axisX = tc.X + (tm.X - tc.X) * t;
                    double axisZ = tc.Z + (tm.Z - tc.Z) * t;
                    // 웹 수평 스팬: 팜 안쪽(검지 MCP 쪽)에서 엄지 축까지. 굽힘이 클수록 검지쪽
                    // 시작점을 엄지 쪽으로 당겨(팜 모서리 밖 능선 노출 방지) 웹을 접는다.
                    // <260811_2>: 접기를 상단(t 비례)에만 걸면 중간 높이 링들의 검지쪽 가장자리가
                    // 갈고리(뿔) 능선으로 남는다 — 소거 실험으로 확인. 그래서 굽힘 시에는 **모든 높이**를
                    // bend01 비례로 접고(t 로는 추가 가중만) 웹이 엄지 밑동 옆 작은 덩어리로 수축하게 한다.
                    double fold = (0.45 + 0.45 * t) * bend01;
                    double innerX = knuckCX + thumbDir * (1.2 + 1.7 * fold) * s;
                    double outerX = axisX + thumbDir * 0.55 * s * v;
                    double lean = 0.5 + 0.42 * fold;
                    double cx = innerX * (1 - lean) + outerX * lean;
                    double hw = Math.Abs(outerX - innerX) / 2 * (1 - 0.68 * fold) + 0.35 * s * v;
                    // Z: 엄지 축의 Z 를 따라간다(굽힘 추종). axisZ 자체가 t 보간이라(하단=CMC=팜 평면)
                    // 하단은 자연히 팜 평면에 묻힌다 — t 를 다시 곱하면 중간 링이 얕게 붕 떠
                    // 팜 옆으로 얇은 판(지느러미)이 노출되므로 곱하지 않는다.
                    // <260810_8>: 감쇠는 팜 평면(baseZ) 기준 상대 거리에 적용(절대 z 를 곱하면 손 위치
                    // Z 오프셋까지 감쇠돼 손이 어긋난다).
                    // 굽힘 시 감쇠 1.0(엄지 축 완전 추종) — 덜 따라가면 웹이 엄지 등쪽에 얹혀 뾰족 잔재가 남는다
                    double cz = baseZ + (axisZ - baseZ) * (0.55 + 0.45 * bend01);
                    // 중심이 팜 평면에서 멀수록 두께를 더해 팜과 이어지게(틈 방지).
                    double tk = halfT * (0.95 - 0.18 * t) + Math.Abs(cz - baseZ) * 0.30;
                    // <260811_2>: 엄지를 손바닥 쪽으로 굽히면(모음) 제1웹 공간은 접혀 사라진다 —
                    // 링 이동·테이퍼만으로는 수퍼타원 모서리가 갈고리(뿔) 능선으로 남는 것을 소거
                    // 실험으로 확인. 그래서 굽힘이 클수록 웹 볼륨 자체를 줄여 팜·무지구 방추 안에
                    // 잠기게 한다(그 자리는 무지구 근육 덩어리가 채운다). rest(bend01=0)는 불변.
                    double shrink = 1 - 0.8 * bend01;
                    hw *= shrink;
                    tk *= shrink;
                    // <260811_1> 살 반응(`손바닥살.jpg`): 사람 손의 살은 손바닥 쪽에 몰려 있고 손등은
                    // 뼈에 밀착이라, 모으면 살이 **손바닥(-Z) 방향으로만** 부푼다. 두께 증가분만큼
                    // 중심을 손바닥 쪽으로 내려 등쪽 표면을 고정한다(폭 X 확장도 하지 않는다).
                    double dz = tk * (fleshMul - 1);
                    webRings.Add((new Point3D(cx, y, cz - dz), hw, tk + dz));
                }
                // 상단 마감 (<260811_2>): 위로 좁게 뽑던 마감이 굽힘 자세에서 뾰족한 뿔처럼 솟아 보였다.
                // 엄지모음근 상단은 실제로 낮게 접히므로, 캡을 거의 같은 높이에서 넓고 얇게 눌러 닫는다.
                var last = webRings[webRings.Count - 1];
                webRings.Add((new Point3D(last.c.X, last.c.Y + 0.10 * s, last.c.Z),
                              last.halfW * 0.90, last.halfT * 0.72));
                palm.AddTube(webRings, 2.3, 22);

                // 무지구(엄지 밑동) 볼륨에도 같은 살 반응을 절반 강도로 적용한다.
                thenarFleshMul = 1 + (fleshMul - 1) * 0.5;
            }

            // 중수골 부채: 손바닥 깊숙한 곳에서 각 너클로 뻗는 짧은 뼈대(너클 줄의 아치 형성)
            foreach (var kv in mcp)
            {
                var k = kv.Value;
                var start = new Point3D(knuckCX + (k.Pos.X - knuckCX) * 0.55, topY - 2.2 * s,
                                        baseZ + (k.Pos.Z - baseZ) * 0.3);   // 팜 평면(baseZ) 기준 감쇠 (<260810_8>)
                palm.AddTaperedCylinder(start, k.Pos, k.Radius * 1.25 * v, k.Radius * 1.0 * v, 14);
                // 너클(주먹뼈) 돌출: 손허리뼈 머리는 뚜렷하게 튀어나온다.
                // 구를 키우고 손등쪽(+Z)으로 치우쳐 굽혔을 때 옆에서도 보이게 한다.
                // <260811_1>: 너클은 뼈(손등엔 살이 거의 없음)이므로 살 반응(fingerMul) 대상이 아니다.
                palm.AddSphere(new Point3D(k.Pos.X, k.Pos.Y, k.Pos.Z + 0.22 * s * v),
                               k.Radius * 1.22 * v, 12, 16);
            }
            // 손가락 사이 웹빙 (<260811_4>): 인접 손가락 밑동 사이 골이 살로 메워지지 않아, 벌린
            // 자세에서 손바닥 상단 모서리가 골 틈으로 배경에 노출돼 '윤곽선만' 모드에서 떠 있는
            // 가로 점선·조각(rest/arch/halfbend/사선 뷰에서 확인)이 생겼다. 실제 손은 손가락 사이가
            // 물갈퀴 살로 이어져 윤곽이 끊기지 않는다 — 인접 MCP 쌍 사이를 낮은 타원체로 메운다.
            // 위치는 MCP 중점에서 PIP 쪽으로 28% 지점: 굽힘 자세에서도 골(주름) 자리를 따라간다.
            {
                var webbingPairs = new[] { (Digit.Index, Digit.Middle), (Digit.Middle, Digit.Ring), (Digit.Ring, Digit.Pinky) };
                foreach (var (da, db) in webbingPairs)
                {
                    if (!mcp.ContainsKey(da) || !mcp.ContainsKey(db) ||
                        !pip.ContainsKey(da) || !pip.ContainsKey(db)) continue;
                    var ma = mcp[da].Pos; var mb = mcp[db].Pos;
                    var qa = pip[da]; var qb = pip[db];
                    var m0 = new Point3D((ma.X + mb.X) / 2, (ma.Y + mb.Y) / 2, (ma.Z + mb.Z) / 2);
                    var q0 = new Point3D((qa.X + qb.X) / 2, (qa.Y + qb.Y) / 2, (qa.Z + qb.Z) / 2);
                    // MCP 중점에서 손가락 방향(PIP 쪽)으로 0.75s — 굽힘 자세에서도 골 자리를 따라간다.
                    var dir = new Vector3D(q0.X - m0.X, q0.Y - m0.Y, q0.Z - m0.Z);
                    if (dir.Length < 1e-6) continue;
                    dir.Normalize();
                    var c = m0 + dir * (0.75 * s);
                    // 아래는 팜 슬래브(같은 태그 → 내부 경계선 없음)와, 좌우는 두 손가락 밑동 살과
                    // 겹치도록 낮고 넓게 — 떠 있는 조각이 아니라 윤곽이 이어지는 다리가 되게 한다.
                    // <260811_4-1>(1): 높이(웹빙 채움 정도)는 '물갈퀴 살' 슬라이더로 0~200% 조절.
                    double rx = Math.Abs(ma.X - mb.X) / 2 + 0.50 * s * v;
                    if (webbingHeight > 0.01)
                        palm.AddEllipsoid(c, rx, 1.15 * s * webbingHeight, halfT * 0.70, 10, 12);
                }
            }
            // 엄지 밑동(무지구) — <260811_2> 해부학 재작성(`손근육\손횡단_2`·`손바닥방향근육_*`·Gray425):
            // 무지구근 3개(맞섬근·짧은굽힘근·짧은벌림근)는 엄지 중수골 **뼈의 손바닥쪽에만** 층층이 붙고,
            // 주행은 "손목 굽힘근지지띠 → 엄지 MCP" 부채꼴이다. 뼈 등쪽엔 얇은 폄 힘줄뿐이라 등쪽은 뼈
            // 윤곽(손가락 메시의 중수골 레이드)이 그대로 드러나야 한다.
            // 그래서 이전처럼 뼈 축(CMC→MCP)을 감싸는 원기둥을 쓰지 않는다 — 그 방식은 엄지가 굽거나
            // 대립 회전할 때 덩어리가 뼈와 함께 돌아 손등 쪽으로 드러났다. 대신 근육 덩어리를
            // **손목 손바닥면에 정박(고정)하고 끝만 엄지 MCP 의 손바닥쪽을 따라가는 방추형**으로 만들고,
            // 등쪽 상한(팜 슬래브 안)을 강제한다. 굽히면 MCP 끝이 손바닥쪽으로 내려가 덩어리가 더
            // 불룩해질 뿐, 어떤 자세에서도 손등 쪽으로는 나오지 않는다.
            {
                // CMC 관절 구는 '뼈'(관절 머리: 큰마름뼈+제1중수골 기저 — 실제로도 굵다). 살 반응 없음.
                // 반지름을 중수골 레이드(끝 bulge ≈1.08)보다 약간 크게 잡아, 레이드가 손바닥 모서리와
                // 만나는 접합부가 뾰족한 능선으로 노출되지 않게 둥글게 덮는다 (<260811_2>).
                palm.AddSphere(thumbCmc.Pos, thumbCmc.Radius * 1.16 * v, 10, 14);

                if (thumbMcp != null)
                {
                    var tc2 = thumbCmc.Pos;
                    var tm2 = thumbMcp.Pos;
                    double dorsalCap = baseZ + halfT * 0.55;          // 근육 등쪽 상한(팜 슬래브 안)
                    double f = Math.Max(0, thenarFleshMul - 1);       // 살(근육) 반응 — 손바닥으로만

                    // 시작(고정 정박): 손목 부근 손바닥면 — 엄지쪽으로 치우친 위치
                    var p0 = new Point3D(wristCX + thumbDir * 1.1 * s, wristY + 1.7 * s,
                                         baseZ - 0.30 * halfT);
                    // 끝(엄지 추종): 엄지 MCP 의 손바닥쪽(항상 뼈보다 -Z)
                    double endZ = Math.Min(tm2.Z, baseZ) - 0.35 * s - f * 0.8 * s;
                    var p1 = new Point3D(tm2.X * 0.92 + tc2.X * 0.08, tm2.Y, endZ);
                    // 중간(가장 불룩한 배): 정박점과 끝의 중간에서 손바닥쪽으로 더 내려간 지점
                    double rMid = (1.30 + f * 0.6) * s * v;
                    var pm = new Point3D((p0.X + p1.X) / 2 + thumbDir * 0.25 * s,
                                         (p0.Y + p1.Y) / 2,
                                         Math.Min((p0.Z + p1.Z) / 2 - 0.35 * s, dorsalCap - rMid));

                    double r0 = 0.95 * s * v;
                    double r1 = (0.72 + f * 0.35) * s * v;
                    palm.AddTaperedCylinder(p0, pm, r0, rMid, 16);
                    palm.AddTaperedCylinder(pm, p1, rMid, r1, 16);
                    palm.AddSphere(pm, rMid, 12, 16);                 // 배 부분 매끈하게
                }
            }

            // 피부장갑 갈래 소매 (<260811_4-1>, 사용자 인터뷰로 확정): 손 피부는 손가락 5개가 각각
            // 싸이고 손바닥과 하나로 이어진 '장갑'이며, 갈래(fork)가 갈라지기 전인 손가락 밑동은
            // 손바닥과 **한 장의 피부**다. 각 손가락 첫마디 밑동을 손바닥 태그의 소매(반지름 +10%,
            // 첫마디의 42%까지)로 덮어, 굽힌 손가락 밑동 살이 이웃 손가락 밑동과 만나도 같은
            // 피부(태그)로 읽혀 경계선이 그려지지 않게 한다(3.json L자 갈고리의 원인 — 진단 덤프로
            // 손가락↔손가락 태그 경계 확인). 갈래 위 자유 구간에서 손가락끼리 맞닿은 경계선
            // ('장갑 손가락 둘이 닿음')은 그대로 남는다.
            foreach (var seg in r.Segments)
            {
                if (seg.Kind != SegKind.Proximal || seg.Digit == Digit.Thumb) continue;
                var fdir = seg.B - seg.A;
                if (fdir.Length < 1e-6) continue;
                var split = seg.A + fdir * 0.42;
                double rSleeveA = seg.RA * 1.10 * v;
                double rSleeveB = (seg.RA + (seg.RB - seg.RA) * 0.42) * 1.06 * v;
                palm.AddTaperedCylinder(seg.A, split, rSleeveA, rSleeveB, 14);
            }

            if (rotM.HasValue) palm.Transform(rotM.Value);
            meshes.Palm = palm.ToMesh();

            // ----- 손가락 -----
            var fingers = new Dictionary<Digit, MeshBuilder>();
            var tips = new Dictionary<Digit, MeshBuilder>();
            MeshBuilder Finger(Digit d) { if (!fingers.TryGetValue(d, out var b)) { b = new MeshBuilder(); fingers[d] = b; } return b; }
            MeshBuilder Tip(Digit d) { if (!tips.TryGetValue(d, out var b)) { b = new MeshBuilder(); tips[d] = b; } return b; }

            foreach (var seg in r.Segments)
            {
                var mb = seg.Kind == SegKind.Distal ? Tip(seg.Digit) : Finger(seg.Digit);
                mb.AddLathe(seg.A, seg.B, BoneProfile(seg.RA * v, seg.RB * v), 16);
            }
            foreach (var j in r.Joints)
            {
                if (j.Digit != Digit.Thumb && j.Kind == JointKind.MCP) continue;   // 너클은 손바닥 메시에
                if (j.Digit == Digit.Thumb && (j.Kind == JointKind.CMC || j.Kind == JointKind.MCP))
                {
                    if (j.Kind == JointKind.MCP) Finger(j.Digit).AddSphere(j.Pos, j.Radius * 1.05 * v, 10, 14);
                    continue;   // CMC 구는 손바닥 쪽에서 추가함
                }
                if (j.IsTip)
                    Tip(j.Digit).AddSphere(j.Pos, j.Radius * 1.0 * v, 10, 12);      // 손끝 둥근 캡
                else if (j.Kind == JointKind.DIP || j.Kind == JointKind.PIP || j.Kind == JointKind.IP)
                {
                    var mb = j.Kind == JointKind.DIP || j.Kind == JointKind.IP ? Tip(j.Digit) : Finger(j.Digit);
                    double f = j.Kind == JointKind.PIP ? 1.07 : 1.04;               // 관절 볼록(뼈 머리)
                    mb.AddSphere(j.Pos, j.Radius * f * v, 10, 12);
                }
            }

            if (rotM.HasValue)
            {
                foreach (var kv in fingers) kv.Value.Transform(rotM.Value);
                foreach (var kv in tips) kv.Value.Transform(rotM.Value);
            }
            foreach (var kv in fingers) meshes.Fingers[kv.Key] = kv.Value.ToMesh();
            foreach (var kv in tips) meshes.Tips[kv.Key] = kv.Value.ToMesh();
            return meshes;
        }
    }
}
