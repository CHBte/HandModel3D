using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HandModel3D
{
    /// <summary>정보 창. 빌드 날짜·라이선스 표시(제작자는 아직 표시 안 함, (3-11)). OTP 정보 창과 유사.</summary>
    public sealed class AboutWindow : Window
    {
        public AboutWindow()
        {
            Title = "프로그램 정보";
            Width = 460; Height = 440; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF3, 0xF5));
            FontFamily = new FontFamily("Malgun Gothic, Segoe UI");

            string ver = AsmMeta("DisplayVersion") ?? "0.1.0";
            string build = AsmMeta("BuildTimestamp") ?? "-";

            var subInk = new SolidColorBrush(Color.FromRgb(0x49, 0x50, 0x57));

            var sp = new StackPanel { Margin = new Thickness(24, 20, 24, 18) };
            // 정식 명칭: 한글명(영어명) (<260810_1>(1))
            sp.Children.Add(new TextBlock { Text = "손 모델 3D(Hand Model 3D)", FontWeight = FontWeights.Bold, FontSize = 18 });
            sp.Children.Add(new TextBlock { Text = $"버전 {ver}", Foreground = subInk, Margin = new Thickness(0, 4, 0, 0) });
            sp.Children.Add(new TextBlock { Text = $"빌드: {build}", Foreground = subInk, Margin = new Thickness(0, 12, 0, 0) });
            sp.Children.Add(new TextBlock
            {
                Text = "Open Typing Plus 의 손가락 레이어 그림 제작을 위한 3D 손 모델링 도구입니다.",
                TextWrapping = TextWrapping.Wrap, Foreground = subInk,
                Margin = new Thickness(0, 16, 0, 0)
            });
            // 라이선스 문구 (<260810_1>(2))
            sp.Children.Add(new TextBlock
            {
                Text = "원본 코드(3D 손 모델링·IK·렌더링 등)는 CC0(퍼블릭 도메인)로 헌정합니다. 자유롭게 변형 및 배포 가능합니다.",
                TextWrapping = TextWrapping.Wrap, Foreground = subInk,
                Margin = new Thickness(0, 14, 0, 0)
            });
            sp.Children.Add(new TextBlock
            {
                Text = "키보드 배경 그림은 OpenTyping(© 2018 Gear, MIT 라이선스)의 KeyBox 디자인을 참고·변형했습니다.",
                TextWrapping = TextWrapping.Wrap, Foreground = subInk,
                Margin = new Thickness(0, 10, 0, 0)
            });

            var ok = new Button { Content = "확인", Width = 80, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0), Padding = new Thickness(6, 3, 6, 3) };
            ok.Click += (s, e) => Close();
            sp.Children.Add(ok);
            Content = sp;
        }

        private static string AsmMeta(string key)
        {
            foreach (var a in Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>())
                if (a.Key == key) return a.Value;
            return null;
        }
    }
}
