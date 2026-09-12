using System;
using System.Windows.Media.Media3D;

namespace HandModel3D.Render
{
    /// <summary>
    /// 자체 3D 카메라(궤도/줌/팬). WPF Viewport3D 의 투영을 쓰지 않고 world→screen 을 직접 계산해
    /// 환경(DPI/샌드박스)과 무관하게 정확한 2D 투영을 얻는다. 윤곽선 추출·픽킹·클리핑이 공유한다.
    /// 화면 좌표는 좌상단 원점, Y 아래로 증가.
    /// </summary>
    public sealed class Camera3D
    {
        public Point3D Target = new Point3D(0, 8, 0);
        public double Distance = 84;
        public double YawDeg = 0;      // 좌우 궤도(0 = 정면에서 손등)
        public double PitchDeg = 0;    // 상하 궤도(+ = 위에서 내려다봄). 시작값 0 (<260810_4>(1))
        public double RollDeg = 0;     // 시선축 둘레 기울임(<260810_3-1>(4): 기즈모 z축 제어)
        public double PanX = 0, PanY = 0;  // 화면 평행이동(픽셀)
        public double FovDeg = 34;
        public int Width = 800, Height = 600;
        /// <summary>
        /// 투영 기준 크기(0이면 Width/Height). 캡처의 확장 렌더에서 캔버스(Width/Height)만 키우고
        /// 투영 스케일은 화면과 동일하게 유지할 때 사용한다 — 안 그러면 캔버스 확대가 곧 줌이 된다.
        /// </summary>
        public int ViewW = 0, ViewH = 0;

        // 계산 캐시
        private Vector3D _f, _r, _u;   // forward, right, up (정규직교)
        private Point3D _pos;
        private double _tanHalfX, _tanHalfY;

        public Point3D Position => _pos;
        public Vector3D Forward => _f;
        public Vector3D Right => _r;
        public Vector3D Up => _u;
        public const double Near = 0.05;

        public void Update()
        {
            double yaw = YawDeg * Math.PI / 180.0;
            double pitch = PitchDeg * Math.PI / 180.0;
            var dir = new Vector3D(Math.Cos(pitch) * Math.Sin(yaw),
                                   Math.Sin(pitch),
                                   Math.Cos(pitch) * Math.Cos(yaw));
            _pos = Target + dir * Distance;
            _f = -dir; _f.Normalize();

            // 수직에 가까울 때의 대체 up 은 정상 구간과 연속이어야 한다. 부호가 반대면 |pitch|가
            // 약 84.3°를 넘는 순간 Right/Up 이 동시에 뒤집혀 화면이 한 프레임에 180° 회전한다.
            var worldUp = new Vector3D(0, 1, 0);
            if (Math.Abs(_f.Y) > 0.995) worldUp = new Vector3D(0, 0, _f.Y > 0 ? 1 : -1);
            _r = Vector3D.CrossProduct(_f, worldUp); _r.Normalize();
            _u = Vector3D.CrossProduct(_r, _f); _u.Normalize();

            // 롤(<260810_3-1>): Right/Up 을 시선축 둘레로 회전 — 투영·픽킹·캡처가 함께 기울어진다.
            if (RollDeg != 0)
            {
                double rr = RollDeg * Math.PI / 180.0;
                Vector3D r2 = _r * Math.Cos(rr) + _u * Math.Sin(rr);
                Vector3D u2 = _u * Math.Cos(rr) - _r * Math.Sin(rr);
                _r = r2; _u = u2;
            }

            int pw = ViewW > 0 ? ViewW : Width;
            int ph = ViewH > 0 ? ViewH : Height;
            double aspect = (double)pw / Math.Max(1, ph);
            _tanHalfX = Math.Tan(FovDeg * Math.PI / 180.0 / 2.0);
            _tanHalfY = _tanHalfX / aspect;
            _pw = pw; _ph = ph;
        }

        private int _pw = 800, _ph = 600;

        public struct Proj { public double X, Y, Depth; public bool InFront; }

        /// <summary>월드 점 → 화면 좌표(+깊이). Depth = 시선방향 거리(작을수록 앞).</summary>
        public Proj Project(Point3D w)
        {
            Vector3D d = w - _pos;
            double camZ = Vector3D.DotProduct(d, _f);
            var p = new Proj { Depth = camZ, InFront = camZ > Near };
            if (camZ <= 1e-6) camZ = 1e-6;
            double camX = Vector3D.DotProduct(d, _r);
            double camY = Vector3D.DotProduct(d, _u);
            double ndcX = (camX / camZ) / _tanHalfX;
            double ndcY = (camY / camZ) / _tanHalfY;
            p.X = (ndcX * 0.5 + 0.5) * _pw + PanX;
            p.Y = (1.0 - (ndcY * 0.5 + 0.5)) * _ph + PanY;
            return p;
        }

        /// <summary>화면 픽셀 → 월드 광선(원점=카메라, 방향=정규화). 팬 보정 포함.</summary>
        public void Ray(double sx, double sy, out Point3D origin, out Vector3D dir)
        {
            double ndcX = ((sx - PanX) / _pw * 2.0 - 1.0);
            double ndcY = (1.0 - (sy - PanY) / _ph * 2.0);
            Vector3D d = _f + _r * (ndcX * _tanHalfX) + _u * (ndcY * _tanHalfY);
            d.Normalize();
            origin = _pos;
            dir = d;
        }

        /// <summary>화면 픽셀당 월드 단위(Target 평면 기준). 드래그 감도용.</summary>
        public double WorldPerPixelAtTarget()
        {
            double h = 2.0 * Distance * _tanHalfY;
            return h / Math.Max(1, _ph);
        }
    }
}
