using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;

namespace HandModel3D
{
    /// <summary>
    /// <260811_32> '도움말' 창. 사용법 문서(Resources\사용법.md)를 실행 파일에 박아 두고(EmbeddedResource)
    /// 그 내용을 읽어 보여 준다. 문서 파일을 따로 들고 다닐 필요가 없고, '출력' 버튼으로 원본 그대로
    /// (마크다운 텍스트) 다시 꺼낼 수 있다.
    ///
    /// 마크다운 전체를 지원하지는 않는다. 이 문서가 실제로 쓰는 문법만 그린다:
    /// 제목(#·##·###), 문단, 목록(-·1.), 표(|), 인용(>), 코드 블록(```), 가로줄(---),
    /// 그리고 줄 안의 **굵게**·`코드`·[글자](링크).
    /// </summary>
    public sealed class HelpWindow : Window
    {
        private const string ResourceName = "HandModel3D.사용법.md";
        public const string DefaultFileName = "손모델3D_사용법.md";

        private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0x21, 0x25, 0x29));
        private static readonly Brush SubInk = new SolidColorBrush(Color.FromRgb(0x49, 0x50, 0x57));
        private static readonly Brush Rule = new SolidColorBrush(Color.FromRgb(0xDE, 0xE2, 0xE6));
        private static readonly Brush CodeBg = new SolidColorBrush(Color.FromRgb(0xF1, 0xF3, 0xF5));
        private static readonly Brush HeadBg = new SolidColorBrush(Color.FromRgb(0xE9, 0xEC, 0xEF));
        private static readonly Brush QuoteBar = new SolidColorBrush(Color.FromRgb(0x1C, 0x7E, 0xD6));
        private static readonly FontFamily CodeFont = new FontFamily("Consolas, D2Coding, Malgun Gothic");

        private readonly string _markdown;

        public HelpWindow()
        {
            Title = "도움말 — 손 모델 3D 사용법";
            Width = 900; Height = 720; MinWidth = 520; MinHeight = 320;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF3, 0xF5));
            FontFamily = new FontFamily("Malgun Gothic, Segoe UI");

            _markdown = LoadMarkdown();

            var root = new DockPanel();

            // 아래쪽 버튼 줄
            var bar = new DockPanel { Margin = new Thickness(16, 10, 16, 12), LastChildFill = false };
            var close = new Button { Content = "닫기", Width = 80, Padding = new Thickness(6, 3, 6, 3) };
            close.Click += (s, e) => Close();
            var export = new Button { Content = "출력", Width = 80, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(6, 3, 6, 3) };
            export.Click += Export_Click;
            DockPanel.SetDock(close, Dock.Right);
            DockPanel.SetDock(export, Dock.Right);
            bar.Children.Add(close);
            bar.Children.Add(export);
            DockPanel.SetDock(bar, Dock.Bottom);
            root.Children.Add(bar);

            var body = new StackPanel { Margin = new Thickness(24, 18, 24, 18) };
            Render(_markdown, body);

            root.Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = Brushes.White,
                Content = body
            });

            Content = root;
        }

        /// <summary>실행 파일에 박아 둔 사용법 문서를 읽는다.</summary>
        private static string LoadMarkdown()
        {
            try
            {
                using (Stream st = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
                {
                    if (st == null) return "사용법 문서를 실행 파일에서 찾지 못했습니다.";
                    using (var r = new StreamReader(st, Encoding.UTF8)) return r.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                return "사용법 문서를 읽지 못했습니다: " + ex.Message;
            }
        }

        /// <summary>'출력': 문서를 원본 마크다운 그대로 어디에 저장할지 물어 저장한다.</summary>
        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "사용법 문서 저장",
                Filter = "마크다운 문서 (*.md)|*.md|텍스트 파일 (*.txt)|*.txt|모든 파일 (*.*)|*.*",
                FileName = DefaultFileName,
                DefaultExt = ".md",
                AddExtension = true,
                OverwritePrompt = true,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                WriteTo(dlg.FileName);
                MessageBox.Show(this, "저장했습니다.\n" + dlg.FileName, "출력",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "저장하지 못했습니다.\n" + ex.Message, "출력",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>'출력'이 실제로 쓰는 코드. 원본과 같은 BOM 없는 UTF-8 로 쓴다(-helptest 도 이 길을 쓴다).</summary>
        internal void WriteTo(string path) => File.WriteAllText(path, _markdown, new UTF8Encoding(false));

        internal string Markdown => _markdown;

        // ===== 마크다운 → WPF 요소 =====

        /// <summary>-helptest 가 "문서에 있는 만큼 실제로 그려졌는가"를 대조하는 데 쓰는 개수.</summary>
        internal int HeadingCount, TableCount, CodeCount;

        private void Render(string md, Panel host)
        {
            string[] lines = md.Replace("\r\n", "\n").Split('\n');
            var para = new List<string>();      // 이어지는 문단 줄
            var quote = new List<string>();     // 이어지는 인용 줄
            var code = new List<string>();      // 코드 블록 줄
            bool inCode = false;

            void FlushPara()
            {
                if (para.Count == 0) return;
                host.Children.Add(Body(string.Join(" ", para), new Thickness(0, 0, 0, 10)));
                para.Clear();
            }
            void FlushQuote()
            {
                if (quote.Count == 0) return;
                var inner = new StackPanel();
                foreach (string q in quote) inner.Children.Add(Body(q, new Thickness(0, 0, 0, 2), SubInk));
                host.Children.Add(new Border
                {
                    BorderBrush = QuoteBar,
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(10, 6, 6, 6),
                    Margin = new Thickness(0, 0, 0, 10),
                    Background = CodeBg,
                    Child = inner
                });
                quote.Clear();
            }

            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i];
                string t = raw.TrimEnd();

                if (t.TrimStart().StartsWith("```"))
                {
                    if (inCode)
                    {
                        host.Children.Add(CodeBlock(string.Join("\n", code)));
                        CodeCount++;
                        code.Clear();
                        inCode = false;
                    }
                    else { FlushPara(); FlushQuote(); inCode = true; }
                    continue;
                }
                if (inCode) { code.Add(raw); continue; }

                if (t.Trim().Length == 0) { FlushPara(); FlushQuote(); continue; }

                // 인용
                if (t.TrimStart().StartsWith(">"))
                {
                    FlushPara();
                    quote.Add(t.TrimStart().TrimStart('>').Trim());
                    continue;
                }
                FlushQuote();

                // 가로줄
                if (Regex.IsMatch(t.Trim(), @"^(-{3,}|\*{3,}|_{3,})$"))
                {
                    FlushPara();
                    host.Children.Add(new Border
                    {
                        BorderBrush = Rule,
                        BorderThickness = new Thickness(0, 1, 0, 0),
                        Margin = new Thickness(0, 8, 0, 14)
                    });
                    continue;
                }

                // 제목
                Match h = Regex.Match(t, @"^(#{1,6})\s+(.*)$");
                if (h.Success)
                {
                    FlushPara();
                    int level = h.Groups[1].Value.Length;
                    double size = level == 1 ? 22 : level == 2 ? 17 : level == 3 ? 14 : 13;
                    var tb = new TextBlock
                    {
                        FontSize = size,
                        FontWeight = FontWeights.Bold,
                        Foreground = Ink,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, level <= 2 ? 16 : 12, 0, 6)
                    };
                    AddInlines(tb, h.Groups[2].Value);
                    host.Children.Add(tb);
                    HeadingCount++;
                    continue;
                }

                // 표 — 헤더 줄 다음이 구분 줄(|---|---|)이면 표로 본다
                if (t.TrimStart().StartsWith("|") && i + 1 < lines.Length && IsTableRule(lines[i + 1]))
                {
                    FlushPara();
                    var rows = new List<string[]> { SplitRow(t) };
                    int j = i + 2;
                    while (j < lines.Length && lines[j].TrimStart().StartsWith("|"))
                    {
                        rows.Add(SplitRow(lines[j]));
                        j++;
                    }
                    host.Children.Add(Table(rows));
                    TableCount++;
                    i = j - 1;
                    continue;
                }

                // 목록
                Match li = Regex.Match(t, @"^(\s*)([-*]|\d+\.)\s+(.*)$");
                if (li.Success)
                {
                    FlushPara();
                    int indent = li.Groups[1].Value.Length / 2;
                    string bullet = li.Groups[2].Value == "-" || li.Groups[2].Value == "*" ? "·" : li.Groups[2].Value;
                    var row = new DockPanel { Margin = new Thickness(6 + indent * 16, 0, 0, 4) };
                    var dot = new TextBlock
                    {
                        Text = bullet + " ",
                        Foreground = SubInk,
                        MinWidth = 14,
                        VerticalAlignment = VerticalAlignment.Top
                    };
                    DockPanel.SetDock(dot, Dock.Left);
                    row.Children.Add(dot);
                    var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Ink, LineHeight = 20 };
                    AddInlines(tb, li.Groups[3].Value);
                    row.Children.Add(tb);
                    host.Children.Add(row);
                    continue;
                }

                para.Add(t.Trim());
            }
            if (inCode && code.Count > 0) host.Children.Add(CodeBlock(string.Join("\n", code)));
            FlushPara();
            FlushQuote();
        }

        private static bool IsTableRule(string line)
        {
            string t = line.Trim();
            if (!t.StartsWith("|")) return false;
            foreach (string c in SplitRow(t))
                if (!Regex.IsMatch(c.Trim(), @"^:?-{2,}:?$")) return false;
            return true;
        }

        private static string[] SplitRow(string line)
        {
            string t = line.Trim();
            if (t.StartsWith("|")) t = t.Substring(1);
            if (t.EndsWith("|")) t = t.Substring(0, t.Length - 1);
            return t.Split('|');
        }

        private static UIElement Table(List<string[]> rows)
        {
            int cols = 0;
            foreach (string[] r in rows) cols = Math.Max(cols, r.Length);

            var grid = new Grid { Margin = new Thickness(0, 2, 0, 12) };
            for (int c = 0; c < cols; c++)
            {
                // 첫 열은 대개 짧은 이름표라 내용에 맞추고, 나머지는 남는 폭을 나눠 갖는다.
                grid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = c == 0 && cols > 1 ? GridLength.Auto : new GridLength(1, GridUnitType.Star),
                    MaxWidth = c == 0 && cols > 1 ? 280 : double.PositiveInfinity
                });
            }
            for (int r = 0; r < rows.Count; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            for (int r = 0; r < rows.Count; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    string text = c < rows[r].Length ? rows[r][c].Trim() : "";
                    var tb = new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = Ink,
                        FontWeight = r == 0 ? FontWeights.Bold : FontWeights.Normal,
                        LineHeight = 19
                    };
                    AddInlines(tb, text);
                    var cell = new Border
                    {
                        BorderBrush = Rule,
                        // 격자가 겹쳐 두꺼워지지 않도록 왼쪽·위 선만 그리고 바깥은 아래에서 채운다.
                        BorderThickness = new Thickness(c == 0 ? 1 : 0, r == 0 ? 1 : 0, 1, 1),
                        Background = r == 0 ? HeadBg : Brushes.Transparent,
                        Padding = new Thickness(8, 5, 8, 5),
                        Child = tb
                    };
                    Grid.SetRow(cell, r);
                    Grid.SetColumn(cell, c);
                    grid.Children.Add(cell);
                }
            }
            return grid;
        }

        private static UIElement CodeBlock(string text)
        {
            return new Border
            {
                Background = CodeBg,
                BorderBrush = Rule,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 2, 0, 12),
                Child = new TextBox
                {
                    Text = text,
                    IsReadOnly = true,
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    FontFamily = CodeFont,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    // 명령줄은 그대로 복사해 쓰는 일이 많아 선택할 수 있게 TextBox 로 둔다.
                    IsReadOnlyCaretVisible = false
                }
            };
        }

        private static TextBlock Body(string text, Thickness margin, Brush fg = null)
        {
            var tb = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = fg ?? Ink,
                LineHeight = 21,
                Margin = margin
            };
            AddInlines(tb, text);
            return tb;
        }

        private static readonly Regex InlineRe =
            new Regex(@"\*\*(?<b>.+?)\*\*|`(?<c>[^`]+)`|\[(?<t>[^\]]+)\]\((?<u>[^)]*)\)");
        private static readonly Brush CodeInk = new SolidColorBrush(Color.FromRgb(0xC9, 0x2A, 0x2A));

        /// <summary>
        /// 줄 안의 **굵게**·`코드`·[글자](링크)를 반영해 Inline 을 채운다(링크는 글자만 남긴다).
        /// 굵게 안에 코드가 든 경우(**`svg-out`을 …**)가 실제로 있으므로 재귀로 처리한다 —
        /// 그러지 않으면 백틱이 글자 그대로 보인다.
        /// </summary>
        private static void AddInlines(TextBlock tb, string text, bool bold = false)
        {
            int pos = 0;
            foreach (Match m in InlineRe.Matches(text))
            {
                if (m.Index > pos) tb.Inlines.Add(Styled(text.Substring(pos, m.Index - pos), bold, false));
                if (m.Groups["b"].Success) AddInlines(tb, m.Groups["b"].Value, true);
                else if (m.Groups["c"].Success) tb.Inlines.Add(Styled(m.Groups["c"].Value, bold, true));
                else tb.Inlines.Add(new Run(m.Groups["t"].Value) { Foreground = QuoteBar, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal });
                pos = m.Index + m.Length;
            }
            if (pos < text.Length) tb.Inlines.Add(Styled(text.Substring(pos), bold, false));
        }

        private static Run Styled(string s, bool bold, bool code)
        {
            var run = new Run(s);
            if (bold) run.FontWeight = FontWeights.Bold;
            if (code) { run.FontFamily = CodeFont; run.Background = CodeBg; run.Foreground = CodeInk; }
            return run;
        }
    }
}
