// ============================================================
// 文件：GCodeParserV2.cs
// 用途：扩展G代码解析器V2 — 支持圆弧插补、坐标系选择、模态状态
// 设计思路：
//   在原有G代码解析器基础上扩展，新增以下功能：
//   - G2/G3 圆弧插补（顺时针/逆时针），支持I/J偏移法和R半径法
//   - G4 暂停（Dwell）
//   - G17/G18/G19 平面选择
//   - G54~G59 工件坐标系选择
//   - G90/G91 绝对/增量坐标模式
//   - M30 程序结束并复位
//   解析器维护模态状态，使得后续行可省略已设定的模态参数。
// ============================================================

using SmartMES.Core.Models;

namespace SmartMES.Modules.MotionControl
{
    /// <summary>
    /// 平面选择枚举 — 决定圆弧插补在哪个平面进行。
    /// </summary>
    public enum WorkPlane
    {
        /// <summary>XY 平面（默认，G17）。</summary>
        XY,
        /// <summary>XZ 平面（G18）。</summary>
        XZ,
        /// <summary>YZ 平面（G19）。</summary>
        YZ
    }

    /// <summary>
    /// 解析后的G代码指令 — 一行G代码解析的结果。
    /// </summary>
    public class GCodeCommand
    {
        /// <summary>G代码编号（0=G0, 1=G1, 2=G2, ...）。-1 表示非G指令。</summary>
        public int GCode { get; set; } = -1;

        /// <summary>M代码编号（30=M30）。-1 表示非M指令。</summary>
        public int MCode { get; set; } = -1;

        /// <summary>所有参数的字典（X, Y, Z, I, J, K, R, F, P 等）。</summary>
        public Dictionary<char, double> Parameters { get; set; } = new();

        /// <summary>是否为圆弧指令（G2或G3）。</summary>
        public bool IsArc => GCode == 2 || GCode == 3;

        /// <summary>是否为顺时针圆弧（G2）。</summary>
        public bool IsClockwise => GCode == 2;

        /// <summary>原始文本。</summary>
        public string RawText { get; set; } = string.Empty;

        /// <summary>行号。</summary>
        public int LineNumber { get; set; }
    }

    /// <summary>
    /// 解析器模态状态 — 跟踪当前的坐标模式、坐标系、平面等。
    /// G代码是"模态"的，一旦设定某模式，后续行自动继承。
    /// </summary>
    public class GCodeModalState
    {
        /// <summary>绝对坐标模式（true=G90绝对, false=G91增量）。</summary>
        public bool IsAbsolute { get; set; } = true;

        /// <summary>当前工件坐标系。</summary>
        public CoordinateSystem CurrentCoordinateSystem { get; set; } = CoordinateSystem.Machine;

        /// <summary>当前工作平面。</summary>
        public WorkPlane CurrentPlane { get; set; } = WorkPlane.XY;

        /// <summary>当前进给速率（mm/min）。</summary>
        public double FeedRate { get; set; } = 1000;

        /// <summary>当前运动模式（0=快速, 1=直线, 2=顺弧, 3=逆弧）。</summary>
        public int MotionMode { get; set; } = 0;

        /// <summary>程序是否结束。</summary>
        public bool ProgramEnded { get; set; }
    }

    /// <summary>
    /// 扩展G代码解析器V2 — 解析G代码文本并生成插补点序列。
    ///
    /// 支持的指令：
    ///   G0  — 快速定位
    ///   G1  — 直线插补
    ///   G2  — 顺时针圆弧插补
    ///   G3  — 逆时针圆弧插补
    ///   G4  — 暂停（P参数为秒数）
    ///   G17 — XY平面选择
    ///   G18 — XZ平面选择
    ///   G19 — YZ平面选择
    ///   G28 — 回零（回机械原点）
    ///   G54~G59 — 工件坐标系选择
    ///   G90 — 绝对坐标模式
    ///   G91 — 增量坐标模式
    ///   M30 — 程序结束并复位
    /// </summary>
    public class GCodeParserV2
    {
        // 圆弧插补器 — 将圆弧指令转换为微线段
        private readonly CircularInterpolator _arcInterpolator = new();

        // 坐标系管理器 — 用于坐标变换
        private readonly ICoordinateManager? _coordinateManager;

        /// <summary>当前模态状态（外部可读取）。</summary>
        public GCodeModalState ModalState { get; } = new();

        /// <summary>当前位置（解析过程中跟踪）。</summary>
        public Dictionary<string, double> CurrentPosition { get; } = new()
        {
            ["X"] = 0, ["Y"] = 0, ["Z"] = 0
        };

        /// <summary>日志事件。</summary>
        public event EventHandler<string>? MessageLogged;

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="coordinateManager">坐标系管理器（可选，传null则不做坐标变换）。</param>
        public GCodeParserV2(ICoordinateManager? coordinateManager = null)
        {
            _coordinateManager = coordinateManager;
        }

        /// <summary>
        /// 解析一行G代码文本为GCodeCommand对象。
        /// </summary>
        /// <param name="line">G代码文本行。</param>
        /// <param name="lineNumber">行号（用于错误定位）。</param>
        /// <returns>解析后的指令对象。</returns>
        public GCodeCommand ParseLine(string line, int lineNumber = 0)
        {
            var cmd = new GCodeCommand { RawText = line, LineNumber = lineNumber };

            // 去除注释（分号和括号注释）
            string cleaned = RemoveComments(line).Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(cleaned)) return cmd;

            // 提取所有参数字母+数值
            var tokens = TokenizeLine(cleaned);

            foreach (var (letter, value) in tokens)
            {
                switch (letter)
                {
                    case 'G':
                        cmd.GCode = (int)value;
                        break;
                    case 'M':
                        cmd.MCode = (int)value;
                        break;
                    default:
                        cmd.Parameters[letter] = value;
                        break;
                }
            }

            return cmd;
        }

        /// <summary>
        /// 解析完整的G代码程序，生成插补点序列。
        /// 这是主入口方法，处理所有模态切换和坐标变换。
        /// </summary>
        /// <param name="program">G代码程序文本（多行）。</param>
        /// <returns>插补点序列。</returns>
        public List<InterpolationPoint> ParseProgram(string program)
        {
            var points = new List<InterpolationPoint>();
            var lines = program.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // 重置模态状态
            ModalState.IsAbsolute = true;
            ModalState.CurrentCoordinateSystem = CoordinateSystem.Machine;
            ModalState.CurrentPlane = WorkPlane.XY;
            ModalState.MotionMode = 0;
            ModalState.ProgramEnded = false;

            for (int i = 0; i < lines.Length; i++)
            {
                if (ModalState.ProgramEnded) break;

                var cmd = ParseLine(lines[i], i + 1);
                var result = ProcessCommand(cmd);
                points.AddRange(result);
            }

            return points;
        }

        /// <summary>
        /// 处理单条G代码指令，生成零或多个插补点。
        /// </summary>
        private List<InterpolationPoint> ProcessCommand(GCodeCommand cmd)
        {
            var points = new List<InterpolationPoint>();

            // 处理G代码
            if (cmd.GCode >= 0)
            {
                switch (cmd.GCode)
                {
                    case 0: // G0 快速定位
                        ModalState.MotionMode = 0;
                        points.AddRange(ProcessLinearMove(cmd, isRapid: true));
                        break;

                    case 1: // G1 直线插补
                        ModalState.MotionMode = 1;
                        points.AddRange(ProcessLinearMove(cmd, isRapid: false));
                        break;

                    case 2: // G2 顺时针圆弧
                        ModalState.MotionMode = 2;
                        points.AddRange(ProcessArcMove(cmd, clockwise: true));
                        break;

                    case 3: // G3 逆时针圆弧
                        ModalState.MotionMode = 3;
                        points.AddRange(ProcessArcMove(cmd, clockwise: false));
                        break;

                    case 4: // G4 暂停
                        ProcessDwell(cmd);
                        break;

                    case 17: // G17 XY平面
                        ModalState.CurrentPlane = WorkPlane.XY;
                        Log($"[G代码] 切换到 XY 平面 (G17)");
                        break;

                    case 18: // G18 XZ平面
                        ModalState.CurrentPlane = WorkPlane.XZ;
                        Log($"[G代码] 切换到 XZ 平面 (G18)");
                        break;

                    case 19: // G19 YZ平面
                        ModalState.CurrentPlane = WorkPlane.YZ;
                        Log($"[G代码] 切换到 YZ 平面 (G19)");
                        break;

                    case 28: // G28 回零
                        points.AddRange(ProcessHome(cmd));
                        break;

                    case 54: ModalState.CurrentCoordinateSystem = CoordinateSystem.Work1;
                        Log("[G代码] 选择工件坐标系 G54"); break;
                    case 55: ModalState.CurrentCoordinateSystem = CoordinateSystem.Work2;
                        Log("[G代码] 选择工件坐标系 G55"); break;
                    case 56: ModalState.CurrentCoordinateSystem = CoordinateSystem.Work3;
                        Log("[G代码] 选择工件坐标系 G56"); break;
                    case 57: ModalState.CurrentCoordinateSystem = CoordinateSystem.Work4;
                        Log("[G代码] 选择工件坐标系 G57"); break;
                    case 58: ModalState.CurrentCoordinateSystem = CoordinateSystem.Work5;
                        Log("[G代码] 选择工件坐标系 G58"); break;
                    case 59: ModalState.CurrentCoordinateSystem = CoordinateSystem.Work6;
                        Log("[G代码] 选择工件坐标系 G59"); break;

                    case 90: // G90 绝对坐标
                        ModalState.IsAbsolute = true;
                        Log("[G代码] 切换到绝对坐标模式 (G90)");
                        break;

                    case 91: // G91 增量坐标
                        ModalState.IsAbsolute = false;
                        Log("[G代码] 切换到增量坐标模式 (G91)");
                        break;
                }
            }

            // 处理M代码
            if (cmd.MCode >= 0)
            {
                switch (cmd.MCode)
                {
                    case 30: // M30 程序结束并复位
                        ModalState.ProgramEnded = true;
                        Log("[G代码] 程序结束 (M30)");
                        break;
                }
            }

            // 更新进给速率（F参数在任何行都可以出现）
            if (cmd.Parameters.ContainsKey('F'))
            {
                ModalState.FeedRate = cmd.Parameters['F'];
            }

            return points;
        }

        /// <summary>
        /// 处理直线移动（G0/G1）— 生成一个目标插补点。
        /// </summary>
        private List<InterpolationPoint> ProcessLinearMove(GCodeCommand cmd, bool isRapid)
        {
            var points = new List<InterpolationPoint>();
            var target = CalculateTargetPosition(cmd);

            // 如果目标与当前位置相同，不生成点
            if (Math.Abs(target["X"] - CurrentPosition["X"]) < 0.0001 &&
                Math.Abs(target["Y"] - CurrentPosition["Y"]) < 0.0001 &&
                Math.Abs(target["Z"] - CurrentPosition["Z"]) < 0.0001)
                return points;

            var point = new InterpolationPoint
            {
                Type = InterpolationType.Linear,
                FeedRate = isRapid ? 0 : ModalState.FeedRate / 60.0 // F是mm/min，转为mm/s
            };
            point.AxisTargets["X"] = target["X"];
            point.AxisTargets["Y"] = target["Y"];
            point.AxisTargets["Z"] = target["Z"];

            // 应用坐标变换
            ApplyCoordinateTransform(point);

            // 更新当前位置
            CurrentPosition["X"] = target["X"];
            CurrentPosition["Y"] = target["Y"];
            CurrentPosition["Z"] = target["Z"];

            points.Add(point);
            return points;
        }

        /// <summary>
        /// 处理圆弧移动（G2/G3）— 使用CircularInterpolator生成微线段序列。
        /// </summary>
        private List<InterpolationPoint> ProcessArcMove(GCodeCommand cmd, bool clockwise)
        {
            var target = CalculateTargetPosition(cmd);
            double feedRate = ModalState.FeedRate / 60.0;

            // 构建圆弧参数
            var arcParams = new ArcParameters();

            if (cmd.Parameters.ContainsKey('R'))
            {
                // R半径法
                arcParams.R = cmd.Parameters['R'];
                arcParams.UseRadius = true;
            }
            else
            {
                // I/J偏移法
                arcParams.I = cmd.Parameters.GetValueOrDefault('I', 0);
                arcParams.J = cmd.Parameters.GetValueOrDefault('J', 0);
                arcParams.UseRadius = false;
            }

            // 使用圆弧插补器生成微线段
            var arcPoints = _arcInterpolator.Interpolate(
                CurrentPosition["X"], CurrentPosition["Y"],
                target["X"], target["Y"],
                arcParams, clockwise, feedRate);

            // 为每个圆弧点添加Z轴（圆弧插补在XY平面，Z轴线性插补）
            double zStart = CurrentPosition["Z"];
            double zEnd = target["Z"];
            for (int i = 0; i < arcPoints.Count; i++)
            {
                double t = arcPoints.Count > 1 ? (double)i / (arcPoints.Count - 1) : 1.0;
                arcPoints[i].AxisTargets["Z"] = zStart + t * (zEnd - zStart);

                // 应用坐标变换
                ApplyCoordinateTransform(arcPoints[i]);
            }

            // 更新当前位置
            CurrentPosition["X"] = target["X"];
            CurrentPosition["Y"] = target["Y"];
            CurrentPosition["Z"] = target["Z"];

            return arcPoints;
        }

        /// <summary>
        /// 处理暂停指令（G4）— 记录暂停时间。
        /// </summary>
        private void ProcessDwell(GCodeCommand cmd)
        {
            double seconds = cmd.Parameters.GetValueOrDefault('P', 1.0);
            Log($"[G代码] 暂停 {seconds} 秒 (G4)");
        }

        /// <summary>
        /// 处理回零指令（G28）— 生成移动到零点的插补点。
        /// </summary>
        private List<InterpolationPoint> ProcessHome(GCodeCommand cmd)
        {
            Log("[G代码] 回零 (G28)");

            var point = new InterpolationPoint { Type = InterpolationType.Linear, FeedRate = 0 };
            point.AxisTargets["X"] = 0;
            point.AxisTargets["Y"] = 0;
            point.AxisTargets["Z"] = 0;

            CurrentPosition["X"] = 0;
            CurrentPosition["Y"] = 0;
            CurrentPosition["Z"] = 0;

            return new List<InterpolationPoint> { point };
        }

        /// <summary>
        /// 根据绝对/增量模式计算目标位置。
        /// 绝对模式：目标 = 参数值
        /// 增量模式：目标 = 当前位置 + 参数值
        /// </summary>
        private Dictionary<string, double> CalculateTargetPosition(GCodeCommand cmd)
        {
            var target = new Dictionary<string, double>(CurrentPosition);

            foreach (var axis in new[] { ('X', "X"), ('Y', "Y"), ('Z', "Z") })
            {
                if (cmd.Parameters.ContainsKey(axis.Item1))
                {
                    double value = cmd.Parameters[axis.Item1];
                    target[axis.Item2] = ModalState.IsAbsolute
                        ? value                                    // 绝对模式
                        : CurrentPosition[axis.Item2] + value;     // 增量模式
                }
            }

            return target;
        }

        /// <summary>
        /// 应用坐标系变换 — 将工件坐标转换为机械坐标。
        /// 如果当前坐标系不是机械坐标系，且有坐标管理器，则进行变换。
        /// </summary>
        private void ApplyCoordinateTransform(InterpolationPoint point)
        {
            if (_coordinateManager == null) return;
            if (ModalState.CurrentCoordinateSystem == CoordinateSystem.Machine) return;

            double wx = point.AxisTargets.GetValueOrDefault("X");
            double wy = point.AxisTargets.GetValueOrDefault("Y");
            double wz = point.AxisTargets.GetValueOrDefault("Z");

            // 工件坐标 → 机械坐标
            var (mx, my, mz) = _coordinateManager.WorkToMachine(
                wx, wy, wz, ModalState.CurrentCoordinateSystem);

            point.AxisTargets["X"] = mx;
            point.AxisTargets["Y"] = my;
            point.AxisTargets["Z"] = mz;
        }

        // ========== 文本解析辅助方法 ==========

        /// <summary>
        /// 移除G代码中的注释。
        /// 支持两种注释格式：
        ///   - 分号注释：; 后面的内容
        ///   - 括号注释：(注释内容)
        /// </summary>
        private static string RemoveComments(string line)
        {
            // 移除分号注释
            int semiIdx = line.IndexOf(';');
            if (semiIdx >= 0) line = line[..semiIdx];

            // 移除括号注释
            while (true)
            {
                int open = line.IndexOf('(');
                int close = line.IndexOf(')');
                if (open >= 0 && close > open)
                    line = line[..open] + line[(close + 1)..];
                else
                    break;
            }

            return line;
        }

        /// <summary>
        /// 将G代码行分词为(字母, 数值)对列表。
        /// 例如 "G1 X100.5 Y-50 F2000" → [('G',1), ('X',100.5), ('Y',-50), ('F',2000)]
        /// </summary>
        private static List<(char Letter, double Value)> TokenizeLine(string line)
        {
            var tokens = new List<(char, double)>();
            int i = 0;

            while (i < line.Length)
            {
                // 跳过空白
                if (char.IsWhiteSpace(line[i])) { i++; continue; }

                // 查找字母
                if (char.IsLetter(line[i]))
                {
                    char letter = line[i];
                    i++;

                    // 提取后续数值字符串
                    int numStart = i;
                    while (i < line.Length && (char.IsDigit(line[i]) || line[i] == '.' ||
                           line[i] == '-' || line[i] == '+'))
                    {
                        i++;
                    }

                    if (numStart < i && double.TryParse(line[numStart..i],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double value))
                    {
                        tokens.Add((letter, value));
                    }
                }
                else
                {
                    i++; // 跳过未识别字符
                }
            }

            return tokens;
        }

        private void Log(string message)
        {
            MessageLogged?.Invoke(this, message);
        }
    }
}
