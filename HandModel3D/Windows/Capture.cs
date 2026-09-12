using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HandModel3D.Render;

namespace HandModel3D
{
    /// <summary>카메라 캡처: 비트맵(PNG)·벡터(SVG). 파일명 규칙 (3-10-3-1), SVG 에 Path.Data 임베드 (3-10-2-1-1).</summary>
    public static class Capture
    {
        /// <summary>
        /// 비트맵 캡처(PNG). 손(+배경 키보드)만 — 축·라벨·충돌 경고 등 오버레이는 제외. 키보드 없으면
        /// 투명 배경. <260811_24>: **활성화된 빨간 고정점만은 예외로 포함**한다(pinDots).
        /// keyInfo = 눌린 키의 (라벨, "행-열") — 있으면 "[라벨]_시각" + "[행-열]_[라벨]_시각" 으로도 내보낸다.
        /// kbVisual/kbRectPx = 배경 키보드 합성 정보(키보드 표시 중일 때만).
        /// </summary>
        /// <param name="pinDots">
        /// <260811_24> 활성화된 빨간 고정점(물리 픽셀 중심·반지름·불투명도) — MainWindow.
        /// ComputeActivePinDotsPx() 가 만든다. 비어 있거나 null 이면 아무것도 안 그린다.
        /// </param>
        /// <param name="explicitPath">
        /// <260811_15> 파일 이름 수동 입력일 때 사용자가 고른 전체 경로. 주면 그 파일 하나만
        /// 저장한다(자동 이름 규칙도, [행-열] 사본도 만들지 않는다 — 이름을 사용자가 정했으므로).
        /// </param>
        public static string SaveBitmap(WriteableBitmap bmp, (string label, string rowcol)? keyInfo,
                                        Visual kbVisual, Rect kbRectPx, double dpi, string explicitPath = null,
                                        IReadOnlyList<(Point pos, double r, double opacity)> pinDots = null)
        {
            if (bmp == null) throw new InvalidOperationException("렌더 결과 없음");

            bool hasKeyboard = kbVisual is FrameworkElement fe0 && fe0.ActualWidth > 0;
            bool hasPins = pinDots != null && pinDots.Count > 0;

            BitmapSource output = bmp;
            if (hasKeyboard || hasPins)
            {
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    if (hasKeyboard)
                    {
                        var fe = (FrameworkElement)kbVisual;
                        // 키보드(2D)를 자연 크기로 렌더한 뒤 화면 배치대로 스케일해 손 아래에 합성
                        var kbRtb = new RenderTargetBitmap((int)Math.Ceiling(fe.ActualWidth), (int)Math.Ceiling(fe.ActualHeight),
                                                           96, 96, PixelFormats.Pbgra32);
                        kbRtb.Render(fe);
                        // <260811_14> 후속: ceil 크기 RTB 를 소수 rect 로 되-늘이면 서브픽셀 리샘플로
                        // 흐려진다. 정수 위치에 RTB 픽셀 크기 그대로 1:1 로 얹는다(오차 최대 0.5px).
                        dc.DrawImage(kbRtb, new Rect(Math.Round(kbRectPx.X), Math.Round(kbRectPx.Y),
                                                     kbRtb.PixelWidth, kbRtb.PixelHeight));
                    }
                    dc.DrawImage(bmp, new Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight));
                    // <260811_24> 고정점은 손 위(맨 위)에 — 화면에서 Overlay 캔버스가 SceneImage 위에
                    // 있는 것과 같은 순서. 물리 픽셀 좌표라 스케일 변환 없이 그대로 찍는다.
                    if (hasPins)
                        foreach (var (pos, r, opacity) in pinDots)
                            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)Math.Round(opacity * 255), 0xFF, 0, 0)),
                                           null, pos, r, r);
                }
                var final = new RenderTargetBitmap(bmp.PixelWidth, bmp.PixelHeight, 96, 96, PixelFormats.Pbgra32);
                final.Render(dv);
                output = final;
            }

            if (!string.IsNullOrEmpty(explicitPath)) { SavePng(output, explicitPath); return explicitPath; }

            string primary = UniquePath(AutoFileName(keyInfo?.label), ".png", AppSettings.PngDir);
            SavePng(output, primary);
            // <260811_19-1> 마스터 표에 없는 특수키(Space 등)는 "행-열"이 없어 사본을 만들지 않는다.
            if (keyInfo.HasValue && !string.IsNullOrEmpty(keyInfo.Value.rowcol))
                SavePng(output, UniquePath(RowColBase(keyInfo.Value), ".png", AppSettings.PngDir));   // [행-열]_[라벨]_시각 으로도
            return primary;
        }

        private static void SavePng(BitmapSource src, string path)
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(src));
            using (var fs = File.Create(path)) enc.Save(fs);
        }

        /// <summary>
        /// 벡터 캡처(SVG). 손 윤곽선(맞닿은 손가락 경계 포함)만. 배경 키보드는 제외.
        /// target(<260719_9>): 0=둘 다, 1=왼손만, 2=오른손만 — 태그(왼손 0~9, 오른손 10~)로 거른다.
        /// </summary>
        /// <param name="pointTransform">
        /// <260811_26> 있으면 추적된 각 윤곽 점(렌더 캔버스 물리 픽셀)을 이 함수로 변환해 출력한다
        /// (예: OTP 777x260 키보드 단위 정합). 없으면 종전처럼 물리 픽셀 그대로 출력한다.
        /// </param>
        /// <param name="viewBoxW">
        /// 있으면 "키보드 단위" 모드 — viewBox 기준 폭(777). 손이 이 상자 밖(키보드 아래 손목쪽 등)까지
        /// 뻗으므로 viewBox 는 이 상자와 **실제 그림 범위의 합집합**으로 넓힌다. 좌표값 자체는 그대로라
        /// OpenTypingPlus 이식(Path.Data)에는 아무 영향이 없고, 파일을 눈으로 볼 때 잘리지만 않게 된다.
        /// 없으면 종전대로 캔버스 크기(내용이 항상 캔버스 안이라 잘릴 일 없음).
        /// </param>
        /// <param name="viewBoxH">위와 같음 — viewBox 기준 높이(260).</param>
        /// <param name="lean">
        /// <260811_26> true 면 data-wpf 속성과 metadata 를 넣지 않는다. 이 둘은 d 값의 복제라(d 자체가
        /// 이미 WPF Path.Data 미니 언어) 파일이 3배가 되는데, OpenTypingPlus 에 넣을 산출물은 d 만
        /// 읽으므로 낭비다. 대화형 '벡터 캡처' 버튼은 false — 재저장 경고까지 있는 설계된 기능이라 유지.
        /// </param>
        /// <param name="strokeColor">선 색(없으면 종전 기본 #212529). 비트맵과 같아 보이게 앱의 윤곽선 색을 준다.</param>
        /// <param name="strokeWidthPx">
        /// 선 두께(**렌더 캔버스 물리 픽셀** 단위, 없으면 종전 기본 2). 출력 좌표계로 자동 환산한다.
        /// </param>
        public static string SaveVector(SoftRenderer renderer, (string label, string rowcol)? keyInfo, bool fill,
                                        int target = 0, string explicitPath = null,
                                        Func<double, double, (double x, double y)> pointTransform = null,
                                        double? viewBoxW = null, double? viewBoxH = null, bool lean = false,
                                        Color? strokeColor = null, double? strokeWidthPx = null)
        {
            int w = renderer.RW, h = renderer.RH;
            var tag = renderer.TagBuffer;
            if (tag == null) throw new InvalidOperationException("렌더 결과 없음");

            // <260811_29> 실루엣만 훑던 방식을 버리고, 렌더러가 만든 **최종 윤곽선 그대로**(화면·비트맵과
            // 같은 판정+후처리) 픽셀 사이 경계를 이어 벡터화한다. 손가락끼리 맞닿은 선·살 속 섬·구멍이
            // 모두 같은 기준으로 들어온다.
            var (vert, horz) = renderer.BuildOutlineCracks();
            if (vert == null) throw new InvalidOperationException("윤곽선 계산 결과 없음");
            var polys = ChainCracks(vert, horz, w, h, target, tag);

            // 변환은 한 번만 해 두고(범위 계산과 출력에 같은 값을 쓴다) 좌표 범위를 재 둔다.
            var shaped = new List<(List<(double x, double y)> pts, bool closed)>();
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var poly in polys)
            {
                var tp = new List<(double x, double y)>(poly.pts.Count);
                foreach (var p in poly.pts)
                {
                    var q = pointTransform != null ? pointTransform(p.x, p.y) : (x: p.x, y: p.y);
                    tp.Add(q);
                    if (q.x < minX) minX = q.x;
                    if (q.y < minY) minY = q.y;
                    if (q.x > maxX) maxX = q.x;
                    if (q.y > maxY) maxY = q.y;
                }
                shaped.Add((tp, poly.closed));
            }

            // 출력 좌표계 배율(물리 픽셀 1 = 몇 단위인가) — 선 두께 환산에 쓴다.
            double unitPerPx = 1.0;
            if (pointTransform != null)
            {
                var o = pointTransform(0, 0);
                var e = pointTransform(1, 0);
                double d = Math.Sqrt((e.x - o.x) * (e.x - o.x) + (e.y - o.y) * (e.y - o.y));
                if (d > 1e-9) unitPerPx = d;
            }
            string strokeHex = strokeColor.HasValue
                ? $"#{strokeColor.Value.R:X2}{strokeColor.Value.G:X2}{strokeColor.Value.B:X2}" : "#212529";
            double strokeW = strokeWidthPx.HasValue ? strokeWidthPx.Value * unitPerPx : 2.0;

            double vbX = 0, vbY = 0, vbW = viewBoxW ?? w, vbH = viewBoxH ?? h;
            if (viewBoxW.HasValue && viewBoxH.HasValue && shaped.Count > 0)
            {
                // 키보드 상자(0,0,777,260)와 그림 범위의 합집합 + 선 두께 여유. 원점(0,0)은 그대로
                // 키보드 왼쪽 위이므로, 이 SVG 를 열어 보면 좌표계가 어긋나지 않으면서 손 전체가 보인다.
                const double margin = 4;
                vbX = Math.Min(0, minX - margin);
                vbY = Math.Min(0, minY - margin);
                vbW = Math.Max(viewBoxW.Value, maxX + margin) - vbX;
                vbH = Math.Max(viewBoxH.Value, maxY + margin) - vbY;
            }

            string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{N(vbW)}\" height=\"{N(vbH)}\" viewBox=\"{N(vbX)} {N(vbY)} {N(vbW)} {N(vbH)}\">");
            sb.AppendLine(lean
                ? "<!-- 손 모델 3D 벡터 캡처. path 의 d 값이 곧 WPF Path.Data 문자열(OpenTypingPlus 활용용). -->"
                : "<!-- 손 모델 3D 벡터 캡처. 아래 각 path 의 data-wpf 속성이 WPF Path.Data 문자열(OpenTypingPlus 활용용). -->");
            // <260811_29> 조각이 여러 개여도 **<path> 요소는 하나**로 묶는다 — d 안에 "M…Z M…L…" 로
            // 이어 붙이면 WPF Path.Data 와 그대로 호환되고, OpenTypingPlus 로더(첫 <path> 만 읽음)도
            // 손대지 않아도 된다. '벡터 내부 채움'을 켰을 때만 닫힌 조각(채움)과 열린 선(획)을 나눈다
            // — 열린 조각은 채우기가 제멋대로 닫아 버려 엉뚱한 삼각형이 칠해지기 때문.
            var closedSb = new StringBuilder();
            var openSb = new StringBuilder();
            foreach (var poly in shaped)
            {
                string d = ToPathData(poly.pts, poly.closed);
                var into = poly.closed ? closedSb : openSb;
                if (into.Length > 0) into.Append(' ');
                into.Append(d);
            }
            string allD = closedSb.ToString();
            if (openSb.Length > 0) allD = allD.Length > 0 ? allD + " " + openSb : openSb.ToString();

            string Attrs(string d, string fillVal) =>
                $"d=\"{d}\"{(lean ? "" : $" data-wpf=\"{d}\"")} fill=\"{fillVal}\" stroke=\"{strokeHex}\"" +
                $" stroke-width=\"{N(strokeW)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"";

            if (fill)
            {
                if (closedSb.Length > 0) sb.AppendLine($"  <path {Attrs(closedSb.ToString(), "#E8B698")} fill-rule=\"evenodd\"/>");
                if (openSb.Length > 0) sb.AppendLine($"  <path {Attrs(openSb.ToString(), "none")}/>");
            }
            else if (allD.Length > 0)
            {
                sb.AppendLine($"  <path {Attrs(allD, "none")}/>");
            }
            if (!lean)
                sb.AppendLine($"  <metadata data-wpf-pathdata=\"{System.Security.SecurityElement.Escape(allD)}\"/>");
            sb.AppendLine("</svg>");
            string content = sb.ToString();

            if (!string.IsNullOrEmpty(explicitPath))   // <260811_15> 수동 이름: 그 파일 하나만
            {
                File.WriteAllText(explicitPath, content, new UTF8Encoding(false));
                return explicitPath;
            }

            string primary = UniquePath(AutoFileName(keyInfo?.label), ".svg", AppSettings.SvgDir);
            File.WriteAllText(primary, content, new UTF8Encoding(false));
            if (keyInfo.HasValue && !string.IsNullOrEmpty(keyInfo.Value.rowcol))
                File.WriteAllText(UniquePath(RowColBase(keyInfo.Value), ".svg", AppSettings.SvgDir), content, new UTF8Encoding(false));
            return primary;
        }

        // ----- 파일명 규칙 -----

        private static string Timestamp() => DateTime.Now.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture);

        /// <summary>자동 파일 이름(확장자 제외). 수동 입력 대화상자의 기본값으로도 쓴다 (<260811_15>).</summary>
        public static string AutoFileName(string keyLabel)
        {
            string ts = Timestamp();
            if (string.IsNullOrEmpty(keyLabel)) return ts;
            return $"[{Normalize(keyLabel)}]_{ts}";   // [키라벨]_시각
        }

        private static string RowColBase((string label, string rowcol) keyInfo)
            => $"[{keyInfo.rowcol}]_[{Normalize(keyInfo.label)}]_{Timestamp()}";

        // 파일명 금지문자를 전각으로 대체 (3-10-3-1)(4)
        private static string Normalize(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                switch (c)
                {
                    case '/': sb.Append('／'); break;
                    case '\\': sb.Append('＼'); break;
                    case '?': sb.Append('？'); break;
                    case '*': sb.Append('＊'); break;
                    case ':': sb.Append('：'); break;
                    case '<': sb.Append('＜'); break;
                    case '>': sb.Append('＞'); break;
                    case '|': sb.Append('｜'); break;
                    case '"': sb.Append('＂'); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        private static string UniquePath(string baseName, string ext, string dir)
        {
            Directory.CreateDirectory(dir);
            string p = Path.Combine(dir, baseName + ext);
            if (!File.Exists(p)) return p;
            for (int i = 1; ; i++)   // 같은 초 여러 번: _1, _2 …
            {
                string p2 = Path.Combine(dir, baseName + "_" + i + ext);
                if (!File.Exists(p2)) return p2;
            }
        }

        // ----- <260811_29> 윤곽선 크랙 추적 -----

        /// <summary>
        /// 단순화 허용 오차(렌더 캔버스 물리 픽셀). 크랙 폴리라인은 1픽셀 계단이라 이 정도만 줘도
        /// 계단이 직선으로 접히고, 선 두께(보통 5px 안팎)의 1/5 미만이라 눈으로 구분되지 않는다.
        /// </summary>
        private const double SimplifyTolPx = 1.0;

        /// <summary>
        /// 크랙(픽셀 사이 경계) 집합을 이어 폴리라인 조각들로 만든다. 격자점 그래프를 따라가되
        /// 갈림길에서는 직진을 우선해 지그재그를 막고, 갈림점·끝점에서 시작해 조각을 끊는다.
        /// 되돌아와 시작점에서 끝나면 닫힌 조각(Z)이다. target: 0=둘 다, 1=왼손, 2=오른손.
        /// </summary>
        private static List<(List<(double x, double y)> pts, bool closed)> ChainCracks(
            bool[] vert, bool[] horz, int w, int h, int target, int[] tag)
        {
            bool WantPix(int idx)
            {
                if (idx < 0) return false;
                int t = tag[idx];
                if (t < 0) return false;
                return target == 0 || (target == 1 ? t < 10 : t >= 10);
            }
            // 세로 선분 (x,y)-(x,y+1) : 픽셀 (x-1,y) | (x,y) 사이
            bool KeepV(int x, int y)
            {
                if (x < 0 || x > w || y < 0 || y >= h || !vert[y * (w + 1) + x]) return false;
                if (target == 0) return true;
                return WantPix(x > 0 ? y * w + (x - 1) : -1) || WantPix(x < w ? y * w + x : -1);
            }
            // 가로 선분 (x,y)-(x+1,y) : 픽셀 (x,y-1) | (x,y) 사이
            bool KeepH(int x, int y)
            {
                if (x < 0 || x >= w || y < 0 || y > h || !horz[y * w + x]) return false;
                if (target == 0) return true;
                return WantPix(y > 0 ? (y - 1) * w + x : -1) || WantPix(y < h ? y * w + x : -1);
            }

            var usedV = new bool[(w + 1) * h];
            var usedH = new bool[w * (h + 1)];

            // 방향 0=위 1=아래 2=왼쪽 3=오른쪽. 해당 방향으로 아직 안 쓴 선분이 있으면 true.
            bool Step(int x, int y, int d, out int nx, out int ny, out int ei, out bool isV)
            {
                nx = x; ny = y; ei = -1; isV = false;
                switch (d)
                {
                    case 0:
                        if (y <= 0 || !KeepV(x, y - 1)) return false;
                        ei = (y - 1) * (w + 1) + x; isV = true;
                        if (usedV[ei]) return false;
                        ny = y - 1; return true;
                    case 1:
                        if (y >= h || !KeepV(x, y)) return false;
                        ei = y * (w + 1) + x; isV = true;
                        if (usedV[ei]) return false;
                        ny = y + 1; return true;
                    case 2:
                        if (x <= 0 || !KeepH(x - 1, y)) return false;
                        ei = y * w + (x - 1);
                        if (usedH[ei]) return false;
                        nx = x - 1; return true;
                    default:
                        if (x >= w || !KeepH(x, y)) return false;
                        ei = y * w + x;
                        if (usedH[ei]) return false;
                        nx = x + 1; return true;
                }
            }

            int Degree(int x, int y)
            {
                int n = 0;
                if (KeepV(x, y - 1)) n++;
                if (KeepV(x, y)) n++;
                if (KeepH(x - 1, y)) n++;
                if (KeepH(x, y)) n++;
                return n;
            }

            var result = new List<(List<(double x, double y)>, bool)>();

            void WalkFrom(int sx, int sy)
            {
                while (true)
                {
                    int firstDir = -1;
                    for (int d = 0; d < 4; d++)
                        if (Step(sx, sy, d, out _, out _, out _, out _)) { firstDir = d; break; }
                    if (firstDir < 0) return;

                    var pts = new List<(double x, double y)> { (sx, sy) };
                    int cx = sx, cy = sy, last = -1;
                    while (true)
                    {
                        int pick = -1;
                        if (last >= 0 && Step(cx, cy, last, out _, out _, out _, out _)) pick = last;   // 직진 우선
                        else
                            for (int d = 0; d < 4; d++)
                                if (Step(cx, cy, d, out _, out _, out _, out _)) { pick = d; break; }
                        if (pick < 0) break;

                        Step(cx, cy, pick, out int nx, out int ny, out int ei, out bool isV);
                        if (isV) usedV[ei] = true; else usedH[ei] = true;
                        cx = nx; cy = ny; last = pick;
                        pts.Add((cx, cy));
                        if (cx == sx && cy == sy) break;      // 닫힘
                    }
                    bool closed = pts.Count > 2 && pts[pts.Count - 1] == pts[0];
                    if (pts.Count >= 2) result.Add((Simplify(pts, SimplifyTolPx), closed));
                }
            }

            // 1) 끝점·갈림점에서 시작해야 조각이 자연스럽게 끊긴다
            for (int y = 0; y <= h; y++)
                for (int x = 0; x <= w; x++)
                {
                    int deg = Degree(x, y);
                    if (deg != 0 && deg != 2) WalkFrom(x, y);
                }
            // 2) 남은 것은 순수한 고리 — 아무 데서나 시작
            for (int y = 0; y < h; y++)
                for (int x = 0; x <= w; x++)
                    if (KeepV(x, y) && !usedV[y * (w + 1) + x]) WalkFrom(x, y);
            for (int y = 0; y <= h; y++)
                for (int x = 0; x < w; x++)
                    if (KeepH(x, y) && !usedH[y * w + x]) WalkFrom(x, y);

            return result;
        }

        /// <summary>Douglas–Peucker 단순화(양 끝 고정). tol 은 입력 좌표 단위(물리 픽셀).</summary>
        private static List<(double x, double y)> Simplify(List<(double x, double y)> pts, double tol)
        {
            int n = pts.Count;
            if (n < 3) return pts;
            var keep = new bool[n];
            keep[0] = keep[n - 1] = true;
            var stack = new Stack<(int a, int b)>();
            stack.Push((0, n - 1));
            while (stack.Count > 0)
            {
                var (a, b) = stack.Pop();
                if (b - a < 2) continue;
                double ax = pts[a].x, ay = pts[a].y;
                double dx = pts[b].x - ax, dy = pts[b].y - ay;
                double len2 = dx * dx + dy * dy;
                double worst = -1; int wi = -1;
                for (int i = a + 1; i < b; i++)
                {
                    double px = pts[i].x - ax, py = pts[i].y - ay, d2;
                    if (len2 < 1e-12) d2 = px * px + py * py;
                    else
                    {
                        double t = (px * dx + py * dy) / len2;
                        t = t < 0 ? 0 : (t > 1 ? 1 : t);
                        double qx = px - dx * t, qy = py - dy * t;
                        d2 = qx * qx + qy * qy;
                    }
                    if (d2 > worst) { worst = d2; wi = i; }
                }
                if (wi > 0 && worst > tol * tol) { keep[wi] = true; stack.Push((a, wi)); stack.Push((wi, b)); }
            }
            var outp = new List<(double x, double y)>();
            for (int i = 0; i < n; i++) if (keep[i]) outp.Add(pts[i]);
            return outp;
        }

        /// <summary>
        /// <260811_26> 소수 2자리까지 출력한다. 변환 없이 물리 픽셀(정수)일 때는 "0.##" 서식이 소수점
        /// 없이 정수와 똑같이 찍히므로 종전 출력과 바이트까지 동일하다.
        /// </summary>
        private static string ToPathData(List<(double x, double y)> poly, bool closed)
        {
            void Append(StringBuilder s, double x, double y) => s
                .Append(x.ToString("0.##", CultureInfo.InvariantCulture)).Append(' ')
                .Append(y.ToString("0.##", CultureInfo.InvariantCulture));

            // 닫힌 조각은 마지막 점이 첫 점과 같으므로 Z 로 대신한다(중복 점 제거).
            int count = closed && poly.Count > 1 ? poly.Count - 1 : poly.Count;
            var sb = new StringBuilder();
            sb.Append("M "); Append(sb, poly[0].x, poly[0].y);
            for (int i = 1; i < count; i++)
            {
                sb.Append(" L "); Append(sb, poly[i].x, poly[i].y);
            }
            if (closed) sb.Append(" Z");
            return sb.ToString();
        }
    }
}
