// ============================================================
// 文件：DomainModelTests.cs
// 用途：领域模型单元测试
// 设计思路：
//   验证领域模型（POCO对象）的默认值和属性行为。
//   这些模型在系统中广泛使用，默认值的正确性直接影响
//   运动控制参数、报警判断等关键逻辑。
//
// 测试覆盖：
//   1. AxisConfig — 轴配置默认值（脉冲当量、速度、限位等）
//   2. AlarmRecord — IsCleared 计算属性逻辑
//   3. PointData — 点位数据默认值（速度、加减速度）
//   4. RecipeData — 参数字典的增删改查操作
// ============================================================

using SmartSemiCon.Domain.Enums;
using SmartSemiCon.Domain.Models;

namespace SmartSemiCon.Tests.Domain
{
    /// <summary>
    /// 领域模型测试类 — 验证各核心模型的默认值和属性行为。
    /// </summary>
    public class DomainModelTests
    {
        // =============================================
        // AxisConfig 默认值验证
        // =============================================

        /// <summary>
        /// 验证 AxisConfig 新建实例时，脉冲当量默认值为 1000.0。
        /// 脉冲当量决定了运动控制的距离换算精度。
        /// </summary>
        [Fact]
        public void AxisConfig_DefaultPulsePerUnit_ShouldBe1000()
        {
            // Arrange & Act — 创建默认轴配置
            var config = new AxisConfig();

            // Assert — 脉冲当量默认 1000 pulse/mm
            Assert.Equal(1000.0, config.PulsePerUnit);
        }

        /// <summary>
        /// 验证 AxisConfig 新建实例时，最大速度默认值为 100.0 mm/s。
        /// </summary>
        [Fact]
        public void AxisConfig_DefaultMaxVelocity_ShouldBe100()
        {
            var config = new AxisConfig();

            Assert.Equal(100.0, config.MaxVelocity);
        }

        /// <summary>
        /// 验证 AxisConfig 新建实例时，最大加速度默认值为 500.0 mm/s^2。
        /// </summary>
        [Fact]
        public void AxisConfig_DefaultMaxAcceleration_ShouldBe500()
        {
            var config = new AxisConfig();

            Assert.Equal(500.0, config.MaxAcceleration);
        }

        /// <summary>
        /// 验证 AxisConfig 的软限位默认值。
        /// 正方向 +1000mm，负方向 -1000mm，默认启用软限位保护。
        /// </summary>
        [Fact]
        public void AxisConfig_DefaultSoftLimits_ShouldBeCorrect()
        {
            var config = new AxisConfig();

            Assert.Equal(1000.0, config.SoftLimitPositive);
            Assert.Equal(-1000.0, config.SoftLimitNegative);
            Assert.True(config.SoftLimitEnabled);
        }

        /// <summary>
        /// 验证 AxisConfig 的回原点参数默认值。
        /// 默认负方向回原，回原速度 10mm/s，偏移量 0。
        /// </summary>
        [Fact]
        public void AxisConfig_DefaultHomeParams_ShouldBeCorrect()
        {
            var config = new AxisConfig();

            Assert.False(config.HomeDirection);       // 默认负方向回原
            Assert.Equal(10.0, config.HomeVelocity);  // 回原速度 10mm/s
            Assert.Equal(0.0, config.HomeOffset);     // 无偏移
        }

        /// <summary>
        /// 验证 AxisConfig 的控制卡类型默认为 Simulation（模拟卡）。
        /// </summary>
        [Fact]
        public void AxisConfig_DefaultCardType_ShouldBeSimulation()
        {
            var config = new AxisConfig();

            Assert.Equal(MotionCardType.Simulation, config.CardType);
        }

        /// <summary>
        /// 验证 AxisConfig 的名称默认为空字符串，而不是 null。
        /// </summary>
        [Fact]
        public void AxisConfig_DefaultName_ShouldBeEmptyString()
        {
            var config = new AxisConfig();

            Assert.Equal(string.Empty, config.Name);
        }

        /// <summary>
        /// 使用 Theory 参数化测试：验证不同 AxisId 设置是否正确保存。
        /// </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(5)]
        [InlineData(29)]
        public void AxisConfig_SetAxisId_ShouldRetainValue(int axisId)
        {
            var config = new AxisConfig { AxisId = axisId };

            Assert.Equal(axisId, config.AxisId);
        }

        // =============================================
        // AlarmRecord IsCleared 属性测试
        // =============================================

        /// <summary>
        /// 验证 AlarmRecord 新建时，ClearedAt 为 null，IsCleared 应为 false。
        /// 新报警一定是"未清除"状态。
        /// </summary>
        [Fact]
        public void AlarmRecord_NewInstance_IsClearedShouldBeFalse()
        {
            var alarm = new AlarmRecord();

            Assert.Null(alarm.ClearedAt);
            Assert.False(alarm.IsCleared);
        }

        /// <summary>
        /// 验证 AlarmRecord 设置 ClearedAt 后，IsCleared 应为 true。
        /// IsCleared 是基于 ClearedAt.HasValue 的计算属性。
        /// </summary>
        [Fact]
        public void AlarmRecord_WhenClearedAtSet_IsClearedShouldBeTrue()
        {
            var alarm = new AlarmRecord
            {
                ClearedAt = DateTime.Now
            };

            Assert.True(alarm.IsCleared);
        }

        /// <summary>
        /// 验证 AlarmRecord 清除后再重置为 null，IsCleared 应恢复为 false。
        /// </summary>
        [Fact]
        public void AlarmRecord_WhenClearedAtResetToNull_IsClearedShouldBeFalse()
        {
            var alarm = new AlarmRecord { ClearedAt = DateTime.Now };
            Assert.True(alarm.IsCleared);

            // 重置清除时间为 null
            alarm.ClearedAt = null;
            Assert.False(alarm.IsCleared);
        }

        /// <summary>
        /// 验证 AlarmRecord 默认字段值。
        /// Message 和 Source 应为空字符串，ClearedBy 应为 null。
        /// </summary>
        [Fact]
        public void AlarmRecord_DefaultValues_ShouldBeCorrect()
        {
            var alarm = new AlarmRecord();

            Assert.Equal(string.Empty, alarm.Message);
            Assert.Equal(string.Empty, alarm.Source);
            Assert.Null(alarm.ClearedBy);
            Assert.Equal(0, alarm.AlarmCode);
        }

        /// <summary>
        /// 使用 Theory 测试不同报警级别是否正确存储。
        /// </summary>
        [Theory]
        [InlineData(AlarmLevel.Info)]
        [InlineData(AlarmLevel.Warning)]
        [InlineData(AlarmLevel.Light)]
        [InlineData(AlarmLevel.Heavy)]
        [InlineData(AlarmLevel.Fatal)]
        public void AlarmRecord_SetLevel_ShouldRetainValue(AlarmLevel level)
        {
            var alarm = new AlarmRecord { Level = level };

            Assert.Equal(level, alarm.Level);
        }

        // =============================================
        // PointData 默认值验证
        // =============================================

        /// <summary>
        /// 验证 PointData 的运动速度默认值为 50.0 mm/s。
        /// </summary>
        [Fact]
        public void PointData_DefaultVelocity_ShouldBe50()
        {
            var point = new PointData();

            Assert.Equal(50.0, point.Velocity);
        }

        /// <summary>
        /// 验证 PointData 的加速度和减速度默认值均为 200.0 mm/s^2。
        /// </summary>
        [Fact]
        public void PointData_DefaultAcceleration_ShouldBe200()
        {
            var point = new PointData();

            Assert.Equal(200.0, point.Acceleration);
            Assert.Equal(200.0, point.Deceleration);
        }

        /// <summary>
        /// 验证 PointData 的位置和名称默认值。
        /// 位置默认为 0，名称默认为空字符串。
        /// </summary>
        [Fact]
        public void PointData_DefaultPositionAndName_ShouldBeZeroAndEmpty()
        {
            var point = new PointData();

            Assert.Equal(0.0, point.Position);
            Assert.Equal(string.Empty, point.Name);
        }

        /// <summary>
        /// 使用 Theory 测试 PointData 设置不同位置值是否正确保存。
        /// </summary>
        [Theory]
        [InlineData(0.0)]
        [InlineData(100.5)]
        [InlineData(-200.75)]
        public void PointData_SetPosition_ShouldRetainValue(double position)
        {
            var point = new PointData { Position = position };

            Assert.Equal(position, point.Position);
        }

        // =============================================
        // RecipeData 参数字典操作
        // =============================================

        /// <summary>
        /// 验证 RecipeData 新建时，Parameters 字典已初始化（非 null），且为空。
        /// </summary>
        [Fact]
        public void RecipeData_DefaultParameters_ShouldBeEmptyDictionary()
        {
            var recipe = new RecipeData();

            Assert.NotNull(recipe.Parameters);
            Assert.Empty(recipe.Parameters);
        }

        /// <summary>
        /// 验证向 RecipeData.Parameters 添加工艺参数后能正确读取。
        /// </summary>
        [Fact]
        public void RecipeData_AddParameter_ShouldBeRetrievable()
        {
            var recipe = new RecipeData();

            // 添加典型半导体工艺参数
            recipe.Parameters["温度"] = 350.0;
            recipe.Parameters["压力"] = 1.5;
            recipe.Parameters["时间"] = 60;

            Assert.Equal(3, recipe.Parameters.Count);
            Assert.Equal(350.0, recipe.Parameters["温度"]);
            Assert.Equal(1.5, recipe.Parameters["压力"]);
            Assert.Equal(60, recipe.Parameters["时间"]);
        }

        /// <summary>
        /// 验证 RecipeData.Parameters 的更新和删除操作。
        /// </summary>
        [Fact]
        public void RecipeData_UpdateAndRemoveParameter_ShouldWork()
        {
            var recipe = new RecipeData();
            recipe.Parameters["速度"] = 100.0;

            // 更新参数值
            recipe.Parameters["速度"] = 200.0;
            Assert.Equal(200.0, recipe.Parameters["速度"]);

            // 删除参数
            recipe.Parameters.Remove("速度");
            Assert.False(recipe.Parameters.ContainsKey("速度"));
        }

        /// <summary>
        /// 验证 RecipeData.PointTable 可正确添加和检索点位数据。
        /// </summary>
        [Fact]
        public void RecipeData_PointTable_ShouldStorePointDataCorrectly()
        {
            var recipe = new RecipeData();

            // 为轴0添加两个点位
            recipe.PointTable[0] = new List<PointData>
            {
                new PointData { Name = "取料位", Position = 100.0 },
                new PointData { Name = "放料位", Position = 200.0 }
            };

            Assert.Single(recipe.PointTable);         // 只有轴0
            Assert.Equal(2, recipe.PointTable[0].Count); // 轴0有两个点位
            Assert.Equal("取料位", recipe.PointTable[0][0].Name);
            Assert.Equal(200.0, recipe.PointTable[0][1].Position);
        }

        /// <summary>
        /// 验证 RecipeData 的版本号默认值为 "1.0"。
        /// </summary>
        [Fact]
        public void RecipeData_DefaultVersion_ShouldBe1Point0()
        {
            var recipe = new RecipeData();

            Assert.Equal("1.0", recipe.Version);
        }
    }
}
