// ============================================================
// 文件：GCodeParserV2Tests.cs
// 用途：V2 G代码解析器（GCodeParserV2）单元测试
// 测试目标：
//   验证 G 代码解析器对各种指令的解析正确性，包括：
//   1. G1 直线插补指令 — 轴坐标和进给速率解析
//   2. G2 圆弧指令 — I/J 圆弧参数提取
//   3. G90/G91 模态跟踪 — 绝对/增量模式切换
//   4. G54 坐标系设置 — 工件坐标系识别
//   5. 注释剥离 — 分号和括号注释处理
//   6. 模态运动 — G1 后省略 G 代码的行继承运动模式
// 开发思路：
//   向 Parse 方法传入不同格式的 G 代码字符串，
//   检查返回的 GCodeCommandV2 对象各属性是否正确设置。
// ============================================================

using SmartMES.Modules.MotionControl;
using SmartMES.Core.Models;

namespace SmartMES.Tests
{
    /// <summary>
    /// V2 G代码解析器单元测试类。
    /// 测试 GCodeParserV2.Parse 方法的各种解析场景。
    /// </summary>
    public class GCodeParserV2Tests
    {
        /// <summary>
        /// 辅助方法：创建解析器并解析单行 G 代码。
        /// </summary>
        private static List<GCodeCommandV2> ParseProgram(string program)
        {
            var parser = new GCodeParserV2();
            return parser.Parse(program);
        }

        /// <summary>
        /// 测试1：解析 G1 直线插补指令，验证轴坐标和进给速率。
        /// 输入："G1 X10 Y20 Z-5 F1000"
        /// 预期：Type=G1, X=10, Y=20, Z=-5, FeedRate=1000
        /// </summary>
        [Fact]
        public void ParseG1_ExtractsAxes()
        {
            // 执行：解析 G1 指令
            var commands = ParseProgram("G1 X10 Y20 Z-5 F1000");

            // 断言：应解析出 1 条指令
            Assert.Single(commands);
            var cmd = commands[0];

            // 断言：指令类型应为 G1
            Assert.Equal(GCodeTypeV2.G1, cmd.Type);

            // 断言：轴坐标应正确
            Assert.True(cmd.AxisPositions.ContainsKey("X"), "应包含 X 轴坐标");
            Assert.Equal(10.0, cmd.AxisPositions["X"]);

            Assert.True(cmd.AxisPositions.ContainsKey("Y"), "应包含 Y 轴坐标");
            Assert.Equal(20.0, cmd.AxisPositions["Y"]);

            Assert.True(cmd.AxisPositions.ContainsKey("Z"), "应包含 Z 轴坐标");
            Assert.Equal(-5.0, cmd.AxisPositions["Z"]);

            // 断言：进给速率应正确
            Assert.Equal(1000.0, cmd.FeedRate);
        }

        /// <summary>
        /// 测试2：解析 G2 顺时针圆弧指令，验证 I/J 参数提取。
        /// 输入："G2 X10 Y0 I5 J0"
        /// 预期：Type=G2, I=5, J=0
        /// </summary>
        [Fact]
        public void ParseG2_ExtractsArcParams()
        {
            // 执行：解析 G2 指令
            var commands = ParseProgram("G2 X10 Y0 I5 J0");

            // 断言：应解析出 1 条指令
            Assert.Single(commands);
            var cmd = commands[0];

            // 断言：指令类型应为 G2
            Assert.Equal(GCodeTypeV2.G2, cmd.Type);

            // 断言：I/J 圆弧参数应正确
            Assert.NotNull(cmd.I);
            Assert.Equal(5.0, cmd.I!.Value);

            Assert.NotNull(cmd.J);
            Assert.Equal(0.0, cmd.J!.Value);

            // 断言：轴坐标应正确
            Assert.Equal(10.0, cmd.AxisPositions["X"]);
            Assert.Equal(0.0, cmd.AxisPositions["Y"]);
        }

        /// <summary>
        /// 测试3：G90/G91 模态跟踪测试。
        /// G91 应将 IsAbsolute 设为 false，
        /// G90 应将 IsAbsolute 设回 true。
        /// </summary>
        [Fact]
        public void G90G91_ModalTracking()
        {
            // 准备：包含 G91 和 G90 切换的程序
            string program = @"G90
G1 X10 Y10 F1000
G91
G1 X5 Y5
G90
G1 X20 Y20";

            // 执行：解析
            var commands = ParseProgram(program);

            // 断言：第一条运动指令（G1 X10 Y10）应为绝对模式
            // commands[0] = G90, commands[1] = G1 X10 Y10
            var cmd绝对1 = commands.First(c => c.Type == GCodeTypeV2.G1 && c.AxisPositions.GetValueOrDefault("X") == 10);
            Assert.True(cmd绝对1.IsAbsolute, "G90 后的指令应为绝对模式");

            // 断言：G91 后的指令应为增量模式
            var cmdG91 = commands.First(c => c.Type == GCodeTypeV2.G91);
            Assert.False(cmdG91.IsAbsolute, "G91 指令本身应标记为增量模式");

            // G91 之后的 G1 X5 Y5 应为增量模式
            var cmd增量 = commands.First(c => c.Type == GCodeTypeV2.G1 && c.AxisPositions.GetValueOrDefault("X") == 5);
            Assert.False(cmd增量.IsAbsolute, "G91 后的运动指令应为增量模式");

            // G90 切回后的 G1 X20 Y20 应为绝对模式
            var cmd绝对2 = commands.First(c => c.Type == GCodeTypeV2.G1 && c.AxisPositions.GetValueOrDefault("X") == 20);
            Assert.True(cmd绝对2.IsAbsolute, "G90 切回后的运动指令应为绝对模式");
        }

        /// <summary>
        /// 测试4：G54 坐标系设置，应正确设置 CoordinateSystem 为 Work1。
        /// </summary>
        [Fact]
        public void G54_SetsCoordinateSystem()
        {
            // 准备：包含 G54 坐标系设置的程序
            string program = @"G54
G1 X10 Y10 F1000";

            // 执行：解析
            var commands = ParseProgram(program);

            // 断言：G54 指令应存在
            var cmdG54 = commands.First(c => c.Type == GCodeTypeV2.G54);
            Assert.Equal(CoordinateSystem.Work1, cmdG54.CoordinateSystem);

            // 断言：G54 之后的运动指令应继承 Work1 坐标系
            var cmdG1 = commands.First(c => c.Type == GCodeTypeV2.G1);
            Assert.Equal(CoordinateSystem.Work1, cmdG1.CoordinateSystem);
        }

        /// <summary>
        /// 测试5：注释剥离测试。
        /// 分号注释 "; comment" 和括号注释 "(comment)" 应被正确移除，
        /// 不影响指令解析。
        /// </summary>
        [Fact]
        public void CommentStripping()
        {
            // 场景1：分号注释
            string program1 = "G1 X10 Y20 F1000 ; 这是一条注释";
            var commands1 = ParseProgram(program1);
            Assert.Single(commands1);
            Assert.Equal(GCodeTypeV2.G1, commands1[0].Type);
            Assert.Equal(10.0, commands1[0].AxisPositions["X"]);

            // 场景2：括号注释
            string program2 = "G1 X30 (这是括号注释) Y40 F500";
            var commands2 = ParseProgram(program2);
            Assert.Single(commands2);
            Assert.Equal(30.0, commands2[0].AxisPositions["X"]);
            Assert.Equal(40.0, commands2[0].AxisPositions["Y"]);

            // 场景3：纯注释行应被忽略
            string program3 = @"; 纯注释行
G1 X5 Y5 F1000
(另一条纯注释)";
            var commands3 = ParseProgram(program3);
            // 纯注释行不应产生指令，括号注释行去掉后为空也不应产生指令
            Assert.Single(commands3);
            Assert.Equal(GCodeTypeV2.G1, commands3[0].Type);
        }

        /// <summary>
        /// 测试6：模态运动测试。
        /// 在 G1 指令之后，仅包含轴坐标的行应继承 G1 运动模式。
        /// </summary>
        [Fact]
        public void ModalMotion()
        {
            // 准备：G1 后跟仅有坐标的行
            string program = @"G1 X10 Y10 F1000
X20 Y20
X30 Y30";

            // 执行：解析
            var commands = ParseProgram(program);

            // 断言：应解析出 3 条指令
            Assert.Equal(3, commands.Count);

            // 断言：所有 3 条指令的类型都应为 G1（模态继承）
            foreach (var cmd in commands)
            {
                Assert.Equal(GCodeTypeV2.G1, cmd.Type);
            }

            // 断言：第二条指令的坐标正确（X=20, Y=20）
            Assert.Equal(20.0, commands[1].AxisPositions["X"]);
            Assert.Equal(20.0, commands[1].AxisPositions["Y"]);

            // 断言：第三条指令的坐标正确（X=30, Y=30）
            Assert.Equal(30.0, commands[2].AxisPositions["X"]);
            Assert.Equal(30.0, commands[2].AxisPositions["Y"]);

            // 断言：模态进给速率也应继承
            Assert.Equal(1000.0, commands[1].FeedRate);
            Assert.Equal(1000.0, commands[2].FeedRate);
        }
    }
}
