using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace HandModel3D.KeyboardUi
{
    /// <summary>physical-key-master.json 의 물리 키 한 개.</summary>
    public sealed class MasterKey
    {
        public int Row, Col;
        public string OutputBaseName;      // "1-3"
        public string QwertyBase, QwertyShift;
        public string DubeolBase, DubeolShift;   // shift "" = Shift 로 달라지는 글자 없음

        public string BaseFor(bool dubeol) => dubeol ? DubeolBase : QwertyBase;
        public string ShiftFor(bool dubeol)
        {
            string s = dubeol ? DubeolShift : QwertyShift;
            return string.IsNullOrEmpty(s) ? BaseFor(dubeol) : s;   // 값 없으면 base 유지(해석 ⓕ)
        }
    }

    /// <summary>물리 키 마스터 표 로더. 캡처 파일명 [행-열] 매핑과 키보드 렌더링 데이터의 원천.</summary>
    public static class KeyMaster
    {
        private static List<MasterKey> _keys;
        public static IReadOnlyList<MasterKey> Keys => _keys ?? Load();

        public static MasterKey Find(int row, int col)
        {
            foreach (var k in Keys) if (k.Row == row && k.Col == col) return k;
            return null;
        }

        private static List<MasterKey> Load()
        {
            _keys = new List<MasterKey>();
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "physical-key-master.json");
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var k in doc.RootElement.GetProperty("keys").EnumerateArray())
                {
                    var pos = k.GetProperty("keyPos");
                    var q = k.GetProperty("qwerty");
                    var d = k.GetProperty("dubeolsik");
                    _keys.Add(new MasterKey
                    {
                        Row = pos[0].GetInt32(),
                        Col = pos[1].GetInt32(),
                        OutputBaseName = k.GetProperty("outputBaseName").GetString(),
                        QwertyBase = q.GetProperty("base").GetString(),
                        QwertyShift = q.GetProperty("shift").GetString(),
                        DubeolBase = d.GetProperty("base").GetString(),
                        DubeolShift = d.GetProperty("shift").GetString(),
                    });
                }
            }
            catch
            {
                // 마스터 표가 없으면 키보드 배경/파일명 [행-열] 기능이 비활성화될 뿐, 앱은 동작한다.
            }
            return _keys;
        }
    }
}
