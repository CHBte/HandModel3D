using System;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace HandModel3D.Geometry
{
    /// <summary>
    /// 순수 WPF 로 절차적 3D 메시를 쌓는 간단한 빌더. 외부 패키지(HelixToolkit 등) 없이
    /// 손가락 마디(테이퍼드 캡슐)·관절(구)을 만든다. 손은 뼈대 위에 이 볼륨들을 얹어 '피부'만
    /// 보이게 렌더링한다.
    /// </summary>
    public sealed class MeshBuilder
    {
        public readonly Point3DCollection Positions = new Point3DCollection();
        public readonly Vector3DCollection Normals = new Vector3DCollection();
        public readonly Int32Collection Indices = new Int32Collection();

        /// <summary>모든 정점·법선에 강체 변환 적용(ToMesh/Freeze 전에 호출).</summary>
        public void Transform(Matrix3D m)
        {
            for (int i = 0; i < Positions.Count; i++) Positions[i] = m.Transform(Positions[i]);
            for (int i = 0; i < Normals.Count; i++) Normals[i] = m.Transform(Normals[i]);
        }

        public MeshGeometry3D ToMesh()
        {
            var m = new MeshGeometry3D
            {
                Positions = Positions,
                Normals = Normals,
                TriangleIndices = Indices
            };
            m.Freeze();
            return m;
        }

        private int Add(Point3D p, Vector3D n)
        {
            int i = Positions.Count;
            Positions.Add(p);
            Normals.Add(n);
            return i;
        }

        /// <summary>축이 두 점을 잇는, 반지름이 양끝에서 다를 수 있는 원뿔대(테이퍼드 원기둥).</summary>
        public void AddTaperedCylinder(Point3D p0, Point3D p1, double r0, double r1, int seg = 16)
        {
            Vector3D axis = p1 - p0;
            double len = axis.Length;
            if (len < 1e-9) return;
            axis /= len;

            // axis 에 수직인 두 기저 벡터
            Vector3D u = Vector3D.CrossProduct(axis, new Vector3D(0, 0, 1));
            if (u.Length < 1e-6) u = Vector3D.CrossProduct(axis, new Vector3D(0, 1, 0));
            u.Normalize();
            Vector3D v = Vector3D.CrossProduct(axis, u);
            v.Normalize();

            int baseIdx = Positions.Count;
            for (int i = 0; i <= seg; i++)
            {
                double a = 2 * Math.PI * i / seg;
                Vector3D radial = Math.Cos(a) * u + Math.Sin(a) * v;
                // 원뿔대 옆면 법선 (반지름 변화 반영)
                Vector3D normal = radial * len + axis * (r0 - r1);
                normal.Normalize();
                Add(p0 + radial * r0, normal);
                Add(p1 + radial * r1, normal);
            }
            for (int i = 0; i < seg; i++)
            {
                int a0 = baseIdx + i * 2;
                int a1 = a0 + 1;
                int b0 = a0 + 2;
                int b1 = a0 + 3;
                Indices.Add(a0); Indices.Add(b0); Indices.Add(a1);
                Indices.Add(a1); Indices.Add(b0); Indices.Add(b1);
            }
        }

        /// <summary>중심 c, 반지름 r 의 구. 관절 이음매를 매끄럽게 하는 데 쓴다.</summary>
        public void AddSphere(Point3D c, double r, int stacks = 12, int slices = 16)
        {
            int baseIdx = Positions.Count;
            for (int st = 0; st <= stacks; st++)
            {
                double phi = Math.PI * st / stacks;          // 0..pi
                double y = Math.Cos(phi);
                double rr = Math.Sin(phi);
                for (int sl = 0; sl <= slices; sl++)
                {
                    double th = 2 * Math.PI * sl / slices;
                    var n = new Vector3D(rr * Math.Cos(th), y, rr * Math.Sin(th));
                    Add(c + n * r, n);
                }
            }
            int cols = slices + 1;
            for (int st = 0; st < stacks; st++)
            {
                for (int sl = 0; sl < slices; sl++)
                {
                    int i0 = baseIdx + st * cols + sl;
                    int i1 = i0 + 1;
                    int i2 = i0 + cols;
                    int i3 = i2 + 1;
                    // ⚠ qksqhr(2026-08-11) 검토 기록 — 고치지 말 것: 이 winding 은 내향(뒷면 렌더)이다.
                    // 외향으로 '교정'해 보니 너클·관절 구·무지구 배 등 구 기반 볼록이 전부 앞면 기준으로
                    // 튀어나와, 사용자가 사진 대조로 승인한 손 외형(매끈한 손등·은은한 너클)이 무너졌다.
                    // 현재의 모든 구 반지름·오프셋·윤곽 깊이 임계는 이 뒷면 렌더 위에 실측 튜닝돼 있어
                    // 사실상 계약의 일부다. 바꾸려면 전체 시각 재튜닝+사용자 재승인이 필요하다.
                    Indices.Add(i0); Indices.Add(i2); Indices.Add(i1);
                    Indices.Add(i1); Indices.Add(i2); Indices.Add(i3);
                }
            }
        }

        /// <summary>
        /// 축이 두 점을 잇고 링마다 반지름이 다른 회전체(레이드). 뼈 프로파일(양끝 두껍고
        /// 중간 잘록)을 표현한다. 법선은 면 법선 누적으로 계산한다. 끝은 열려 있음(관절 구가 덮음).
        /// </summary>
        public void AddLathe(Point3D p0, Point3D p1, double[] radii, int seg = 16)
        {
            Vector3D axis = p1 - p0;
            double len = axis.Length;
            if (len < 1e-9 || radii == null || radii.Length < 2) return;
            axis /= len;
            Vector3D u = Vector3D.CrossProduct(axis, new Vector3D(0, 0, 1));
            if (u.Length < 1e-6) u = Vector3D.CrossProduct(axis, new Vector3D(0, 1, 0));
            u.Normalize();
            Vector3D v = Vector3D.CrossProduct(axis, u);
            v.Normalize();

            int n = radii.Length;
            var pos = new Point3D[n * (seg + 1)];
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / (n - 1);
                Point3D c = p0 + (p1 - p0) * t;
                for (int j = 0; j <= seg; j++)
                {
                    double a = 2 * Math.PI * j / seg;
                    pos[i * (seg + 1) + j] = c + (Math.Cos(a) * u + Math.Sin(a) * v) * radii[i];
                }
            }
            var idxList = new System.Collections.Generic.List<int>();
            int cols = seg + 1;
            // (u, v=axis×u, axis) 는 오른손계이므로 각도-우선 순서가 바깥 winding 이다.
            for (int i = 0; i < n - 1; i++)
                for (int j = 0; j < seg; j++)
                {
                    int i0 = i * cols + j, i1 = i0 + 1, i2 = i0 + cols, i3 = i2 + 1;
                    idxList.Add(i0); idxList.Add(i1); idxList.Add(i2);
                    idxList.Add(i1); idxList.Add(i3); idxList.Add(i2);
                }
            AppendWithNumericNormals(pos, idxList);
        }

        /// <summary>
        /// +Y 방향 로프트 튜브(손바닥용). 링마다 중심·X반폭·Z반두께가 다르고 단면은 수퍼타원
        /// (exponent 2=타원, 클수록 사각형에 가까움). 상하는 중심 팬으로 닫는다.
        /// 링 좌표를 월드에서 직접 만들므로 미러된 손에서도 winding 이 항상 바깥이다.
        /// </summary>
        public void AddTube(System.Collections.Generic.IList<(Point3D c, double halfW, double halfT)> rings,
                            double exponent = 2.5, int seg = 24)
        {
            if (rings == null || rings.Count < 2) return;
            int n = rings.Count;
            double e = Math.Max(2.0, exponent);
            var pos = new Point3D[n * (seg + 1) + 2];
            for (int i = 0; i < n; i++)
            {
                var (c, hw, ht) = rings[i];
                for (int j = 0; j <= seg; j++)
                {
                    double a = 2 * Math.PI * j / seg;
                    double ca = Math.Cos(a), sa = Math.Sin(a);
                    double px = Math.Sign(ca) * Math.Pow(Math.Abs(ca), 2.0 / e) * hw;
                    double pz = Math.Sign(sa) * Math.Pow(Math.Abs(sa), 2.0 / e) * ht;
                    pos[i * (seg + 1) + j] = new Point3D(c.X + px, c.Y, c.Z + pz);
                }
            }
            int bottomC = n * (seg + 1), topC = bottomC + 1;
            pos[bottomC] = rings[0].c;
            pos[topC] = rings[n - 1].c;

            var idxList = new System.Collections.Generic.List<int>();
            int cols = seg + 1;
            for (int i = 0; i < n - 1; i++)
                for (int j = 0; j < seg; j++)
                {
                    int i0 = i * cols + j, i1 = i0 + 1, i2 = i0 + cols, i3 = i2 + 1;
                    idxList.Add(i0); idxList.Add(i2); idxList.Add(i1);
                    idxList.Add(i1); idxList.Add(i2); idxList.Add(i3);
                }
            for (int j = 0; j < seg; j++)
            {
                // 아래 캡(-Y 법선), 위 캡(+Y 법선)
                idxList.Add(bottomC); idxList.Add(j); idxList.Add(j + 1);
                int t0 = (n - 1) * cols + j, t1 = t0 + 1;
                idxList.Add(topC); idxList.Add(t1); idxList.Add(t0);
            }
            AppendWithNumericNormals(pos, idxList);
        }

        /// <summary>정점·인덱스를 면 법선 누적(스무스 셰이딩)으로 추가.</summary>
        private void AppendWithNumericNormals(Point3D[] pos, System.Collections.Generic.List<int> idx)
        {
            var nrm = new Vector3D[pos.Length];
            for (int t = 0; t + 2 < idx.Count; t += 3)
            {
                int a = idx[t], b = idx[t + 1], c = idx[t + 2];
                Vector3D fn = Vector3D.CrossProduct(pos[b] - pos[a], pos[c] - pos[a]);
                nrm[a] += fn; nrm[b] += fn; nrm[c] += fn;
            }
            int baseIdx = Positions.Count;
            for (int i = 0; i < pos.Length; i++)
            {
                var nn = nrm[i];
                if (nn.LengthSquared > 1e-12) nn.Normalize(); else nn = new Vector3D(0, 1, 0);
                Positions.Add(pos[i]);
                Normals.Add(nn);
            }
            foreach (int i in idx) Indices.Add(baseIdx + i);
        }

        /// <summary>반타원체(손끝 뭉툭한 끝, 손바닥/손등 볼록 볼륨용).</summary>
        public void AddEllipsoid(Point3D c, double rx, double ry, double rz, int stacks = 12, int slices = 16)
        {
            int baseIdx = Positions.Count;
            for (int st = 0; st <= stacks; st++)
            {
                double phi = Math.PI * st / stacks;
                double y = Math.Cos(phi);
                double rr = Math.Sin(phi);
                for (int sl = 0; sl <= slices; sl++)
                {
                    double th = 2 * Math.PI * sl / slices;
                    double nx = rr * Math.Cos(th), ny = y, nz = rr * Math.Sin(th);
                    var p = new Point3D(c.X + nx * rx, c.Y + ny * ry, c.Z + nz * rz);
                    // 타원체 법선
                    var n = new Vector3D(nx / rx, ny / ry, nz / rz);
                    if (n.Length > 1e-9) n.Normalize();
                    Add(p, n);
                }
            }
            int cols = slices + 1;
            for (int st = 0; st < stacks; st++)
            {
                for (int sl = 0; sl < slices; sl++)
                {
                    int i0 = baseIdx + st * cols + sl;
                    int i1 = i0 + 1;
                    int i2 = i0 + cols;
                    int i3 = i2 + 1;
                    // ⚠ qksqhr(2026-08-11): AddSphere 와 동일 — 내향 winding 이 승인된 외형의 일부(위 기록 참고).
                    Indices.Add(i0); Indices.Add(i2); Indices.Add(i1);
                    Indices.Add(i1); Indices.Add(i2); Indices.Add(i3);
                }
            }
        }
    }
}
