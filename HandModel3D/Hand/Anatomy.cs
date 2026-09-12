using System.Collections.Generic;

namespace HandModel3D.Hand
{
    public enum HandSide { Left, Right }

    // 새끼(Pinky)~엄지(Thumb). 손가락 끝에서 손목 순 배치와 무관한 식별용.
    public enum Digit { Pinky = 0, Ring = 1, Middle = 2, Index = 3, Thumb = 4 }

    public enum JointKind { CMC, MCP, PIP, DIP, IP }

    public enum Axis { Flex, Abduct }   // Flex = 상하(↕), Abduct = 좌우(↔)

    /// <summary>
    /// 한 관절 슬라이더의 식별자. 손·손가락·관절종류·축. 문자열 키로 직렬화한다.
    /// 손바닥 아치는 Digit=Thumb 자리 대신 별도 IsArch 플래그로 표시한다.
    /// </summary>
    public struct SliderId
    {
        public HandSide Side;
        public Digit Digit;
        public JointKind Kind;
        public Axis Axis;
        public bool IsArch;

        public static SliderId Joint(HandSide s, Digit d, JointKind k, Axis a)
            => new SliderId { Side = s, Digit = d, Kind = k, Axis = a, IsArch = false };
        public static SliderId Arch(HandSide s)
            => new SliderId { Side = s, IsArch = true, Kind = JointKind.CMC };

        public string Key
        {
            get
            {
                if (IsArch) return (Side == HandSide.Left ? "L" : "R") + ".ARCH";
                string code = Anatomy.FingerCode(Side, Digit);
                string ax = Axis == Axis.Flex ? "UD" : "LR";
                // 단일 자유도 관절은 축 접미어를 붙이지 않는다.
                bool twoDof = (Kind == JointKind.MCP) || (Kind == JointKind.CMC);
                return twoDof ? $"{code}.{Kind}.{ax}" : $"{code}.{Kind}";
            }
        }
    }

    /// <summary>손가락별 뼈 길이·굵기, 관절 가동범위, 표준 손가락 코드 등 해부학 상수.</summary>
    public static class Anatomy
    {
        // 손가락 표준 코드 (<260719_5>). 왼손 소지=L1 … 엄지=L5 / 오른손 엄지=R6 … 소지=R0.
        public static string FingerCode(HandSide s, Digit d)
        {
            if (s == HandSide.Left)
                switch (d)
                {
                    case Digit.Pinky: return "L1";
                    case Digit.Ring: return "L2";
                    case Digit.Middle: return "L3";
                    case Digit.Index: return "L4";
                    default: return "L5";
                }
            switch (d)
            {
                case Digit.Thumb: return "R6";
                case Digit.Index: return "R7";
                case Digit.Middle: return "R8";
                case Digit.Ring: return "R9";
                default: return "R0";
            }
        }

        public static readonly Digit[] Fingers = { Digit.Index, Digit.Middle, Digit.Ring, Digit.Pinky };
        public static readonly Digit[] AllDigits = { Digit.Thumb, Digit.Index, Digit.Middle, Digit.Ring, Digit.Pinky };

        // 뼈 길이(정지 상태, 임의 단위 ≈ cm). 오른손 기준; 왼손은 좌우 반전.
        public struct DigitDims
        {
            public double MetaLen;     // 손허리뼈(엄지=CMC부터) 길이 — 손가락은 팔 렌더에 흡수, 엄지는 실제 뼈
            public double ProxLen, IntLen, DistLen;
            public double Radius;      // 기본 손가락 굵기(볼륨 1.0 기준)
            public double KnuckleX, KnuckleY;  // MCP(손허리뼈머리) 위치 (엄지는 CMC 위치)
            public double SplayDeg;    // 100%(최대 벌림)일 때 좌우 splay 각(부호 = 방향)
        }

        public static DigitDims Dims(Digit d)
        {
            switch (d)
            {
                case Digit.Index:  return new DigitDims { ProxLen = 4.5, IntLen = 2.7, DistLen = 2.0, Radius = 0.85, KnuckleX = 1.75, KnuckleY = 7.7, SplayDeg = 16 };
                case Digit.Middle: return new DigitDims { ProxLen = 5.0, IntLen = 3.1, DistLen = 2.1, Radius = 0.88, KnuckleX = 0.55, KnuckleY = 8.3, SplayDeg = 3 };
                case Digit.Ring:   return new DigitDims { ProxLen = 4.6, IntLen = 3.0, DistLen = 2.0, Radius = 0.82, KnuckleX = -0.7, KnuckleY = 7.9, SplayDeg = -11 };
                case Digit.Pinky:  return new DigitDims { ProxLen = 3.6, IntLen = 2.1, DistLen = 1.7, Radius = 0.72, KnuckleX = -1.85, KnuckleY = 6.9, SplayDeg = -22 };
                // <260811_9>: 실제 손 비율 대비 엄지가 길어, 세 분절을 0.8배(옛 길이 80% = 새 100%)로 축소.
                default: /*Thumb*/ return new DigitDims { MetaLen = 3.52, ProxLen = 2.56, IntLen = 0, DistLen = 2.0, Radius = 1.0, KnuckleX = 2.15, KnuckleY = 2.9, SplayDeg = 0 };
            }
        }

        // 관절 가동범위(도). 0% = 최대 굽힘, 100% = 최대 폄(=0도, 벌림).
        public const double DipMaxFlex = 75;
        public const double PipMaxFlex = 105;
        public const double IpMaxFlex = 80;   // 엄지 IP
        public const double McpMaxFlex = 90;
        public const double ThumbMcpMaxFlex = 55;
        public const double ThumbCmcMaxFlex = 45;   // 대립 방향 굽힘
        public const double ThumbCmcMaxAbduct = 55; // 손바닥면에서 벌어짐
        public const double ThumbMcpMaxAbduct = 12;
        public const double ArchMaxCup = 22;   // 아치 최대 오목(약지·소지 손허리뼈 회전)

        // 손목/손바닥(팔) 크기
        public const double PalmHalfWidth = 3.9;   // X 반폭 (엄지 쪽 포함 전)
        public const double WristY = 0.0;
        public const double PalmThickness = 1.4;   // Z 반두께
    }
}
