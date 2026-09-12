using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace HandModel3D.Render
{
    public struct RenderMesh
    {
        public MeshGeometry3D Mesh;
        public Color Color;
        public double Alpha;   // 1 = 불투명
        public int Tag;        // 손가락 식별자(윤곽선 경계 검출용). 손바닥=0/10, 손가락=base+1+digit, 빈 곳=-1
    }

    /// <summary>
    /// 자체 소프트웨어 3D 래스터라이저. 색 패스(Z버퍼+램버트, 투명은 알파블렌드)와
    /// 태그/깊이 패스(투명도와 무관)를 나눠, 윤곽선·픽킹·SVG 추출이 항상 정확한 실루엣을 얻는다.
    /// </summary>
    public sealed class SoftRenderer
    {
        public Color Background = Color.FromArgb(0, 0, 0, 0);  // 투명 배경
        public double Ambient = 0.58;
        private readonly (Vector3D dir, double lvl)[] _lights;

        public SoftRenderer()
        {
            var d1 = new Vector3D(-0.35, -0.5, -0.78); d1.Normalize();
            var d2 = new Vector3D(0.55, 0.35, 0.55); d2.Normalize();
            _lights = new[] { (d1, 0.55), (d2, 0.22) };
        }

        private int[] _pix;
        private float[] _z;      // 색 패스용
        private float[] _zt;     // 태그 패스용(투명 포함 전체 손)
        private int[] _tag;
        private bool[] _mask;    // '항상' 모드용 커버리지
        private int _w, _h;

        public int[] TagBuffer => _tag;
        public float[] TagDepthBuffer => _zt;
        public int RW => _w;
        public int RH => _h;

        /// <summary>
        /// 윤곽선 옵션. Show=그림, Only=윤곽선만(피부 제거), AlwaysMode=가려진 손가락 윤곽도 유지((3-9-1-1) 둘째 모드).
        /// 같은 손의 손가락↔손바닥 경계는 깊이가 연속이면(접합) 선을 긋지 않아, 🖐️ 상태에서 단일폐곡선이 된다.
        /// </summary>
        public struct OutlineOptions { public bool Show, Only, AlwaysMode; public double ThicknessPx; public Color Color; }

        /// <summary>손가락↔손바닥(같은 손) 경계에서 이 깊이 차(월드 단위) 이상일 때만 '겹침'으로 보고 선을 긋는다.
        /// qksqhr(2026-08-11): 실제 사용 시 WorldScale(손 크기 배율)이 곱해진다 — 손 기하의 깊이 차가
        /// 크기에 비례하므로, 고정 상수면 '크기' 슬라이더에 따라 윤곽 분류가 뒤바뀐다.</summary>
        public double DepthEdgeThreshold = 1.5;

        /// <summary>손 크기 배율(OverallScale) — 월드 단위 임계값들(깊이 차·스침각·태그 섬 크기)에 곱한다.</summary>
        public double WorldScale = 1.0;

        // qksqhr(2026-08-11): 매 렌더마다 화면 크기 배열을 새로 할당하면(성분 라벨·에지 맵 등 ~17n바이트)
        // 전부 LOH로 가 드래그 중 GC 정지를 유발한다 — _pix 처럼 멤버로 승격해 재사용한다.
        private int[] _tmpInt;     // 성분 라벨링 공용(사용 전 Clear)
        private bool[] _tmpBool;   // 팽창/시임/앵커/선 공용(순차 사용, 사용 전 Clear)
        private bool[] _edge;      // 에지 맵

        private enum Pass { Color, ColorBlend, Tag, Mask }

        /// <summary><260811_4-1-1> 손가락 뼈대(축) 선분 — 관통 교차 지대의 태그 판정에 쓴다. 월드 좌표.</summary>
        public struct BoneSeg
        {
            public int Tag;
            public Point3D A, B;
        }

        public WriteableBitmap Render(Camera3D cam, IList<RenderMesh> meshes, double dpi = 96,
                                      OutlineOptions? outline = null, IList<BoneSeg> bones = null)
        {
            _w = Math.Max(1, cam.Width); _h = Math.Max(1, cam.Height);
            cam.Update();
            int n = _w * _h;
            if (_pix == null || _pix.Length != n)
            {
                _pix = new int[n]; _z = new float[n]; _zt = new float[n]; _tag = new int[n]; _mask = new bool[n];
                _tmpInt = new int[n]; _tmpBool = new bool[n]; _edge = new bool[n];
            }
            int bg = ToArgb(Background);
            for (int i = 0; i < n; i++)
            { _pix[i] = bg; _z[i] = float.PositiveInfinity; _zt[i] = float.PositiveInfinity; _tag[i] = -1; }

            // 1) 태그/깊이 패스 — 투명도와 무관하게 전체 손의 최전면 표면을 기록
            foreach (var rm in meshes) DrawMesh(cam, rm, Pass.Tag);
            CleanTagIslands(cam);   // <260811_4-1-1> 피부장갑: 살 속 작은 삐져나옴(태그 섬) 흡수
            _bones = bones; _cam = cam;   // 에지 후처리(교차 이음매 선 제거)용

            // 2) 색 패스 — qksqhr(2026-08-11): '윤곽선만' 모드에선 PaintEdges 가 전부 덮어쓰므로 생략(낭비 제거)
            bool skipColor = outline.HasValue && outline.Value.Show && outline.Value.Only;
            if (!skipColor)
            {
                var transparent = new List<RenderMesh>();
                foreach (var rm in meshes)
                {
                    if (rm.Alpha >= 0.999) DrawMesh(cam, rm, Pass.Color);
                    else transparent.Add(rm);
                }
                if (transparent.Count > 0) DrawTransparent(cam, transparent);
            }

            // 3) 윤곽선
            if (outline.HasValue && outline.Value.Show)
            {
                var o = outline.Value;
                bool[] edge = o.AlwaysMode ? EdgesAlways(cam, meshes) : EdgesOccluded();
                PaintEdges(edge, o, bg);
            }

            var bmp = new WriteableBitmap(_w, _h, dpi, dpi, PixelFormats.Bgra32, null);
            bmp.WritePixels(new System.Windows.Int32Rect(0, 0, _w, _h), _pix, _w * 4, 0);
            bmp.Freeze();
            return bmp;
        }

        /// <summary>
        /// <260811_4-1-1> 피부장갑의 '작은 삐져나옴 덮기': 마디끼리 살이 관통하면 뒤 손가락 표면의
        /// 볼록한 일부가 앞 손가락 살을 뚫고 나와 작은 태그 섬이 되고, 그 둘레가 손가락↔손가락
        /// 경계선으로 그려져 마디가 물어뜯긴 자국이 된다(2.json — 굽힌 검지 끝↔중지 둘째 마디).
        /// 실제 피부라면 이런 작은 관통은 눌려 덮인다 — 배경(실루엣)에 전혀 닿지 않고 살 속에 완전히
        /// 갇힌 작은(월드 기준 ≤ 약 1×1) 같은 손 태그 섬을, 둘레의 지배 태그로 흡수한다.
        /// 문턱이 월드 크기 기준이라 줌 배율과 무관하고, 배경에 닿은 진짜 노출(손가락 사이로 보이는
        /// 다른 손가락 끝 등)은 건드리지 않는다. 태그 버퍼를 고치므로 화면·비트맵·벡터 캡처에 일관 적용.
        /// </summary>
        private void CleanTagIslands(Camera3D cam)
        {
            int n = _w * _h;
            double wpp = cam.WorldPerPixelAtTarget();
            if (wpp <= 0) return;
            // qksqhr: 손 크기(WorldScale)에 비례한 문턱 — 크기 0.5의 손에서도 같은 비율의 섬이 흡수된다
            int maxArea = (int)(WorldScale * WorldScale / (wpp * wpp));   // (월드 1.0×s)² 픽셀 수
            if (maxArea < 4) return;
            var comp = _tmpInt; Array.Clear(comp, 0, n);
            var stack = new Stack<int>();
            var members = new List<int>(512);
            var neighborCount = new Dictionary<int, int>();
            int id = 0;
            for (int s = 0; s < n; s++)
            {
                if (_tag[s] < 0 || comp[s] != 0) continue;
                id++;
                int myTag = _tag[s];
                members.Clear();
                neighborCount.Clear();
                bool touchesBg = false, tooBig = false;
                stack.Push(s); comp[s] = id;
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    if (!tooBig) members.Add(i);
                    if (members.Count > maxArea) tooBig = true;
                    int x = i % _w, y = i / _w;
                    Visit(x - 1, y); Visit(x + 1, y); Visit(x, y - 1); Visit(x, y + 1);

                    void Visit(int nx, int ny)
                    {
                        if (nx < 0 || ny < 0 || nx >= _w || ny >= _h) { touchesBg = true; return; }
                        int j = ny * _w + nx;
                        int tj = _tag[j];
                        if (tj == myTag) { if (comp[j] == 0) { comp[j] = id; stack.Push(j); } return; }
                        if (tj < 0) { touchesBg = true; return; }
                        neighborCount.TryGetValue(tj, out int c);
                        neighborCount[tj] = c + 1;
                    }
                }
                if (tooBig || touchesBg || neighborCount.Count == 0) continue;
                // 지배 이웃 태그(같은 손, 60% 이상)로 흡수
                int best = -1, bestC = 0, total = 0;
                foreach (var kv in neighborCount) { total += kv.Value; if (kv.Value > bestC) { bestC = kv.Value; best = kv.Key; } }
                bool sameHand = (best >= 10) == (myTag >= 10);
                if (best < 0 || !sameHand || bestC < total * 0.6) continue;
                foreach (int i in members) _tag[i] = best;
            }
        }

        private IList<BoneSeg> _bones;   // <260811_4-1-1> 이번 렌더의 손가락 뼈대(교차 이음매 판정용)
        private Camera3D _cam;

        /// <summary>
        /// <260811_4-1-1> 피부장갑의 관통 이음매 선 제거: 굽힌 손가락 살이 이웃 손가락 살을
        /// 뚫으면(물리적으로 불가능한 포즈의 메시 관통) 표면이 교차하는 이음매 둘레가 손가락↔손가락
        /// 경계선으로 그려져 마디가 물어뜯긴 자국이 된다(2.json — 태그 덤프로 반도 확인).
        /// 구별 기준 두 가지를 모두 만족하는 에지만 지운다:
        ///  ① 깊이 차가 작다(표면 교차 이음매 — 진짜 가림(겹침)은 깊이 차가 큼)
        ///  ② 경계의 국소 방향이 두 손가락 축 어느 쪽과도 나란하지 않다(나란히 붙인 손가락의
        ///     경계선은 축과 평행하게 달리므로 유지된다 — 사용자 확정: 맞닿은 경계선은 보여야 함)
        /// </summary>
        private void EraseCrossSeams(bool[] edge)
        {
            if (_bones == null || _cam == null) return;
            double wpp = _cam.WorldPerPixelAtTarget();
            if (wpp <= 0) return;
            double epsDz = 0.18 * WorldScale;   // 월드 단위 — 교차 이음매 판정(qksqhr: 손 크기에 비례)
            const double maxAngleCos = 0.82; // cos 35° — 축과 이보다 덜 나란하면 이음매로 봄

            // 뼈대 투영(방향 벡터용)
            var proj = new Dictionary<int, List<(double x0, double y0, double x1, double y1)>>();
            foreach (var b in _bones)
            {
                var p0 = _cam.Project(b.A);
                var p1 = _cam.Project(b.B);
                if (!p0.InFront || !p1.InFront) continue;
                if (!proj.TryGetValue(b.Tag, out var list)) { list = new List<(double, double, double, double)>(); proj[b.Tag] = list; }
                list.Add((p0.X, p0.Y, p1.X, p1.Y));
            }
            if (proj.Count == 0) return;

            (double dx, double dy) AxisDir(int tag, double px, double py)
            {
                if (!proj.TryGetValue(tag, out var segs)) return (0, 0);
                double bx = 0, by = 0, bestD2 = double.PositiveInfinity;
                foreach (var sgm in segs)
                {
                    double dx = sgm.x1 - sgm.x0, dy = sgm.y1 - sgm.y0;
                    double len2 = dx * dx + dy * dy;
                    double t = len2 < 1e-9 ? 0 : ((px - sgm.x0) * dx + (py - sgm.y0) * dy) / len2;
                    t = Math.Max(0, Math.Min(1, t));
                    double qx = sgm.x0 + dx * t - px, qy = sgm.y0 + dy * t - py;
                    double d2 = qx * qx + qy * qy;
                    if (d2 < bestD2)
                    {
                        bestD2 = d2;
                        double len = Math.Sqrt(len2);
                        if (len > 1e-6) { bx = dx / len; by = dy / len; }
                    }
                }
                return (bx, by);
            }

            int n = _w * _h;
            // 1) 교차 이음매 후보(손가락↔손가락 & 깊이 연속) 에지 픽셀 표시
            var seam = _tmpBool; Array.Clear(seam, 0, n);
            for (int y = 1; y < _h - 1; y++)
                for (int x = 1; x < _w - 1; x++)
                {
                    int i = y * _w + x;
                    if (!edge[i]) continue;
                    int t = _tag[i];
                    if (t < 0 || (t % 10) == 0) continue;
                    foreach (int j in new[] { i - 1, i + 1, i - _w, i + _w })
                    {
                        int u = _tag[j];
                        if (u < 0 || (u % 10) == 0 || u == t) continue;
                        if ((t >= 10) != (u >= 10)) continue;
                        if (Math.Abs(_zt[i] - _zt[j]) < epsDz) { seam[i] = true; }
                        break;
                    }
                }
            // 2) 후보 픽셀의 국소 경계 방향(7×7 창의 후보 픽셀 최장 쌍) vs 두 손가락 축 방향
            for (int y = 1; y < _h - 1; y++)
                for (int x = 1; x < _w - 1; x++)
                {
                    int i = y * _w + x;
                    if (!seam[i]) continue;
                    double fx = 0, fy = 0, far2 = -1;
                    for (int dy = -3; dy <= 3; dy++)
                        for (int dx = -3; dx <= 3; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 1 || ny < 1 || nx >= _w - 1 || ny >= _h - 1) continue;
                            if (!seam[ny * _w + nx]) continue;
                            double d2 = dx * dx + dy * dy;
                            if (d2 > far2) { far2 = d2; fx = dx; fy = dy; }
                        }
                    if (far2 < 4) { edge[i] = false; continue; }   // 고립 이음매 점 — 제거
                    double flen = Math.Sqrt(far2);
                    double ex = fx / flen, ey = fy / flen;
                    int t = _tag[i];
                    // 상대 태그: 4-이웃에서 찾음
                    int other = -1;
                    foreach (int j in new[] { i - 1, i + 1, i - _w, i + _w })
                    {
                        int u = _tag[j];
                        if (u >= 0 && u != t && (u % 10) != 0 && (u >= 10) == (t >= 10)) { other = u; break; }
                    }
                    var a1 = AxisDir(t, x, y);
                    double c1 = Math.Abs(ex * a1.dx + ey * a1.dy);
                    double c2 = 0;
                    if (other >= 0)
                    {
                        var a2 = AxisDir(other, x, y);
                        c2 = Math.Abs(ex * a2.dx + ey * a2.dy);
                    }
                    if (Math.Max(c1, c2) < maxAngleCos) edge[i] = false;   // 어느 축과도 안 나란함 → 이음매 선 제거
                }
        }

        /// <summary>진단용(환경변수 HM3D_TAGDUMP 로만 작동): 태그 버퍼를 태그별 색 PNG로 저장.
        /// 윤곽선 문제(끊긴 선·물어뜯김 등)의 원인 태그를 눈으로 확인할 때 쓴다. 평상시 비용 0.</summary>
        public void DumpTagsPng(string path)
        {
            var pix = new int[_w * _h];
            for (int i = 0; i < _w * _h; i++)
            {
                int t = _tag[i];
                // 태그별 구분색: 배경=흰색, 손바닥=회색, 손가락=디지트별 원색
                pix[i] = t < 0 ? unchecked((int)0xFFFFFFFF)
                    : (t % 10) == 0 ? unchecked((int)0xFFB0B0B0)
                    : new[] { unchecked((int)0xFFE03131), unchecked((int)0xFF2F9E44), unchecked((int)0xFF1971C2),
                              unchecked((int)0xFFF08C00), unchecked((int)0xFF9C36B5) }[(t % 10 - 1) % 5]
                      ^ (t >= 10 ? 0x00303030 : 0);   // 오른손은 약간 어둡게
            }
            var bmp = new WriteableBitmap(_w, _h, 96, 96, PixelFormats.Bgra32, null);
            bmp.WritePixels(new System.Windows.Int32Rect(0, 0, _w, _h), pix, _w * 4, 0);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var fs = System.IO.File.Create(path)) enc.Save(fs);
        }

        /// <summary>
        /// <260811_29> 벡터 캡처용: 화면·비트맵과 **똑같은 판정**으로 만든 최종 윤곽선을
        /// '픽셀 사이 경계(크랙)' 집합으로 낸다. EdgesOccluded() 의 결과(IsEdgeAt 4규칙 + 후처리
        /// EraseCrossSeams·Close1·KeepAnchored·Despeckle)를 통과한 에지 픽셀 옆의 크랙만 남기므로,
        /// **비트맵에 실제로 그려진 선만** 1픽셀 폭 중심선으로 얻는다. 실루엣뿐 아니라 손가락끼리
        /// 맞닿은 내부 경계·살 속에 갇힌 섬·구멍까지 같은 기준으로 들어온다.
        /// 반환 격자: vert[y*(w+1)+x] = 격자점 (x,y)-(x,y+1) 세로 선분,
        ///            horz[y*w+x]     = 격자점 (x,y)-(x+1,y) 가로 선분. (격자점 = 픽셀의 모서리)
        /// ⚠️ '항상' 모드는 태그별 커버리지 경계라 크랙 개념이 다르다 — 종전처럼 벡터 출력엔 반영 안 함.
        /// </summary>
        public (bool[] vert, bool[] horz) BuildOutlineCracks()
        {
            if (_tag == null) return (null, null);
            bool[] edge = EdgesOccluded();
            int w = _w, h = _h;
            var vert = new bool[(w + 1) * h];
            var horz = new bool[w * (h + 1)];

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    bool hand = _tag[i] >= 0;

                    if (x == 0) { if (hand && edge[i]) vert[y * (w + 1)] = true; }       // 화면 왼쪽 끝 = 잘린 단면
                    else if (CrackBetween(i, i - 1, edge)) vert[y * (w + 1) + x] = true;

                    if (y == 0) { if (hand && edge[i]) horz[x] = true; }
                    else if (CrackBetween(i, i - w, edge)) horz[y * w + x] = true;

                    if (x == w - 1 && hand && edge[i]) vert[y * (w + 1) + w] = true;
                    if (y == h - 1 && hand && edge[i]) horz[h * w + x] = true;
                }
            return (vert, horz);
        }

        /// <summary>두 이웃 픽셀 사이에 '그려진 선'이 있는가 — 판정은 IsEdgeAt, 생존은 최종 edge 맵 기준.</summary>
        private bool CrackBetween(int i, int j, bool[] edge)
        {
            bool hi = _tag[i] >= 0, hj = _tag[j] >= 0;
            if (!hi && !hj) return false;                    // 둘 다 배경
            if (hi != hj) return edge[hi ? i : j];           // 실루엣 — 손 쪽 픽셀이 살아남았을 때만
            return edge[i] && edge[j] && IsEdgeAt(i, j);     // 내부 경계(손가락↔손가락 등)
        }

        // ----- 윤곽선: 기본(가림) 모드 -----
        // 규칙: 손↔배경 = 항상 / 다른 손 = 항상 / 같은 손 손가락↔손가락 = 항상(맞닿은 경계)
        //       같은 손 손가락↔손바닥 = 깊이 불연속일 때만(접합부는 선 없음 → 단일폐곡선)
        private bool[] EdgesOccluded()
        {
            int n = _w * _h;
            var edge = _edge; Array.Clear(edge, 0, n);
            for (int y = 0; y < _h; y++)
                for (int x = 0; x < _w; x++)
                {
                    int i = y * _w + x;
                    int t = _tag[i];
                    if (t < 0) continue;
                    if (x == 0 || x == _w - 1 || y == 0 || y == _h - 1) { edge[i] = true; continue; }
                    if (IsEdgeAt(i, i - 1) || IsEdgeAt(i, i + 1) || IsEdgeAt(i, i - _w) || IsEdgeAt(i, i + _w))
                        edge[i] = true;
                }
            EraseCrossSeams(edge);   // <260811_4-1-1> 관통 교차 이음매 선 제거(축과 나란한 경계선은 유지)
            Close1(edge);      // <260811_4> 미세 단절 봉합 — Despeckle 이 이어진 곡선을 조각내지 않게 먼저
            KeepAnchored(edge);
            Despeckle(edge);
            return edge;
        }

        /// <summary>
        /// <260811_4>: 실루엣(배경 경계)에 이어지지 않은 에지 성분을 지운다. 손가락 밑동이 웹빙 살과
        /// 만나는 골에서 손가락↔손가락/손가락↔손바닥 규칙이 살 속에 몇 픽셀짜리 '떠 있는 조각'을
        /// 그리는데(사용자 1.json — 진단 덤프로 확인), 실제 손 윤곽에는 끊어진 선이 없다.
        /// 모은 손가락 사이의 경계선(V 홈에서 시작)이나 겹친 손가락의 겹침선은 윤곽에 붙어 있어
        /// 그대로 유지된다 — 크기가 아니라 연결성 기준이라 줌 배율과 무관하다.
        /// </summary>
        private void KeepAnchored(bool[] edge)
        {
            int n = _w * _h;
            // 배경(태그 -1)과 맞닿은 에지 픽셀 = 실루엣 앵커
            var anchor = _tmpBool; Array.Clear(anchor, 0, n);
            for (int y = 1; y < _h - 1; y++)
                for (int x = 1; x < _w - 1; x++)
                {
                    int i = y * _w + x;
                    if (!edge[i]) continue;
                    if (_tag[i - 1] < 0 || _tag[i + 1] < 0 || _tag[i - _w] < 0 || _tag[i + _w] < 0)
                        anchor[i] = true;
                }
            // 화면 가장자리 에지도 앵커(잘린 손목 절단선 등)
            for (int x = 0; x < _w; x++)
            {
                if (edge[x]) anchor[x] = true;
                if (edge[(_h - 1) * _w + x]) anchor[(_h - 1) * _w + x] = true;
            }
            for (int y = 0; y < _h; y++)
            {
                if (edge[y * _w]) anchor[y * _w] = true;
                if (edge[y * _w + _w - 1]) anchor[y * _w + _w - 1] = true;
            }
            var comp = _tmpInt; Array.Clear(comp, 0, n);
            var stack = new Stack<int>();
            var members = new List<int>(256);
            int id = 0;
            for (int s = 0; s < n; s++)
            {
                if (!edge[s] || comp[s] != 0) continue;
                id++;
                members.Clear();
                bool anchored = false;
                stack.Push(s); comp[s] = id;
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    members.Add(i);
                    if (anchor[i]) anchored = true;
                    int x = i % _w, y = i / _w;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= _w || ny >= _h) continue;
                            int j = ny * _w + nx;
                            if (edge[j] && comp[j] == 0) { comp[j] = id; stack.Push(j); }
                        }
                }
                // qksqhr(2026-08-11) 검토 기록: '실루엣 안에 갇힌 진짜 겹침 폐곡선까지 지워진다'는
                // 지적이 있었으나, 강한 깊이 불연속 성분을 유지하는 예외를 넣어 보니 표준 포즈에서
                // 접합부의 짧은 조각(사용자가 여러 차례 제거를 지시한 '떠 있는 선')이 되살아났다.
                // 내부 겹침 윤곽이 필요한 경우는 '항상' 모드가 이미 그 역할을 하므로, 가림 모드는
                // 배경 앵커 연결성 기준을 유지한다(의도된 트레이드오프 — 재론 시 이 기록 참고).
                if (!anchored)
                    foreach (int i in members) edge[i] = false;
            }
        }

        /// <summary>
        /// 모폴로지 닫힘(팽창 1 + 침식 1). 손가락 가장자리↔웹빙 가장자리로 윤곽이 넘어가는 지점 등에서
        /// 태그 전환 지터로 생기는 1~2px 단절을 봉합한다 (<260811_4> — 골 사이 '떠 있는 점선' 원인 중 하나).
        /// 1px 두께 선은 팽창 후 침식으로 원형이 보존되고, 끊긴 틈만 메워진다.
        /// </summary>
        private void Close1(bool[] edge)
        {
            int n = _w * _h;
            var dil = _tmpBool; Array.Clear(dil, 0, n);
            for (int y = 0; y < _h; y++)
                for (int x = 0; x < _w; x++)
                {
                    int i = y * _w + x;
                    if (!edge[i]) continue;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx >= 0 && ny >= 0 && nx < _w && ny < _h) dil[ny * _w + nx] = true;
                        }
                }
            for (int y = 0; y < _h; y++)
                for (int x = 0; x < _w; x++)
                {
                    int i = y * _w + x;
                    if (!dil[i] || edge[i]) continue;
                    bool all = true;
                    for (int dy = -1; dy <= 1 && all; dy++)
                        for (int dx = -1; dx <= 1 && all; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= _w || ny >= _h || !dil[ny * _w + nx]) all = false;
                        }
                    if (all) edge[i] = true;   // 침식 후에도 남는 새 픽셀 = 틈 메움
                }
        }

        /// <summary>깊이 임계 근처에서 생기는 고립된 점 얼룩(작은 에지 성분)을 지운다. 실루엣 같은 큰 곡선은 유지.</summary>
        private void Despeckle(bool[] edge, int minSize = 12)
        {
            int n = _w * _h;
            var comp = _tmpInt; Array.Clear(comp, 0, n);   // 0 = 미방문
            var stack = new Stack<int>();
            var members = new List<int>(64);
            int id = 0;
            for (int s = 0; s < n; s++)
            {
                if (!edge[s] || comp[s] != 0) continue;
                id++;
                members.Clear();
                stack.Push(s); comp[s] = id;
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    members.Add(i);
                    int x = i % _w, y = i / _w;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= _w || ny >= _h) continue;
                            int j = ny * _w + nx;
                            if (edge[j] && comp[j] == 0) { comp[j] = id; stack.Push(j); }
                        }
                    if (members.Count > minSize) { /* 큰 성분 — 더 셀 필요 없음 */ }
                }
                if (members.Count < minSize)
                    foreach (int i in members) edge[i] = false;
            }
        }

        private bool IsEdgeAt(int i, int j)
        {
            int t = _tag[i], u = _tag[j];
            if (u == t) return false;
            if (u < 0) return true;                       // 실루엣
            bool handT = t >= 10, handU = u >= 10;
            if (handT != handU) return true;              // 다른 손끼리 겹침
            bool palmT = (t % 10) == 0, palmU = (u % 10) == 0;
            if (!palmT && !palmU) return true;            // 같은 손 손가락↔손가락(맞닿음)
            // 같은 손 손가락↔손바닥: 접합(깊이 연속)이면 무시, 겹침(불연속)이면 선
            // qksqhr(2026-08-11): 임계를 손 크기(WorldScale)에 비례시켜 '크기' 슬라이더와 무관하게
            // 같은 포즈가 같은 윤곽으로 분류되게 한다(깊이 차는 손 기하에 비례하므로).
            double dz = Math.Abs(_zt[i] - _zt[j]);
            if (dz <= DepthEdgeThreshold * WorldScale) return false;
            // <260811_4>: 손바닥(웹빙) 살이 더 앞이고, 손가락 쪽이 실루엣 스침각(경계에서 깊이 급락)이면
            // '겹침'이 아니라 손가락이 살 속에 파묻히는 접합부다 — 실루엣 가장자리의 깊이값이 극단으로
            // 튀어 dz 임계만으로는 오발해, 손가락 밑동 골에 '떠 있는 점선'이 생겼다(사용자 1.json).
            // 손가락이 손바닥 앞을 지나는 진짜 겹침(주먹 등)은 손가락이 더 앞이므로 영향 없다.
            int fi = palmT ? j : i;                       // 손가락 쪽 픽셀
            int pi = palmT ? i : j;                       // 손바닥 쪽 픽셀
            if (_zt[pi] < _zt[fi])                        // 깊이는 작을수록 앞 — 손바닥이 앞
            {
                // qksqhr(2026-08-11): back 인덱스가 화면 좌우 끝 열에서 다음/이전 행으로 감기던
                // 것(행 랩)을 수평 스텝일 때 같은 행인지 확인해 차단. 스침각 임계도 크기 비례.
                int step = fi - pi;
                int back = fi + step;
                bool valid = back >= 0 && back < _w * _h;
                if (valid && (step == 1 || step == -1) && back / _w != fi / _w) valid = false;
                if (valid && _tag[back] == _tag[fi]
                    && Math.Abs(_zt[fi] - _zt[back]) > 0.45 * WorldScale)
                    return false;                         // 스침각 가장자리 = 접합 — 선 없음
            }
            return true;
        }

        // ----- 윤곽선: '항상' 모드 — 태그(손가락/손바닥)별 전체 실루엣의 합집합 -----
        private bool[] EdgesAlways(Camera3D cam, IList<RenderMesh> meshes)
        {
            int n = _w * _h;
            var edge = new bool[n];
            var byTag = new Dictionary<int, List<RenderMesh>>();
            foreach (var rm in meshes)
            {
                if (!byTag.TryGetValue(rm.Tag, out var l)) { l = new List<RenderMesh>(); byTag[rm.Tag] = l; }
                l.Add(rm);
            }
            foreach (var kv in byTag)
            {
                Array.Clear(_mask, 0, n);
                foreach (var rm in kv.Value) DrawMesh(cam, rm, Pass.Mask);
                for (int y = 0; y < _h; y++)
                    for (int x = 0; x < _w; x++)
                    {
                        int i = y * _w + x;
                        if (!_mask[i]) continue;
                        if (x == 0 || x == _w - 1 || y == 0 || y == _h - 1 ||
                            !_mask[i - 1] || !_mask[i + 1] || !_mask[i - _w] || !_mask[i + _w])
                            edge[i] = true;
                    }
            }
            return edge;
        }

        private void PaintEdges(bool[] edge, OutlineOptions o, int bg)
        {
            int n = _w * _h;
            int r = Math.Max(0, (int)Math.Round(o.ThicknessPx / 2.0));
            var line = _tmpBool; Array.Clear(line, 0, n);
            for (int y = 0; y < _h; y++)
                for (int x = 0; x < _w; x++)
                {
                    if (!edge[y * _w + x]) continue;
                    for (int dy = -r; dy <= r; dy++)
                        for (int dx = -r; dx <= r; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= _w || ny >= _h) continue;
                            if (dx * dx + dy * dy <= r * r + 1) line[ny * _w + nx] = true;
                        }
                }
            int lineArgb = ToArgb(o.Color);
            if (o.Only)
                for (int i = 0; i < n; i++) _pix[i] = bg;
            for (int i = 0; i < n; i++) if (line[i]) _pix[i] = lineArgb;
        }

        // ----- 메시 그리기 -----

        private void DrawMesh(Camera3D cam, RenderMesh rm, Pass pass)
        {
            var pos = rm.Mesh.Positions; var nor = rm.Mesh.Normals; var idx = rm.Mesh.TriangleIndices;
            int a = (int)Math.Round(Clamp01(rm.Alpha) * 255);
            for (int t = 0; t + 2 < idx.Count; t += 3)
            {
                int i0 = idx[t], i1 = idx[t + 1], i2 = idx[t + 2];
                RasterTri(cam, pos[i0], pos[i1], pos[i2],
                          Nor(nor, i0), Nor(nor, i1), Nor(nor, i2),
                          rm.Color, a, rm.Tag, pass);
            }
        }

        private void DrawTransparent(Camera3D cam, List<RenderMesh> meshes)
        {
            var tris = new List<(Point3D p0, Point3D p1, Point3D p2, Vector3D n0, Vector3D n1, Vector3D n2, Color c, int a, double depth)>();
            foreach (var rm in meshes)
            {
                var pos = rm.Mesh.Positions; var nor = rm.Mesh.Normals; var idx = rm.Mesh.TriangleIndices;
                int a = (int)Math.Round(Clamp01(rm.Alpha) * 255);
                for (int t = 0; t + 2 < idx.Count; t += 3)
                {
                    int i0 = idx[t], i1 = idx[t + 1], i2 = idx[t + 2];
                    var c0 = pos[i0]; var c1 = pos[i1]; var c2 = pos[i2];
                    var mid = new Point3D((c0.X + c1.X + c2.X) / 3, (c0.Y + c1.Y + c2.Y) / 3, (c0.Z + c1.Z + c2.Z) / 3);
                    double depth = Vector3D.DotProduct(mid - cam.Position, cam.Forward);
                    tris.Add((c0, c1, c2, Nor(nor, i0), Nor(nor, i1), Nor(nor, i2), rm.Color, a, depth));
                }
            }
            tris.Sort((x, y) => y.depth.CompareTo(x.depth)); // 뒤(먼 것) 먼저
            foreach (var tr in tris)
                RasterTri(cam, tr.p0, tr.p1, tr.p2, tr.n0, tr.n1, tr.n2, tr.c, tr.a, -1, Pass.ColorBlend);
        }

        private static Vector3D Nor(Vector3DCollection nor, int i)
            => (nor != null && i < nor.Count) ? nor[i] : new Vector3D(0, 0, 1);

        private void RasterTri(Camera3D cam, Point3D w0, Point3D w1, Point3D w2,
                               Vector3D n0, Vector3D n1, Vector3D n2,
                               Color baseColor, int alpha, int tag, Pass pass)
        {
            var a0 = cam.Project(w0); var a1 = cam.Project(w1); var a2 = cam.Project(w2);
            if (!a0.InFront || !a1.InFront || !a2.InFront) return;   // 간단 근평면 클리핑

            // 후면 제거
            Vector3D gn = Vector3D.CrossProduct(w1 - w0, w2 - w0);
            if (Vector3D.DotProduct(gn, cam.Position - w0) <= 0) return;

            double x0 = a0.X, y0 = a0.Y, x1 = a1.X, y1 = a1.Y, x2 = a2.X, y2 = a2.Y;
            double area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0);
            if (Math.Abs(area) < 1e-9) return;
            double invArea = 1.0 / area;

            int minX = (int)Math.Floor(Math.Min(x0, Math.Min(x1, x2)));
            int maxX = (int)Math.Ceiling(Math.Max(x0, Math.Max(x1, x2)));
            int minY = (int)Math.Floor(Math.Min(y0, Math.Min(y1, y2)));
            int maxY = (int)Math.Ceiling(Math.Max(y0, Math.Max(y1, y2)));
            if (minX < 0) minX = 0; if (minY < 0) minY = 0;
            if (maxX >= _w) maxX = _w - 1; if (maxY >= _h) maxY = _h - 1;
            if (minX > maxX || minY > maxY) return;

            double s0 = 0, s1 = 0, s2 = 0;
            if (pass == Pass.Color || pass == Pass.ColorBlend)
            { s0 = Shade(n0); s1 = Shade(n1); s2 = Shade(n2); }
            double iz0 = 1.0 / a0.Depth, iz1 = 1.0 / a1.Depth, iz2 = 1.0 / a2.Depth;

            for (int py = minY; py <= maxY; py++)
            {
                for (int px = minX; px <= maxX; px++)
                {
                    double cx = px + 0.5, cy = py + 0.5;
                    double b0 = ((x1 - cx) * (y2 - cy) - (x2 - cx) * (y1 - cy)) * invArea;
                    double b1 = ((x2 - cx) * (y0 - cy) - (x0 - cx) * (y2 - cy)) * invArea;
                    double b2 = 1 - b0 - b1;
                    if (b0 < 0 || b1 < 0 || b2 < 0) continue;

                    int di = py * _w + px;

                    if (pass == Pass.Mask) { _mask[di] = true; continue; }

                    double invz = b0 * iz0 + b1 * iz1 + b2 * iz2;
                    double depth = 1.0 / invz;

                    if (pass == Pass.Tag)
                    {
                        if (depth < _zt[di]) { _zt[di] = (float)depth; _tag[di] = tag; }
                        continue;
                    }

                    if (depth >= _z[di]) continue;

                    double shade = b0 * s0 + b1 * s1 + b2 * s2;
                    int r = (int)(baseColor.R * shade), g = (int)(baseColor.G * shade), b = (int)(baseColor.B * shade);
                    if (r > 255) r = 255; if (g > 255) g = 255; if (b > 255) b = 255;

                    if (pass == Pass.ColorBlend)
                    {
                        int dst = _pix[di];
                        int da = (dst >> 24) & 0xFF, dr = (dst >> 16) & 0xFF, dg = (dst >> 8) & 0xFF, db = dst & 0xFF;
                        double sa = alpha / 255.0;
                        int or_ = (int)(r * sa + dr * (1 - sa));
                        int og = (int)(g * sa + dg * (1 - sa));
                        int ob = (int)(b * sa + db * (1 - sa));
                        int oa = (int)(alpha + da * (1 - sa));
                        _pix[di] = (oa << 24) | (or_ << 16) | (og << 8) | ob;
                    }
                    else
                    {
                        _pix[di] = (alpha << 24) | (r << 16) | (g << 8) | b;
                        _z[di] = (float)depth;
                    }
                }
            }
        }

        private double Shade(Vector3D nWorld)
        {
            var n = nWorld; if (n.LengthSquared > 1e-12) n.Normalize();
            double s = Ambient;
            foreach (var (dir, lvl) in _lights)
            {
                double d = -Vector3D.DotProduct(n, dir);
                if (d > 0) s += d * lvl;
            }
            return s > 1.6 ? 1.6 : s;
        }

        private static int ToArgb(Color c) => (c.A << 24) | (c.R << 16) | (c.G << 8) | c.B;
        private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);
    }
}
