using System.Collections.Generic;

namespace HandModel3D.Hand
{
    /// <summary>
    /// 모든 관절 슬라이더의 퍼센트 값(0~100). 100% = 최대 폄+벌림(🖐), 0% = 최대 굽힘+모음.
    /// 저장/열기(3Dm_*.json) 시 이 값들이 그대로 직렬화된다.
    /// </summary>
    public sealed class HandPose
    {
        public readonly Dictionary<string, double> Percent = new Dictionary<string, double>();

        public double Get(SliderId id) => Get(id.Key);
        public double Get(string key) => Percent.TryGetValue(key, out double v) ? v : 100.0;
        public void Set(SliderId id, double pct) => Percent[id.Key] = Clamp(pct);
        public void Set(string key, double pct) => Percent[key] = Clamp(pct);

        private static double Clamp(double v) => v < 0 ? 0 : (v > 100 ? 100 : v);

        public HandPose Clone()
        {
            var p = new HandPose();
            foreach (var kv in Percent) p.Percent[kv.Key] = kv.Value;
            return p;
        }

        /// <summary>
        /// 모든 슬라이더를 100%(활짝 편 손)로 초기화. 단 두 엄지의 좌우 CMC 만 예외
        /// (<260811_21> 추가지시, ThumbCmcLRDefault).
        /// </summary>
        public static HandPose Default()
        {
            var p = new HandPose();
            foreach (var id in AllSliderIds()) p.Percent[id.Key] = 100.0;
            // <260811_21> 두 엄지의 좌우 CMC 초기값 = 50%. 100%(최대 벌림)에서는 새 초기값
            // (크기 95%·좌우 130%·손 위치)과 겹쳐 양손 엄지가 서로 닿아, 프로그램을 열자마자
            // 충돌 경고가 뜨는 문제가 있었다. 설정 창 '초기값'(AppSettings.DefThumbCmcLR)의
            // 기본값과 같은 값이며, 실제 시작 포즈는 MainWindow.ApplyDefaultsToScene() 이 정한다.
            foreach (HandSide side in new[] { HandSide.Left, HandSide.Right })
                p.Percent[SliderId.Joint(side, Digit.Thumb, JointKind.CMC, Axis.Abduct).Key] = ThumbCmcLRDefault;
            return p;
        }

        /// <summary>두 엄지 좌우 CMC 의 초기값(%) — <260811_21>.</summary>
        public const double ThumbCmcLRDefault = 50.0;

        /// <summary>프로그램이 다루는 모든 슬라이더 식별자를 나열한다(좌·우 패널 구성과 일치).</summary>
        public static IEnumerable<SliderId> AllSliderIds()
        {
            foreach (HandSide side in new[] { HandSide.Left, HandSide.Right })
            {
                foreach (Digit d in new[] { Digit.Index, Digit.Middle, Digit.Ring, Digit.Pinky })
                {
                    yield return SliderId.Joint(side, d, JointKind.DIP, Axis.Flex);
                    yield return SliderId.Joint(side, d, JointKind.PIP, Axis.Flex);
                    yield return SliderId.Joint(side, d, JointKind.MCP, Axis.Flex);
                    yield return SliderId.Joint(side, d, JointKind.MCP, Axis.Abduct);
                }
                // 엄지
                yield return SliderId.Joint(side, Digit.Thumb, JointKind.IP, Axis.Flex);
                yield return SliderId.Joint(side, Digit.Thumb, JointKind.MCP, Axis.Flex);
                yield return SliderId.Joint(side, Digit.Thumb, JointKind.MCP, Axis.Abduct);
                yield return SliderId.Joint(side, Digit.Thumb, JointKind.CMC, Axis.Flex);
                yield return SliderId.Joint(side, Digit.Thumb, JointKind.CMC, Axis.Abduct);
                // 손바닥 아치
                yield return SliderId.Arch(side);
            }
        }
    }
}
