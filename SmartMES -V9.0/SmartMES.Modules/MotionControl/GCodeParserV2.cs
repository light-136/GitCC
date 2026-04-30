// ============================================================
// 文件：GCodeParserV2.cs
// 用途：扩展G代码解析器V2 — 支持圆弧/坐标系/增量模式
// 设计思路：
//   在 V1 解析器（TenAxisController 中的简单解析）基础上，
//   V2 解析器增加对以下 G 代码的支持：
//
//   运动指令：
//   - G0/G1    快速移动/直线插补（V1已有）
//   - G2/G3    顺时针/逆时针圆弧插补（支持 I/J 和 R 两种模式）
//   - G4       暂停（Dwell），参数 P=毫秒 或 S=秒
//   - G28      回零
//
//   坐标系选择：
//   - G54~G59  选择工件坐标系 1~6
//
//   平面选择：
//   - G17      XY 平面（圆弧插补默认平面）
//   - G18      XZ 平面
//   - G19      YZ 平面
//
//   模态指令：
//   - G90      绝对坐标模式
//   - G91      增量坐标模式
//
//   辅助指令：
//   - M0       程序暂停
//   - M2/M30   程序结束
//
//   解析器维护模态状态（当前运动模式、坐标模式、坐标系），
//   使后续行可以省略模态代码（如连续 G1 运动只需写一次 G1）。
// ============================================================

using SmartMES.Core.Models;

namespace SmartMES.Modules.MotionControl
{
    /// <summary>
    /// V2 G代码指令类型 — 扩展支持圆弧和坐标系。
    /// </summary>
    public enum GCodeTypeV2
    {
        /// <summary>快速移动。</summary>
        G0,
        /// <summary>直线插补。</summary>
        G1,
        /// <summary>顺时针圆弧插补。</summary>
        G2,
        /// <summary>逆时针圆弧插补。</summary>
        G3,
        /// <summary>暂停（Dwell）。</summary>
        G4,
        /// <summary>XY 平面选择。</summary>
        G17,
        /// <summary>XZ 平面选择。</summary>
        G18,
        /// <summary>YZ 平面选择。</summary>
        G19,
        /// <summary>回零。</summary>
        G28,
        /// <summary>工件坐标系 G54~G59。</summary>
        G54, G55, G56, G57, G58, G59,
        /// <summary>绝对坐标模式。</summary>
        G90,
        /// <summary>增量坐标模式。</summary>
        G91,
        /// <summary>程序暂停。</summary>
        M0,
        /// <summary>程序结束。</summary>
        M2,
        /// <summary>程序结束并复位。</summary>
        M30,
        /// <summary>未知指令。</summary>
        Unknown
    }

    /// <summary>
    /// V2 G代码指令 — 包含解析后的完整指令信息。
    /// </summary>
    public class GCodeCommandV2
    {
        /// <summary>指令类型。</summary>
        public GCodeTypeV2 Type { get; set; } = GCodeTypeV2.Unknown;

        /// <summary>轴目标位置（X/Y/Z/A/B/C/U/V/W/S）。</summary>
        public Dictionary<string, double> AxisPositions { get; set; } = new();

        /// <summary>进给速率 F（mm/min）。</summary>
        public double FeedRate { get; set; }

        /// <summary>圆弧参数 I — 圆心 X 偏移。</summary>
        public double? I { get; set; }

        /// <summary>圆弧参数 J — 圆心 Y 偏移。</summary>
        public double? J { get; set; }

        /// <summary>圆弧参数 K — 圆心 Z 偏移。</summary>
        public double? K { get; set; }

        /// <summary>圆弧参数 R — 半径。</summary>
        public double? R { get; set; }

        /// <summary>暂停时间参数 P（毫秒）。</summary>
        public double? P { get; set; }

        /// <summary>暂停时间参数 S（秒）。</summary>
        public double? DwellSeconds { get; set; }

        /// <summary>原始行文本。</summary>
        public string Raw { get; set; } = string.Empty;

        /// <summary>行号。</summary>
        public int LineNumber { get; set; }

        /// <summary>是否为绝对坐标模式下的指令。</summary>
        public bool IsAbsolute { get; set; } = true;

        /// <summary>当前坐标系。</summary>
        public CoordinateSystem CoordinateSystem { get; set; } = CoordinateSystem.Machine;
    }

    /// <summary>
    /// V2 G代码解析器 — 支持模态状态、圆弧插补和坐标系切换。
    ///
    /// 使用示例：
    ///   var parser = new GCodeParserV2();
    ///   var commands = parser.Parse(gcodeText);
    ///   foreach (var cmd in commands)
    ///   {
    ///       Console.WriteLine($"{cmd.Type} → {string.Join(", ", cmd.AxisPositions)}");
    ///   }
    /// </summary>
    public class GCodeParserV2
    {
        // ========== 模态状态 ==========
        // 这些状态在解析过程中持续保持，直到被新指令改变

        /// <summary>当前运动模式（G0/G1/G2/G3）— 模态保持。</summary>
        private GCodeTypeV2 _currentMotionMode = GCodeTypeV2.G1;

        /// <summary>当前坐标模式 — true=绝对(G90)，false=增量(G91)。</summary>
        private bool _isAbsolute = true;

        /// <summary>当前坐标系。</summary>
        private CoordinateSystem _currentCS = CoordinateSystem.Machine;

        /// <summary>当前进给速率（mm/min）。</summary>
        private double _currentFeedRate = 1000.0;

        /// <summary>当前平面选择（用于圆弧插补）。</summary>
        private GCodeTypeV2 _currentPlane = GCodeTypeV2.G17;

        /// <summary>日志事件。</summary>
        public event EventHandler<string>? MessageLogged;

        /// <summary>
        /// 解析 G 代码文本，返回指令列表。
        /// </summary>
        /// <param name="program">G 代码程序文本。</param>
        /// <returns>解析后的指令列表。</returns>
        public List<GCodeCommandV2> Parse(string program)
        {
            var commands = new List<GCodeCommandV2>();
            if (string.IsNullOrWhiteSpace(program)) return commands;

            // 重置模态状态
            _currentMotionMode = GCodeTypeV2.G1;
            _isAbsolute = true;
            _currentCS = CoordinateSystem.Machine;
            _currentFeedRate = 1000.0;
            _currentPlane = GCodeTypeV2.G17;

            var lines = program.Split('\n');
            int lineNum = 0;

            foreach (var rawLine in lines)
            {
                lineNum++;

                // 去除注释（; 或 () 注释）和空白
                string line = StripComments(rawLine).Trim();
                if (string.IsNullOrEmpty(line)) continue;

                // 跳过行号 N
                if (line.StartsWith("N", StringComparison.OrdinalIgnoreCase))
                {
                    int spaceIdx = line.IndexOf(' ');
                    if (spaceIdx > 0)
                        line = line.Substring(spaceIdx).Trim();
                    else
                        continue;
                }

                var cmd = ParseLine(line, lineNum);
                if (cmd != null)
                    commands.Add(cmd);
            }

            return commands;
        }

        /// <summary>
        /// 去除 G 代码行中的注释。
        /// 支持分号注释和括号注释。
        /// </summary>
        private static string StripComments(string line)
        {
            // 分号注释：从 ; 到行尾
            int semicolonIdx = line.IndexOf(';');
            if (semicolonIdx >= 0)
                line = line.Substring(0, semicolonIdx);

            // 括号注释：(...)
            int parenStart;
            while ((parenStart = line.IndexOf('(')) >= 0)
            {
                int parenEnd = line.IndexOf(')', parenStart);
                if (parenEnd >= 0)
                    line = line.Remove(parenStart, parenEnd - parenStart + 1);
                else
                    line = line.Substring(0, parenStart);
            }

            return line;
        }

        /// <summary>
        /// 解析单行 G 代码。
        /// </summary>
        private GCodeCommandV2? ParseLine(string line, int lineNum)
        {
            var tokens = Tokenize(line);
            if (tokens.Count == 0) return null;

            var cmd = new GCodeCommandV2
            {
                Raw = line,
                LineNumber = lineNum,
                FeedRate = _currentFeedRate,
                IsAbsolute = _isAbsolute,
                CoordinateSystem = _currentCS
            };

            GCodeTypeV2? explicitType = null;

            foreach (var (letter, value) in tokens)
            {
                switch (letter)
                {
                    // ===== G 指令 =====
                    case 'G':
                        int gCode = (int)value;
                        switch (gCode)
                        {
                            case 0:
                                explicitType = GCodeTypeV2.G0;
                                _currentMotionMode = GCodeTypeV2.G0;
                                break;
                            case 1:
                                explicitType = GCodeTypeV2.G1;
                                _currentMotionMode = GCodeTypeV2.G1;
                                break;
                            case 2:
                                explicitType = GCodeTypeV2.G2;
                                _currentMotionMode = GCodeTypeV2.G2;
                                break;
                            case 3:
                                explicitType = GCodeTypeV2.G3;
                                _currentMotionMode = GCodeTypeV2.G3;
                                break;
                            case 4:
                                explicitType = GCodeTypeV2.G4;
                                break;
                            case 17:
                                explicitType = GCodeTypeV2.G17;
                                _currentPlane = GCodeTypeV2.G17;
                                break;
                            case 18:
                                explicitType = GCodeTypeV2.G18;
                                _currentPlane = GCodeTypeV2.G18;
                                break;
                            case 19:
                                explicitType = GCodeTypeV2.G19;
                                _currentPlane = GCodeTypeV2.G19;
                                break;
                            case 28:
                                explicitType = GCodeTypeV2.G28;
                                break;
                            case 54:
                                explicitType = GCodeTypeV2.G54;
                                _currentCS = CoordinateSystem.Work1;
                                break;
                            case 55:
                                explicitType = GCodeTypeV2.G55;
                                _currentCS = CoordinateSystem.Work2;
                                break;
                            case 56:
                                explicitType = GCodeTypeV2.G56;
                                _currentCS = CoordinateSystem.Work3;
                                break;
                            case 57:
                                explicitType = GCodeTypeV2.G57;
                                _currentCS = CoordinateSystem.Work4;
                                break;
                            case 58:
                                explicitType = GCodeTypeV2.G58;
                                _currentCS = CoordinateSystem.Work5;
                                break;
                            case 59:
                                explicitType = GCodeTypeV2.G59;
                                _currentCS = CoordinateSystem.Work6;
                                break;
                            case 90:
                                explicitType = GCodeTypeV2.G90;
                                _isAbsolute = true;
                                break;
                            case 91:
                                explicitType = GCodeTypeV2.G91;
                                _isAbsolute = false;
                                break;
                        }
                        cmd.IsAbsolute = _isAbsolute;
                        cmd.CoordinateSystem = _currentCS;
                        break;

                    // ===== M 指令 =====
                    case 'M':
                        int mCode = (int)value;
                        switch (mCode)
                        {
                            case 0:
                                explicitType = GCodeTypeV2.M0;
                                break;
                            case 2:
                                explicitType = GCodeTypeV2.M2;
                                break;
                            case 30:
                                explicitType = GCodeTypeV2.M30;
                                break;
                        }
                        break;

                    // ===== 轴坐标 =====
                    case 'X': cmd.AxisPositions["X"] = value; break;
                    case 'Y': cmd.AxisPositions["Y"] = value; break;
                    case 'Z': cmd.AxisPositions["Z"] = value; break;
                    case 'A': cmd.AxisPositions["A"] = value; break;
                    case 'B': cmd.AxisPositions["B"] = value; break;
                    case 'C': cmd.AxisPositions["C"] = value; break;
                    case 'U': cmd.AxisPositions["U"] = value; break;
                    case 'V': cmd.AxisPositions["V"] = value; break;
                    case 'W': cmd.AxisPositions["W"] = value; break;

                    // ===== 圆弧参数 =====
                    case 'I': cmd.I = value; break;
                    case 'J': cmd.J = value; break;
                    case 'K': cmd.K = value; break;
                    case 'R': cmd.R = value; break;

                    // ===== 进给与暂停 =====
                    case 'F':
                        cmd.FeedRate = value;
                        _currentFeedRate = value;
                        break;
                    case 'P':
                        cmd.P = value;
                        break;
                    case 'S':
                        // S 在 G4 上下文中是暂停秒数，否则是主轴/S轴
                        if (explicitType == GCodeTypeV2.G4)
                            cmd.DwellSeconds = value;
                        else
                            cmd.AxisPositions["S"] = value;
                        break;
                }
            }

            // 确定最终指令类型
            if (explicitType != null)
            {
                cmd.Type = explicitType.Value;
            }
            else if (cmd.AxisPositions.Count > 0 || cmd.I != null || cmd.J != null || cmd.R != null)
            {
                // 没有显式 G 代码但有坐标参数 → 使用当前模态运动模式
                cmd.Type = _currentMotionMode;
            }
            else
            {
                return null;
            }

            return cmd;
        }

        /// <summary>
        /// 词法分析 — 将 G 代码行拆分为 (字母, 数值) 对。
        /// 例如 "G1 X10.5 Y-3.2 F1000" → [('G',1), ('X',10.5), ('Y',-3.2), ('F',1000)]
        /// </summary>
        private static List<(char Letter, double Value)> Tokenize(string line)
        {
            var tokens = new List<(char, double)>();
            line = line.ToUpperInvariant().Trim();

            int i = 0;
            while (i < line.Length)
            {
                // 跳过空白
                if (char.IsWhiteSpace(line[i])) { i++; continue; }

                // 必须是字母开头
                if (!char.IsLetter(line[i])) { i++; continue; }

                char letter = line[i];
                i++;

                // 读取数值部分（可包含负号和小数点）
                int start = i;
                if (i < line.Length && (line[i] == '-' || line[i] == '+')) i++;
                while (i < line.Length && (char.IsDigit(line[i]) || line[i] == '.')) i++;

                if (start < i && double.TryParse(line.AsSpan(start, i - start),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double val))
                {
                    tokens.Add((letter, val));
                }
                else
                {
                    tokens.Add((letter, 0));
                }
            }

            return tokens;
        }

        /// <summary>
        /// 获取 V2 样例 G 代码程序 — 包含圆弧、坐标系切换和增量模式。
        /// </summary>
        public static string GetSampleProgramV2() =>
@"; SmartMES V2 扩展G代码示例程序
; 支持圆弧插补、坐标系切换、增量模式
G90                       ; 绝对坐标模式
G54                       ; 选择工件坐标系 1
G17                       ; XY 平面

G28                       ; 回零
G0 X0 Y0 Z50             ; 快速移动到安全高度

; === 直线插补 ===
G1 X50 Y0 Z0 F2000       ; 直线插补到 (50,0,0)
G1 X50 Y50               ; 继续到 (50,50)

; === 圆弧插补（I/J模式） ===
G2 X50 Y0 I0 J-25 F1500  ; 顺时针半圆，圆心在 (50,25)
G3 X0 Y0 I-25 J0         ; 逆时针四分之一圆

; === 圆弧插补（R模式） ===
G2 X30 Y30 R20 F1000     ; 顺时针圆弧，半径20mm

; === 暂停 ===
G4 P500                  ; 暂停 500 毫秒

; === 坐标系切换 ===
G55                       ; 切换到工件坐标系 2
G1 X10 Y10 F1500

; === 增量模式 ===
G91                       ; 切换到增量模式
G1 X5 Y5                 ; 增量移动 (+5,+5)
G1 X-5 Y-5               ; 增量移动 (-5,-5)
G90                       ; 切回绝对模式

G0 Z50                   ; 抬刀到安全高度
G28                       ; 回零
M30                       ; 程序结束";

        private void Log(string msg) => MessageLogged?.Invoke(this, msg);
    }
}
