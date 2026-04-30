using System.Threading;

namespace SmartMES.Modules.MotionControl
{
    /// <summary>G代码指令类型。</summary>
    public enum GCodeType
    {
        G0,
        G1,
        G28,
        M0,
        M2,
        Unknown
    }

    /// <summary>G代码指令。</summary>
    public class GCodeCommand
    {
        public GCodeType Type { get; set; } = GCodeType.Unknown;
        public Dictionary<string, double> AxisPositions { get; set; } = new();
        public double FeedRate { get; set; } = 1000.0;
        public string Raw { get; set; } = string.Empty;

        /// <summary>解析一行 G 代码。</summary>
        public static GCodeCommand Parse(string line)
        {
            var cmd = new GCodeCommand { Raw = line.Trim() };
            if (string.IsNullOrWhiteSpace(line)) return cmd;

            var parts = line.Trim().ToUpper().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return cmd;

            cmd.Type = parts[0] switch
            {
                "G0" or "G00" => GCodeType.G0,
                "G1" or "G01" => GCodeType.G1,
                "G28" => GCodeType.G28,
                "M0" or "M00" => GCodeType.M0,
                "M2" or "M02" => GCodeType.M2,
                _ => GCodeType.Unknown
            };

            var axisNames = new[] { "X", "Y", "Z", "A", "B", "C", "U", "V", "W", "S" };
            foreach (var part in parts.Skip(1))
            {
                if (part.StartsWith("F") && double.TryParse(part[1..], out var f))
                    cmd.FeedRate = f;
                else
                {
                    foreach (var axis in axisNames)
                    {
                        if (part.StartsWith(axis) && double.TryParse(part[axis.Length..], out var v))
                            cmd.AxisPositions[axis] = v;
                    }
                }
            }

            return cmd;
        }
    }

    /// <summary>程序执行结果。</summary>
    public class MotionProgramResult
    {
        public bool Success { get; set; }
        public int TotalLines { get; set; }
        public int ExecutedLines { get; set; }
        public string Message { get; set; } = string.Empty;
        public TimeSpan ElapsedTime { get; set; }
    }

    /// <summary>10轴控制器。</summary>
    public class TenAxisController
    {
        private readonly MultiAxisController _mc = new();

        private static readonly (string Name, double Vel, double Acc)[] AxisConfigs =
        {
            ("X", 500, 2000), ("Y", 500, 2000), ("Z", 300, 1500), ("A", 180, 900), ("B", 180, 900),
            ("C", 360, 1800), ("U", 200, 1000), ("V", 200, 1000), ("W", 150, 750), ("S", 100, 500),
        };

        public IReadOnlyDictionary<string, AxisController> Axes => _mc.Axes;
        public event EventHandler<string>? MessageLogged;

        private bool _programRunning;
        public bool ProgramRunning => _programRunning;

        private CancellationTokenSource _programCts = new();

        /// <summary>构造 10 轴控制器并初始化各轴。</summary>
        public TenAxisController()
        {
            foreach (var (name, vel, acc) in AxisConfigs)
                _mc.AddAxis(name, vel, acc);
            _mc.MessageLogged += (_, msg) => MessageLogged?.Invoke(this, msg);
        }

        public Task HomeAllAsync() => _mc.HomeAllAsync();

        public void EmergencyStop()
        {
            _programCts.Cancel();
            _mc.EmergencyStop();
        }

        public void ResetAll()
        {
            _programCts = new CancellationTokenSource();
            _mc.ResetAll();
        }

        /// <summary>执行 G 代码程序。</summary>
        public async Task<MotionProgramResult> RunGCodeAsync(string program, CancellationToken ct = default)
        {
            _programRunning = true;
            var start = DateTime.Now;
            var lines = program.Split('\n').Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l) && !l.StartsWith(";")).ToList();
            int executed = 0;

            try
            {
                foreach (var line in lines)
                {
                    if (ct.IsCancellationRequested) break;
                    var cmd = GCodeCommand.Parse(line);
                    MessageLogged?.Invoke(this, $"[G-CODE] {line}");

                    switch (cmd.Type)
                    {
                        case GCodeType.G28:
                            await HomeAllAsync();
                            break;

                        case GCodeType.G0:
                        case GCodeType.G1:
                            foreach (var (axis, target) in cmd.AxisPositions)
                            {
                                if (_mc.Axes.TryGetValue(axis, out var ax))
                                {
                                    ax.Velocity = cmd.Type == GCodeType.G0 ? ax.Velocity : cmd.FeedRate / 60.0;
                                    ax.MoveTo(target);
                                }
                            }

                            await Task.Run(() =>
                            {
                                var movingAxes = cmd.AxisPositions.Keys.Where(k => _mc.Axes.ContainsKey(k)).Select(k => _mc.Axes[k]).ToList();
                                while (movingAxes.Any(a => a.State == AxisState.Running))
                                {
                                    ct.ThrowIfCancellationRequested();
                                    Thread.Sleep(20);
                                }
                            }, ct);
                            break;

                        case GCodeType.M0:
                            MessageLogged?.Invoke(this, "[M0] 程序暂停，等待恢复...");
                            await Task.Delay(2000, ct).ContinueWith(_ => { });
                            break;

                        case GCodeType.M2:
                            MessageLogged?.Invoke(this, "[M2] 程序正常结束");
                            executed++;
                            goto done;
                    }

                    executed++;
                }

            done:
                return new MotionProgramResult
                {
                    Success = true,
                    TotalLines = lines.Count,
                    ExecutedLines = executed,
                    ElapsedTime = DateTime.Now - start,
                    Message = $"程序完成：{executed}/{lines.Count} 行"
                };
            }
            catch (OperationCanceledException)
            {
                return new MotionProgramResult
                {
                    Success = false,
                    TotalLines = lines.Count,
                    ExecutedLines = executed,
                    ElapsedTime = DateTime.Now - start,
                    Message = "程序被取消"
                };
            }
            finally
            {
                _programRunning = false;
            }
        }

        /// <summary>内置样例程序。</summary>
        public static string GetSampleProgram() =>
@"; SmartMES 10轴示例程序
G28
G0 X0 Y0 Z50 A0 C0
G1 X100 Y0 Z0 F3000
G1 X100 Y100 Z0 A15 C90 F2000
G1 X0 Y100 Z0 B-10 F2000
G1 X0 Y0 Z0 A0 B0 C0 F2000
G0 Z50
G1 U50 V30 W20 S100 F1500
G1 U0 V0 W0 S0 F1000
M0
G28
M2";
    }
}
